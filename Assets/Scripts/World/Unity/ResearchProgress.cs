// ResearchProgress.cs — timed research at a building (one upgrade at a time per building origin).
// The build menu deducts the cost + calls TryStart; GoblinProductionRunner ticks it each frame and, on
// completion, applies the upgrade via NetCommandIssuer.IssuePurchaseUpgrade (which level-ups + syncs).
// The actions-panel progress bar reads Get(origin).Progress. Reset on new world via OnNewWorld.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-building in-progress upgrade research with a 0..1 progress for the UI.</summary>
    public static class ResearchProgress
    {
        public sealed class Slot
        {
            public UpgradeDefinition Def;
            public ulong Owner;
            public float Elapsed;
            public float Progress => Def == null || Def.ResearchDuration <= 0f ? 1f : Mathf.Clamp01(Elapsed / Def.ResearchDuration);
        }

        private static readonly Dictionary<Vector2Int, Slot> _active = new();

        /// <summary>Fires when research starts/finishes/clears (UI repaint).</summary>
        public static event Action OnChanged;

        public static bool IsBusy(Vector2Int origin) => _active.ContainsKey(origin);
        public static Slot Get(Vector2Int origin) => _active.TryGetValue(origin, out var s) ? s : null;

        /// <summary>Begin researching <paramref name="def"/> at <paramref name="origin"/>. Fails if that
        /// building is already researching something. Cost is charged by the caller before this.</summary>
        public static bool TryStart(Vector2Int origin, ulong owner, UpgradeDefinition def)
        {
            if (def == null || _active.ContainsKey(origin)) return false;
            _active[origin] = new Slot { Def = def, Owner = owner, Elapsed = 0f };
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Advance all in-progress research. Returns the ones that finished this frame.</summary>
        public static List<(Vector2Int origin, ulong owner, UpgradeKind kind)> Tick(float dt)
        {
            if (_active.Count == 0) return null;
            List<(Vector2Int, ulong, UpgradeKind)> done = null;
            List<Vector2Int> finished = null;
            foreach (var kv in _active)
            {
                kv.Value.Elapsed += dt;
                if (kv.Value.Elapsed >= kv.Value.Def.ResearchDuration)
                {
                    (done ??= new List<(Vector2Int, ulong, UpgradeKind)>()).Add((kv.Key, kv.Value.Owner, kv.Value.Def.Kind));
                    (finished ??= new List<Vector2Int>()).Add(kv.Key);
                }
            }
            if (finished != null)
            {
                foreach (var o in finished) _active.Remove(o);
                OnChanged?.Invoke();
            }
            return done;
        }

        /// <summary>Drop the research at a building (e.g. when it's destroyed).</summary>
        public static void Remove(Vector2Int origin) { if (_active.Remove(origin)) OnChanged?.Invoke(); }

        /// <summary>Wipe all research. Called when a new world is generated.</summary>
        public static void Clear() { _active.Clear(); OnChanged?.Invoke(); }
    }
}
