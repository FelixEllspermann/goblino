// CameraFitter.cs  (static helper — RTSCL.World.Unity)
// One-shot utility called at world-generation time to snap the camera so the
// entire map is visible. After this call, RTSCamera2D takes over for pan/zoom.
// To change the initial view: adjust padding (1.05 = 5% border) or call
// RTSCamera2D.FocusOn() afterwards to re-centre on a spawn point.

using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Snaps an orthographic camera to frame an axis-aligned world rectangle.
    /// Picks the tighter of the width-driven or height-driven ortho size so the full
    /// map fits inside the viewport regardless of aspect ratio.</summary>
    public static class CameraFitter
    {
        /// <summary>Fits <paramref name="camera"/> to show a <paramref name="width"/> x
        /// <paramref name="height"/> world (in <paramref name="cellSize"/> world-units per cell).
        /// <paramref name="padding"/> multiplies the final ortho size (default 1.05 = 5% border).</summary>
        public static void Fit(Camera camera, int width, int height, float cellSize = 1f,
                                float padding = 1.05f)
        {
            if (camera == null) return;
            float wWorld = width  * cellSize;
            float hWorld = height * cellSize;
            // Derive the orthographic half-height required to show the full width
            // (accounting for aspect) vs. the full height — use whichever is larger.
            float orthoFromWidth  = (wWorld / camera.aspect) * 0.5f;
            float orthoFromHeight = hWorld * 0.5f;
            camera.orthographic   = true;
            camera.orthographicSize = Mathf.Max(orthoFromWidth, orthoFromHeight) * padding;
            // Centre the camera over the map. z = -10 keeps it behind all scene content.
            camera.transform.position = new Vector3(wWorld * 0.5f, hWorld * 0.5f, -10f);
        }
    }
}
