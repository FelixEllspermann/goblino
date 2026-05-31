// GoblinProduction.cs — static store of unit-training jobs, with a QUEUE per building origin cell.
// Flow: TryEnqueue() reserves population + NetId index and either starts immediately (idle building) or
// appends to that building's queue → GoblinProductionRunner.Update() calls Tick() each frame → Tick()
// advances the ACTIVE job, returns finished jobs, and promotes the next queued job into the active slot.
// Population cost is reserved per queued job at enqueue time (so the cap stays honest), and the spawned
// unit keeps it on completion. OnChanged fires so the UI repaints the progress bar + queue count.
// Reset on new world via MainBaseSetup.OnNewWorld → Clear() (releases all reserved pop, active + queued).
// Key: origin cell of the producing building (Keep, Barracks, etc.).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-building unit production with a queue. One job trains at a time; the rest wait in line.</summary>
    public static class GoblinProduction
    {
        /// <summary>Max jobs (active + queued) a single building may hold.</summary>
        public const int MaxQueue = 6;

        /// <summary>Mutable training-job state for a single building. Reference type so Tick() can mutate Elapsed in-place.</summary>
        public sealed class Slot
        {
            /// <summary>The unit definition being trained.</summary>
            public GoblinUnitDefinition Def;
            /// <summary>Seconds elapsed since training started.</summary>
            public float Elapsed;
            /// <summary>Pre-reserved GoblinNetId local index so network clients assign the same ID as the host.</summary>
            public ushort ReservedIndex;
            /// <summary>Owner of the producing building — drives the per-owner training-speed multiplier.</summary>
            public ulong Owner;
            /// <summary>Normalised training progress 0..1. Used by the UI progress bar.</summary>
            public float Progress => Def == null || Def.SpawnDuration <= 0
                ? 1f
                : Mathf.Clamp01(Elapsed / Def.SpawnDuration);
        }

        // Per building origin: the active job (head) followed by queued jobs. A non-empty queue's [0] is
        // the one currently training; [1..] are waiting.
        private sealed class Line { public readonly List<Slot> Jobs = new(); }
        private static readonly Dictionary<Vector2Int, Line> _lines = new();

        /// <summary>Fires whenever a job starts, finishes, is queued, or is cleared. UI subscribes for repaints.</summary>
        public static event Action OnChanged;

        /// <summary>True while the building at <paramref name="origin"/> has any active or queued job.</summary>
        public static bool IsBusy(Vector2Int origin) =>
            _lines.TryGetValue(origin, out var l) && l.Jobs.Count > 0;

        /// <summary>The active (currently-training) Slot for the building, or null if idle.</summary>
        public static Slot Get(Vector2Int origin) =>
            _lines.TryGetValue(origin, out var l) && l.Jobs.Count > 0 ? l.Jobs[0] : null;

        /// <summary>Total jobs at this building (active + queued). 0 = idle.</summary>
        public static int Count(Vector2Int origin) =>
            _lines.TryGetValue(origin, out var l) ? l.Jobs.Count : 0;

        /// <summary>Number of jobs WAITING behind the active one (Count - 1, clamped to ≥0).</summary>
        public static int QueuedBehind(Vector2Int origin) => Mathf.Max(0, Count(origin) - 1);

        /// <summary>True if the building can accept one more job (queue not full).</summary>
        public static bool CanQueue(Vector2Int origin) => Count(origin) < MaxQueue;

        /// <summary>
        /// Enqueue a job to train <paramref name="def"/> at the building at <paramref name="origin"/>.
        /// Starts immediately if the building is idle, otherwise waits in line. Fails if the queue is full
        /// or population cap is exceeded. Reserves population immediately on success.
        /// </summary>
        /// <param name="reservedIndex">Pre-allocated GoblinNetId.LocalIndex for deterministic MP identity.</param>
        public static bool TryEnqueue(Vector2Int origin, GoblinUnitDefinition def, ushort reservedIndex = 0, ulong owner = 0UL)
        {
            if (def == null) return false;
            if (!_lines.TryGetValue(origin, out var line)) { line = new Line(); _lines[origin] = line; }
            if (line.Jobs.Count >= MaxQueue) return false;        // queue full
            if (!PopulationManager.CanAfford(def.PopulationCost)) return false;
            // Reserve population now so the cap accounting is honest while producing/queued.
            PopulationManager.AddUsed(def.PopulationCost);
            line.Jobs.Add(new Slot { Def = def, Elapsed = 0f, ReservedIndex = reservedIndex, Owner = owner });
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Advance the ACTIVE job at each building by <paramref name="dt"/> seconds. Returns completed jobs
        /// this frame (origin, def, reservedIndex), or null if none finished. On completion the job is
        /// removed and the next queued job at that building becomes active (Elapsed reset to 0).
        /// </summary>
        public static List<(Vector2Int origin, GoblinUnitDefinition def, ushort reservedIndex)> Tick(float dt)
        {
            List<(Vector2Int, GoblinUnitDefinition, ushort)> done = null;
            bool changed = false;
            foreach (var kvp in _lines)
            {
                var jobs = kvp.Value.Jobs;
                if (jobs.Count == 0) continue;
                var active = jobs[0];
                // Per-owner training-speed upgrade makes elapsed time accrue faster.
                active.Elapsed += dt * TrainingSpeed.Get(active.Owner);
                if (active.Elapsed >= active.Def.SpawnDuration)
                {
                    done ??= new List<(Vector2Int, GoblinUnitDefinition, ushort)>();
                    done.Add((kvp.Key, active.Def, active.ReservedIndex));
                    jobs.RemoveAt(0);          // finished → drop it; next job (if any) is now active
                    if (jobs.Count > 0) jobs[0].Elapsed = 0f;
                    changed = true;
                }
            }
            if (changed) OnChanged?.Invoke();
            // Population cost stays reserved — the spawned unit keeps it until death.
            return done;
        }

        /// <summary>Cancel every job (active + queued) at all buildings and release their reserved
        /// population. Called when a new world is generated.</summary>
        public static void Clear()
        {
            if (_lines.Count == 0) return;
            foreach (var line in _lines.Values)
                foreach (var s in line.Jobs)
                    if (s.Def != null) PopulationManager.RemoveUsed(s.Def.PopulationCost);
            _lines.Clear();
            OnChanged?.Invoke();
        }
    }
}
