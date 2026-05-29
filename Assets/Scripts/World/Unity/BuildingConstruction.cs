// BuildingConstruction.cs — tracks buildings that are still being constructed.
// Key: origin cell (same as BuildingHP / BuildingPlacer). Value: progress 0..MaxProgress + live GO ref.
// Visual feedback: sprite tints from ConstructionTint (semi-transparent grey) → original colour
// as progress rises. Completion fires OnCompleted so callers (e.g. PopulationManager Hut bonus) can react.
// Reset on new world via MainBaseSetup.OnNewWorld → Clear().
// To adjust build speed: change the `amount` passed in Goblin.cs / harvest loop (not here).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    public static class BuildingConstruction
    {
        /// <summary>Progress value that represents a fully-built building (0 = just placed, 100 = done).</summary>
        public const int MaxProgress = 100;

        /// <summary>Fires with the building's origin cell when construction finishes. Subscribe to grant HP bonus, pop cap, etc.</summary>
        public static event Action<Vector2Int> OnCompleted;

        // Internal per-site state. Stored as a struct to avoid heap allocation per building.
        private struct Site
        {
            public int Progress;
            public GameObject BuildingGO;
            public Color OriginalColor; // saved so we can restore the sprite on completion
        }

        private static readonly Dictionary<Vector2Int, Site> _sites = new();

        // Semi-transparent grey applied to buildings during construction; lerped away as work proceeds.
        private static readonly Color ConstructionTint = new(0.65f, 0.65f, 0.65f, 0.55f);

        /// <summary>
        /// Begin tracking a newly-placed building as under construction.
        /// Saves the sprite's original colour and applies ConstructionTint immediately.
        /// </summary>
        /// <param name="origin">Bottom-left cell of the building footprint (shared key with BuildingHP).</param>
        /// <param name="buildingGO">The building's live GameObject (may be null for headless/test paths).</param>
        public static void Register(Vector2Int origin, GameObject buildingGO)
        {
            var sr = buildingGO != null ? buildingGO.GetComponent<SpriteRenderer>() : null;
            Color orig = sr != null ? sr.color : Color.white;
            if (sr != null) sr.color = ConstructionTint;
            _sites[origin] = new Site { Progress = 0, BuildingGO = buildingGO, OriginalColor = orig };
        }

        /// <summary>True while the building at <paramref name="origin"/> is still being built.</summary>
        public static bool IsUnderConstruction(Vector2Int origin) => _sites.ContainsKey(origin);

        /// <summary>
        /// Returns current progress (0..MaxProgress).
        /// Returns MaxProgress (i.e. "complete") for unknown origins so callers can freely query any cell.
        /// </summary>
        public static int GetProgress(Vector2Int origin) =>
            _sites.TryGetValue(origin, out var s) ? s.Progress : MaxProgress;

        /// <summary>
        /// Advance construction by <paramref name="amount"/> progress points.
        /// Lerps the sprite tint toward full colour. Fires OnCompleted and removes the site when done.
        /// </summary>
        /// <param name="origin">Building origin cell.</param>
        /// <param name="amount">Progress to add per call (Farmer work tick).</param>
        /// <param name="justCompleted">Set to true if this call pushed the building to completion.</param>
        /// <returns>False if no site is registered at <paramref name="origin"/> (e.g. already complete).</returns>
        public static bool AddProgress(Vector2Int origin, int amount, out bool justCompleted)
        {
            justCompleted = false;
            if (!_sites.TryGetValue(origin, out var s)) return false;
            s.Progress = Mathf.Min(MaxProgress, s.Progress + amount);

            var sr = s.BuildingGO != null ? s.BuildingGO.GetComponent<SpriteRenderer>() : null;
            if (s.Progress >= MaxProgress)
            {
                // Construction complete: restore full-colour sprite, remove tracking entry.
                if (sr != null) sr.color = s.OriginalColor;
                _sites.Remove(origin);
                justCompleted = true;
                OnCompleted?.Invoke(origin);
                return true;
            }
            if (sr != null)
            {
                // Lerp tint: t=0 → ConstructionTint, t=1 → original colour.
                float t = (float)s.Progress / MaxProgress;
                sr.color = Color.Lerp(ConstructionTint, s.OriginalColor, t);
            }
            _sites[origin] = s; // write back (struct value type)
            return true;
        }

        /// <summary>Drop a single in-progress site (e.g. when a half-built building is destroyed).</summary>
        public static void Remove(Vector2Int origin) => _sites.Remove(origin);

        /// <summary>Wipe all in-progress construction sites. Called when a new world is generated.</summary>
        public static void Clear() => _sites.Clear();
    }
}
