﻿using Agents;
using Enemies;
using ExtraWeaponCustomization.CustomWeapon.ObjectWrappers;
using ExtraWeaponCustomization.CustomWeapon.WeaponContext.Contexts;
using ExtraWeaponCustomization.Dependencies;
using System.Collections.Generic;
using System.Linq;

namespace ExtraWeaponCustomization.CustomWeapon.KillTracker
{
    public static class KillTrackerManager
    {
        private static readonly Dictionary<AgentWrapper, WeaponPreHitEnemyContext> _lastHits = new();
        private static readonly Dictionary<AgentWrapper, bool> _shownHits = new();
        private static AgentWrapper TempWrapper => AgentWrapper.SharedInstance;

        public static void ClearHit(EnemyAgent enemy)
        {
            TempWrapper.SetAgent(enemy);
            _lastHits.Remove(TempWrapper);
            _shownHits.Remove(TempWrapper);
        }

        public static void RegisterHit(WeaponPreHitEnemyContext hitContext)
        {
            EnemyAgent? enemy = hitContext.Damageable.GetBaseAgent()?.TryCast<EnemyAgent>();
            if (enemy == null || !hitContext.Weapon.Owner.IsLocallyOwned) return;

            // Tag the enemy to ensure KillIndicatorFix tracks hit correctly.
            KillAPIWrapper.TagEnemy(enemy, hitContext.Weapon, hitContext.LocalPosition);

            // Still need to track weapon since KIF doesn't do that for host (only uses wielded, which may not be right for DoT)
            TempWrapper.SetAgent(enemy);
            if (_lastHits.ContainsKey(TempWrapper))
                _lastHits[TempWrapper] = hitContext;
            else
            {
                AgentWrapper wrapper = new(enemy);
                _lastHits[wrapper] = hitContext;
                _shownHits[wrapper] = false;
            }
        }

        public static WeaponPreHitEnemyContext? GetKillHitContext(Agent? enemy)
        {
            _lastHits.Keys
                .Where(wrapper => wrapper.Agent == null || _lastHits[wrapper].Weapon == null)
                .ToList()
                .ForEach(wrapper =>
                {
                    _lastHits.Remove(wrapper);
                    _shownHits.Remove(wrapper);
                });

            if (enemy == null) return null;

            TempWrapper.SetAgent(enemy);
            if (!_shownHits.ContainsKey(TempWrapper) || _shownHits[TempWrapper])
                return null;

            _shownHits[TempWrapper] = true;
            return _lastHits[TempWrapper];
        }
    }
}
