using System;

namespace RTSCL.World.Unity
{
    /// <summary>Asmdef-boundary bridge for outgoing network commands.
    /// The Lobby side sets <see cref="OutgoingSender"/> at game start; the World side calls it to
    /// send packed command payloads without referencing Steamworks types directly.
    /// Null in solo (commands are applied locally without a wire send).</summary>
    public static class NetCommandBridge
    {
        public static Action<byte[]> OutgoingSender;

        public static void Send(byte[] payload)
        {
            OutgoingSender?.Invoke(payload);
        }

        public static void Reset()
        {
            OutgoingSender = null;
        }
    }
}
