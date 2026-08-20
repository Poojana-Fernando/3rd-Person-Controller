using UnityEditor;

namespace Motiong.vCharacterController
{
    [InitializeOnLoad]
    public class vMotiongStartup
    {
        static vMotiongStartup()
        {
            EditorApplication.update -= TriggerWelcomeScreen;
            EditorApplication.update += TriggerWelcomeScreen;
        }

        private static void TriggerWelcomeScreen()
        {

            EditorApplication.update -= TriggerWelcomeScreen;
        }

        private static void PlayModeChanged()
        {
            EditorApplication.update -= TriggerWelcomeScreen;
        }
    }
}