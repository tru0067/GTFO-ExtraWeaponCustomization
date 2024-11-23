﻿using System.Text.Json;

namespace EWC.CustomWeapon.Properties
{
    public sealed class ReferenceProperty : 
        WeaponPropertyBase,
        IGunProperty,
        IMeleeProperty
    {
        public ReferenceProperty() { }

        public ReferenceProperty(uint id)
        {
            ReferenceID = id;
        }
        public ReferenceProperty(string id)
        {
            ReferenceID = StringIDToInt(id);
        }

        public PropertyRef? Reference { get; set; }
        public uint ReferenceID { get; private set; } = 0;

        public override void Serialize(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", GetType().Name);
            writer.WriteNumber(nameof(ReferenceID), ReferenceID);
            writer.WriteEndObject();
        }

        public override void DeserializeProperty(string property, ref Utf8JsonReader reader)
        {
            switch (property)
            {
                case "referenceid":
                case "id":
                    if (reader.TokenType == JsonTokenType.String)
                        ReferenceID = StringIDToInt(reader.GetString()!);
                    else
                        ReferenceID = reader.GetUInt32();
                    break;
            }
        }
    }

    public sealed class PropertyRef
    {
        public WeaponPropertyBase property;
        public uint refCount = 0;

        public PropertyRef(WeaponPropertyBase prop) => property = prop;
    }
}
