using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    public static class BuildingConstruction
    {
        public const int MaxProgress = 100;

        private struct Site
        {
            public int Progress;
            public GameObject BuildingGO;
            public Color OriginalColor;
        }

        private static readonly Dictionary<Vector2Int, Site> _sites = new();
        private static readonly Color ConstructionTint = new(0.65f, 0.65f, 0.65f, 0.55f);

        public static void Register(Vector2Int origin, GameObject buildingGO)
        {
            var sr = buildingGO != null ? buildingGO.GetComponent<SpriteRenderer>() : null;
            Color orig = sr != null ? sr.color : Color.white;
            if (sr != null) sr.color = ConstructionTint;
            _sites[origin] = new Site { Progress = 0, BuildingGO = buildingGO, OriginalColor = orig };
        }

        public static bool IsUnderConstruction(Vector2Int origin) => _sites.ContainsKey(origin);

        public static int GetProgress(Vector2Int origin) =>
            _sites.TryGetValue(origin, out var s) ? s.Progress : MaxProgress;

        public static bool AddProgress(Vector2Int origin, int amount, out bool justCompleted)
        {
            justCompleted = false;
            if (!_sites.TryGetValue(origin, out var s)) return false;
            s.Progress = Mathf.Min(MaxProgress, s.Progress + amount);

            var sr = s.BuildingGO != null ? s.BuildingGO.GetComponent<SpriteRenderer>() : null;
            if (s.Progress >= MaxProgress)
            {
                if (sr != null) sr.color = s.OriginalColor;
                _sites.Remove(origin);
                justCompleted = true;
                return true;
            }
            if (sr != null)
            {
                float t = (float)s.Progress / MaxProgress;
                sr.color = Color.Lerp(ConstructionTint, s.OriginalColor, t);
            }
            _sites[origin] = s;
            return true;
        }

        public static void Clear() => _sites.Clear();
    }
}
