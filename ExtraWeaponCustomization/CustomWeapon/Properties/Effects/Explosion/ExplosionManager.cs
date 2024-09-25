﻿using Agents;
using AIGraph;
using CharacterDestruction;
using Enemies;
using ExtraWeaponCustomization.CustomWeapon.KillTracker;
using ExtraWeaponCustomization.CustomWeapon.Properties.Effects.EEC_Explosion;
using ExtraWeaponCustomization.CustomWeapon.WeaponContext.Contexts;
using ExtraWeaponCustomization.Dependencies;
using ExtraWeaponCustomization.Networking.Structs;
using ExtraWeaponCustomization.Utils;
using ExtraWeaponCustomization.Utils.Log;
using Player;
using SNetwork;
using System;
using System.Collections.Generic;
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
        private const SearchSetting SearchSettings = SearchSetting.CheckLOS | SearchSetting.CacheHit;
        public const float MaxRadius = 1024f;
        public const float MaxStagger = 16384f; // 2^14
        public const float MaxGlowDuration = 50f;

        internal static void Init()
        {
            DamageSync.Setup();
            FXSync.Setup();
            ExplosionEffectPooling.Initialize();
        }

        public static void DoExplosion(Vector3 position, Vector3 direction, PlayerAgent source, float falloffMod, Explosive eBase, float triggerAmt, IDamageable? directLimb = null)
        {
            if (!source.IsLocallyOwned) return;

            ExplosionFXData fxData = new() { position = position, soundID = eBase.SoundID, color = eBase.GlowColor };
            fxData.radius.Set(eBase.Radius, MaxRadius);
            fxData.duration.Set(eBase.GlowDuration, MaxGlowDuration);
            FXSync.Send(fxData, null, SNet_ChannelType.GameNonCritical);
            DoExplosionDamage(position, direction, source, falloffMod, eBase, triggerAmt, directLimb);
        }
    
        internal static void Internal_ReceiveExplosionFX(Vector3 position, float radius, uint soundID, Color color, float duration)
        {
            // Sound
            if (Configuration.PlayExplosionSFX)
            {
                _soundShotOverride++;
                if (_soundShotOverride > Configuration.ExplosionSFXShotOverride || Clock.Time - _lastSoundTime > Configuration.ExplosionSFXCooldown)
                {
                    CellSound.Post(soundID, position);
                    _soundShotOverride = 0;
                    _lastSoundTime = Clock.Time;
                }
            }

            // Lighting
            if (Configuration.ShowExplosionEffect)
            {
                ExplosionEffectPooling.TryDoEffect(new ExplosionEffectData()
                {
                    position = position,
                    flashColor = color,
                    intensity = 5.0f,
                    range = radius,
                    duration = duration
                });
            }
        }

        internal static void DoExplosionDamage(Vector3 position, Vector3 direction, PlayerAgent source, float falloffMod, Explosive explosiveBase, float triggerAmt, IDamageable? directLimb = null)
        {
            AIG_CourseNode? node = SearchUtil.GetCourseNode(position, source);
            if (node == null)
            {
                EWCLogger.Error($"Unable to find node containing position [{position}] for an explosion.");
                return;
            }

            SearchUtil.SightBlockLayer = LayerManager.MASK_EXPLOSION_BLOCKERS;
            List<(EnemyAgent, RaycastHit)> hits = SearchUtil.GetHitsInRange(new Ray(position, direction), explosiveBase.Radius, 180f, node, SearchSettings);
            foreach ((EnemyAgent _, RaycastHit hit) in hits)
            {
                SendExplosionDamage(
                    hit.collider.GetComponent<IDamageable>(),
                    hit.point,
                    direction,
                    hit.distance,
                    source,
                    falloffMod,
                    explosiveBase,
                    triggerAmt);
            }
        }

        internal static void SendExplosionDamage(IDamageable damageable, Vector3 position, Vector3 direction, float distance, PlayerAgent source, float falloffMod, Explosive eBase, float triggerAmt)
        {
            float damage = distance.Map(eBase.InnerRadius, eBase.Radius, eBase.MaxDamage, eBase.MinDamage);
            float distFalloff = damage / eBase.MaxDamage;
            damage *= triggerAmt;
            float precisionMult = eBase.PrecisionDamageMulti;

            WeaponDamageContext context = new(damage, precisionMult, damageable);
            eBase.CWC.Invoke(context);
            if (!eBase.IgnoreDamageMods)
                damage = context.Damage.Value;
            precisionMult = context.Precision.Value;

            // 0.001f to account for rounding error
            damage = falloffMod * damage + 0.001f;

            Agent? agent = damageable.GetBaseAgent();
            if (agent?.Type == AgentType.Player)
            {
                if (agent == eBase.Weapon.Owner)
                {
                    if (!eBase.DamageOwner)
                        return;
                }
                else if (!eBase.DamageFriendly)
                    return;

                GuiManager.CrosshairLayer.PopFriendlyTarget();
                // Seems like the damageable is always the base, but just in case
                Dam_PlayerDamageBase playerBase = damageable.GetBaseDamagable().TryCast<Dam_PlayerDamageBase>()!;
                damage *= playerBase.m_playerData.friendlyFireMulti;
                // Only damage and direction are used AFAIK, but again, just in case...
                playerBase.BulletDamage(damage, source, position, playerBase.DamageTargetPos - position, Vector3.zero);
                return;
            }
            else if (agent == null) // Lock damage; direction doesn't matter
            {
                damageable.BulletDamage(damage, source, Vector3.zero, Vector3.zero, Vector3.zero);
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
            float weakspotMulti = precHit ? Math.Max(limb.m_weakspotDamageMulti * precisionMult, 1f) : 1f;
            float backstabMulti = 1f;
            if (!eBase.IgnoreBackstab)
            {
                if (eBase.CacheBackstab > 0f)
                    backstabMulti = eBase.CacheBackstab;
                else
                {
                    WeaponBackstabContext backContext = new();
                    eBase.CWC.Invoke(backContext);
                    backstabMulti = limb.ApplyDamageFromBehindBonus(1f, position, direction).Map(1f, 2f, 1f, backContext.Value);
                }
            }
            float precDamage = damage * weakspotMulti * armorMulti * backstabMulti;

            // Clamp damage for bubbles
            if (limb.DestructionType == eLimbDestructionType.Custom)
                precDamage = Math.Min(precDamage, limb.m_healthMax + 1);

            data.damage.Set(precDamage, limb.m_base.DamageMax);

            WeaponPreHitEnemyContext hitContext = new(
                precDamage,
                distFalloff * falloffMod,
                backstabMulti,
                damageable,
                position,
                direction,
                precHit ? DamageType.WeakspotExplosive : DamageType.Explosive
                );
            eBase.CWC.Invoke(hitContext);

            KillTrackerManager.RegisterHit(eBase.Weapon, hitContext);
            limb.ShowHitIndicator(precDamage > damage, limb.m_base.WillDamageKill(precDamage), position, armorMulti < 1f);

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
        public uint soundID;
        public LowResColor color;
        public UFloat16 duration;
    }
}