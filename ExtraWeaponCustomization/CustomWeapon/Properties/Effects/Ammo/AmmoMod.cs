﻿using System;
using Player;
using EWC.CustomWeapon.WeaponContext.Contexts;
using System.Text.Json;
using System.Collections.Generic;
using EWC.CustomWeapon.Properties.Effects.Triggers;
using System.Linq;
using Gear;

namespace EWC.CustomWeapon.Properties.Effects
{
    public sealed class AmmoMod :
        Effect,
        IGunProperty,
        IWeaponProperty<WeaponPreFireContext>
    {
        public float ClipChange { get; private set; } = 0;
        public float ReserveChange { get; private set; } = 0;
        public bool OverflowToReserve { get; private set; } = true;
        public bool PullFromReserve { get; private set; } = false;
        public bool UseRawAmmo { get; private set; } = false;
        public InventorySlot ReceiverSlot { get; private set; } = InventorySlot.None;

        private float _clipBuffer = 0;
        private float _reserveBuffer = 0;
        private float _lastFireTime = 0;

        public void Invoke(WeaponPreFireContext context)
        {
            _lastFireTime = Clock.Time;
        }

        public override void TriggerReset()
        {
            _clipBuffer = 0;
            _reserveBuffer = 0;
        }

        public override void TriggerApply(List<TriggerContext> triggerList)
        {
            PlayerBackpack backpack = PlayerBackpackManager.GetBackpack(CWC.Weapon.Owner.Owner);
            ItemEquippable weapon = CWC.Gun!;
            if (ReceiverSlot != InventorySlot.None && backpack.TryGetBackpackItem(ReceiverSlot, out var bpItem) && bpItem.Instance != null)
                weapon = bpItem.Instance.Cast<ItemEquippable>();

            PlayerAmmoStorage ammoStorage = backpack.AmmoStorage;
            InventorySlotAmmo slotAmmo = ammoStorage.GetInventorySlotAmmo(weapon.AmmoType);

            float triggers = triggerList.Sum(context => context.triggerAmt);
            _clipBuffer += ClipChange * triggers;
            _reserveBuffer += ReserveChange * triggers;

            float costOfBullet = slotAmmo.CostOfBullet;
            float min = UseRawAmmo ? costOfBullet : 1f;
            if (Math.Abs(_clipBuffer) < min && Math.Abs(_reserveBuffer) < min) return;
            
            // Ammo decrements after this callback if on kill/shot/hit, need to account for that.
            // But if this weapon didn't get the kill (e.g. DOT kill), shouldn't do that.
            int accountForShot = Clock.Time == _lastFireTime ? 1 : 0;

            if (UseRawAmmo)
            {
                _clipBuffer /= costOfBullet;
                _reserveBuffer /= costOfBullet;
            }

            // Calculate the actual changes we can make to clip/ammo
            int clipChange = (int) (PullFromReserve ? Math.Min(_clipBuffer, slotAmmo.BulletsInPack) : _clipBuffer);
            int newClip = Math.Clamp(weapon.GetCurrentClip() + clipChange, accountForShot, weapon.GetMaxClip() + accountForShot);

            // If we overflow/underflow the magazine, send the rest to reserves (if not pulling from reserves)
            int bonusReserve = OverflowToReserve ? clipChange - (newClip - weapon.GetCurrentClip()) : 0;
            clipChange = newClip - weapon.GetCurrentClip();

            int reserveChange = (int) (PullFromReserve ? _reserveBuffer - clipChange : _reserveBuffer + bonusReserve);

            _clipBuffer -= (int) _clipBuffer;
            _reserveBuffer -= (int) _reserveBuffer;

            weapon.SetCurrentClip(newClip);

            if (UseRawAmmo)
            {
                _clipBuffer *= costOfBullet;
                _reserveBuffer *= costOfBullet;
            }

            ammoStorage.UpdateBulletsInPack(weapon.AmmoType, reserveChange);
            // Need to update UI again since UpdateBulletsInPack does it without including the clip
            ammoStorage.UpdateSlotAmmoUI(slotAmmo, newClip);
        }

        public override void Serialize(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", GetType().Name);
            writer.WriteNumber(nameof(ClipChange), ClipChange);
            writer.WriteNumber(nameof(ReserveChange), ReserveChange);
            writer.WriteBoolean(nameof(OverflowToReserve), OverflowToReserve);
            writer.WriteBoolean(nameof(PullFromReserve), PullFromReserve);
            writer.WriteBoolean(nameof(UseRawAmmo), UseRawAmmo);
            writer.WriteString(nameof(ReceiverSlot), SlotToName(ReceiverSlot));
            SerializeTrigger(writer);
            writer.WriteEndObject();
        }

        public override void DeserializeProperty(string property, ref Utf8JsonReader reader)
        {
            base.DeserializeProperty(property, ref reader);
            switch (property)
            {
                case "clipchange":
                case "clip":
                    ClipChange = reader.GetSingle();
                    break;
                case "reservechange":
                case "reserve":
                    ReserveChange = reader.GetSingle();
                    break;
                case "overflowtoreserve":
                case "overflow":
                    OverflowToReserve = reader.GetBoolean();
                    break;
                case "pullfromreserve":
                    PullFromReserve = reader.GetBoolean();
                    break;
                case "userawammo":
                case "useammo":
                    UseRawAmmo = reader.GetBoolean();
                    break;
                case "receiverslot":
                case "slot":
                    ReceiverSlot = SlotNameToSlot(reader.GetString()!);
                    break;
                default:
                    break;
            }
        }

        private static InventorySlot SlotNameToSlot(string name)
        {
            return name.ToLowerInvariant().Replace(" ", null) switch
            {
                "main" or "primary" => InventorySlot.GearStandard,
                "special" or "secondary" => InventorySlot.GearSpecial,
                "tool" or "class" => InventorySlot.GearClass,
                _ => InventorySlot.None
            };
        }

        private static string SlotToName(InventorySlot slot)
        {
            return slot switch
            {
                InventorySlot.GearStandard => "Main",
                InventorySlot.GearSpecial => "Special",
                InventorySlot.GearClass => "Tool",
                _ => "None",
            };
        }
    }
}
