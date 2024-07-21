﻿using Agents;
using AK;
using CharacterDestruction;
using Enemies;
using ExtraWeaponCustomization.CustomWeapon.KillTracker;
using ExtraWeaponCustomization.CustomWeapon.Properties.Effects.EEC_Explosion;
using ExtraWeaponCustomization.CustomWeapon.WeaponContext.Contexts;
using ExtraWeaponCustomization.Dependencies;
using ExtraWeaponCustomization.Utils;
using Gear;
using Player;
using SNetwork;
using System;
using System.Linq;
using UnityEngine;

namespace ExtraWeaponCustomization.CustomWeapon.Properties.Effects
{
    public static class ExplosionManager
    {
        public static readonly Color FlashColor = new(1, 0.2f, 0, 1);

        internal static ExplosionFXSync FXSync { get; private set; } = new();
        internal static ExplosionDamageSync DamageSync { get; private set; } = new();

        private static float _lastSoundTime = 0f;
        private static int _soundShotOverride = 0;
        public const float MaxRadius = 1024f;
        public const float MaxStagger = 16384f; // 2^14

        internal static void Init()
        {
            DamageSync.Setup();
            FXSync.Setup();
            ExplosionEffectPooling.Initialize();
        }

        public static void DoExplosion(Vector3 position, Vector3 direction, PlayerAgent source, float falloffMod, Explosive eBase, BulletWeapon weapon, IDamageable? directLimb = null)
        {
            ExplosionFXData fxData = new() { position = position };
            fxData.radius.Set(eBase.Radius, MaxRadius);
            FXSync.Send(fxData, null, SNet_ChannelType.GameNonCritical);
            DoExplosionDamage(position, direction, source, falloffMod, eBase, weapon, directLimb);
        }
    
        internal static void Internal_ReceiveExplosionFX(Vector3 position, float radius)
        {
            // Sound
            _soundShotOverride++;
            if (_soundShotOverride > Configuration.ExplosionSFXShotOverride || Clock.Time - _lastSoundTime > Configuration.ExplosionSFXCooldown)
            {
                CellSound.Post(EVENTS.STICKYMINEEXPLODE, position);
                _soundShotOverride = 0;
                _lastSoundTime = Clock.Time;
            }

            // Lighting
            ExplosionEffectPooling.TryDoEffect(new ExplosionEffectData()
            {
                position = position,
                flashColor = FlashColor,
                intensity = 5.0f,
                range = radius,
                duration = 0.05f
            });
        }

        internal static void DoExplosionDamage(Vector3 position, Vector3 direction, PlayerAgent source, float falloffMod, Explosive explosiveBase, BulletWeapon weapon, IDamageable? directLimb = null)
        {
            DamageUtil.IncrementSearchID();
            var searchID = DamageUtil.SearchID;

            if (directLimb != null)
            {
                IDamageable? directBase = directLimb.GetBaseDamagable();
                Agent? agent = directLimb.GetBaseAgent();
                if (directBase != null && agent != null && agent.Alive)
                {
                    directBase.TempSearchID = searchID;
                    SendExplosionDamage(directLimb, position, direction, 0, source, falloffMod, explosiveBase, weapon);
                }
            }

            var colliders = Physics.OverlapSphere(position, explosiveBase.Radius, LayerManager.MASK_EXPLOSION_TARGETS);
            if (colliders.Length < 1)
                return;

            // Loop over colliders in order of distance to make sure we do the most possible damage and hit the correct limb.
            // Not 100% consistent with expectations, but good enough.
            foreach (Collider collider in colliders.OrderBy(
                    t => t.GetComponent<IDamageable>() != null ?
                        Vector3.SqrMagnitude(position - t.GetComponent<IDamageable>().DamageTargetPos)
                      : float.MaxValue)
                )
            {
                IDamageable? limb = collider.GetComponent<IDamageable>();
                if (limb == null)
                    continue;

                Vector3 targetPosition = limb.DamageTargetPos;

                IDamageable? damBase = limb.GetBaseDamagable();
                if (damBase == null)
                    continue;

                if (damBase.TempSearchID == searchID)
                    continue;

                if (damBase.GetBaseAgent() == null || !damBase.GetBaseAgent().Alive)
                    continue;

                // Ensure there is nothing between the explosion and this target
                if (Physics.Linecast(position, targetPosition, out RaycastHit hit, LayerManager.MASK_EXPLOSION_BLOCKERS))
                {
                    if (hit.collider == null)
                        continue;

                    if (hit.collider.gameObject == null)
                        continue;

                    if (hit.collider.gameObject.GetInstanceID() != collider.gameObject.GetInstanceID())
                        continue;

                    targetPosition = hit.point;
                }

                damBase.TempSearchID = searchID;
                SendExplosionDamage(limb, targetPosition, direction, Vector3.Distance(position, targetPosition), source, falloffMod, explosiveBase, weapon);
            }
        }

        internal static void SendExplosionDamage(IDamageable damageable, Vector3 position, Vector3 direction, float distance, PlayerAgent source, float falloffMod, Explosive eBase, BulletWeapon weapon)
        {
            float damage = distance.Map(eBase.InnerRadius, eBase.Radius, eBase.MaxDamage, eBase.MinDamage);
            float distFalloff = damage / eBase.MaxDamage;
            // 0.001f to account for rounding error
            damage = falloffMod * damage + 0.001f;

            CustomWeaponComponent? cwc = weapon.GetComponent<CustomWeaponComponent>();
            if (cwc != null && !eBase.IgnoreDamageMods)
            {
                WeaponDamageContext context = new(damage, damageable, weapon);
                cwc.Invoke(context);
                damage = context.Value;
            }

            if (damageable.GetBaseAgent()?.Type == AgentType.Player)
            {
                GuiManager.CrosshairLayer.PopFriendlyTarget();
                // Seems like the damageable is always the base, but just in case
                Dam_PlayerDamageBase playerBase = damageable.GetBaseDamagable().TryCast<Dam_PlayerDamageBase>()!;
                damage *= playerBase.m_playerData.friendlyFireMulti;
                // Only damage and direction are used AFAIK, but again, just in case...
                playerBase.BulletDamage(damage, source, position, playerBase.DamageTargetPos - position, Vector3.zero);
                return;
            }

            // Applied after FF damage since EXP mod doesn't affect FF damage
            EXPAPIWrapper.ApplyMod(ref damage);

            Dam_EnemyDamageLimb? limb = damageable.TryCast<Dam_EnemyDamageLimb>();
            if (limb == null || limb.m_base.IsImortal) return;

            Vector3 localPosition = position - limb.m_base.Owner.Position;
            ExplosionDamageData data = default;
            data.target.Set(limb.m_base.Owner);
            data.source.Set(source);
            data.limbID = (byte)(eBase.DamageLimb ? limb.m_limbID : 0);
            data.localPosition.Set(localPosition, 10f);
            data.staggerMult.Set(eBase.StaggerDamageMulti, MaxStagger);

            bool precHit = !limb.IsDestroyed && limb.m_type == eLimbDamageType.Weakspot;
            float armorMulti = eBase.IgnoreArmor ? 1f : limb.m_armorDamageMulti;
            float weakspotMulti = precHit ? Math.Max(limb.m_weakspotDamageMulti * eBase.PrecisionDamageMulti, 1f) : 1f;
            float backstabMulti = 1f;
            if (!eBase.IgnoreBackstab)
                backstabMulti = eBase.CacheBackstab > 0f ? eBase.CacheBackstab : limb.ApplyDamageFromBehindBonus(1f, position, direction);
            float precDamage = damage * weakspotMulti * armorMulti * backstabMulti;

            // Clamp damage for bubbles
            if (limb.DestructionType == eLimbDestructionType.Custom)
                precDamage = Math.Min(precDamage, limb.m_healthMax + 1);

            data.damage.Set(precDamage, limb.m_base.DamageMax);

            DamageType flag = precHit ? DamageType.WeakspotExplosive : DamageType.Explosive;
            if (cwc != null)
            {
                cwc.Invoke(new WeaponPreHitEnemyContext(
                    precDamage,
                    distFalloff * falloffMod,
                    backstabMulti,
                    damageable,
                    position,
                    direction,
                    limb.m_base.TempSearchID,
                    weapon,
                    flag
                    ));
            }

            if (source.IsLocallyOwned == true)
            {
                limb.ShowHitIndicator(precDamage > damage, limb.m_base.WillDamageKill(precDamage), position, armorMulti < 1f);
                KillTrackerManager.RegisterHit(limb.GetBaseAgent(), position - limb.m_base.Owner.Position, weapon, flag);
            }

            DamageSync.Send(data, SNet_ChannelType.GameNonCritical);
        }

        internal static void Internal_ReceiveExplosionDamage(EnemyAgent target, PlayerAgent? source, int limbID, Vector3 localPos, float damage, float staggerMult)
        {
            Dam_EnemyDamageBase damBase = target.Damage;
            Dam_EnemyDamageLimb? limb = limbID > 0 ? damBase.DamageLimbs[limbID] : null;
            if (!damBase.Owner.Alive || damBase.IsImortal) return;

            ES_HitreactType hitreact = staggerMult > 0 ? ES_HitreactType.Light : ES_HitreactType.None;
            bool tryForceHitreact = false;
            bool willKill = damBase.WillDamageKill(damage);
            CD_DestructionSeverity severity;
            if (willKill)
            {
                tryForceHitreact = true;
                hitreact = ES_HitreactType.Heavy;
                severity = CD_DestructionSeverity.Death;
            }
            else
            {
                severity = CD_DestructionSeverity.Severe;
            }

            EXPAPIWrapper.RegisterDamage(target, source, damage, willKill);

            Vector3 direction = Vector3.up;
            if (limb != null && (willKill || limb.DoDamage(damage)))
                damBase.CheckDestruction(limb, ref localPos, ref direction, limbID, ref severity, ref tryForceHitreact, ref hitreact);
            
            Vector3 position = localPos + target.Position;
            damBase.ProcessReceivedDamage(damage, source, position, Vector3.up * 1000f, hitreact, tryForceHitreact, limbID, staggerMult);
        }
    }

    public struct ExplosionDamageData
    {
        public pEnemyAgent target;
        public pPlayerAgent source;
        public byte limbID;
        public LowResVector3 localPosition;
        public UFloat16 damage;
        public UFloat16 staggerMult;
    }

    public struct ExplosionFXData
    {
        public Vector3 position;
        public UFloat16 radius;
    }
}