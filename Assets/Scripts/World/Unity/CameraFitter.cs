using UnityEngine;

namespace RTSCL.World.Unity
{
    public static class CameraFitter
    {
        public static void Fit(Camera camera, int width, int height, float cellSize = 1f,
                                float padding = 1.05f)
        {
            if (camera == null) return;
            float wWorld = width  * cellSize;
            float hWorld = height * cellSize;
            float orthoFromWidth  = (wWorld / camera.aspect) * 0.5f;
            float orthoFromHeight = hWorld * 0.5f;
            camera.orthographic   = true;
            camera.orthographicSize = Mathf.Max(orthoFromWidth, orthoFromHeight) * padding;
            camera.transform.position = new Vector3(wWorld * 0.5f, hWorld * 0.5f, -10f);
        }
    }
}
