using System;
using Unity.Mathematics;
using static Infinigame.Core.Biquad;
using Random = Unity.Mathematics.Random;

namespace Infinigame.Core
{
    public enum Wave { Sine, Saw, Square }

    // One second-order filter, the audio cookbook's biquad, with its own memory.
    public struct Biquad
    {
        double b0, b1, b2, a1, a2, z1, z2;

        Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            (this.b0, this.b1, this.b2, this.a1, this.a2) = (b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
            z1 = z2 = 0;
        }

        public float Tick(float x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return (float)y;
        }

        static (double cos, double alpha) Angle(float hertz, float q, int rate)
        {
            double w = 2 * math.PI_DBL * hertz / rate;
            return (math.cos(w), math.sin(w) / (2 * q));
        }

        public static Biquad LowPass(float hertz, float q = 0.707f, int rate = Synth.Rate)
        {
            var (c, a) = Angle(hertz, q, rate);
            return new Biquad((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
        }

        public static Biquad HighPass(float hertz, float q = 0.707f, int rate = Synth.Rate)
        {
            var (c, a) = Angle(hertz, q, rate);
            return new Biquad((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
        }

        // Passes its centre untouched and falls away either side; a higher q is a narrower band.
        public static Biquad BandPass(float hertz, float q = 0.707f, int rate = Synth.Rate)
        {
            var (c, a) = Angle(hertz, q, rate);
            return new Biquad(a, 0, -a, 1 + a, -2 * c, 1 - a);
        }

        // Lifts or cuts a band by so many decibels and leaves the rest alone.
        public static Biquad Peak(float hertz, float q, float decibels, int rate = Synth.Rate)
        {
            var (c, a) = Angle(hertz, q, rate);
            double gain = math.pow(10.0, decibels / 40.0);
            return new Biquad(1 + a * gain, -2 * c, 1 - a * gain, 1 + a / gain, -2 * c, 1 - a / gain);
        }
    }

    // Every sound in the game, worked out from nothing: a small kit of oscillators, noise, filters,
    // strings, distortion and reverb, with the recipe for each effect below it. A sound in the making
    // is one float[] per side; a finished one is both sides interleaved, left first, at Rate.
    public static class Synth
    {
        public const int Rate = 48000;
        const float Tau = 2f * math.PI;

        public static int Samples(float seconds) => (int)(seconds * Rate);

        // White noise: equal power at every frequency.
        public static float[] Noise(int length, uint seed)
        {
            var rnd = new Random(math.hash(new uint2(seed, 0x5EEDu)) | 1u);
            var x = new float[length];
            for (int i = 0; i < length; i++) x[i] = rnd.NextFloat(-1f, 1f);
            return x;
        }

        // Tilts white noise to pink: equal power in every octave, which is how wind, surf and fire are spread.
        // (Paul Kellet's three-pole fit.)
        public static float[] Pink(this float[] x)
        {
            float a = 0f, b = 0f, c = 0f;
            for (int i = 0; i < x.Length; i++)
            {
                a = 0.99765f * a + x[i] * 0.099046f;
                b = 0.963f * b + x[i] * 0.2965164f;
                c = 0.57f * c + x[i] * 1.0526913f;
                x[i] = 0.2f * (a + b + c + x[i] * 0.1848f);
            }
            return x;
        }

        // Tilts white noise to brown: twice as steep as pink, nearly all rumble.
        public static float[] Brown(this float[] x)
        {
            float sum = 0f;
            for (int i = 0; i < x.Length; i++) x[i] = sum = 0.997f * sum + 0.05f * x[i];
            return x;
        }

        // Scattered clicks, so many a second on average: what rain, embers and falling debris are made of.
        public static float[] Crackle(int length, uint seed, float perSecond)
        {
            var rnd = new Random(math.hash(new uint2(seed, 0xC1ACu)) | 1u);
            var x = new float[length];
            for (int i = 0; i < length; i++)
                if (rnd.NextFloat() < perSecond / Rate) x[i] = rnd.NextFloat(-1f, 1f);
            return x;
        }

        // Rounds off the step in a saw or square so its overtones stop short of folding back (PolyBLEP).
        static float Blep(float phase, float step)
        {
            if (phase < step) return 2f * phase / step - phase * phase / (step * step) - 1f;
            if (phase > 1f - step) return (phase - 1f) * (phase - 1f) / (step * step) + 2f * (phase - 1f) / step + 1f;
            return 0f;
        }

        // A tone whose pitch may move as it sounds.
        public static float[] Tone(int length, Func<float, float> hertz, Wave wave = Wave.Sine)
        {
            var x = new float[length];
            double turn = 0;
            for (int i = 0; i < length; i++)
            {
                float step = hertz(i / (float)Rate) / Rate, phase = (float)turn;
                x[i] = wave == Wave.Sine ? math.sin(Tau * phase)
                    : wave == Wave.Saw ? 2f * phase - 1f - Blep(phase, step)
                    : (phase < 0.5f ? 1f : -1f) + Blep(phase, step) - Blep((phase + 0.5f) % 1f, step);
                turn = (turn + step) % 1.0;
            }
            return x;
        }

        // One sine bending the pitch of another (FM). A little gives warmth; a lot, at a ratio that is
        // not a whole number, gives the clang of a bell.
        public static float[] Fm(int length, Func<float, float> hertz, float ratio, Func<float, float> depth)
        {
            var x = new float[length];
            double carrier = 0, bender = 0;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate, step = hertz(t) / Rate;
                x[i] = math.sin(Tau * (float)carrier + depth(t) * math.sin(Tau * (float)bender));
                carrier = (carrier + step) % 1.0;
                bender = (bender + step * ratio) % 1.0;
            }
            return x;
        }

        // Struck metal: partials that are not whole multiples of one another, each a close pair that
        // beats, the higher ones quieter and sooner gone.
        public static float[] Ring(float seconds, float hertz, uint seed, params float[] ratios)
        {
            var rnd = new Random(math.hash(new uint2(seed, 0xBE11u)) | 1u);
            var x = new float[Samples(seconds)];
            for (int k = 0; k < ratios.Length; k++)
            {
                double pitch = 2 * math.PI_DBL * hertz * ratios[k] / Rate, pair = pitch * rnd.NextDouble(1.002, 1.006), offset = rnd.NextDouble(6.0);
                float ring = seconds / (1f + 0.6f * k);
                for (int i = 0; i < x.Length; i++)
                    x[i] += (float)(math.sin(pitch * i) + 0.5 * math.sin(pair * i + offset)) * Hit(i / (float)Rate, 0.001f, ring) / (1 + k);
            }
            return x;
        }

        // A plucked string (Karplus-Strong): a burst of noise goes round a delay one period long and
        // loses its highs a little faster than its lows on every trip, as a real string does.
        // `ring` is how many seconds the note takes to fall 60 dB; `tone` is the pitch above which the
        // loss bites, low for a string muted by the palm and high for one left to sing.
        public static float[] Pluck(int length, float hertz, float ring, float tone, uint seed, int rate = Rate)
        {
            double pole = math.exp(-2 * math.PI_DBL * tone / rate), w = 2 * math.PI_DBL * hertz / rate;
            // one trip is the delay line, the lag of the loss filter and an all-pass that supplies the odd fraction of a sample
            double period = rate / (double)hertz - math.atan2(pole * math.sin(w), 1 - pole * math.cos(w)) / w;
            int whole = math.max(2, (int)(period - 0.5));
            double fraction = period - whole, pass = math.sin((1 - fraction) * w / 2) / math.sin((1 + fraction) * w / 2);
            double keep = math.pow(10.0, -3.0 / (hertz * ring)), damped = 0, before = 0, after = 0;

            // a pick is not perfectly sharp, less so under the palm; and it plucks an eighth of the way along,
            // which leaves out every eighth overtone
            var burst = Noise(whole, seed).Filter(LowPass(math.min(3f * tone, 0.45f * rate), 0.707f, rate));
            var line = new double[whole];
            for (int i = 0; i < whole; i++) line[i] = burst[i] - burst[(i + whole - whole / 8) % whole];

            var x = new float[length];
            for (int i = 0, at = 0; i < length; i++, at = (at + 1) % whole)
            {
                double leaving = line[at];
                x[i] = (float)leaving;
                damped = leaving + pole * (damped - leaving);
                after = pass * (damped - after) + before;
                before = damped;
                line[at] = keep * after;
            }
            return x;
        }

        public static float[] Filter(this float[] x, Biquad filter)
        {
            for (int i = 0; i < x.Length; i++) x[i] = filter.Tick(x[i]);
            return x;
        }

        // A filter whose corner moves as it runs: a low-pass or, when `band` is set, a band-pass.
        // (The zero-delay state-variable form, which stays stable however fast it is swept.)
        public static float[] Sweep(this float[] x, Func<float, float> hertz, float q = 0.707f, bool band = false)
        {
            double low = 0, mid = 0, damp = 1 / q;
            for (int i = 0; i < x.Length; i++)
            {
                double g = math.tan(math.PI_DBL * math.min(hertz(i / (float)Rate), 0.45f * Rate) / Rate), a = 1 / (1 + g * (g + damp));
                double over = x[i] - low, v1 = a * (mid + g * over), v2 = low + g * v1;
                mid = 2 * v1 - mid;
                low = 2 * v2 - low;
                x[i] = (float)(band ? damp * v1 : v2);
            }
            return x;
        }

        // Scales a sound by a gain that changes with time: its envelope.
        public static float[] Shape(this float[] x, Func<float, float> gain)
        {
            for (int i = 0; i < x.Length; i++) x[i] *= gain(i / (float)Rate);
            return x;
        }

        // The same by a curve worked out beforehand: untouched where the curve is 0, up or down by `depth` where it reaches 1 or -1.
        public static float[] Shape(this float[] x, float[] curve, float depth)
        {
            for (int i = 0; i < x.Length; i++) x[i] *= 1f + depth * curve[i];
            return x;
        }

        // A curve that wanders at random between -1 and 1, no faster than `hertz`. Gusts, flames and turbulence are
        // unsteady like this, where a machine pulses evenly. It runs from its end into its start, for a loop.
        public static float[] Wander(int length, uint seed, float hertz)
        {
            // anything this slow can be worked out every sixty-fourth sample and joined up with straight lines
            const int stride = 64;
            int knots = (length + stride - 1) / stride;
            var slow = Noise(knots, seed).Lap(x => x.Filter(LowPass(hertz, 0.707f, Rate / stride)).Filter(LowPass(hertz, 0.707f, Rate / stride))).Level();
            var curve = new float[length];
            for (int i = 0; i < length; i++) curve[i] = math.lerp(slow[i / stride], slow[(i / stride + 1) % knots], i % stride / (float)stride);
            return curve;
        }

        // Overdrive: pushes a sound into a curve that flattens its peaks, adding overtones. A bias
        // makes the two halves of the wave clip unequally, as valves do.
        public static float[] Drive(this float[] x, float gain, float bias = 0f)
        {
            float rest = math.tanh(bias);
            for (int i = 0; i < x.Length; i++) x[i] = math.tanh(gain * x[i] + bias) - rest;
            return x;
        }

        // Scales a sound so its loudest sample is this big, so layers can be balanced by number.
        public static float[] Level(this float[] x, float peak = 1f)
        {
            float loudest = 1e-9f;
            foreach (float sample in x) loudest = math.max(loudest, math.abs(sample));
            for (int i = 0; i < x.Length; i++) x[i] *= peak / loudest;
            return x;
        }

        public static float[] Add(this float[] x, float[] other, float gain = 1f)
        {
            for (int i = 0; i < math.min(x.Length, other.Length); i++) x[i] += gain * other[i];
            return x;
        }

        // Runs a process that has memory over a loop, with a lead-in taken from the loop's own end,
        // so what comes out still runs from its last sample into its first.
        public static float[] Lap(this float[] loop, Func<float[], float[]> process, int lead = int.MaxValue)
        {
            lead = math.min(lead, loop.Length);
            var run = new float[lead + loop.Length];
            Array.Copy(loop, loop.Length - lead, run, 0, lead);
            Array.Copy(loop, 0, run, lead, loop.Length);
            Array.Copy(process(run), lead, loop, 0, loop.Length);
            return loop;
        }

        // The envelope of something struck: up in `attack` seconds, then falling 60 dB every `ring` seconds.
        public static float Hit(float t, float attack, float ring) => t < attack ? t / attack : math.exp(-6.9f * (t - attack) / ring);

        // The envelope of something held: up in `attack`, settling to `sustain` at the pace of `decay`,
        // held until `hold` seconds in and gone `release` seconds after that.
        public static float Adsr(float t, float attack, float decay, float sustain, float hold, float release) =>
            t < attack ? t / attack : math.lerp(sustain, 1f, math.exp(-5f * (math.min(t, hold) - attack) / decay)) * math.saturate(1f - (t - hold) / release);

        // Leaves quiet samples alone and bends loud ones over, so that nothing ever passes full scale.
        public static float Limit(float x) => math.abs(x) < 0.5f ? x : math.sign(x) * (0.5f + 0.5f * math.tanh(2f * math.abs(x) - 1f));

        // Adds a layer to both sides, panned from -1 hard left to 1 hard right, starting `at` seconds in.
        public static void Place(float[] left, float[] right, float[] layer, float gain = 1f, float pan = 0f, float at = 0f)
        {
            float angle = (pan + 1f) * math.PI / 4f, cos = math.cos(angle), sin = math.sin(angle);
            for (int i = 0, start = Samples(at); i < layer.Length && start + i < left.Length; i++)
            {
                left[start + i] += layer[i] * gain * cos;
                right[start + i] += layer[i] * gain * sin;
            }
        }

        // What comes back off the country round about: four returns of everything placed so far, each later, quieter and
        // duller than the last and from another side, as a shot or a blast returns from rock faces at different distances.
        public static void Echo(float[] left, float[] right, uint seed, float nearest, float furthest, float gain)
        {
            var rnd = new Random(seed | 1u);
            var sound = ((float[])left.Clone()).Add(right);
            // no more of it than there is anything in, and a moment over for the dulling to ring out
            Array.Resize(ref sound, math.min(sound.Length, Array.FindLastIndex(sound, sample => sample != 0f) + Rate / 100));
            for (int k = 0; k < 4; k++)
            {
                var back = ((float[])sound.Clone()).Filter(LowPass(5000f / (1 + k)));
                float pan = (k % 2 == 0 ? -1f : 1f) * rnd.NextFloat(0.3f, 0.9f), at = math.lerp(nearest, furthest, (k + 0.8f * rnd.NextFloat()) / 4f);
                // a rock face is rough: what it sends back arrives as a scatter over a few hundredths of a second, not as a copy
                for (int piece = 0; piece < 3; piece++) Place(left, right, back, (piece == 1 ? -1f : 1f) * gain / (1 + k) / math.sqrt(3f), pan, at + rnd.NextFloat(0.02f));
            }
        }

        // The sound of a room. A click is first smeared by two all-passes, then goes round eight delays
        // of unrelated lengths, each fed a mix of all eight (a feedback delay network) and each losing
        // highs as it goes. `seconds` is how long the tail takes to fall 60 dB. `lead` runs that many
        // samples from the end of the input through first, for a loop whose tail must fold back into its start.
        public static (float[] left, float[] right) Reverb(float[] dry, float seconds, float size = 1f, float damp = 6000f, int lead = 0)
        {
            int n = dry.Length;
            float[] left = new float[n], right = new float[n], gain = new float[8], low = new float[8], tap = new float[8];
            var smear = new[] { new float[241], new float[577] };
            var lines = new float[8][];
            int[] lengths = { 1031, 1327, 1523, 1871, 2053, 2311, 2609, 2903 };
            for (int k = 0; k < 8; k++)
            {
                lines[k] = new float[(int)(lengths[k] * size) | 1];
                // each trip loses just enough for the whole tail to last as long as asked; the root eight belongs to the mix below
                gain[k] = math.pow(10f, -3f * lines[k].Length / (seconds * Rate)) / math.sqrt(8f);
            }
            float keep = math.exp(-Tau * damp / Rate);
            for (int i = -lead; i < n; i++)
            {
                float x = dry[(i + n) % n];
                foreach (var line in smear)
                {
                    float y = line[(i + lead) % line.Length] - 0.6f * x;
                    line[(i + lead) % line.Length] = x + 0.6f * y;
                    x = y;
                }
                for (int k = 0; k < 8; k++) tap[k] = low[k] = math.lerp(lines[k][(i + lead) % lines[k].Length], low[k], keep);
                if (i >= 0)
                {
                    left[i] = 0.5f * (tap[0] + tap[2] + tap[4] + tap[6]);
                    right[i] = 0.5f * (tap[1] + tap[3] + tap[5] + tap[7]);
                }
                // a Hadamard mix: every delay hears every other and no energy is made or lost
                for (int span = 1; span < 8; span *= 2)
                    for (int k = 0; k < 8; k++)
                        if ((k & span) == 0) (tap[k], tap[k + span]) = (tap[k] + tap[k + span], tap[k] - tap[k + span]);
                for (int k = 0; k < 8; k++) lines[k][(i + lead) % lines[k].Length] = x + gain[k] * tap[k];
            }
            return (left, right);
        }

        // Sets the level by the loudest sample, rounds off the peaks and interleaves the two sides,
        // fading out over the last `fade` samples.
        static float[] Interleave(float[] left, float[] right, float drive, int fade)
        {
            int n = left.Length;
            float peak = 1e-9f;
            for (int i = 0; i < n; i++) peak = math.max(peak, math.max(math.abs(left[i]), math.abs(right[i])));
            var mix = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                float gain = drive / peak * (fade > 0 ? math.min(1f, (n - 1 - i) / (float)fade) : 1f);
                mix[2 * i] = Limit(left[i] * gain);
                mix[2 * i + 1] = Limit(right[i] * gain);
            }
            return mix;
        }

        // Finishes a sound that plays once: puts it in a room, takes out anything too low to hear,
        // sets its level and fades the very end to silence.
        public static float[] Master(float[] left, float[] right, float drive = 1.3f, float room = 0f, float wet = 0f, float size = 1f)
        {
            if (wet > 0f)
            {
                var (l, r) = Reverb(((float[])left.Clone()).Add(right), room, size);
                left.Add(l, wet);
                right.Add(r, wet);
            }
            return Interleave(left.Filter(HighPass(25f)), right.Filter(HighPass(25f)), drive, Samples(0.03f));
        }

        // Finishes a sound that repeats. It gets no fade. Its standing offset is simply its average.
        public static float[] Loop(float[] left, float[] right, float drive = 1.2f)
        {
            foreach (var side in new[] { left, right })
            {
                double sum = 0;
                foreach (float sample in side) sum += sample;
                for (int i = 0; i < side.Length; i++) side[i] -= (float)(sum / side.Length);
            }
            return Interleave(left, right, drive, 0);
        }

        static float[] Loop(Func<int, float[]> side) => Loop(side(0), side(1));

        static (float[] left, float[] right) Blank(float seconds) => (new float[Samples(seconds)], new float[Samples(seconds)]);

        // Two takes of the same layer, one each side, for width. The layer is told which side it is, -1 or 1.
        static void Wide(float[] left, float[] right, Func<int, float[]> take, float gain = 1f, float at = 0f)
        {
            Place(left, right, take(-1), gain, -0.8f, at);
            Place(left, right, take(1), gain, 0.8f, at);
        }

        // A sine falling in pitch as it dies away: the thump of a drum, a gun or a blast.
        public static float[] Thump(float seconds, float from, float to, float fall, float attack = 0.002f) =>
            Tone(Samples(seconds), t => to + (from - to) * math.exp(-fall * t)).Shape(t => Hit(t, attack, seconds));

        // A burst of filtered noise that is gone by the end of its time.
        public static float[] Burst(float seconds, uint seed, Biquad filter, float attack = 0.001f) =>
            Noise(Samples(seconds), seed).Filter(filter).Level().Shape(t => Hit(t, attack, seconds));

        // Noise that repeats: coloured round its own join, at a known level. Half a second of lead-in is
        // many times longer than any of the colours remembers.
        static float[] Hiss(int length, int seed, Func<float[], float[]> colour) => Noise(length, (uint)seed).Lap(colour, Rate / 2).Level();

        // The shock a bullet drags behind it once it outruns sound: the pressure leaps up, falls steadily to as far
        // below and leaps back, all inside a thousandth of a second. On a trace it is the letter N.
        public static float[] Crack(float seconds) => Tone(Samples(seconds), _ => 1f / seconds, Wave.Saw);

        // Metal meeting metal in a mechanism: a tick, the knock of the part that was struck, a little of its weight and
        // only a trace of ring, since a part held in a mechanism is damped by everything it touches.
        public static float[] Clack(float hertz, uint seed, float seconds = 0.04f) =>
            Burst(seconds, seed, BandPass(hertz, 2.5f), 0.0003f).Add(Burst(0.008f, seed + 1, HighPass(2500f), 0.0002f), 0.7f)
                .Add(Ring(seconds / 2f, hertz, seed, 1f, 1.47f, 2.31f, 3.38f), 0.3f).Add(Thump(seconds, 0.3f * hertz, 0.2f * hertz, 30f, 0.0005f), 0.5f).Level();

        // One thing dragged along another: a hiss about one pitch that swells and dies, rough with catching and letting go.
        public static float[] Scrape(float seconds, float hertz, uint seed) =>
            Burst(seconds, seed, BandPass(hertz, 1.2f), seconds / 2f).Shape(Wander(Samples(seconds), seed + 1, 90f), 0.7f);

        // The weapons: 0 ship pulse cannon, 1 ship missile launch, 2 ship lance, 3 pistol, 4 pump shotgun,
        // 5 bolt-action sniper rifle, 6 raider guns, 7 drone fireball, 8 station turret, 9 assault rifle,
        // 10 ship torpedo launch. Each has a crack that is heard first, a body that tells it from the
        // rest and a thump for weight, kept up where a small speaker can still give it. Each leaves a
        // little of itself in the room. The firearms are what powder does: the crack of the bullet
        // through the sound barrier with the first edge of the blast, the blast itself, lower and longer
        // the bigger the gun, the action working by the right hand and the report coming back off the
        // land and rolling away. A take is the same gun fired again: the same parts, a hair apart in
        // pitch and different in every grain of noise.
        public static float[] Shot(int voice, int take = 0)
        {
            uint salt = 1000u * (uint)take;
            float bend = 1f + 0.04f * math.sin(2.4f * take);
            float[] l, r;
            switch (voice)
            {
                case 0:
                    // fires eleven times a second, so it is over quickly: a falling buzz on a short thump and the clack of the breech
                    (l, r) = Blank(0.5f);
                    Place(l, r, Thump(0.14f, 170f * bend, 70f * bend, 40f), 0.6f);
                    Place(l, r, Ring(0.06f, 880f * bend, 8 + salt, 1f, 1.7f, 2.9f), 0.25f);
                    Place(l, r, Tone(Samples(0.2f), t => bend * (140f + 800f * math.exp(-45f * t)), Wave.Saw).Sweep(t => 500f + 6000f * math.exp(-38f * t), 1.5f).Drive(2f)
                        .Shape(t => Hit(t, 0.001f, 0.16f)), 0.7f);
                    Wide(l, r, side => Burst(0.03f, (uint)(10 + side) + salt, HighPass(1500f), 0.0003f), 0.7f);
                    return Master(l, r, 1.5f, 0.5f, 0.15f);
                case 1:
                    // the clamps let go, then the motor lights and roars away, climbing as it gathers speed
                    (l, r) = Blank(1.5f);
                    Place(l, r, Thump(0.15f, 120f * bend, 50f * bend, 30f));
                    Place(l, r, Ring(0.08f, 640f * bend, 12 + salt, 1f, 2.3f, 3.6f), 0.3f);
                    Wide(l, r, side => Noise(Samples(1.3f), (uint)(14 + side) + salt).Pink().Sweep(t => 300f + 2200f * math.saturate(t / 0.5f), 1.2f, true).Level().Drive(2.5f)
                        .Shape(t => Hit(t, 0.07f, 1.2f)), 0.8f, 0.04f);
                    Wide(l, r, side => Burst(1.2f, (uint)(17 + side) + salt, LowPass(160f), 0.1f), 0.6f, 0.04f);
                    Wide(l, r, side => Burst(0.9f, (uint)(20 + side) + salt, HighPass(5000f), 0.05f), 0.15f, 0.04f);
                    return Master(l, r, 1.4f, 1.2f, 0.25f);
                case 2:
                    // a slug thrown by magnets: a whip-crack, a thump to be felt and the rails buzzing and singing as they discharge
                    (l, r) = Blank(1.7f);
                    Wide(l, r, side => Burst(0.02f, (uint)(23 + side) + salt, HighPass(4000f), 0.0003f), 0.9f);
                    Wide(l, r, side => Burst(0.12f, (uint)(26 + side) + salt, BandPass(1500f, 0.8f)).Drive(3f), 0.5f);
                    Place(l, r, Ring(0.6f, 2100f * bend, 29 + salt, 1f, 1.47f, 2.09f, 2.83f, 3.71f), 0.3f, -0.3f);
                    Place(l, r, Tone(Samples(0.35f), _ => 55f * bend, Wave.Saw).Drive(6f).Filter(BandPass(3000f, 1.5f)).Level().Shape(t => Hit(t, 0.002f, 0.3f)), 0.3f, 0.3f);
                    // the weight is a blow and then a rumble of noise, not a held note that only a subwoofer could give
                    Place(l, r, Thump(0.5f, 95f * bend, 45f * bend, 14f, 0.003f));
                    Wide(l, r, side => Burst(0.8f, (uint)(30 + side) + salt, BandPass(110f, 0.8f), 0.01f), 0.6f);
                    return Master(l, r, 1.6f, 1.5f, 0.3f, 1.4f);
                case 3:
                    // a nine-millimetre pistol: a dry bark with little weight to it, the slide back and home again before the ear
                    // can part them from the shot, and the spent case ringing on the ground a moment after
                    (l, r) = Blank(0.9f);
                    Place(l, r, Crack(0.0004f).Add(Burst(0.004f, 31 + salt, HighPass(3000f), 0.0001f), 0.5f), 0.5f);
                    Wide(l, r, side => Noise(Samples(0.07f), (uint)(33 + side) + salt).Sweep(t => 900f * bend + 4000f * math.exp(-70f * t)).Level().Drive(6f).Shape(t => Hit(t, 0.0003f, 0.07f)), 0.9f);
                    Place(l, r, Thump(0.05f, 260f * bend, 120f * bend, 40f, 0.0005f), 0.8f);
                    Place(l, r, Clack(2300f * bend, 35 + salt, 0.03f), 0.2f, 0.3f, 0.03f);
                    Place(l, r, Clack(1900f * bend, 37 + salt), 0.25f, 0.3f, 0.06f);
                    Echo(l, r, 39 + salt, 0.1f, 0.35f, 0.1f);
                    Wide(l, r, side => Noise(Samples(0.5f), (uint)(42 + side) + salt).Pink().Sweep(t => 250f + 3000f * math.exp(-9f * t)).Level().Shape(t => Hit(t, 0.004f, 0.5f)), 0.2f, 0.01f);
                    Place(l, r, Ring(0.12f, 5200f * bend, 44 + salt, 1f, 1.19f, 1.71f), 0.05f, 0.6f, 0.42f + 0.03f * take);
                    return Master(l, r, 1.5f, 0.4f, 0.12f);
                case 4:
                    // a pump shotgun: a broad, heavy boom with a torn edge, then the fore-end hauled back and shut on the next shell
                    (l, r) = Blank(1.3f);
                    Place(l, r, Crack(0.0006f).Add(Burst(0.006f, 41 + salt, HighPass(2000f), 0.0001f), 0.5f), 0.4f);
                    Wide(l, r, side => Noise(Samples(0.18f), (uint)(43 + side) + salt).Sweep(t => 380f * bend + 3000f * math.exp(-30f * t)).Level().Drive(6f).Shape(t => Hit(t, 0.0004f, 0.18f)));
                    Place(l, r, Thump(0.14f, 170f * bend, 62f * bend, 26f, 0.001f), 1.1f);
                    Echo(l, r, 45 + salt, 0.14f, 0.55f, 0.14f);
                    Wide(l, r, side => Noise(Samples(0.9f), (uint)(46 + side) + salt).Pink().Sweep(t => 200f + 2500f * math.exp(-6f * t)).Level().Shape(t => Hit(t, 0.004f, 0.9f)), 0.3f, 0.01f);
                    for (uint stroke = 0; stroke < 2; stroke++)
                    {
                        Wide(l, r, side => Scrape(0.07f, 1700f + 500f * stroke, (uint)(49 + 2 * side) + 5 * stroke + salt), 0.1f, 0.32f + 0.18f * stroke);
                        Place(l, r, Clack((950f + 250f * stroke) * bend, 60 + 2 * stroke + salt, 0.05f), 0.22f, 0.3f, 0.38f + 0.18f * stroke);
                    }
                    return Master(l, r, 1.6f, 0.7f, 0.2f);
                case 5:
                    // a bolt-action rifle of heavy calibre: the hardest crack of them all, a blast with real weight in it and a report
                    // that rolls about the hills for seconds; under that the bolt is worked by hand: up, back, forward and down
                    (l, r) = Blank(3.5f);
                    Place(l, r, Crack(0.0003f).Add(Burst(0.004f, 62 + salt, HighPass(3500f), 0.0001f), 0.4f), 0.9f);
                    Wide(l, r, side => Noise(Samples(0.22f), (uint)(64 + side) + salt).Sweep(t => 320f * bend + 4000f * math.exp(-35f * t)).Level().Drive(6f).Shape(t => Hit(t, 0.0003f, 0.22f)));
                    Place(l, r, Thump(0.2f, 150f * bend, 52f * bend, 20f, 0.001f), 1.2f);
                    Echo(l, r, 66 + salt, 0.25f, 1.5f, 0.22f);
                    Wide(l, r, side => Noise(Samples(3.4f), (uint)(68 + side) + salt).Pink().Sweep(t => 300f + 3500f * math.exp(-1.5f * t)).Level()
                        .Shape(Wander(Samples(3.4f), (uint)(70 + side) + salt, 3f), 0.4f).Shape(t => Hit(t, 0.005f, 4.5f)), 0.8f, 0.01f);
                    for (uint stroke = 0; stroke < 4; stroke++)
                        Place(l, r, Clack((1700f - 150f * stroke) * bend, 72 + 2 * stroke + salt, stroke % 2 == 1 ? 0.05f : 0.025f), stroke % 2 == 1 ? 0.2f : 0.12f, 0.3f, new[] { 0.6f, 0.76f, 0.98f, 1.1f }[stroke]);
                    Wide(l, r, side => Scrape(0.13f, 2400f, (uint)(81 + 2 * side) + salt), 0.1f, 0.63f);
                    Wide(l, r, side => Scrape(0.15f, 2000f, (uint)(86 + 2 * side) + salt), 0.1f, 0.83f);
                    return Master(l, r, 1.7f, 1.2f, 0.25f, 1.3f);
                case 6:
                    // theirs growl: two saws a little apart sliding down, roughened by a low flutter and overdriven
                    (l, r) = Blank(0.8f);
                    Wide(l, r, side => Tone(Samples(0.5f), t => bend * (110f + 420f * math.exp(-13f * t)) * (1f + 0.004f * side), Wave.Saw)
                        .Shape(t => 1f + 0.6f * math.sin(Tau * 73f * t)).Drive(4f).Filter(BandPass(1700f, 0.8f)).Shape(t => Hit(t, 0.002f, 0.5f)), 0.8f);
                    Wide(l, r, side => Burst(0.04f, (uint)(52 + side) + salt, BandPass(2200f, 1f), 0.0005f), 0.5f);
                    Place(l, r, Thump(0.16f, 105f * bend, 45f * bend, 30f), 0.4f);
                    return Master(l, r, 1.4f, 0.7f, 0.2f);
                case 7:
                    // a slow ball of fire: it swells rather than cracks, roaring and fluttering as flame does and spitting as it goes
                    (l, r) = Blank(1.2f);
                    Wide(l, r, side => Noise(Samples(1f), (uint)(55 + side) + salt).Pink().Filter(HighPass(200f)).Sweep(t => 500f + 2500f * Hit(t, 0.08f, 0.9f)).Level()
                        .Shape(Wander(Samples(1f), (uint)(60 + side) + salt, 25f), 0.5f).Shape(t => Hit(t, 0.05f, 0.9f)), 0.9f);
                    Place(l, r, Thump(0.5f, 150f * bend, 60f * bend, 9f, 0.02f), 0.3f);
                    Wide(l, r, side => Crackle(Samples(0.8f), (uint)(58 + side) + salt, 60f).Filter(BandPass(3000f, 1.5f)).Level().Shape(t => Hit(t, 0.05f, 0.8f)), 0.25f, 0.03f);
                    Echo(l, r, 59 + salt, 0.15f, 0.4f, 0.08f);
                    return Master(l, r, 1.4f, 0.8f, 0.2f);
                case 8:
                    // a big gun on a fixed mount: a knock, the breech clanking and the report rolling off
                    (l, r) = Blank(0.9f);
                    Place(l, r, Thump(0.22f, 190f * bend, 60f * bend, 30f).Drive(2f));
                    Place(l, r, Ring(0.14f, 430f * bend, 61 + salt, 1f, 1.83f, 2.67f, 3.52f, 4.9f), 0.45f, -0.2f);
                    Wide(l, r, side => Burst(0.06f, (uint)(63 + side) + salt, BandPass(1800f, 0.7f), 0.0005f).Drive(3f), 0.6f);
                    Wide(l, r, side => Burst(0.4f, (uint)(66 + side) + salt, LowPass(800f), 0.005f), 0.3f);
                    return Master(l, r, 1.5f, 1f, 0.3f);
                case 9:
                    // an assault rifle, ten rounds a second: each round a crack and a short hard bark with the bolt carrier clattering
                    // behind it, and next to nothing left of it by the time the next one goes
                    (l, r) = Blank(0.45f);
                    Place(l, r, Crack(0.0003f).Add(Burst(0.004f, 91 + salt, HighPass(3000f), 0.0001f), 0.4f), 0.7f);
                    Wide(l, r, side => Noise(Samples(0.06f), (uint)(93 + side) + salt).Sweep(t => 650f * bend + 3500f * math.exp(-80f * t)).Level().Drive(6f).Shape(t => Hit(t, 0.0003f, 0.06f)), 0.9f);
                    Place(l, r, Thump(0.05f, 210f * bend, 95f * bend, 40f, 0.0005f), 0.9f);
                    Place(l, r, Clack(1700f * bend, 95 + salt, 0.025f), 0.2f, 0.3f, 0.02f);
                    Place(l, r, Clack(1450f * bend, 97 + salt, 0.03f), 0.2f, 0.3f, 0.05f);
                    Wide(l, r, side => Noise(Samples(0.3f), (uint)(98 + side) + salt).Pink().Sweep(t => 250f + 2500f * math.exp(-14f * t)).Level().Shape(t => Hit(t, 0.004f, 0.3f)), 0.15f, 0.01f);
                    return Master(l, r, 1.5f, 0.3f, 0.1f);
                default:
                    // a heavy torpedo let go: the clamps open with a clang that the hull carries, gas kicks it clear, then its motor
                    // lights with a roar far bigger than a missile's and bears it away
                    (l, r) = Blank(3.4f);
                    Place(l, r, Thump(0.3f, 130f * bend, 55f * bend, 18f, 0.002f));
                    Place(l, r, Ring(0.35f, 230f * bend, 101 + salt, 1f, 1.59f, 2.27f, 3.14f, 4.4f), 0.5f, -0.2f);
                    Wide(l, r, side => Burst(0.4f, (uint)(103 + side) + salt, HighPass(1200f), 0.03f), 0.3f, 0.05f);
                    Wide(l, r, side => Noise(Samples(2.9f), (uint)(105 + side) + salt).Pink().Sweep(t => 130f + 450f * math.saturate(t / 0.7f) * math.exp(-0.5f * t), 0.8f, true).Level().Drive(3f)
                        .Shape(Wander(Samples(2.9f), (uint)(107 + side) + salt, 20f), 0.35f).Shape(t => Hit(t, 0.15f, 2.8f)), 0.9f, 0.3f);
                    Wide(l, r, side => Burst(2.9f, (uint)(109 + side) + salt, BandPass(85f, 0.6f), 0.2f).Shape(Wander(Samples(2.9f), (uint)(111 + side) + salt, 6f), 0.5f), 0.8f, 0.3f);
                    return Master(l, r, 1.6f, 1.3f, 0.25f, 1.4f);
            }
        }

        // Something destroyed. A small one opens with the shock, a crack right across the spectrum, on
        // the blow of the blast, which is felt; both come back off the land. Then a ball of fire closes
        // down into a rumble and bits rain out of it. A big one is three of those going off in turn
        // over a longer, lower roll, in a bigger space, with wreckage clattering as it scatters. The
        // weight is a short blow and a rumble of noise rather than a long low note, so it survives a
        // small speaker and leaves the mix room. No two takes go off alike.
        public static float[] Boom(bool big, int take = 0)
        {
            float span = big ? 3.4f : 2.4f;
            var (l, r) = Blank(big ? 5.5f : 3.2f);
            var rnd = new Random((big ? 71u : 72u) + 977u * (uint)take);
            var blasts = new (float at, float pan, uint seed)[big ? 3 : 1];
            for (int i = 0; i < blasts.Length; i++) blasts[i] = (i * rnd.NextFloat(0.15f, 0.25f), i == 0 ? 0f : rnd.NextFloat(-0.6f, 0.6f), rnd.NextUInt(1u << 30));
            foreach (var (at, pan, seed) in blasts)
            {
                Wide(l, r, side => Burst(0.04f, (uint)(seed + side), HighPass(500f), 0.0003f), 0.9f, at);
                Place(l, r, Thump(0.6f, big ? 100f : 130f, big ? 40f : 50f, big ? 10f : 16f, 0.004f), 1.1f, pan, at);
            }
            // only the cracks and the blows come back: the roar that follows would bury an echo of itself
            Echo(l, r, rnd.NextUInt(), big ? 0.3f : 0.2f, big ? 1.1f : 0.7f, 0.3f);
            foreach (var (at, pan, seed) in blasts)
            {
                // the first is the one that burns on; those that follow it are over sooner
                float roar = at > 0f ? 0.6f * span : span;
                Wide(l, r, side => Noise(Samples(roar), (uint)(seed + 3 + side)).Pink().Sweep(t => (big ? 140f : 220f) + 6000f * math.exp(-(big ? 4f : 7f) * t)).Level().Drive(3f)
                    .Shape(t => Hit(t, 0.002f, roar)), 1f, at);
                Place(l, r, Burst(roar, seed + 6, BandPass(big ? 70f : 95f, 0.7f), 0.02f).Shape(Wander(Samples(roar), seed + 7, 9f), 0.6f), 0.9f, pan, at);
            }
            Wide(l, r, side => Crackle(Samples(span), (uint)(110 + side + 9 * take), 50f).Filter(BandPass(2500f, 1.2f)).Level().Shape(t => Hit(t, 0.2f, span)), 0.2f, 0.1f);
            for (int piece = 0; big && piece < 9; piece++)
                Place(l, r, Burst(rnd.NextFloat(0.03f, 0.12f), rnd.NextUInt(), BandPass(rnd.NextFloat(700f, 3500f), 5f), 0.0005f), rnd.NextFloat(0.06f, 0.18f), rnd.NextFloat(-1f, 1f), rnd.NextFloat(0.4f, 2.4f));
            // the roll after a big one, swelling and sinking like thunder
            if (big) Wide(l, r, side => Burst(5f, (uint)(113 + side + 9 * take), BandPass(75f, 0.6f), 0.3f).Shape(Wander(Samples(5f), (uint)(116 + side + 9 * take), 2.5f), 0.7f), 0.7f, 0.2f);
            // the room is kept shorter than the roar: a tail left to the room alone thins out into a few ringing notes
            return Master(l, r, big ? 1.8f : 1.6f, big ? 1.6f : 1.2f, big ? 0.3f : 0.25f, big ? 1.6f : 1.2f);
        }

        // A bolt meeting ground or rock: the smack of it, a dull knock, dirt thrown up and pattering back down.
        public static float[] Thud(int take = 0)
        {
            var rnd = new Random(123u + 977u * (uint)take);
            var (l, r) = Blank(0.6f);
            Wide(l, r, side => Burst(0.015f, (uint)(127 + side + 9 * take), HighPass(1200f), 0.0003f), 0.35f);
            Place(l, r, Thump(0.1f, rnd.NextFloat(105f, 125f), 60f, 40f), 0.7f);
            Place(l, r, Burst(0.12f, rnd.NextUInt(), BandPass(rnd.NextFloat(140f, 190f), 0.7f), 0.002f));
            Wide(l, r, side => Noise(Samples(0.25f), (uint)(120 + side + 9 * take)).Sweep(t => 400f + 2200f * math.exp(-14f * t), 0.8f, true).Level().Drive(2f).Shape(t => Hit(t, 0.003f, 0.25f)), 0.4f);
            for (int grain = 0; grain < 9; grain++)
                Place(l, r, Burst(rnd.NextFloat(0.008f, 0.03f), rnd.NextUInt(), BandPass(rnd.NextFloat(1200f, 4000f), 2f), 0.0005f), rnd.NextFloat(0.06f, 0.2f), rnd.NextFloat(-1f, 1f), rnd.NextFloat(0.03f, 0.35f));
            return Master(l, r, 1.4f, 0.4f, 0.15f);
        }

        // The player or the ship taking a knock: a blow, something crunching and metal complaining.
        public static float[] Hurt()
        {
            var (l, r) = Blank(0.8f);
            Wide(l, r, side => Burst(0.02f, (uint)(137 + side), HighPass(1500f), 0.0003f), 0.5f);
            Place(l, r, Thump(0.2f, 150f, 58f, 26f).Drive(3f), 0.7f);
            Wide(l, r, side => Burst(0.13f, (uint)(130 + side), BandPass(700f, 0.5f)).Drive(5f), 0.8f);
            Place(l, r, Ring(0.45f, 310f, 133, 1f, 1.41f, 2.24f, 2.92f, 4.1f), 0.4f, 0.3f);
            Place(l, r, Ring(0.25f, 1240f, 134, 1f, 1.33f, 1.9f, 2.74f), 0.2f, -0.4f, 0.02f);
            Wide(l, r, side => Burst(0.05f, (uint)(135 + side), HighPass(5000f), 0.0005f), 0.3f);
            return Master(l, r, 1.5f, 0.6f, 0.2f);
        }

        // One footfall on gritty ground, as most ground is: the heel coming down, then the ball of the
        // foot, each a dull knock with the sole scuffing and grit crushed under it, thickest at the
        // moment of contact. Most of what a footstep sounds like is that grit, not the knock. Each
        // variant lands a little differently, so a run of them never sounds like one sample repeated.
        public static float[] Step(int variant)
        {
            var rnd = new Random((uint)variant * 7919u + 13u);
            var (l, r) = Blank(0.4f);
            for (int part = 0; part < 2; part++)
            {
                float at = part * rnd.NextFloat(0.09f, 0.14f), weight = 1f - 0.4f * part, seconds = rnd.NextFloat(0.05f, 0.09f), pitch = rnd.NextFloat(500f, 1100f);
                uint seed = rnd.NextUInt(1u << 30);
                Place(l, r, Burst(rnd.NextFloat(0.03f, 0.05f), seed, BandPass(rnd.NextFloat(110f, 180f), 0.8f), 0.003f), 0.75f * weight, 0f, at);
                Wide(l, r, side => Burst(seconds, (uint)(seed + 2 + side), BandPass(pitch, 0.6f), 0.004f), 0.5f * weight, at);
                for (int grain = 0; grain < 12; grain++)
                    Place(l, r, Burst(rnd.NextFloat(0.004f, 0.016f), rnd.NextUInt(), BandPass(rnd.NextFloat(1800f, 7000f), 1.5f), 0.0003f),
                        weight * rnd.NextFloat(0.08f, 0.3f), rnd.NextFloat(-0.8f, 0.8f), at + 0.1f * rnd.NextFloat() * rnd.NextFloat());
            }
            return Master(l, r, 1.2f, 0.25f, 0.1f);
        }

        // A fresh magazine: the catch, the empty one drawn out, the full one pushed up until it locks, then the bolt hauled
        // back and let fly forward on the first round. All of it happens by the right hand, in about a second and a half.
        public static float[] Reload()
        {
            var (l, r) = Blank(1.7f);
            Place(l, r, Clack(3100f, 260, 0.02f), 0.3f, 0.3f);
            Wide(l, r, side => Scrape(0.2f, 2600f, (uint)(262 + 2 * side)), 0.25f, 0.03f);
            Wide(l, r, side => Scrape(0.16f, 2100f, (uint)(267 + 2 * side)), 0.3f, 0.62f);
            Place(l, r, Clack(1500f, 271), 0.7f, 0.3f, 0.78f);
            Wide(l, r, side => Scrape(0.12f, 3200f, (uint)(273 + 2 * side)), 0.35f, 1.1f);
            Place(l, r, Clack(1900f, 277, 0.03f), 0.5f, 0.3f, 1.21f);
            Place(l, r, Clack(1250f, 279, 0.06f), 1f, 0.3f, 1.36f);
            return Master(l, r, 1.3f, 0.3f, 0.12f);
        }

        // The trigger pulled on an empty chamber: the hammer falls on nothing and the spring behind it settles.
        public static float[] DryFire()
        {
            var (l, r) = Blank(0.25f);
            Place(l, r, Clack(2700f, 281, 0.025f), 1f, 0.3f);
            Place(l, r, Clack(3900f, 283, 0.012f), 0.3f, 0.3f, 0.014f);
            return Master(l, r, 1.3f, 0.2f, 0.1f);
        }

        // Digging out: the blade driven into packed earth with a knock, the load torn free of the rest and the loose
        // soil pattering down after it. A hole in the ground gives nothing back, so there is next to no room in it.
        public static float[] Dig(int take = 0)
        {
            var rnd = new Random(281u + 977u * (uint)take);
            var (l, r) = Blank(0.7f);
            Place(l, r, Burst(rnd.NextFloat(0.05f, 0.08f), rnd.NextUInt(), BandPass(rnd.NextFloat(150f, 220f), 0.7f), 0.003f), 0.9f);
            Place(l, r, Thump(0.07f, rnd.NextFloat(130f, 160f), 75f, 40f), 0.5f);
            Wide(l, r, side => Burst(0.03f, (uint)(283 + side + 9 * take), HighPass(1000f), 0.0005f), 0.2f);
            float pull = rnd.NextFloat(0.18f, 0.28f), grit = rnd.NextFloat(450f, 800f);
            // earth gives way a grain at a time: a thick crackle, where metal on metal would hiss
            Wide(l, r, side => Crackle(Samples(pull), (uint)(292 + side + 9 * take), 1200f).Filter(BandPass(grit, 0.7f)).Level().Shape(t => Hit(t, pull / 3f, pull)), 0.7f, 0.04f);
            for (int clod = 0; clod < 12; clod++)
                Place(l, r, Burst(rnd.NextFloat(0.008f, 0.035f), rnd.NextUInt(), BandPass(rnd.NextFloat(300f, 1800f), 1.5f), 0.0005f), rnd.NextFloat(0.05f, 0.2f), rnd.NextFloat(-0.8f, 0.8f),
                    0.12f + pull * rnd.NextFloat() + 0.3f * rnd.NextFloat() * rnd.NextFloat());
            return Master(l, r, 1.3f, 0.15f, 0.08f);
        }

        // A nuclear blast, kilometres off. What arrives is the shock, twice, as the wave thrown back off the ground comes in
        // behind the one through the air, and the distance has taken the top off both. After them comes the roar, swelling and
        // sinking like thunder as the country sends it back from further and further away, for the best part of a quarter of a minute.
        public static float[] Nuke()
        {
            var (l, r) = Blank(14f);
            for (int bang = 0; bang < 2; bang++)
            {
                Wide(l, r, side => Burst(0.12f, (uint)(230 + side + 3 * bang), LowPass(3500f), 0.001f).Drive(3f), 0.9f, 0.45f * bang);
                Place(l, r, Thump(1.4f, 95f, 42f, 5f, 0.006f), 1.2f, 0f, 0.45f * bang);
            }
            Echo(l, r, 236, 1.2f, 6f, 0.5f);
            Wide(l, r, side => Noise(Samples(13.5f), (uint)(238 + side)).Pink().Sweep(t => 160f + 3000f * math.exp(-0.8f * t)).Level().Drive(3f)
                .Shape(Wander(Samples(13.5f), (uint)(240 + side), 1.5f), 0.6f).Shape(t => Hit(t, 0.05f, 18f)), 1f, 0.05f);
            Wide(l, r, side => Noise(Samples(13.5f), (uint)(242 + side)).Filter(BandPass(75f, 0.6f)).Level().Shape(Wander(Samples(13.5f), (uint)(244 + side), 0.8f), 0.8f).Shape(t => Hit(t, 0.5f, 18f)), 0.9f, 0.1f);
            // as with the smaller blasts, the room is kept shorter than the roar
            return Master(l, r, 1.8f, 1.6f, 0.3f, 1.6f);
        }

        // Good news: two bell notes a fifth apart, each a pair a hair out of tune so that it shimmers
        // from side to side, over a soft low note.
        public static float[] Chime()
        {
            var (l, r) = Blank(2.6f);
            for (int note = 0; note < 2; note++)
                Wide(l, r, side => Fm(Samples(2f), _ => (note == 0 ? 587.33f : 880f) * (1f + 0.002f * side), 3.5f, t => 2.2f * math.exp(-4f * t)).Shape(t => Hit(t, 0.004f, 1.6f)), 0.5f, 0.14f * note);
            Wide(l, r, side => Burst(0.03f, (uint)(140 + side), BandPass(2400f, 1f)), 0.15f);
            Place(l, r, Tone(Samples(1f), _ => 293.66f).Shape(t => Hit(t, 0.01f, 0.9f)), 0.25f);
            return Master(l, r, 1.2f, 2.2f, 0.3f);
        }

        // The jump: everything winds up for a second and a half, lets go with a thump and rushes away.
        public static float[] Warp()
        {
            const float jump = 1.5f;
            var (l, r) = Blank(4.8f);
            Wide(l, r, side => Noise(Samples(jump), (uint)(150 + side)).Sweep(t => 150f * math.pow(45f, t / jump), 3f, true).Level()
                .Shape(t => t * t / (jump * jump) * math.saturate((jump - t) / 0.02f)), 0.8f);
            // three saws climbing four octaves, a hertz or two apart on each side
            Wide(l, r, side => Tone(Samples(jump), t => (40f + side) * math.pow(16f, t / jump), Wave.Saw)
                .Add(Tone(Samples(jump), t => 60f * math.pow(16f, t / jump), Wave.Saw), 0.7f)
                .Add(Tone(Samples(jump), t => (80f - side) * math.pow(16f, t / jump), Wave.Saw), 0.5f)
                .Sweep(t => 200f * math.pow(30f, t / jump), 1.5f).Level().Shape(t => t / jump * math.saturate((jump - t) / 0.02f)), 0.6f);
            // the thump is a blow with a rumble of noise rolling on after it: a held note that low is lost on most speakers
            Place(l, r, Thump(1.2f, 95f, 40f, 6f, 0.003f), 1.1f, 0f, jump);
            Wide(l, r, side => Burst(3.3f, (uint)(147 + side), BandPass(70f, 0.6f), 0.01f).Shape(Wander(Samples(3.3f), (uint)(144 + side), 6f), 0.6f), 0.8f, jump);
            Wide(l, r, side => Burst(0.08f, (uint)(153 + side), HighPass(2000f), 0.0005f), 0.8f, jump);
            Wide(l, r, side => Noise(Samples(3.3f), (uint)(156 + side)).Sweep(t => 200f + 5000f * math.exp(-3f * t), 2f, true).Level().Shape(t => Hit(t, 0.01f, 3.3f)), 0.9f, jump);
            Place(l, r, Ring(2.2f, 520f, 159, 1f, 1.5f, 2.76f, 3.4f, 5.4f), 0.15f, 0f, jump);
            return Master(l, r, 1.7f, 1.8f, 0.3f, 1.5f);
        }

        // Foes inbound: three rising blasts on a horn that is a tritone against itself, so it cannot
        // be taken for good news.
        public static float[] Alarm()
        {
            var (l, r) = Blank(2.2f);
            for (int blast = 0; blast < 3; blast++)
            {
                Wide(l, r, side =>
                {
                    float Pitch(float t) => (370f + 150f * math.saturate(t / 0.3f)) * (1f + 0.004f * side);
                    return Tone(Samples(0.45f), Pitch, Wave.Saw).Add(Tone(Samples(0.45f), t => Pitch(t) * 1.4142f, Wave.Saw), 0.8f)
                        .Sweep(t => 500f + 2600f * math.sin(math.PI * math.saturate(t / 0.45f)), 2f).Drive(2f).Shape(t => Adsr(t, 0.02f, 0.1f, 0.8f, 0.36f, 0.08f));
                }, 0.6f, 0.5f * blast);
                Place(l, r, Thump(0.3f, 70f, 55f, 10f, 0.01f), 0.6f, 0f, 0.5f * blast);
            }
            return Master(l, r, 1.3f, 1.2f, 0.25f);
        }

        // The autopilot taking over: a latch, a servo winding up and two soft notes to say it has the ship.
        public static float[] Engage()
        {
            var (l, r) = Blank(1.6f);
            Place(l, r, Thump(0.07f, 120f, 60f, 40f), 0.7f);
            Place(l, r, Ring(0.04f, 1200f, 160, 1f, 2.76f), 0.3f);
            Wide(l, r, side => Fm(Samples(0.5f), t => (220f + side) * math.pow(3f, math.smoothstep(0f, 0.4f, t)), 2f, _ => 1.2f).Shape(t => Adsr(t, 0.3f, 0.1f, 1f, 0.4f, 0.1f)), 0.4f, 0.03f);
            Wide(l, r, side => Noise(Samples(0.5f), (uint)(162 + side)).Sweep(t => 800f * math.pow(3f, t / 0.45f), 8f, true).Level().Shape(t => Adsr(t, 0.25f, 0.1f, 1f, 0.4f, 0.1f)), 0.2f, 0.03f);
            for (int note = 0; note < 2; note++)
                Wide(l, r, side => Fm(Samples(0.7f), _ => (note == 0 ? 659.26f : 987.77f) * (1f + 0.002f * side), 2f, t => 1.2f * math.exp(-6f * t)).Shape(t => Hit(t, 0.004f, 0.6f)), 0.5f, 0.48f + 0.12f * note);
            return Master(l, r, 1.2f, 0.9f, 0.25f);
        }

        // The loops below join up exactly: every tone and every wobble turns a whole number of times
        // in the loop's length and every noise is filtered round its own join.

        // The ship's engine, eight seconds long. Most of it is the roar of what it burns, unsteady as
        // anything turbulent is, over a rumble that both sides share; under that sits a turbine's low
        // drone, with its blades singing and its exhaust hissing. An eighth of a hertz between the
        // sides turns the drone slowly round the listener.
        public static float[] Engine() => Loop(side =>
        {
            int n = Samples(8f);
            float lean = 0.125f * side;
            return Tone(n, _ => 47.25f + lean, Wave.Saw).Add(Tone(n, _ => 71.5f - lean, Wave.Saw), 0.6f).Add(Tone(n, _ => 118.75f + lean, Wave.Saw), 0.4f)
                .Lap(x => x.Filter(LowPass(450f, 0.9f))).Level().Drive(1.5f).Level(0.4f)
                .Add(Hiss(n, 178 + side, x => x.Pink().Filter(BandPass(260f, 0.5f))).Shape(Wander(n, (uint)(166 + side), 5f), 0.45f))
                .Add(Hiss(n, 170, x => x.Brown().Filter(LowPass(110f)).Filter(HighPass(40f))).Shape(Wander(n, 168, 2f), 0.4f), 0.5f)
                .Add(Hiss(n, 172 + side, x => x.Filter(BandPass(495f, 14f))), 0.2f)
                .Add(Hiss(n, 176 + side, x => x.Filter(BandPass(1237f, 20f))), 0.1f)
                .Add(Hiss(n, 174 + side, x => x.Pink().Filter(BandPass(1800f, 0.7f))).Shape(Wander(n, (uint)(164 + side), 12f), 0.3f), 0.25f);
        });

        // The autopilot's drive, two seconds long: a thin, high whine to ride over the engine, made
        // of narrow bands of noise and a wavering tone rather than one bare pitch.
        public static float[] Whine() => Loop(side =>
        {
            int n = Samples(2f);
            float pitch = 1175f + 0.5f * side;
            return Hiss(n, 180 + side, x => x.Filter(BandPass(2350f, 40f)))
                .Add(Hiss(n, 182 + side, x => x.Filter(BandPass(3525f, 50f))), 0.5f)
                .Add(Fm(n, _ => pitch, 6f / pitch, _ => 1.5f), 0.3f)
                .Add(Fm(n, _ => 2f * pitch, 6f / pitch, _ => 2f), 0.15f);
        });

        // Wind, twelve seconds long: a gale that gusts when it likes rather than in time, the gusts
        // reaching both sides together. Within them each band of it swells on its own and a whistle
        // wanders in pitch, as wind does round an edge.
        public static float[] Wind()
        {
            int n = Samples(12f), lead = Rate / 4;
            float[] gust = Wander(n, 198, 0.35f), whistle = Wander(n, 199, 0.5f);
            return Loop(side => Hiss(n, 190 + side, x => x.Pink().Filter(LowPass(250f)).Filter(HighPass(50f))).Shape(Wander(n, (uint)(200 + side), 0.5f), 0.3f)
                .Add(Hiss(n, 192 + side, x => x.Pink().Filter(BandPass(520f, 1.2f))).Shape(Wander(n, (uint)(202 + side), 0.8f), 0.6f), 1.2f)
                .Add(Noise(n, (uint)(194 + side)).Lap(x => x.Sweep(t => 1300f * math.pow(1.5f, whistle[((int)(t * Rate) + n - lead) % n]), 3f, true), lead).Level()
                    .Shape(Wander(n, (uint)(204 + side), 1.2f), 0.8f), 0.5f)
                .Add(Hiss(n, 196 + side, x => x.Filter(HighPass(3000f))), 0.12f)
                // applied twice over, so that from a lull to the top of a gust is well over ten decibels
                .Shape(gust, 0.6f).Shape(gust, 0.6f));
        }

        // Rain, four seconds long: the hiss of it everywhere, drops landing near by and a few fat ones.
        public static float[] Rain() => Loop(side =>
        {
            int n = Samples(4f);
            return Hiss(n, 200 + side, x => x.Filter(HighPass(1200f)).Filter(LowPass(6000f)))
                .Add(Crackle(n, (uint)(202 + side), 160f).Lap(x => x.Filter(BandPass(2800f, 2.5f))).Level(), 0.8f)
                .Add(Crackle(n, (uint)(204 + side), 20f).Lap(x => x.Filter(BandPass(700f, 4f))).Level(), 0.5f)
                .Add(Hiss(n, 206 + side, x => x.Pink().Filter(BandPass(400f, 0.5f))), 0.35f);
        });

        // The jetpack, three seconds long: a torn roar of burning gas, the note of the nozzle in it,
        // fluttering as a flame does, which is never twice the same.
        public static float[] Jetpack() => Loop(side =>
        {
            int n = Samples(3f);
            return Hiss(n, 210 + side, x => x.Pink().Filter(BandPass(700f, 0.6f))).Drive(3f).Lap(x => x.Filter(LowPass(3500f)))
                .Add(Hiss(n, 212 + side, x => x.Brown().Filter(LowPass(140f)).Filter(HighPass(40f))), 0.8f)
                .Add(Hiss(n, 214 + side, x => x.Filter(BandPass(310f, 10f))), 0.3f)
                .Add(Hiss(n, 216 + side, x => x.Filter(HighPass(4500f))), 0.15f)
                .Shape(Wander(n, (uint)(218 + side), 30f), 0.4f).Shape(Wander(n, 220, 4f), 0.15f);
        });

        // The bike, three seconds long, at the middle of its revs: Sounds turns its pitch with the speed. It is a twin,
        // its two cylinders firing unevenly, which puts the lope under the note. Round each bang are the bark of the
        // exhaust, never twice alike, a burst of spent gas on each side, the whine of the drive and the hiss of the intake.
        public static float[] Bike() => Loop(side =>
        {
            int n = Samples(3f);
            return Tone(n, _ => 80f, Wave.Saw).Add(Tone(n, _ => 40f, Wave.Saw), 0.6f)
                .Lap(x => x.Filter(HighPass(90f)).Level().Drive(4f).Filter(Peak(900f, 1.2f, 9f)).Filter(LowPass(3500f)), Rate / 2).Level(0.6f)
                .Shape(Wander(n, (uint)(300 + side), 35f), 0.25f)
                .Add(Hiss(n, 302 + side, x => x.Pink().Filter(BandPass(900f, 0.6f))).Shape(t => math.pow(0.5f + 0.5f * math.cos(Tau * 80f * t), 6f)))
                .Add(Hiss(n, 304 + side, x => x.Filter(BandPass(2300f + 150f * side, 12f))), 0.08f)
                .Add(Hiss(n, 306 + side, x => x.Filter(HighPass(3500f))), 0.05f);
        });

        // A radiation counter, as three loops for Sounds to lay one over another as the dose climbs: a few clicks a
        // second, then fifteen, then seventy-five. Each click is the tube firing into the counter's small speaker,
        // so every one is the same size and has the speaker's ring in it. They come at random, as decay does. The
        // loops are of different lengths, so together they take over a minute to come round.
        public static float[] Geiger(int density)
        {
            int n = Samples(4f - 0.5f * density);
            var clicks = Crackle(n, 250u + (uint)density, 3f * math.pow(5f, density));
            for (int i = 0; i < n; i++) clicks[i] = math.sign(math.abs(clicks[i]));
            return Loop(side => ((float[])clicks.Clone()).Lap(x => x.Filter(Peak(2600f + 500f * side, 3f, 14f)).Filter(HighPass(900f)).Filter(LowPass(9000f)), Rate / 10).Level());
        }
    }

    // How loud the game plays each sound, as a share of full scale, and how many takes of it are kept.
    // Nothing limits the sum on its way out, so every level is set low enough that the busiest
    // fight still comes to less than full scale, which the tests mix and measure. Turning any of
    // these up means turning the rest down.
    public static class Mix
    {
        public const float Music = 0.16f, Engine = 0.16f, Bike = 0.22f, Whine = 0.07f, Wind = 0.18f, Rain = 0.14f, Jetpack = 0.13f, Geiger = 0.12f;
        public const float Chime = 0.21f, Warp = 0.25f, Alarm = 0.21f, Engage = 0.21f, Thud = 0.23f, Hurt = 0.37f, Step = 0.17f, Boom = 0.33f;
        public const float Reload = 0.18f, DryFire = 0.16f, Dig = 0.24f, Nuke = 0.5f;
        public const int Booms = 3, Thuds = 3, Steps = 8, Digs = 5;

        // The weapons: the fast ones lower, since they overlap themselves, and in several takes, so
        // that a burst is not one sample over and over.
        public static readonly float[] Shots = { 0.14f, 0.21f, 0.34f, 0.28f, 0.38f, 0.42f, 0.19f, 0.3f, 0.23f, 0.26f, 0.28f };
        public static readonly int[] Takes = { 4, 1, 1, 4, 3, 2, 3, 1, 1, 6, 1 };
    }
}
