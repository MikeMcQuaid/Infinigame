using Unity.Mathematics;

namespace Infinigame.Core
{
    // Full songs overlap on the audio clock; no sample at the end must join its own beginning.
    public class Playlist
    {
        public const float Crossfade = 5f;
        readonly float[] lengths;
        public int index { get; private set; }
        public int Next => (index + 1) % lengths.Length;
        public double start { get; private set; }
        public double nextAt => start + lengths[index] - Overlap;
        public float Overlap => math.min(Crossfade, 0.5f * math.min(lengths[index], lengths[Next]));
        public Playlist(float[] lengths, uint seed, double start)
        {
            this.lengths = lengths;
            this.start = start;
            index = (int)(seed % lengths.Length);
        }
        public float2 Gains(double now)
        {
            float fraction = math.saturate((float)(now - nextAt) / math.max(Overlap, 0.001f));
            return new float2(math.cos(fraction * math.PI / 2), math.sin(fraction * math.PI / 2));
        }
        public bool Advance(double now)
        {
            if (now < start + lengths[index]) return false;
            start = nextAt;
            index = Next;
            return true;
        }
    }
}
