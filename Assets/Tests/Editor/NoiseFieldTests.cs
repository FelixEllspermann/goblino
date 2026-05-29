// Tests for NoiseField (RTSCL.World assembly).
// Verifies that the noise sampling layer is deterministic, stays in [0,1],
// and that different channel indices produce statistically independent values.
using NUnit.Framework;
using RTSCL.World;
using Unity.Mathematics;

namespace RTSCL.World.Tests
{
    public class NoiseFieldTests
    {
        // Two NoiseField instances constructed with identical seed/channel/scale must return bit-identical samples.
        [Test]
        public void Sample_SameSeedAndCoord_ReturnsSameValue()
        {
            var a = new NoiseField(seed: 42, channel: 0, scale: 40f);
            var b = new NoiseField(seed: 42, channel: 0, scale: 40f);

            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual(a.Sample(i, i * 3), b.Sample(i, i * 3),
                    1e-6f, $"divergence at i={i}");
            }
        }

        // Every sample across a 64×64 grid must lie within the normalised [0, 1] range.
        [Test]
        public void Sample_IsInUnitRange()
        {
            var n = new NoiseField(seed: 1, channel: 0, scale: 30f);
            for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
            {
                float v = n.Sample(x, y);
                Assert.GreaterOrEqual(v, 0f);
                Assert.LessOrEqual(v, 1f);
            }
        }

        // Channel index must act as an independent noise layer: the majority of cells must differ
        // between channel 0 and channel 1 (same seed) so biome axes remain decorrelated.
        [Test]
        public void Sample_DifferentChannelsDiffer()
        {
            var a = new NoiseField(seed: 7, channel: 0, scale: 40f);
            var b = new NoiseField(seed: 7, channel: 1, scale: 40f);

            int differing = 0;
            for (int x = 0; x < 32; x++)
            for (int y = 0; y < 32; y++)
                if (math.abs(a.Sample(x, y) - b.Sample(x, y)) > 0.01f)
                    differing++;

            Assert.Greater(differing, 500, "channels should produce mostly different values");
        }
    }
}
