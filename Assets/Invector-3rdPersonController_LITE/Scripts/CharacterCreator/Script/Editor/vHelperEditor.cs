using UnityEngine;
using UnityEditor;

namespace Motiong
{
    class vHelperEditor : EditorWindow
    {
        [MenuItem("Motiong/Help/Forum")]
        public static void Forum()
        {
            Application.OpenURL("https://assetstore.unity.com/publishers/13943");
        }

        [MenuItem("Motiong/Help/FAQ")]
        public static void FAQMenu()
        {
            Application.OpenURL("https://assetstore.unity.com/publishers/13943");
        }
       
        [MenuItem("Motiong/Help/Release Notes")]
        public static void ReleaseNotes()
        {
            Application.OpenURL("https://assetstore.unity.com/publishers/13943");
        }

        [MenuItem("Motiong/Help/Youtube Tutorials")]
        public static void Youtube()
        {
            Application.OpenURL("https://www.youtube.com/playlist?list=PLvgXGzhT_qehYG_Kzl5P6DuIpHynZP9Ju");
        }

        [MenuItem("Motiong/Help/Online Documentation")]
        public static void Documentation()
        {
            Application.OpenURL("https://assetstore.unity.com/publishers/13943");
        }       
    }
}