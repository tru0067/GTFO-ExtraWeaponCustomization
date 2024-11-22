﻿using EWC.CustomWeapon.Properties.Traits.CustomProjectile.Managers;
using EWC.Utils;
using System;
using UnityEngine;

namespace EWC.CustomWeapon.Properties.Traits.CustomProjectile.Components
{
    public abstract class EWCProjectileComponentBase : MonoBehaviour
    {
        public EWCProjectileComponentBase(IntPtr ptr) : base(ptr)
        {
            Hitbox = new(this);
            Homing = new(this);
            _inactiveCallback = new(() => _inactiveLifetime, Cleanup);
        }

        public EWCProjectileHitbox Hitbox;
        public EWCProjectileHoming Homing;
        protected Projectile? _settings;

        private float _endLifetime;
        private DelayedCallback _inactiveCallback;
        protected float _inactiveLifetime = 0f;
        private Vector3 _baseDir;
        private Vector3 _baseVelocity;
        private Vector3 _velocity;
        private float _accelProgress;

        protected Vector3 _position;
        protected float _gravityVel;

        private float _lerpProgress;
        private float _lerpTime;
        private Vector3 _positionVisualDiff;
        protected Vector3 _positionVisual;
        protected Vector3 _dirVisual;

        protected static Quaternion s_tempRot;
        public bool IsLocal { get; private set; }
        public ushort SyncID { get; private set; }
        public ushort PlayerIndex { get; private set; }


        protected virtual void Awake()
        {
            enabled = false;
        }

        public virtual void Init(ushort playerIndex, ushort ID, Projectile projBase, bool isLocal, Vector3 position, Vector3 dir)
        {
            if (enabled) return;

            _inactiveCallback.Cancel();

            SyncID = ID;
            PlayerIndex = playerIndex;
            _settings = projBase;

            gameObject.transform.localScale = Vector3.one * projBase.ModelScale;
            s_tempRot.SetLookRotation(dir);
            gameObject.transform.SetPositionAndRotation(position, s_tempRot);
            gameObject.active = true;
            enabled = true;

            _lerpProgress = 1f;
            _accelProgress = 0f;
            _endLifetime = Time.time + projBase.Lifetime;

            _position = position;
            _velocity = dir * projBase.Speed;
            _baseVelocity = _velocity;
            _baseDir = dir;
            _gravityVel = 0;
            IsLocal = isLocal;

            Hitbox.Init(projBase);
            Homing.Init(projBase, position, dir);
        }

        public virtual void SetVisualPosition(Vector3 positionVisual, float lerpDist)
        {
            if (_velocity.sqrMagnitude == 0) return;

            _lerpProgress = 0f;
            _lerpTime = lerpDist / _velocity.magnitude;
            _positionVisual = positionVisual;
            _positionVisualDiff = _positionVisual - _position;
            _dirVisual = _position + _velocity * _lerpTime - _positionVisual;
            s_tempRot.SetLookRotation(_dirVisual);
            gameObject.transform.SetPositionAndRotation(_positionVisual, s_tempRot);
        }

        private void LerpVisualOffset()
        {
            if (_lerpProgress == 1f)
            {
                _positionVisual = _position;
                _dirVisual = _velocity.sqrMagnitude > 0.01f ? _velocity : _baseDir * .01f;
                s_tempRot.SetLookRotation(_dirVisual);
                return;
            }

            _lerpProgress = Math.Min(1f, _lerpProgress + Time.deltaTime / _lerpTime);
            float invLerp = 1f - _lerpProgress;
            Vector3 lastPos = _positionVisual;
            _positionVisual = _position + Vector3.Lerp(_positionVisualDiff, Vector3.zero, 1f - invLerp * invLerp);
            _dirVisual = _velocity + _positionVisual - lastPos;
            s_tempRot.SetLookRotation(_dirVisual);
        }

        protected virtual void Update()
        {
            if (_settings == null)
            {
                Die();
                return;
            }

            Homing.Update(_position, ref _baseDir);
            _baseVelocity = _baseDir * _settings.Speed;

            Vector3 deltaVel = UpdateVelocity();
            Vector3 collisionVel = deltaVel.sqrMagnitude > EWCProjectileHitbox.MinCollisionSqrDist ? deltaVel : _baseDir * EWCProjectileHitbox.MinCollisionDist;
            Hitbox.Update(_position, collisionVel);
            if (!enabled) return; // Died by hitbox

            if (Time.time > _endLifetime)
            {
                Die();
                return;
            }

            _position += deltaVel;
            LerpVisualOffset();
        }

        private Vector3 UpdateVelocity()
        {
            float delta = Time.deltaTime;
            float deltaMod;
            if (_settings!.AccelScale != 1f)
            {
                if (_accelProgress == 1f)
                {
                    deltaMod = _settings.AccelScale * delta;
                    _velocity = _baseVelocity * _settings!.AccelScale;
                }
                else // Need to calculate how much distance was covered including the acceleration
                {
                    float trgtProgress = Math.Min(_accelProgress + delta / _settings.AccelTime, 1f);
                    float newExpo = _settings.AccelExponent + 1;
                    float progressMod = (float)(_settings.AccelTime * (_settings.AccelScale - 1.0) * (Math.Pow(trgtProgress, newExpo) - Math.Pow(_accelProgress, newExpo)) / newExpo);
                    if (trgtProgress < 1f)
                        deltaMod = delta + progressMod;
                    else
                    {
                        float timeToAccel = (1f - _accelProgress) * _settings.AccelTime;
                        deltaMod = progressMod + timeToAccel + _settings.AccelScale * (delta - timeToAccel);
                    }
                    _accelProgress = trgtProgress;
                    _velocity = _baseVelocity * Mathf.Lerp(1f, _settings.AccelScale, Mathf.Pow(_accelProgress, _settings.AccelExponent));
                }
            }
            else
            {
                _velocity = _baseVelocity;
                deltaMod = delta;
            }

            Vector3 result = _baseVelocity * deltaMod;
            result.y -= 0.5f * _settings.Gravity * delta * delta + _gravityVel * delta;
            _gravityVel += _settings.Gravity * delta;
            _velocity.y -= _gravityVel;

            return result;
        }

        public virtual void Die()
        {
            if (!enabled) return;

            gameObject.transform.localScale = Vector3.zero;
            enabled = false;
            _inactiveCallback.Start();
            EWCProjectileManager.DoProjectileDestroy(PlayerIndex, SyncID, IsLocal);
            Hitbox.Die();
            Homing.Die();
        }

        protected virtual void Cleanup()
        {
            gameObject.active = false;
        }
    }
}
