// NetCommandBridge.cs — Static asmdef-boundary bridge for outgoing network command bytes.
//
// DEPENDENCY INVERSION: RTSCL.World.Unity cannot reference Assembly-CSharp (Steamworks).
// Instead, GameStartLoader (Assembly-CSharp) sets OutgoingSender = NetworkManager.SendToAll
// before SampleScene loads. NetCommandIssuer calls Send() for every command; if OutgoingSender
// is null (solo), the call is a no-op and the command was already applied locally.
//
// To swap transport: just set OutgoingSender to a different Action<byte[]>. The world side
// has zero knowledge of what the sender does with the bytes.
using System;

namespace RTSCL.World.Unity
{
    /// <summary>Asmdef-boundary bridge for outgoing network commands.
    /// The Lobby side sets <see cref="OutgoingSender"/> at game start; the World side calls it to
    /// send packed command payloads without referencing Steamworks types directly.
    /// Null in solo (commands are applied locally without a wire send).</summary>
    public static class NetCommandBridge
    {
        /// <summary>Set by GameStartLoader to NetworkManager.SendToAll before SampleScene loads.
        /// Null in solo play — Send() becomes a no-op.</summary>
        public static Action<byte[]> OutgoingSender;

        /// <summary>Sends a packed command payload to all remote clients.
        /// No-op if OutgoingSender is null (solo play or bridge not yet initialized).</summary>
        /// <param name="payload">Serialized wire-format bytes starting with the message type byte.</param>
        public static void Send(byte[] payload)
        {
            OutgoingSender?.Invoke(payload);
        }

        /// <summary>Clears the sender delegate. Called on scene teardown / solo restart.</summary>
        public static void Reset()
        {
            OutgoingSender = null;
        }
    }
}
