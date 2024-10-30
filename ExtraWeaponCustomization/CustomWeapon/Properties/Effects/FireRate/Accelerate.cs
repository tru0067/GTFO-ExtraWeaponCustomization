﻿using EWC.CustomWeapon.Properties.Effects.Triggers;
using EWC.CustomWeapon.WeaponContext.Contexts;
using EWC.JSON;
using EWC.Utils;
using EWC.Utils.Log;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace EWC.CustomWeapon.Properties.Effects
{
    public class Accelerate :
        Effect,
        IGunProperty,
        IWeaponProperty<WeaponPreStartFireContext>,
        IWeaponProperty<WeaponPreFireContextSync>,
        IWeaponProperty<WeaponFireCanceledContext>,
        IWeaponProperty<WeaponFireRateContext>,
        IWeaponProperty<WeaponDamageContext>
    {
        public float EndFireRate { get; private set; } = 0f;
        private float _endFireRateMod = 1f;
        public float EndFireRateMod
        {
            get { return _endFireRateMod; }
            private set { _endFireRateMod = Math.Max(0.001f, value); }
        }
        public StackType FireRateStackLayer { get; private set; } = StackType.Multiply;
        private float FireRateMod => EndFireRate > 0f ? EndFireRate / CWC.BaseFireRate : EndFireRateMod;

        public float EndDamageMod { get; private set; } = 1f;
        public StackType DamageStackLayer { get; private set; } = StackType.Multiply;

        private float _accelTime = 1f;
        public float AccelTime
        {
            get { return _accelTime; }
            private set { _accelTime = Math.Max(0.001f, value); }
        }
        private float _decelTime = 0.001f;
        public float DecelTime
        {
            get { return _decelTime; }
            private set { _decelTime = Math.Max(0.001f, value); }
        }
        public float DecelDelay { get; private set; } = 0f;
        public float AccelExponent { get; private set; } = 1f;
        public bool UseContinuousGrowth { get; private set; } = false;

        private float _progress = 0f;
        private float _lastUpdateTime = 0f;

        private float _resetProgress = 0f;
        private float _resetUpdateTime = 0f;

        private void UpdateProgress()
        {
            _resetProgress = _progress;
            _resetUpdateTime = _lastUpdateTime;

            if (_lastUpdateTime == 0f)
                _lastUpdateTime = Clock.Time;

            float delta = Clock.Time - _lastUpdateTime;
            float accelTime = Math.Min(1f / CWC.CurrentFireRate, delta);

            _progress = Math.Min(_progress + accelTime / AccelTime, 1f);

            delta -= accelTime + Clock.Delta;

            if (delta > 0 && delta > DecelDelay)
                _progress = Math.Max(_progress - (delta - DecelDelay) / DecelTime, 0f);

            _lastUpdateTime = Clock.Time;
        }

        public void Invoke(WeaponPreStartFireContext context)
        {
            UpdateProgress();   
        }

        public void Invoke(WeaponPreFireContextSync context)
        {
            UpdateProgress();
        }

        // Runs on every shot fired. Don't need to do a full UpdateProgress since we know the player is
        // holding down the trigger if there are consecutive calls to this without UpdateProgress.
        public void Invoke(WeaponFireRateContext context)
        {
            // Update acceleration progress
            _progress = Math.Min(_progress + (Clock.Time - _lastUpdateTime) / AccelTime, 1f);

            _lastUpdateTime = Clock.Time;

            // Apply accelerated fire rate
            if (FireRateMod != 1f)
                context.AddMod(GetFireRateMod(), FireRateStackLayer);
        }

        // FireRate context is called first, so we know progress is up to date
        public void Invoke(WeaponDamageContext context)
        {
            if (EndDamageMod != 1f)
                context.Damage.AddMod(GetDamageMod(), DamageStackLayer);
        }

        public void Invoke(WeaponFireCanceledContext context)
        {
            _progress = _resetProgress;
            _lastUpdateTime = _resetUpdateTime;
        }

        public override void TriggerReset()
        {
            // Reset acceleration
            _progress = 0;
            _lastUpdateTime = Clock.Time;
        }

        public override void TriggerApply(List<TriggerContext> contexts)
        {
            // Reset acceleration
            _progress = 0;
            _lastUpdateTime = Clock.Time;
        }


        private float GetFireRateMod()
        {
            if (UseContinuousGrowth)
                return (float) Math.Pow(FireRateMod, _progress);
            return UnityEngine.Mathf.Lerp(1f, FireRateMod, (float) Math.Pow(_progress, AccelExponent));
        }

        private float GetDamageMod()
        {
            if (UseContinuousGrowth)
                return (float)Math.Pow(EndDamageMod, _progress);
            return UnityEngine.Mathf.Lerp(1f, EndDamageMod, (float)Math.Pow(_progress, AccelExponent));
        }

        public override void Serialize(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", GetType().Name);
            writer.WriteNumber("EndShotDelay", 0f);
            writer.WriteNumber(nameof(EndFireRate), EndFireRate);
            writer.WriteNumber(nameof(EndFireRateMod), EndFireRateMod);
            writer.WriteNumber(nameof(EndDamageMod), EndDamageMod);
            writer.WriteString(nameof(DamageStackLayer), DamageStackLayer.ToString());
            writer.WriteNumber(nameof(AccelTime), AccelTime);
            if (UseContinuousGrowth)
                writer.WriteString(nameof(AccelExponent), "e");
            else
                writer.WriteNumber(nameof(AccelExponent), AccelExponent);
            writer.WriteNumber(nameof(DecelTime), DecelTime);
            writer.WriteNumber(nameof(DecelDelay), DecelDelay);
            writer.WriteString("ResetTrigger", "Invalid");
            writer.WriteEndObject();
        }

        public override void DeserializeProperty(string property, ref Utf8JsonReader reader)
        {
            switch (property)
            {
                case "endshotdelay":
                case "accelshotdelay":
                    EndFireRate = 1f / Math.Max(CustomWeaponData.MinShotDelay, reader.GetSingle());
                    break;
                case "endfirerate":
                case "accelfirerate":
                    EndFireRate = reader.GetSingle();
                    break;
                case "endfireratemod":
                case "accelfireratemod":
                case "fireratemod":
                    EndFireRateMod = reader.GetSingle();
                    break;
                case "fireratestacklayer":
                    FireRateStackLayer = reader.GetString().ToEnum(StackType.Invalid);
                    break;
                case "enddamagemod":
                case "acceldamagemod":
                case "damagemod":
                    EndDamageMod = reader.GetSingle();
                    break;
                case "damagestacklayer":
                case "stacklayer":
                case "layer":
                    DamageStackLayer = reader.GetString().ToEnum(StackType.Invalid);
                    break;
                case "acceltime":
                    AccelTime = reader.GetSingle();
                    break;
                case "accelexponent":
                case "exponent":
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        if (reader.GetString()!.ToLowerInvariant() == "e")
                            UseContinuousGrowth = true;
                    }
                    else
                    {
                        AccelExponent = reader.GetSingle();
                        UseContinuousGrowth = false;
                    }
                    break;
                case "deceltime":
                    DecelTime = reader.GetSingle();
                    break;
                case "deceldelay":
                    DecelDelay = reader.GetSingle();
                    break;
                case "resettriggertype":
                case "resettrigger":
                    Trigger = EWCJson.Deserialize<TriggerCoordinator>(ref reader);
                    break;
                default:
                    break;
            }
        }
    }
}
