#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ResidentEvilAimCamera))]
public class ResidentEvilAimCameraEditor : Editor
{
    private SerializedProperty _followTarget;
    private SerializedProperty _spineAnchor;
    private SerializedProperty _localPositionOffset;
    private SerializedProperty _localRotationOffset;
    private SerializedProperty _defaultYaw;

    private void OnEnable()
    {
        _followTarget = serializedObject.FindProperty("followTarget");
        _spineAnchor = serializedObject.FindProperty("spineAnchor");
        _localPositionOffset = serializedObject.FindProperty("localPositionOffset");
        _localRotationOffset = serializedObject.FindProperty("localRotationOffset");
        _defaultYaw = serializedObject.FindProperty("defaultYaw");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var aimCamera = (ResidentEvilAimCamera)target;

        EditorGUILayout.LabelField("Anchor", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_followTarget);
        EditorGUILayout.PropertyField(_spineAnchor);

        EditorGUILayout.Space(6f);
        DrawPositionOffsetSection(aimCamera);

        EditorGUILayout.Space(6f);
        DrawRotationOffsetSection(aimCamera);

        EditorGUILayout.Space(4f);
        EditorGUILayout.PropertyField(_defaultYaw);

        EditorGUILayout.Space(8f);
        DrawRemainingProperties();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPositionOffsetSection(ResidentEvilAimCamera aimCamera)
    {
        EditorGUILayout.LabelField("Aim Camera Position (player-local)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Offset relative to Follow Target root.\n" +
            "X = right (+) / left (-)\n" +
            "Y = up (+) / down (-)\n" +
            "Z = forward (+) / back (-)",
            MessageType.None);

        EditorGUI.BeginChangeCheck();

        Vector3 offset = _localPositionOffset.vector3Value;
        offset.x = EditorGUILayout.Slider("Offset X (right)", offset.x, -2f, 2f);
        offset.y = EditorGUILayout.Slider("Offset Y (up)", offset.y, 0f, 3f);
        offset.z = EditorGUILayout.Slider("Offset Z (forward)", offset.z, -2f, 2f);
        offset = EditorGUILayout.Vector3Field("Offset (exact)", offset);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset Position"))
            offset = new Vector3(0.85f, 1.75f, -0.83f);
        EditorGUILayout.EndHorizontal();

        _localPositionOffset.vector3Value = offset;

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(aimCamera, "Edit Aim Camera Offset");
            ApplyPreview(aimCamera);
            EditorUtility.SetDirty(aimCamera);
        }
    }

    private void DrawRotationOffsetSection(ResidentEvilAimCamera aimCamera)
    {
        EditorGUILayout.LabelField("Aim Camera Rotation (local bias)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Applied after aim pitch/yaw while playing.\n" +
            "X = pitch (+ look up)\n" +
            "Y = yaw (+ look right)\n" +
            "Z = roll (+ tilt clockwise)",
            MessageType.None);

        EditorGUI.BeginChangeCheck();

        Vector3 rotation = _localRotationOffset.vector3Value;
        rotation.x = EditorGUILayout.Slider("Rotation X (pitch)", rotation.x, -45f, 45f);
        rotation.y = EditorGUILayout.Slider("Rotation Y (yaw)", rotation.y, -45f, 45f);
        rotation.z = EditorGUILayout.Slider("Rotation Z (roll)", rotation.z, -45f, 45f);
        rotation = EditorGUILayout.Vector3Field("Rotation (exact)", rotation);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset Rotation"))
            rotation = Vector3.zero;
        EditorGUILayout.EndHorizontal();

        _localRotationOffset.vector3Value = rotation;

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(aimCamera, "Edit Aim Camera Rotation");
            ApplyPreview(aimCamera);
            EditorUtility.SetDirty(aimCamera);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Preview In Scene"))
        {
            Undo.RecordObject(aimCamera.transform, "Preview Aim Camera");
            ApplyPreview(aimCamera);
            EditorUtility.SetDirty(aimCamera);
        }

        if (GUILayout.Button("Capture From Scene"))
        {
            Undo.RecordObject(aimCamera, "Capture Aim Camera From Scene");
            CaptureFromScene(aimCamera);
            serializedObject.Update();
            EditorUtility.SetDirty(aimCamera);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawRemainingProperties()
    {
        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "followTarget",
            "spineAnchor",
            "localPositionOffset",
            "localRotationOffset",
            "defaultYaw");
    }

    private void OnSceneGUI()
    {
        var aimCamera = (ResidentEvilAimCamera)target;
        Transform anchor = ResolveAnchor(aimCamera);
        if (anchor == null)
            return;

        Vector3 worldPos = anchor.TransformPoint(aimCamera.localPositionOffset);
        Quaternion previewRotation = ComputePreviewBaseRotation(aimCamera, anchor)
            * Quaternion.Euler(aimCamera.localRotationOffset);

        EditorGUI.BeginChangeCheck();
        Vector3 newWorldPos = Handles.PositionHandle(worldPos, previewRotation);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(aimCamera, "Move Aim Camera Offset");
            aimCamera.localPositionOffset = anchor.InverseTransformPoint(newWorldPos);
            ApplyPreview(aimCamera);
            EditorUtility.SetDirty(aimCamera);
        }

        EditorGUI.BeginChangeCheck();
        Quaternion newWorldRot = Handles.RotationHandle(previewRotation, worldPos);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(aimCamera, "Rotate Aim Camera Offset");
            Quaternion baseRotation = ComputePreviewBaseRotation(aimCamera, anchor);
            aimCamera.localRotationOffset = NormalizeEuler(
                (Quaternion.Inverse(baseRotation) * newWorldRot).eulerAngles);
            ApplyPreview(aimCamera);
            EditorUtility.SetDirty(aimCamera);
        }

        Handles.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Handles.DrawDottedLine(anchor.position, worldPos, 4f);
        Handles.Label(worldPos + Vector3.up * 0.08f,
            $"Pos ({aimCamera.localPositionOffset.x:F2}, {aimCamera.localPositionOffset.y:F2}, {aimCamera.localPositionOffset.z:F2})\n" +
            $"Rot ({aimCamera.localRotationOffset.x:F1}, {aimCamera.localRotationOffset.y:F1}, {aimCamera.localRotationOffset.z:F1})");
    }

    [MenuItem("Tools/Resident Evil Gun/Sync Aim Camera From Scene Placement")]
    public static void SyncFromSceneMenu()
    {
        var aimCameraObject = GameObject.Find("AimCamera");
        if (aimCameraObject == null)
        {
            Debug.LogError("AimCamera not found in the open scene.");
            return;
        }

        var aimCamera = aimCameraObject.GetComponent<ResidentEvilAimCamera>();
        if (aimCamera == null)
        {
            Debug.LogError("ResidentEvilAimCamera component missing on AimCamera.");
            return;
        }

        Undo.RecordObject(aimCamera, "Sync Aim Camera From Scene Placement");
        CaptureFromScene(aimCamera);
        EditorUtility.SetDirty(aimCamera);
        Debug.Log(
            $"Aim camera synced: localPositionOffset={aimCamera.localPositionOffset}, " +
            $"localRotationOffset={aimCamera.localRotationOffset}, defaultYaw={aimCamera.defaultYaw:F1}");
    }

    private static Transform ResolveAnchor(ResidentEvilAimCamera aimCamera)
    {
        if (aimCamera.followTarget != null)
            return aimCamera.followTarget;

        var player = GameObject.Find("ThirdPersonController_LITE");
        return player != null ? player.transform : null;
    }

    private static Quaternion ComputePreviewBaseRotation(ResidentEvilAimCamera aimCamera, Transform anchor)
    {
        float yaw = Application.isPlaying ? aimCamera.Yaw : anchor.eulerAngles.y;
        float pitch = Application.isPlaying ? aimCamera.Pitch : 0f;
        return Quaternion.Euler(pitch, yaw, 0f);
    }

    private static void ApplyPreview(ResidentEvilAimCamera aimCamera)
    {
        Transform anchor = ResolveAnchor(aimCamera);
        if (anchor == null)
            return;

        aimCamera.transform.position = anchor.TransformPoint(aimCamera.localPositionOffset);
        aimCamera.transform.rotation = ComputePreviewBaseRotation(aimCamera, anchor)
            * Quaternion.Euler(aimCamera.localRotationOffset);
    }

    private static void CaptureFromScene(ResidentEvilAimCamera aimCamera)
    {
        Transform anchor = ResolveAnchor(aimCamera);
        if (anchor == null)
        {
            Debug.LogWarning("Follow Target not assigned — assign ThirdPersonController_LITE first.");
            return;
        }

        if (aimCamera.followTarget == null)
            aimCamera.followTarget = anchor;

        aimCamera.localPositionOffset = anchor.InverseTransformPoint(aimCamera.transform.position);

        Quaternion baseRotation = ComputePreviewBaseRotation(aimCamera, anchor);
        aimCamera.localRotationOffset = NormalizeEuler(
            (Quaternion.Inverse(baseRotation) * aimCamera.transform.rotation).eulerAngles);
        aimCamera.defaultYaw = aimCamera.transform.eulerAngles.y;
    }

    private static Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(
            NormalizeAngle(euler.x),
            NormalizeAngle(euler.y),
            NormalizeAngle(euler.z));
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
            angle -= 360f;
        if (angle < -180f)
            angle += 360f;
        return angle;
    }
}
#endif
