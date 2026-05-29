// NoiseField.cs — Thin wrapper around Unity.Mathematics.noise.snoise for seeded, scaled 2D noise.
// Three independent NoiseField instances (elevation, moisture, temperature) drive world generation.
// Each channel gets a different random offset derived from the same seed, ensuring they are
// uncorrelated while staying deterministic. Adjust scale in WorldGenConfig (Elevation/Moisture/TemperatureScale).

using Unity.Mathematics;

namespace RTSCL.World
{
    /// <summary>Produces deterministic, normalised [0..1] 2D simplex noise for one generation channel.
    /// Construct one instance per channel; the channel index advances the RNG so offsets differ.</summary>
    public sealed class NoiseField
    {
        private readonly float2 _offset;
        private readonly float _scale;

        public NoiseField(uint seed, int channel, float scale)
        {
            // Derive a per-channel deterministic offset from seed.
            var rng = new Random(seed == 0 ? 1u : seed);
            for (int i = 0; i < channel; i++) rng.NextFloat2(); // advance per channel
            _offset = new float2(
                rng.NextFloat(-10000f, 10000f),
                rng.NextFloat(-10000f, 10000f));
            _scale = scale <= 0f ? 1f : scale;
        }

        /// <summary>Convenience overload accepting a signed seed (cast to uint).</summary>
        public NoiseField(int seed, int channel, float scale)
            : this(unchecked((uint)seed), channel, scale) { }

        /// <summary>Returns a value in [0..1].</summary>
        public float Sample(int x, int y)
        {
            float2 p = new float2(x / _scale, y / _scale) + _offset;
            // noise.snoise returns roughly [-1..1]. Normalize.
            float raw = noise.snoise(p);
            return math.saturate(raw * 0.5f + 0.5f);
        }
    }
}
