﻿using Gear;

namespace ExtraWeaponCustomization.CustomWeapon.WeaponContext.Contexts
{
    public sealed class WeaponPreFireContext : WeaponTriggerContext
    {
        public WeaponPreFireContext(BulletWeapon weapon) : base(weapon) {}
    }
}
