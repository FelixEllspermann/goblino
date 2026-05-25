using System;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Asmdef boundary bridge. The Lobby side (Assembly-CSharp) pushes
    /// session start data into these static fields; world-side scripts read them
    /// without referencing Steamworks types directly.</summary>
    public static class WorldStartContext
    {
        /// <summary>SteamID of the local player as ulong. 0 = solo / Steam offline.</summary>
        public static ulong LocalPlayer;

        /// <summary>Per-player spawn assignment. null = solo (no MP slots).</summary>
        public static (ulong steamId, int spawnIndex)[] PendingSlots;

        /// <summary>Lookup: SteamID-as-ulong → faction color. Default returns gray.</summary>
        public static Func<ulong, Color> GetPlayerColor = _ => Color.gray;

        public static void Reset()
        {
            LocalPlayer = 0UL;
            PendingSlots = null;
            // GetPlayerColor intentionally not reset — it's a stable delegate set once at game start.
        }
    }
}
