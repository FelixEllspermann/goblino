// GoblinNetRegistry.cs — Authoritative runtime registry of all live Goblin MonoBehaviours,
// keyed by GoblinNetId (owner SteamID + per-owner local index).
//
// PURPOSE: Allows the network layer to look up a Goblin from a wire message without
// scanning all GameObjects. Goblins register themselves on Awake / Start and unregister on destroy.
//
// LocalIndex counter (NextLocalIndex):
//   Incremented ONCE per train command — by the issuer BEFORE it sends CmdTrainUnit,
//   and by remote clients when they receive and apply CmdTrainUnit.
//   This ensures both issuer and all receivers pre-reserve the same index for the incoming unit,
//   so the new Goblin spawns with an identical NetId on every client.
//   Starting units (spawned by MainBaseSetup) must call NextLocalIndex in deterministic order
//   (matching spawn order on all clients) before registering.
//
// Reset() must be called on scene teardown / world reset to clear stale references.
using System.Collections.Generic;
using RTSCL.World;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks live goblins by NetId and hands out owner-scoped LocalIndex values.</summary>
    public static class GoblinNetRegistry
    {
        // Primary lookup: NetId → Goblin MonoBehaviour.
        private static readonly Dictionary<GoblinNetId, Goblin> _byId = new();

        // Per-owner monotonic counter. Key = owner SteamID (ulong). Value = next available index.
        // Bumped on NextLocalIndex call; shared counter ensures all clients assign identical indices.
        private static readonly Dictionary<ulong, ushort> _nextIndex = new();

        /// <summary>Reserves and returns the next LocalIndex for the given owner, then increments the counter.
        /// Called by NetCommandIssuer.IssueTrainUnit (issuer side) AND NetCommandApplier.ApplyTrainUnit
        /// (remote side) so both advance the counter in lockstep, producing identical NetIds.</summary>
        public static ushort NextLocalIndex(ulong owner)
        {
            _nextIndex.TryGetValue(owner, out ushort idx);
            _nextIndex[owner] = (ushort)(idx + 1);
            return idx;
        }

        /// <summary>Registers a live goblin. Called by Goblin.Awake/Start after NetId is assigned.
        /// Overwrites any stale entry with the same id (safe after scene reset).</summary>
        public static void Register(GoblinNetId id, Goblin g)
        {
            _byId[id] = g;
        }

        /// <summary>Removes a goblin from the registry. Called by Goblin.OnDestroy.</summary>
        public static void Unregister(GoblinNetId id)
        {
            _byId.Remove(id);
        }

        /// <summary>Looks up a live goblin by NetId. Returns false if not found (may have died).</summary>
        public static bool TryGet(GoblinNetId id, out Goblin g) =>
            _byId.TryGetValue(id, out g);

        /// <summary>Enumerates all currently registered live goblins (across all owners).</summary>
        public static IEnumerable<Goblin> All => _byId.Values;

        /// <summary>Clears all registrations and resets all LocalIndex counters.
        /// Call on scene teardown or world reset (MainBaseSetup.OnNewWorld) before re-spawning units.</summary>
        public static void Reset()
        {
            _byId.Clear();
            _nextIndex.Clear();
        }
    }
}
