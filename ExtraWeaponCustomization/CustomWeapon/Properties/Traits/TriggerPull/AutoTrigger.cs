﻿using EWC.CustomWeapon.WeaponContext.Contexts;
using System.Text.Json;

namespace EWC.CustomWeapon.Properties.Traits
{
    public sealed class AutoTrigger : 
        Trait,
        IGunProperty,
        IWeaponProperty<WeaponSetupContext>,
        IWeaponProperty<WeaponClearContext>
    {
        private bool _cachedTrigger;

        public void Invoke(WeaponSetupContext context) {
            _cachedTrigger = CWC.Gun!.m_archeType.m_triggerNeedsPress;
            CWC.Gun.m_archeType.m_triggerNeedsPress = false;
        }

        public void Invoke(WeaponClearContext context)
        {
            CWC.Gun!.m_archeType.m_triggerNeedsPress = _cachedTrigger;
        }

        public override void Serialize(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", GetType().Name);
            writer.WriteEndObject();
        }

        public override void DeserializeProperty(string property, ref Utf8JsonReader reader) {}
    }
}
