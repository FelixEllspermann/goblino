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
            public ushort ReservedIndex;
            public float Progress => Def == null || Def.SpawnDuration <= 0
                ? 1f
                : Mathf.Clamp01(Elapsed / Def.SpawnDuration);
        }

        private static readonly Dictionary<Vector2Int, Slot> _slots = new();

        public static event Action OnChanged;

        public static bool IsBusy(Vector2Int origin) => _slots.ContainsKey(origin);

        public static Slot Get(Vector2Int origin) =>
            _slots.TryGetValue(origin, out var s) ? s : null;

        public static bool TryStart(Vector2Int origin, GoblinUnitDefinition def, ushort reservedIndex = 0)
        {
            if (def == null) return false;
            if (_slots.ContainsKey(origin)) return false;
            if (!PopulationManager.CanAfford(def.PopulationCost)) return false;
            // Reserve population now so the cap accounting is honest while producing.
            PopulationManager.AddUsed(def.PopulationCost);
            _slots[origin] = new Slot { Def = def, Elapsed = 0f, ReservedIndex = reservedIndex };
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Advance all production timers. Returns finished slots (origin + unit).</summary>
        public static List<(Vector2Int origin, GoblinUnitDefinition def, ushort reservedIndex)> Tick(float dt)
        {
            List<(Vector2Int, GoblinUnitDefinition, ushort)> done = null;
            foreach (var kvp in _slots)
            {
                kvp.Value.Elapsed += dt;
                if (kvp.Value.Elapsed >= kvp.Value.Def.SpawnDuration)
                {
                    done ??= new List<(Vector2Int, GoblinUnitDefinition, ushort)>();
                    done.Add((kvp.Key, kvp.Value.Def, kvp.Value.ReservedIndex));
                }
            }
            if (done != null)
            {
                foreach (var (origin, _, _) in done) _slots.Remove(origin);
                OnChanged?.Invoke();
                return done;
            }
            return null;
        }

        public static void Clear()
        {
            if (_slots.Count == 0) return;
            // Release any reserved population from in-progress productions.
            foreach (var s in _slots.Values)
                if (s.Def != null) PopulationManager.RemoveUsed(s.Def.PopulationCost);
            _slots.Clear();
            OnChanged?.Invoke();
        }
    }
}
