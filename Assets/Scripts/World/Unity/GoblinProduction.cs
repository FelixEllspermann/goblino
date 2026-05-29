// GoblinProduction.cs — static store of active unit-training jobs, one slot per building origin cell.
// Flow: TryStart() reserves population + begins the timer → GoblinProductionRunner.Update() calls Tick()
// each frame → Tick() returns finished jobs → Runner hands them to GoblinSpawner.
// Population cost is reserved at TryStart so the cap is honest during training (not just on spawn).
// OnChanged fires so BuildingPaletteUI can repaint the progress bar without polling.
// Reset on new world via MainBaseSetup.OnNewWorld → Clear() (also releases reserved pop).
// Key: origin cell of the producing building (Keep, Barracks, etc.).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-building unit production state. One active training job allowed per building at a time.</summary>
    public static class GoblinProduction
    {
        /// <summary>Mutable training-job state for a single building. Reference type so Tick() can mutate Elapsed in-place.</summary>
        public sealed class Slot
        {
            /// <summary>The unit definition being trained.</summary>
            public GoblinUnitDefinition Def;
            /// <summary>Seconds elapsed since training started.</summary>
            public float Elapsed;
            /// <summary>Pre-reserved GoblinNetId local index so network clients assign the same ID as the host.</summary>
            public ushort ReservedIndex;
            /// <summary>Normalised training progress 0..1. Used by the UI progress bar.</summary>
            public float Progress => Def == null || Def.SpawnDuration <= 0
                ? 1f
                : Mathf.Clamp01(Elapsed / Def.SpawnDuration);
        }

        // Key = origin cell of the producing building.
        private static readonly Dictionary<Vector2Int, Slot> _slots = new();

        /// <summary>Fires whenever a job starts, finishes, or is cleared. Subscribe in BuildingPaletteUI for progress bar updates.</summary>
        public static event Action OnChanged;

        /// <summary>True while the building at <paramref name="origin"/> has an active training job.</summary>
        public static bool IsBusy(Vector2Int origin) => _slots.ContainsKey(origin);

        /// <summary>Returns the active Slot for the building at <paramref name="origin"/>, or null if idle.</summary>
        public static Slot Get(Vector2Int origin) =>
            _slots.TryGetValue(origin, out var s) ? s : null;

        /// <summary>
        /// Attempt to start training <paramref name="def"/> at the building at <paramref name="origin"/>.
        /// Fails if the building is already busy or population cap is exceeded.
        /// Reserves population immediately on success.
        /// </summary>
        /// <param name="reservedIndex">Pre-allocated GoblinNetId.LocalIndex for deterministic MP identity.</param>
        public static bool TryStart(Vector2Int origin, GoblinUnitDefinition def, ushort reservedIndex = 0)
        {
            if (def == null) return false;
            if (_slots.ContainsKey(origin)) return false;         // already training
            if (!PopulationManager.CanAfford(def.PopulationCost)) return false;
            // Reserve population now so the cap accounting is honest while producing.
            PopulationManager.AddUsed(def.PopulationCost);
            _slots[origin] = new Slot { Def = def, Elapsed = 0f, ReservedIndex = reservedIndex };
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Advance all production timers by <paramref name="dt"/> seconds.
        /// Returns a list of completed jobs this frame (origin, def, reservedIndex), or null if none finished.
        /// Completed jobs are removed from _slots before returning.
        /// </summary>
        public static List<(Vector2Int origin, GoblinUnitDefinition def, ushort reservedIndex)> Tick(float dt)
        {
            List<(Vector2Int, GoblinUnitDefinition, ushort)> done = null;
            foreach (var kvp in _slots)
            {
                kvp.Value.Elapsed += dt;
                if (kvp.Value.Elapsed >= kvp.Value.Def.SpawnDuration)
                {
                    // Null-coalescing assignment: allocate list only on the first completion this frame.
                    done ??= new List<(Vector2Int, GoblinUnitDefinition, ushort)>();
                    done.Add((kvp.Key, kvp.Value.Def, kvp.Value.ReservedIndex));
                }
            }
            if (done != null)
            {
                // Population cost stays reserved — the spawned unit keeps it until death.
                foreach (var (origin, _, _) in done) _slots.Remove(origin);
                OnChanged?.Invoke();
                return done;
            }
            return null;
        }

        /// <summary>Cancel all in-progress jobs and release their reserved population. Called when a new world is generated.</summary>
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
