#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Game.Train.Editor
{
    [CustomEditor(typeof(TrainWheelRig))]
    public class TrainWheelRigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var rig = (TrainWheelRig)target;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Manual wheels", EditorStyles.boldLabel);

            if (GUILayout.Button("Use manual wheel mode"))
            {
                Undo.RecordObject(rig, "Use manual wheel mode");
                rig.UseManualWheelMode();
                EditorUtility.SetDirty(rig);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add axle from selection (OrbitBounds / Drive)"))
                    AddAxle(rig, TrainWheelRig.ManualAxleRole.Drive, TrainWheelRig.ManualWheelPoseMode.OrbitBounds);
                if (GUILayout.Button("Add axle from selection (RestLocalRoll / Drive)"))
                    AddAxle(rig, TrainWheelRig.ManualAxleRole.Drive, TrainWheelRig.ManualWheelPoseMode.RestLocalRoll);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add lead axle from selection"))
                    AddAxle(rig, TrainWheelRig.ManualAxleRole.Lead, TrainWheelRig.ManualWheelPoseMode.OrbitBounds);
                if (GUILayout.Button("Add trail axle from selection"))
                    AddAxle(rig, TrainWheelRig.ManualAxleRole.Trail, TrainWheelRig.ManualWheelPoseMode.OrbitBounds);
            }

            if (GUILayout.Button("Validate / log wheel axles"))
            {
                rig.ResetMeasurement();
                rig.LogAuroraWheelAxles();
            }
        }

        static void AddAxle(TrainWheelRig rig, TrainWheelRig.ManualAxleRole role, TrainWheelRig.ManualWheelPoseMode poseMode)
        {
            Undo.RecordObject(rig, "Add manual axle");
            rig.AppendManualAxleFromSelection(role, poseMode);
        }
    }
}
#endif
