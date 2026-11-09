using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace Game.Train
{
    /// <summary>
    /// The ordered list of wagons coupled to this locomotive. Each slot has its own gap (clearance between the
    /// wagon and the car in front of it); wagons are chained using their measured lengths and each one's
    /// <see cref="TrainCarFollower"/> is given its spline offset from the locomotive.
    /// Edit the list from the inspector (drag wagon prefabs or scene objects onto it).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TrainSplineDriver))]
    public class TrainConsist : MonoBehaviour
    {
        public enum CouplingSide
        {
            Behind,
            InFront
        }

        [Serializable]
        public class Slot
        {
            public TrainCarFollower car;
            [Tooltip("Clearance between this wagon and the car in front of it (m).")]
            [Min(0f)] public float gap = 0.5f;
        }

        [SerializeField] private List<Slot> wagons = new List<Slot>();
        [Tooltip("Gap given to newly added wagons (m).")]
        [Min(0f)] [SerializeField] private float defaultGap = 0.5f;
        [SerializeField] private CouplingSide side = CouplingSide.Behind;

        private TrainSplineDriver _driver;

        public List<Slot> Wagons => wagons;
        public float DefaultGap => defaultGap;
        public TrainSplineDriver Driver => _driver != null ? _driver : _driver = GetComponent<TrainSplineDriver>();

        private void Start()
        {
            Rebuild();
        }

        public bool Contains(TrainCarFollower car)
        {
            return wagons.Exists(s => s != null && s.car == car);
        }

        public void AddWagon(TrainCarFollower car)
        {
            if (car == null || Contains(car))
                return;
            wagons.Add(new Slot { car = car, gap = defaultGap });
        }

        /// <summary>Recompute every wagon's offset from the locomotive. Returns false if the rail isn't available.</summary>
        public bool Rebuild()
        {
            TrainSplineDriver driver = Driver;
            if (!BindRig(driver))
                return false;

            TrainWheelRig rig = driver.Rig != null ? driver.Rig : GetComponent<TrainWheelRig>();
            int sign = side == CouplingSide.Behind ? -1 : 1;
            float edge = TrainCarFollower.MaxExtentAlong(transform, rig.ReferenceWorldPoint, rig.ForwardWorld * sign);

            foreach (Slot slot in wagons)
            {
                if (slot == null || slot.car == null)
                    continue;

                slot.car.MeasureSelf();
                float half = slot.car.HalfLength;
                float center = edge + Mathf.Max(0f, slot.gap) + half;
                slot.car.SetCoupling(driver, sign * center);
                edge = center + half;
            }

            return true;
        }

        /// <summary>Rebuild and place every wagon on the rails behind the locomotive's current position (edit mode).</summary>
        public void PreviewInEditor()
        {
            if (!Rebuild())
                return;

            TrainWheelRig rig = GetComponent<TrainWheelRig>();
            float distance = rig.NearestReferenceDistance();
            foreach (Slot slot in wagons)
            {
                if (slot != null && slot.car != null)
                    slot.car.PoseNow(distance);
            }
        }

        private bool BindRig(TrainSplineDriver driver)
        {
            if (Application.isPlaying)
                return driver.EnsureInitialized() && driver.Rig != null && driver.Rig.IsBound;

            TrainWheelRig rig = GetComponent<TrainWheelRig>();
            if (rig == null)
                return false;
            if (rig.IsBound)
                return true;

            SplineContainer track = driver.Track != null ? driver.Track : TrainSplineDriver.FindRailTrack();
            return track != null && rig.Bind(track, driver.SplineIndex);
        }

        private void OnValidate()
        {
            defaultGap = Mathf.Max(0f, defaultGap);
            if (Application.isPlaying && isActiveAndEnabled)
                Rebuild();
        }
    }
}
