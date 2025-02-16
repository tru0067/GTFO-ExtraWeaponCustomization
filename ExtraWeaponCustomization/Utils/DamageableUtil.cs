﻿using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace EWC.Utils
{
    internal static class DamageableUtil
    {
        public static IDamageable? GetDamageableFromRayHit(RaycastHit rayHit) => rayHit.collider == null ? null : GetDamageableFromCollider(rayHit.collider);

        public static IDamageable? GetDamageableFromCollider(Collider? collider) => collider == null ? null : GetDamageableFromGO(collider.gameObject);

        public static IDamageable? GetDamageableFromGO(GameObject? go)
        {
            if (go == null) return null;

            IDamageable? colliderDamageable = go.GetComponent<ColliderMaterial>()?.Damageable;
            if (colliderDamageable != null)
                return colliderDamageable;

            return go.GetComponent<IDamageable>();
        }

        public static bool IsValid([NotNullWhen(true)] this IDamageable? damageable)
        {
            if (damageable == null) return false;
            
            Agents.Agent? agent = damageable.GetBaseAgent();
            return agent != null || damageable.TryCast<LevelGeneration.LG_WeakLockDamage>() != null;
        }

        public static bool IsEnemy([NotNullWhen(true)] this IDamageable? damageable)
        {
            if (damageable == null) return false;

            Agents.Agent? agent = damageable.GetBaseAgent();
            return agent != null && agent.Alive && agent.Type == Agents.AgentType.Enemy;
        }

        private static IntPtr _cachedExpedition = default;
        private static float _cachedHealth = 15f;
        public static float LockHealth
        {
            get
            {
                if (RundownManager.ActiveExpedition != null && RundownManager.ActiveExpedition.Pointer != _cachedExpedition)
                {
                    _cachedExpedition = RundownManager.ActiveExpedition.Pointer;
                    _cachedHealth = RundownManager.ActiveExpeditionBalanceData.WeakDoorLockHealth;
                }
                return _cachedHealth;
            }
        }
    }
}
