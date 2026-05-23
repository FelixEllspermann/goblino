using UnityEngine;

namespace RTSCL.World.Unity
{
    public sealed class ResourceUI : MonoBehaviour
    {
        [SerializeField] private bool _showPanel = true;

        private void OnGUI()
        {
            if (!_showPanel || !Application.isPlaying) return;
            GUILayout.BeginArea(new Rect(10, 130, 180, 36), GUI.skin.box);
            GUILayout.Label($"<b>Wood:</b> {ResourceBank.Wood}", Rich());
            GUILayout.EndArea();
        }

        private static GUIStyle s_rich;
        private static GUIStyle Rich()
        {
            if (s_rich == null) s_rich = new GUIStyle(GUI.skin.label) { richText = true };
            return s_rich;
        }
    }
}
