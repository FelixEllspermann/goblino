using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-keep goblin production: one unit in progress at a time, advances on Tick().</summary>
    public static class GoblinProduction
    {
        public sealed class Slot
        {
            public GoblinUnitDefinition Def;
            public float Elapsed;
            public float Progress => Def == null || Def.SpawnDuration <= 0
                ? 1f
                : Mathf.Clamp01(Elapsed / Def.SpawnDuration);
        }

        private static readonly Dictionary<Vector2Int, Slot> _slots = new();

        public static event Action OnChanged;

        public static bool IsBusy(Vector2Int origin) => _slots.ContainsKey(origin);

        public static Slot Get(Vector2Int origin) =>
            _slots.TryGetValue(origin, out var s) ? s : null;

        public static bool TryStart(Vector2Int origin, GoblinUnitDefinition def)
        {
            if (def == null) return false;
            if (_slots.ContainsKey(origin)) return false;
            _slots[origin] = new Slot { Def = def, Elapsed = 0f };
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Advance all production timers. Returns finished slots (origin + unit).</summary>
        public static List<(Vector2Int origin, GoblinUnitDefinition def)> Tick(float dt)
        {
            List<(Vector2Int, GoblinUnitDefinition)> done = null;
            foreach (var kvp in _slots)
            {
                kvp.Value.Elapsed += dt;
                if (kvp.Value.Elapsed >= kvp.Value.Def.SpawnDuration)
                {
                    done ??= new List<(Vector2Int, GoblinUnitDefinition)>();
                    done.Add((kvp.Key, kvp.Value.Def));
                }
            }
            if (done != null)
            {
                foreach (var (origin, _) in done) _slots.Remove(origin);
                OnChanged?.Invoke();
                return done;
            }
            return null;
        }

        public static void Clear()
        {
            if (_slots.Count == 0) return;
            _slots.Clear();
            OnChanged?.Invoke();
        }
    }
}
