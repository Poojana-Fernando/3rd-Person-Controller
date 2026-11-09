using System.Collections.Generic;
using UnityEngine;

namespace Game.Train
{
    /// <summary>
    /// Emits wheel sparks while the train's locked wheels skid along the rails, sprayed away from the direction
    /// of travel (toward the rear when sliding forward, toward the front when sliding in reverse).
    /// Driven by the driver's replicated velocity and wheel lock, so every peer sees the same sparks.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrainWheelSparks : MonoBehaviour
    {
        [Header("Emitters (auto-found under the child named below if empty)")]
        [SerializeField] private ParticleSystem[] emitters;
        [SerializeField] private string sparksRootName = "Sparks";
        [Tooltip("Simulate sparks in world space so they are left behind by the moving train.")]
        [SerializeField] private bool forceWorldSpace = true;

        [Header("When to spark")]
        [Range(0f, 1f)] [SerializeField] private float slideLockThreshold = 0.5f;
        [SerializeField] private float slideMinSpeed = 0.3f;
        [Tooltip("Speed (m/s) at which sparks reach their full emission rate.")]
        [SerializeField] private float fullRateSpeed = 6f;
        [Tooltip("Particles per second per emitter at full intensity. 0 = use each emitter's own rate.")]
        [SerializeField] private float maxRate;

        [Header("Direction")]
        [Tooltip("Seconds to swing the spray around when the sliding direction reverses.")]
        [SerializeField] private float flipTime = 0.15f;

        private class Emitter
        {
            public ParticleSystem System;
            public Transform Transform;
            public Quaternion RestLocalRotation;
            public float BaseRate;
            public int RestSign;
            public float Angle;
        }

        private readonly List<Emitter> _emitters = new List<Emitter>();
        private TrainSplineDriver _driver;
        private int _spraySign = -1;
        private bool _emitting;

        private void Awake()
        {
            _driver = GetComponent<TrainSplineDriver>();
            if (_driver == null)
                _driver = GetComponentInParent<TrainSplineDriver>();
            if (_driver == null)
            {
                TrainCarFollower car = GetComponent<TrainCarFollower>();
                if (car != null)
                    _driver = car.Leader;
            }

            foreach (ParticleSystem ps in FindEmitters())
            {
                ParticleSystem.MainModule main = ps.main;
                main.playOnAwake = false;
                if (forceWorldSpace)
                    main.simulationSpace = ParticleSystemSimulationSpace.World;

                ParticleSystem.EmissionModule emission = ps.emission;
                float baseRate = emission.rateOverTime.constantMax;
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

                _emitters.Add(new Emitter
                {
                    System = ps,
                    Transform = ps.transform,
                    RestLocalRotation = ps.transform.localRotation,
                    BaseRate = baseRate > 0f ? baseRate : 50f
                });
            }
        }

        private void Start()
        {
            Vector3 forward = TrainForward();
            foreach (Emitter e in _emitters)
                e.RestSign = Vector3.Dot(e.Transform.forward, forward) >= 0f ? 1 : -1;
        }

        private IEnumerable<ParticleSystem> FindEmitters()
        {
            if (emitters != null && emitters.Length > 0)
            {
                foreach (ParticleSystem ps in emitters)
                {
                    if (ps != null)
                        yield return ps;
                }
                yield break;
            }

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name != sparksRootName)
                    continue;
                foreach (ParticleSystem ps in t.GetComponentsInChildren<ParticleSystem>(true))
                    yield return ps;
            }
        }

        private Vector3 TrainForward()
        {
            TrainWheelRig rig = _driver != null ? _driver.Rig : null;
            return rig != null ? rig.ForwardWorld : transform.forward;
        }

        private void Update()
        {
            if (_driver == null || _emitters.Count == 0)
                return;

            float velocity = _driver.Velocity;
            float speed = Mathf.Abs(velocity);
            float wheelLock = _driver.WheelLock;

            if (speed > 0.05f)
                _spraySign = velocity > 0f ? -1 : 1;

            bool sliding = wheelLock > slideLockThreshold && speed > slideMinSpeed;
            float intensity = 0f;
            if (sliding)
            {
                float lock01 = Mathf.InverseLerp(slideLockThreshold, 1f, wheelLock);
                float speed01 = Mathf.Clamp01(speed / Mathf.Max(0.1f, fullRateSpeed));
                intensity = Mathf.Lerp(0.3f, 1f, lock01) * Mathf.Lerp(0.25f, 1f, speed01);
            }

            AimEmitters();
            SetEmission(sliding, intensity);
        }

        private void AimEmitters()
        {
            float step = 180f * Time.deltaTime / Mathf.Max(0.01f, flipTime);
            Vector3 upWorld = _driver.transform.up;

            foreach (Emitter e in _emitters)
            {
                if (e.Transform == null)
                    continue;

                float target = e.RestSign == _spraySign ? 0f : 180f;
                if (Mathf.Approximately(e.Angle, target))
                    continue;

                e.Angle = Mathf.MoveTowards(e.Angle, target, step);
                Transform parent = e.Transform.parent;
                Vector3 axis = parent != null ? parent.InverseTransformDirection(upWorld) : upWorld;
                e.Transform.localRotation = Quaternion.AngleAxis(e.Angle, axis) * e.RestLocalRotation;
            }
        }

        private void SetEmission(bool sliding, float intensity)
        {
            foreach (Emitter e in _emitters)
            {
                if (e.System == null)
                    continue;

                if (sliding)
                {
                    ParticleSystem.EmissionModule emission = e.System.emission;
                    emission.rateOverTime = (maxRate > 0f ? maxRate : e.BaseRate) * intensity;
                    if (!_emitting || !e.System.isEmitting)
                        e.System.Play(false);
                }
                else if (_emitting)
                {
                    e.System.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            _emitting = sliding;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            slideMinSpeed = Mathf.Max(0f, slideMinSpeed);
            fullRateSpeed = Mathf.Max(0.1f, fullRateSpeed);
            maxRate = Mathf.Max(0f, maxRate);
            flipTime = Mathf.Max(0f, flipTime);
        }
#endif
    }
}
