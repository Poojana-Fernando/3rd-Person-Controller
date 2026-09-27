#if UNITY_EDITOR
using Game.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Pressing Play while the co-op gameplay scene is open starts from Bootstrap,
/// so the CO-OP Host/Join menu (and NetworkManager/SessionManager) exist; hosting then loads the gameplay scene.
/// </summary>
[InitializeOnLoad]
public static class PlayFromBootstrap
{
    private const string BootstrapPath = "Assets/_Game/Scenes/Bootstrap.unity";

    static PlayFromBootstrap()
    {
        EditorSceneManager.activeSceneChangedInEditMode += (_, _) => Refresh();
        EditorSceneManager.sceneOpened += (_, _) => Refresh();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                Refresh();
        };
        EditorApplication.delayCall += Refresh;
    }

    private static void Refresh()
    {
        if (EditorApplication.isPlaying)
            return;

        Scene active = SceneManager.GetActiveScene();
        bool isGameplay = active.IsValid() && active.name == NetworkGameBootstrap.GameplaySceneName;

        EditorSceneManager.playModeStartScene = isGameplay
            ? AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath)
            : null;
    }
}
#endif
