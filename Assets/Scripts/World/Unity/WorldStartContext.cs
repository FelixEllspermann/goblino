// WorldStartContext.cs — Static asmdef-boundary bridge carrying per-session multiplayer data
// into the RTSCL.World.Unity assembly, which cannot directly reference Assembly-CSharp
// (where Steamworks/lobby code lives).
//
// DEPENDENCY INVERSION: GameStartLoader (Assembly-CSharp) writes into this class before
// calling SceneManager.LoadScene("SampleScene"). World-side MonoBehaviours (MainBaseSetup,
// BuildingOwner, Goblin, etc.) read from it during their Start() / Awake().
//
// Solo play: LocalPlayer = 0, PendingSlots = null, GetPlayerColor returns gray (never called in solo).
// Multiplayer: GameStartLoader sets LocalPlayer = SteamUser.GetSteamID().m_SteamID, populates
// PendingSlots with (steamId, spawnIndex) tuples, and wires GetPlayerColor to PlayerRegistry.GetColorForPlayer.
//
// Reset() is called by MainBaseSetup.OnNewWorld to clear per-session data on map regeneration.
// GetPlayerColor is intentionally NOT reset — it's set once at game start and remains stable.
using System;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Asmdef boundary bridge. The Lobby side (Assembly-CSharp) pushes
    /// session start data into these static fields; world-side scripts read them
    /// without referencing Steamworks types directly.</summary>
    public static class WorldStartContext
    {
        /// <summary>SteamID of the local player as ulong. 0 = solo / Steam offline.
        /// Set by GameStartLoader before SampleScene loads.</summary>
        public static ulong LocalPlayer;

        /// <summary>SteamID of the host player as ulong. 0 = solo. Set by GameStartLoader.
        /// Neutral monsters are owned by the host so only the host authoritatively drives their AI/damage.</summary>
        public static ulong HostPlayer;

        /// <summary>True when no multiplayer slots were assigned → a single local client drives
        /// every unit (including bots). In MP this is false and ownership gating applies normally.</summary>
        public static bool IsSolo => PendingSlots == null;

        /// <summary>Number of AI bots to spawn in solo. Set by the Solo Setup menu; default 1.</summary>
        public static int SoloBotCount = 1;

        /// <summary>Per-player spawn assignment. null = solo (no MP slots).
        /// Each entry maps a SteamID → the spawn point index (0–3) this player owns.
        /// Read by MainBaseSetup.OnNewWorld to place each player's keep at the correct spawn.</summary>
        public static (ulong steamId, int spawnIndex)[] PendingSlots;

        /// <summary>Lookup: SteamID-as-ulong → faction color. Default returns gray.
        /// In multiplayer this is wired to PlayerRegistry.GetColorForPlayer by GameStartLoader.
        /// Used by BuildingOwner and Goblin to tint enemy units/buildings with faction colors.</summary>
        public static Func<ulong, Color> GetPlayerColor = _ => Color.gray;

        /// <summary>Clears per-session player data. Called by MainBaseSetup.OnNewWorld on map reset.
        /// GetPlayerColor is intentionally not reset — it's a stable delegate set once at game start.</summary>
        public static void Reset()
        {
            LocalPlayer = 0UL;
            HostPlayer = 0UL;
            PendingSlots = null;
            // GetPlayerColor intentionally not reset — it's a stable delegate set once at game start.
        }
    }
}
