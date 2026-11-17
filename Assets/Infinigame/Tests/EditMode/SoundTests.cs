using System;
using System.Collections.Generic;
using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;
using static Infinigame.Core.Synth;

namespace Infinigame.Tests
{
    // Nobody can listen to a test run, so these measure instead.
    static class Measure
    {
        // The energy at each frequency of a sound, padded with silence to a power of two: bin i sits at
        // i * Rate / (2 * bins) hertz; the bins add up to the sum of the squares of its samples.
        public static double[] Spectrum(float[] sound)
        {
            int n = 1;
            while (n < sound.Length) n *= 2;
            double[] re = new double[n], im = new double[n];
            for (int i = 0; i < sound.Length; i++) re[i] = sound[i];
            // a fast Fourier transform: reorder, then build up from pairs
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n / 2;
                for (; (j & bit) != 0; bit /= 2) j ^= bit;
                j ^= bit;
                if (i < j) (re[i], re[j]) = (re[j], re[i]);
            }
            for (int span = 2; span <= n; span *= 2)
            {
                double angle = -2 * math.PI_DBL / span, stepRe = math.cos(angle), stepIm = math.sin(angle);
                for (int i = 0; i < n; i += span)
                {
                    double turnRe = 1, turnIm = 0;
                    for (int k = 0; k < span / 2; k++)
                    {
                        int a = i + k, b = a + span / 2;
                        double xRe = re[b] * turnRe - im[b] * turnIm, xIm = re[b] * turnIm + im[b] * turnRe;
                        (re[b], im[b], re[a], im[a]) = (re[a] - xRe, im[a] - xIm, re[a] + xRe, im[a] + xIm);
                        (turnRe, turnIm) = (turnRe * stepRe - turnIm * stepIm, turnRe * stepIm + turnIm * stepRe);
                    }
                }
            }
            var energy = new double[n / 2];
            for (int i = 0; i < n / 2; i++) energy[i] = 2 * (re[i] * re[i] + im[i] * im[i]) / n;
            return energy;
        }

        public static double Band(double[] spectrum, float from, float to)
        {
            double hertz = Rate / 2.0 / spectrum.Length, sum = 0;
            for (int i = (int)(from / hertz); i < spectrum.Length && i * hertz < to; i++) sum += spectrum[i];
            return sum;
        }

        // Where the middle of a sound's energy lies, in hertz: low for a rumble, high for a hiss.
        public static float Centroid(double[] spectrum)
        {
            double sum = 0, weighted = 0;
            for (int i = 0; i < spectrum.Length; i++)
            {
                sum += spectrum[i];
                weighted += spectrum[i] * i;
            }
            return (float)(weighted / sum * Rate / 2.0 / spectrum.Length);
        }

        // The strength of one frequency in a sound (Goertzel's method), for when a whole spectrum is too coarse.
        public static double Strength(float[] sound, double hertz, int rate = Rate)
        {
            double coefficient = 2 * math.cos(2 * math.PI_DBL * hertz / rate), one = 0, two = 0;
            foreach (float sample in sound) (one, two) = (sample + coefficient * one - two, one);
            return one * one + two * two - coefficient * one * two;
        }

        public static float Rms(IEnumerable<float> sound) => (float)math.sqrt(sound.Average(sample => (double)sample * sample));

        // How long a sound goes on for: the seconds until it has fallen 40 dB below its peak for good.
        public static float Lasts(float[] sound)
        {
            float peak = sound.Max(math.abs);
            return (Array.FindLastIndex(sound, sample => math.abs(sample) > peak / 100f) + 1f) / Rate;
        }

        // How nearly the swells of one band of a loop come round again inside it: the loudness of that band every
        // twentieth of a second, set against itself a second or more later (a quarter of the way round, in a short
        // loop). Near 1 is a pattern that plays several times a loop; a band that holds steady has none to hear.
        public static float Again(float[] loop, float hertz)
        {
            const int hop = Rate / 20;
            var band = ((float[])loop.Clone()).Lap(x => x.Filter(Biquad.BandPass(hertz, 1f)), Rate / 2);
            var loud = Enumerable.Range(0, loop.Length / hop).Select(k => Rms(new ArraySegment<float>(band, k * hop, hop))).ToArray();
            float mean = loud.Average(), most = 0f;
            double Against(int lag) => loud.Select((level, k) => (double)(level - mean) * (loud[(k + lag) % loud.Length] - mean)).Sum();
            if (math.sqrt(Against(0) / loud.Length) < 0.05f * mean) return 0f;
            for (int lag = math.min(20, loud.Length / 4); lag <= loud.Length / 2; lag++) most = math.max(most, (float)(Against(lag) / Against(0)));
            return most;
        }

        // The tempo a listener would tap, looked for near the one written down: how loud the music is every
        // few milliseconds, where that jumps (a drum or a pick landing) and the beat that best lines those jumps up with themselves.
        public static float Tempo(float[] sound, float near)
        {
            const int hop = 128;
            var jumps = new float[sound.Length / hop];
            float before = 0f;
            for (int k = 0; k < jumps.Length; k++)
            {
                float loud = Rms(new ArraySegment<float>(sound, k * hop, hop));
                jumps[k] = math.max(0f, loud - before);
                before = loud;
            }
            float best = 0f, tempo = 0f;
            for (float guess = near * 0.85f; guess < near * 1.15f; guess += near * 0.002f)
            {
                float score = 0f;
                for (int beats = 1; beats <= 4; beats++)
                    for (int lag = (int)math.round(beats * 60f / guess * Rate / hop), k = lag; k < jumps.Length; k++)
                        score += jumps[k] * jumps[k - lag];
                if (score > best) (best, tempo) = (score, guess);
            }
            return tempo;
        }
    }

    // Every finished sound, made once and kept along with what the tests measure of it.
    static class Heard
    {
        public static readonly Song Song = Riffs.Compose(1);

        static readonly Dictionary<string, Func<float[]>> recipes = new Dictionary<string, Func<float[]>>
        {
            ["chime"] = Chime, ["warp"] = Warp, ["alarm"] = Alarm, ["engage"] = Engage, ["thud"] = () => Thud(), ["hurt"] = Hurt,
            ["boom"] = () => Boom(false), ["big boom"] = () => Boom(true), ["nuke"] = Nuke,
            ["reload"] = Reload, ["dry fire"] = DryFire, ["dig"] = () => Dig(),
            ["charge"] = Charge, ["mind"] = Mind, ["engine"] = Engine, ["beam"] = Beam, ["whine"] = Whine, ["wind"] = Wind, ["rain"] = Rain, ["jetpack"] = Jetpack, ["bike"] = Bike,
            ["geiger 0"] = () => Geiger(0), ["geiger 1"] = () => Geiger(1), ["geiger 2"] = () => Geiger(2),
            ["calm music"] = () => Riffs.Render(Song, false), ["heavy music"] = () => Riffs.Render(Song, true),
            ["another heavy song"] = () => Riffs.Render(Riffs.Compose(2), true),
        };
        static readonly Dictionary<string, float[]> stereo = new Dictionary<string, float[]>(), mono = new Dictionary<string, float[]>();
        static readonly Dictionary<string, double[]> spectra = new Dictionary<string, double[]>();

        static Heard()
        {
            for (int i = 0; i < Mix.Shots.Length; i++)
            {
                int which = i;
                recipes[$"shot {i}"] = () => Shot(which);
                if (i < Mix.Steps) recipes[$"step {i}"] = () => Step(which);
                for (int take = 1; take < Mix.Takes[i]; take++)
                {
                    int again = take;
                    recipes[$"shot {i} take {take}"] = () => Shot(which, again);
                }
            }
            for (int take = 1; take < math.max(Mix.Digs, math.max(Mix.Booms, Mix.Thuds)); take++)
            {
                int again = take;
                if (take < Mix.Digs) recipes[$"dig take {take}"] = () => Dig(again);
                if (take < Mix.Booms) recipes[$"boom take {take}"] = () => Boom(false, again);
                if (take < Mix.Booms) recipes[$"big boom take {take}"] = () => Boom(true, again);
                if (take < Mix.Thuds) recipes[$"thud take {take}"] = () => Thud(again);
            }
        }

        public static readonly string[] Loops = { "charge", "mind", "engine", "beam", "whine", "wind", "rain", "jetpack", "bike", "geiger 0", "geiger 1", "geiger 2", "calm music", "heavy music", "another heavy song" };
        // Sounds that are mostly the silence between their clicks.
        public static readonly string[] Clicks = { "reload", "dry fire", "geiger 0", "geiger 1", "geiger 2" };
        public static readonly int[] Firearms = { 3, 9 };
        public static IEnumerable<string> All => recipes.Keys;
        public static IEnumerable<string> OneShots => recipes.Keys.Except(Loops);
        public static IEnumerable<string> Shots => Enumerable.Range(0, Mix.Shots.Length).Select(i => $"shot {i}");
        public static IEnumerable<string> Shooting => Firearms.SelectMany(voice => Enumerable.Range(0, Mix.Takes[voice]).Select(turn => Take($"shot {voice}", turn)));

        // The take of a sound that comes round on this turn.
        public static string Take(string name, int turn)
        {
            var takes = recipes.Keys.Where(key => key == name || key.StartsWith(name + " take ")).ToArray();
            return takes[turn % takes.Length];
        }
        public static IEnumerable<string> Takes => recipes.Keys.Where(name => name.Contains(" take "));
        public static IEnumerable<string> Steps => Enumerable.Range(0, Mix.Steps).Select(i => $"step {i}");

        public static float[] Stereo(string name) => stereo.TryGetValue(name, out var sound) ? sound : stereo[name] = recipes[name]();

        // Both sides averaged.
        public static float[] Mono(string name)
        {
            if (mono.TryGetValue(name, out var sound)) return sound;
            var both = Stereo(name);
            sound = new float[both.Length / 2];
            for (int i = 0; i < sound.Length; i++) sound[i] = 0.5f * (both[2 * i] + both[2 * i + 1]);
            return mono[name] = sound;
        }

        public static double[] Spectrum(string name) => spectra.TryGetValue(name, out var spectrum) ? spectrum : spectra[name] = Measure.Spectrum(Mono(name));
    }

    // A moment of the game as Sounds and Music would play it: sounds laid over one another at the levels Mix gives them,
    // with nothing to hold the sum down.
    class Moment
    {
        readonly float[] mix;

        public Moment(float seconds) => mix = new float[2 * Samples(seconds)];

        public float Peak => mix.Max(math.abs);

        public void Loop(string name, float gain)
        {
            var sound = Heard.Stereo(name);
            for (int i = 0; i < mix.Length; i++) mix[i] += gain * sound[i % sound.Length];
        }

        public void Hit(string name, float at, float gain)
        {
            var sound = Heard.Stereo(name);
            for (int i = 0, start = 2 * Samples(at); i < sound.Length && start + i < mix.Length; i++) mix[start + i] += gain * sound[i];
        }

        // One after another from `from`, every `apart` seconds, taking the sound's takes in turn.
        public void Every(string name, float from, float apart, float gain, int times = int.MaxValue)
        {
            for (int turn = 0; turn < times && from + turn * apart < mix.Length / 2f / Rate; turn++) Hit(Heard.Take(name, turn), from + turn * apart, gain);
        }
    }

    public class SoundTests
    {
        [TestCaseSource(typeof(Heard), nameof(Heard.All))]
        public void IsLoudEnoughNeverClipsAndSitsOnZero(string name)
        {
            var sound = Heard.Stereo(name);
            Assert.AreEqual(0, sound.Length % 2, "two sides");
            bool sane = true;
            float peak = 0f;
            double sum = 0;
            foreach (float sample in sound)
            {
                sane &= !float.IsNaN(sample) && !float.IsInfinity(sample);
                peak = math.max(peak, math.abs(sample));
                sum += sample;
            }
            Assert.IsTrue(sane, "not a number");
            Assert.That(peak, Is.InRange(0.5f, 1f), "peak");
            // a sound that is mostly the silence between its clicks is as loud as its loudest twentieth of a second
            int span = Heard.Clicks.Contains(name) ? Rate / 10 : sound.Length;
            Assert.Greater(Enumerable.Range(0, sound.Length / span).Max(k => Measure.Rms(new ArraySegment<float>(sound, k * span, span))), 0.05f, "loudness");
            Assert.Less(math.abs(sum / sound.Length), 0.002, "standing offset");
        }

        [TestCaseSource(typeof(Heard), nameof(Heard.All))]
        public void TheTwoSidesDiffer(string name)
        {
            var sound = Heard.Stereo(name);
            double apart = 0, all = 0;
            for (int i = 0; i < sound.Length; i += 2)
            {
                apart += (sound[i] - sound[i + 1]) * (sound[i] - sound[i + 1]);
                all += sound[i] * sound[i] + sound[i + 1] * sound[i + 1];
            }
            Assert.Greater(apart / all, 0.05);
        }

        [TestCaseSource(typeof(Heard), nameof(Heard.OneShots))]
        public void AOneShotStartsAndEndsInSilence(string name)
        {
            var sound = Heard.Stereo(name);
            foreach (int i in new[] { 0, 1, sound.Length - 2, sound.Length - 1 }) Assert.Less(math.abs(sound[i]), 0.01f);
        }

        // Across the join, neither the samples nor their slope may jump further than they do somewhere inside the loop.
        [TestCaseSource(typeof(Heard), nameof(Heard.Loops))]
        public void ALoopRunsIntoItsOwnStart(string name)
        {
            var sound = Heard.Stereo(name);
            int n = sound.Length / 2;
            for (int side = 0; side < 2; side++)
            {
                float At(int i) => sound[2 * ((i + n) % n) + side];
                float step = 0f, bend = 0f;
                for (int i = 2; i < n - 1; i++)
                {
                    step = math.max(step, math.abs(At(i) - At(i - 1)));
                    bend = math.max(bend, math.abs(At(i + 1) - 2f * At(i) + At(i - 1)));
                }
                Assert.LessOrEqual(math.abs(At(0) - At(-1)), step, "step");
                Assert.LessOrEqual(math.abs(At(1) - 2f * At(0) + At(-1)), bend, "bend at the first sample");
                Assert.LessOrEqual(math.abs(At(0) - 2f * At(-1) + At(-2)), bend, "bend at the last sample");
            }
        }

        // A tone that fits its loop exactly has all its strength at one multiple of the loop's own
        // pitch and none at the next one along. One that does not fit is smeared over both, and
        // its wave is cut short at the join, which the test above cannot tell from one more edge of a saw.
        [TestCase("engine", 47.25f)]
        [TestCase("whine", 1175f)]
        [TestCase("bike", 80f)]
        public void AToneInALoopTurnsAWholeNumberOfTimes(string name, float hertz)
        {
            var left = Heard.Stereo(name).Where((_, i) => i % 2 == 0).ToArray();
            float apart = (float)Rate / left.Length;
            Assert.Greater(Measure.Strength(left, hertz), 100 * Measure.Strength(left, hertz + apart));
            Assert.Greater(Measure.Strength(left, hertz), 100 * Measure.Strength(left, hertz - apart));
        }

        // Brightness in octaves against length in doublings makes a map on which no two weapons may sit close together.
        [Test]
        public void EveryWeaponSoundsUnlikeTheOthers()
        {
            var where = Heard.Shots.Select(name => math.log2(new float2(Measure.Centroid(Heard.Spectrum(name)), Measure.Lasts(Heard.Mono(name))))).ToArray();
            for (int a = 0; a < where.Length; a++)
                for (int b = a + 1; b < where.Length; b++)
                    Assert.Greater(math.distance(where[a], where[b]), 0.5f, $"shots {a} and {b}");
        }

        // Nothing fired has the low end of something blowing up, and only what is built to roll on outlasts a blast: the
        // sniper rifle's report and the motor of the torpedo, which is itself the biggest thing there is short of an
        // explosion. The nuclear blast is as far beyond the big one as that is beyond the small.
        [Test]
        public void ExplosionsOutlastAndUndercutEveryShot()
        {
            float Lasts(string name) => Measure.Lasts(Heard.Mono(name));
            double Low(string name) => Measure.Band(Heard.Spectrum(name), 0f, 150f);
            for (int voice = 0; voice < Mix.Shots.Length; voice++)
            {
                string shot = $"shot {voice}";
                if (voice != 5 && voice != 10) Assert.Greater(Lasts("boom"), Lasts(shot), shot);
                Assert.Greater(Low(voice == 10 ? "big boom" : "boom"), Low(shot), shot);
            }
            Assert.Greater(Lasts("big boom"), 1.5f * Lasts("boom"));
            Assert.Greater(Low("big boom"), 1.5 * Low("boom"));
            Assert.Greater(Lasts("nuke"), 3f * Lasts("big boom"));
            Assert.Greater(Low("nuke"), 2.5 * Low("big boom"));
        }

        // A firearm is loudest in its first thousandths of a second, bright there with the bullet's crack, and what
        // rolls back off the land afterwards is duller than the shot.
        [TestCaseSource(typeof(Heard), nameof(Heard.Shooting))]
        public void AGunshotCracksAtOnceAndItsReportRollsAwayDuller(string name)
        {
            var shot = Heard.Mono(name);
            float[] Part(float from, float to) => shot.Skip(Samples(from)).Take(Samples(to - from)).ToArray();
            Assert.Less(Array.FindIndex(shot, sample => math.abs(sample) == shot.Max(math.abs)), Samples(0.01f), "the loudest moment");
            var first = Measure.Spectrum(Part(0f, 0.01f));
            Assert.Greater(Measure.Band(first, 4000f, Rate), 0.05 * first.Sum(), "the crack");
            Assert.Greater(Measure.Centroid(Measure.Spectrum(Part(0f, 0.02f))), 1.3f * Measure.Centroid(Measure.Spectrum(Part(0.12f, 0.3f))), "the report");
        }

        [Test]
        public void PoweredImpactsLeaveRoomForTheNextPunch()
        {
            var impact = Heard.Mono("shot 4");
            float repeat = Combat.Arms[0].period;
            Assert.Less(Measure.Rms(impact.Skip(Samples(repeat)).Take(Samples(0.2f))), 0.025f * impact.Max(math.abs), "No pump action or shell reload after a powered punch");
            Assert.Greater(Measure.Band(Heard.Spectrum("shot 4"), 60, 300), 0.1 * Heard.Spectrum("shot 4").Sum());
        }

        [Test]
        public void RailDischargeDiesAwayBeforeTheGauntletRecharges()
        {
            Assert.Less(Heard.Mono("shot 5").Length / (float)Rate, Combat.Arms[3].period);
            Assert.Greater(Measure.Lasts(Heard.Mono("shot 5")), Measure.Lasts(Heard.Mono("shot 4")));
        }

        // Ten rounds a second: a round is as good as gone when the next one goes, so a burst is no louder than its rounds.
        [Test]
        public void TheRifleIsOverBeforeItsNextRound()
        {
            var burst = new float[Samples(1.5f)];
            for (int round = 0; round < 10; round++)
            {
                var one = Heard.Mono(Heard.Take("shot 9", round));
                Assert.Less(Measure.Lasts(one), 0.25f);
                Assert.Less(Measure.Rms(one.Skip(Samples(0.1f)).Take(Samples(0.02f))), 0.03f * one.Max(math.abs), "what is left when the next round goes");
                for (int i = 0; i < one.Length; i++) burst[Samples(0.1f * round) + i] += one[i];
            }
            Assert.Less(burst.Max(math.abs), 1.25f * Heard.Mono("shot 9").Max(math.abs));
        }

        // Three things happen, with quiet between them: the magazine comes out, the next goes in and the bolt is
        // worked. The bolt going home is the loudest of them, and the whole takes about a second and a half.
        [Test]
        public void AReloadIsMagazineOutMagazineInAndTheBoltHome()
        {
            var reload = Heard.Mono("reload");
            float Loudest(float from, float to) => reload.Skip(Samples(from)).Take(Samples(to - from)).Max(math.abs);
            Assert.That(Measure.Lasts(reload), Is.InRange(1.3f, 1.7f));
            Assert.Greater(Loudest(0f, 0.3f), 0.2f, "magazine out");
            Assert.Less(Loudest(0.35f, 0.55f), 0.02f, "a pause");
            Assert.Greater(Loudest(0.6f, 0.9f), 0.4f, "magazine in");
            Assert.Less(Loudest(0.95f, 1.05f), 0.02f, "a pause");
            Assert.Greater(Loudest(1.3f, 1.5f), math.max(Loudest(0f, 1.3f), 0.6f), "the bolt home");
        }

        [Test]
        public void ADryClickIsShortAndAllMetal()
        {
            Assert.Less(Measure.Lasts(Heard.Mono("dry fire")), 0.2f);
            Assert.Greater(Measure.Centroid(Heard.Spectrum("dry fire")), 2000f);
            Assert.Less(Measure.Band(Heard.Spectrum("dry fire"), 0f, 250f), 0.05 * Heard.Spectrum("dry fire").Sum());
        }

        // Earth is duller than anything a boot does on top of it, and a blow has to be over in time for the next click.
        [Test]
        public void DiggingIsDullerThanWalkingAndSoonOver()
        {
            for (int take = 0; take < Mix.Digs; take++)
            {
                string dig = Heard.Take("dig", take);
                Assert.Less(Measure.Lasts(Heard.Mono(dig)), 0.6f, dig);
                foreach (string step in Heard.Steps) Assert.Less(Measure.Centroid(Heard.Spectrum(dig)), Measure.Centroid(Heard.Spectrum(step)), $"{dig} against {step}");
            }
        }

        // From far off the shock is heard twice, the second off the ground not half a second behind the first, and the
        // roar after them does not simply die away: somewhere the land sends it back and it swells again.
        [Test]
        public void ANuclearBlastArrivesTwiceAndRollsOn()
        {
            var nuke = Heard.Mono("nuke");
            float Loud(float from, float to) => Measure.Rms(nuke.Skip(Samples(from)).Take(Samples(to - from)));
            Assert.Greater(Loud(0.45f, 0.5f), 1.25f * Loud(0.3f, 0.45f), "the second shock");
            Assert.IsTrue(Enumerable.Range(2, 12).Any(k => Loud(0.5f * k + 0.5f, 0.5f * k + 1f) > 1.1f * Loud(0.5f * k, 0.5f * k + 0.5f)), "a swell");
            Assert.Greater(Loud(8f, 9f), 0.01f, "still rolling eight seconds on");
        }

        // The counter's three loops: clicks all of a size, coming at random as decay does and not in time, about three
        // a second in the first and some five times as many in each of the next.
        [Test]
        public void TheCounterClicksAtRandomAllAlikeAndFasterInEachLoop()
        {
            float before = 0f;
            for (int density = 0; density < 3; density++)
            {
                var left = Heard.Stereo($"geiger {density}").Where((_, i) => i % 2 == 0).ToArray();
                float peak = left.Max(math.abs);
                // a click begins where the sound passes a third of its peak after a thousandth of a second below it
                var clicks = Enumerable.Range(48, left.Length - 48).Where(i => math.abs(left[i]) > peak / 3f && Enumerable.Range(i - 48, 48).All(j => math.abs(left[j]) <= peak / 3f)).ToArray();
                float rate = clicks.Length * (float)Rate / left.Length;
                Assert.That(rate, Is.InRange(1.5f * math.pow(5f, density), 6f * math.pow(5f, density)), $"clicks a second in loop {density}");
                Assert.Greater(rate, 2.5f * before, $"loop {density} against the one before");
                before = rate;
                foreach (int click in clicks) Assert.Greater(left.Skip(click).Take(96).Max(math.abs), 0.8f * peak, "a small click");
                // the gaps between clicks that come at random are as uneven as they are long
                var gaps = clicks.Skip(1).Zip(clicks, (next, last) => (float)(next - last)).ToArray();
                float mean = gaps.Average();
                Assert.That(math.sqrt(gaps.Average(gap => (gap - mean) * (gap - mean))) / mean, Is.InRange(0.5f, 1.6f), $"unevenness of loop {density}");
            }
        }

        [Test]
        public void FootstepsAreNotOneSampleRepeated()
        {
            for (int a = 0; a < Mix.Steps; a++)
                for (int b = a + 1; b < Mix.Steps; b++)
                    Assert.Greater(Heard.Mono($"step {a}").Zip(Heard.Mono($"step {b}"), (x, y) => math.abs(x - y)).Max(), 0.1f, $"steps {a} and {b}");
        }

        // A take is the same thing happening again: about as bright and as long, but not the same samples. No two blows
        // land alike, so there is some room in that: three quarters of an octave, where different sounds sit further apart.
        [TestCaseSource(typeof(Heard), nameof(Heard.Takes))]
        public void ATakeIsTheSameSoundMadeAgain(string name)
        {
            string first = name.Substring(0, name.IndexOf(" take"));
            Assert.Greater(Heard.Mono(first).Zip(Heard.Mono(name), (x, y) => math.abs(x - y)).Max(), 0.1f);
            Assert.AreEqual(0f, math.log2(Measure.Centroid(Heard.Spectrum(name)) / Measure.Centroid(Heard.Spectrum(first))), 0.75f, "octaves brighter");
            Assert.AreEqual(Measure.Lasts(Heard.Mono(first)), Measure.Lasts(Heard.Mono(name)), 0.3f * Measure.Lasts(Heard.Mono(first)), "seconds long");
        }

        // Few speakers answer below 60 Hz, and what they cannot play still takes up room in the mix.
        [TestCaseSource(typeof(Heard), nameof(Heard.All))]
        public void LittleOfItIsTooLowForASmallSpeaker(string name) =>
            Assert.Less(Measure.Band(Heard.Spectrum(name), 0f, 60f), Heard.Spectrum(name).Sum() / 3);

        // Above 250 Hz is where a laptop plays and the ear listens: a footfall is mostly grit and scuff up there, and
        // an explosion has to be more than its own bass.
        [Test]
        public void FootstepsAndExplosionsAreHeardAboveTheirBass()
        {
            double Above(string name) => Measure.Band(Heard.Spectrum(name), 250f, Rate) / Heard.Spectrum(name).Sum();
            foreach (string step in Heard.Steps) Assert.Greater(Above(step), 0.5, step);
            foreach (string boom in Heard.All.Where(name => name.Contains("boom"))) Assert.Greater(Above(boom), 0.25, boom);
        }

        // Each of the drone's three saws and its overtones, as far up as the filter over them lets any through.
        [Test]
        public void TheEngineIsMoreRoarThanDrone()
        {
            var left = Heard.Stereo("engine").Where((_, i) => i % 2 == 0).ToArray();
            double drone = 0;
            foreach (float saw in new[] { 47.25f, 71.5f, 118.75f })
                for (int overtone = 1; overtone * saw < 1000f; overtone++) drone += 2 * Measure.Strength(left, overtone * saw) / left.Length;
            Assert.Less(drone, 0.4 * left.Sum(sample => (double)sample * sample));
        }

        // Gusts, turbulence and flame come when they like: a swell that returns in time inside the loop is heard as a pattern.
        [TestCase("wind", 520f)]
        [TestCase("wind", 1300f)]
        [TestCase("engine", 1800f)]
        [TestCase("jetpack", 700f)]
        public void NoSwellComesRoundAgainInsideALoop(string name, float hertz) => Assert.Less(Measure.Again(Heard.Mono(name), hertz), 0.55f);

        // A flame flutters at no one rate, where a motor pulses at its own: of all the whole numbers of flutters a
        // second in the jetpack's loudness, taken every millisecond, no one of them holds much of the fluttering.
        [Test]
        public void TheJetpackFluttersAtNoOneRate()
        {
            var jetpack = Heard.Mono("jetpack");
            var loud = Enumerable.Range(0, jetpack.Length / 48).Select(k => Measure.Rms(new ArraySegment<float>(jetpack, k * 48, 48))).ToArray();
            var rates = Enumerable.Range(5, 56).Select(hertz => Measure.Strength(loud, hertz, 1000)).ToArray();
            Assert.Less(rates.Max(), 0.3 * rates.Sum());
        }

        [Test]
        public void TheWindGustsAndIsLongEnoughNotToBeKnownByThem()
        {
            var wind = Heard.Mono("wind");
            Assert.GreaterOrEqual(wind.Length, 10 * Rate);
            var loud = Enumerable.Range(0, wind.Length / (Rate / 20)).Select(k => Measure.Rms(new ArraySegment<float>(wind, k * (Rate / 20), Rate / 20))).ToArray();
            // from a lull to the top of a gust is a good ten decibels
            Assert.Greater(loud.Max(), 3f * loud.Min());
        }

        // The loud end of what the game can ask for at once: the heavy song, the engine and the wind flat out, the lance
        // and the missiles both away, three raiders firing, the ship being hit and three raiders blowing up close by,
        // two of them a moment apart. Nothing limits the sum, so it has to fit as it is.
        [Test]
        public void TheBusiestFightStaysUnderFullScale()
        {
            var fight = new Moment(12f);
            fight.Loop("heavy music", Mix.Music);
            fight.Loop("engine", Mix.Engine);
            fight.Loop("wind", Mix.Wind);
            fight.Every("shot 2", 0.5f, 0.8f, Mix.Shots[2]);
            fight.Every("shot 1", 0.9f, 1.1f, Mix.Shots[1]);
            for (int raider = 0; raider < 3; raider++)
                for (float at = 1f + 0.5f * raider; at < 12f; at += 2f) fight.Every("shot 6", at, 0.14f, 0.5f * Mix.Shots[6], 3);
            fight.Every("hurt", 2f, 1.3f, Mix.Hurt);
            fight.Hit("big boom", 3f, Mix.Boom);
            fight.Every("big boom", 7.2f, 0.3f, Mix.Boom, 2);
            Assert.Less(fight.Peak, 1f);
        }

        // The same on foot with the rifle: the heavy song, rain and wind, the counter crackling, magazines of thirty
        // emptied at ten rounds a second with every round landing somewhere, a dry click and a reload between them,
        // four drones spitting fire, the player hit again and again, footsteps, and drones bursting, two of them a moment apart.
        [Test]
        public void AFirefightWithTheRifleStaysUnderFullScale()
        {
            var fight = new Moment(12f);
            fight.Loop("heavy music", Mix.Music);
            fight.Loop("rain", Mix.Rain);
            fight.Loop("wind", 0.3f * Mix.Wind);
            for (int loop = 0; loop < 3; loop++) fight.Loop($"geiger {loop}", Mix.Geiger);
            foreach (float magazine in new[] { 0.5f, 5.7f, 10.9f })
            {
                fight.Every("shot 9", magazine, 0.1f, Mix.Shots[9], 30);
                fight.Every("thud", magazine + 0.05f, 0.1f, 0.25f * Mix.Thud, 30);
                fight.Hit("dry fire", magazine + 3.05f, Mix.DryFire);
                fight.Hit("reload", magazine + 3.3f, Mix.Reload);
            }
            fight.Every("shot 7", 0.8f, 0.45f, 0.5f * Mix.Shots[7]);
            fight.Every("hurt", 1.5f, 1.6f, Mix.Hurt);
            fight.Every("step 0", 0.2f, 0.33f, Mix.Step);
            fight.Hit("boom", 4f, 0.9f * Mix.Boom);
            fight.Every("boom", 9f, 0.4f, 0.9f * Mix.Boom, 2);
            Assert.Less(fight.Peak, 1f);
        }

        // And with the torpedo: a dogfight with the cannon held down, the counter crackling, the launch, then the blast
        // coming back over all of it at its loudest as a raider goes up beside the ship.
        [Test]
        public void TheTorpedoAndItsBlastStayUnderFullScale()
        {
            var fight = new Moment(16f);
            fight.Loop("heavy music", Mix.Music);
            fight.Loop("engine", Mix.Engine);
            fight.Loop("wind", Mix.Wind);
            for (int loop = 0; loop < 3; loop++) fight.Loop($"geiger {loop}", Mix.Geiger);
            fight.Every("shot 0", 0.5f, 0.09f, Mix.Shots[0]);
            for (int raider = 0; raider < 3; raider++)
                for (float at = 1f + 0.5f * raider; at < 16f; at += 2f) fight.Every("shot 6", at, 0.14f, 0.5f * Mix.Shots[6], 3);
            fight.Every("hurt", 2f, 1.3f, Mix.Hurt);
            fight.Hit("shot 10", 2f, Mix.Shots[10]);
            fight.Hit("nuke", 6f, Mix.Nuke);
            fight.Hit("big boom", 6.1f, Mix.Boom);
            fight.Hit("big boom take 1", 9f, Mix.Boom);
            Assert.Less(fight.Peak, 1f);
        }
    }

    public class MusicTests
    {
        [Test]
        public void ASeedAlwaysGivesTheSameSongAndAnotherSeedADifferentOne()
        {
            Assert.IsTrue(Heard.Stereo("calm music").SequenceEqual(Riffs.Render(Riffs.Compose(1), false)));
            Assert.IsFalse(Heard.Stereo("heavy music").SequenceEqual(Heard.Stereo("another heavy song")));
        }

        [Test]
        public void SongsAreSixteenBarsLongTunedLowInADarkModeAndNoTwoAlike()
        {
            var songs = Enumerable.Range(1, 40).Select(seed => Riffs.Compose((uint)seed)).ToList();
            foreach (var song in songs)
            {
                Assert.AreEqual(16 * 16 * song.Step, song.Length);
                Assert.That(song.Tempo, Is.InRange(140f, 190f));
                // from B to D below the E a guitar's lowest string is usually tuned to
                Assert.That(song.Root, Is.InRange(35, 38));
                CollectionAssert.Contains(new[] { "phrygian", "locrian", "harmonic minor" }, song.Mode);
                foreach (var note in new[] { song.Heavy, song.Calm }.SelectMany(part => part.Rhythm.Concat(part.Leads[0]).Concat(part.Leads[1])))
                {
                    Assert.Greater(note.steps, 0);
                    Assert.That(note.step, Is.InRange(0, 256 - note.steps), "notes stay inside the loop");
                }
                for (int bar = 0; bar < 16; bar++)
                {
                    Assert.IsTrue(song.Heavy.Rhythm.Any(note => note.step / 16 == bar), $"heavy bar {bar}");
                    Assert.IsTrue(song.Calm.Rhythm.Any(note => note.step / 16 == bar), $"calm bar {bar}");
                }
                // the calm arrangement takes its snare at half the pace and carries a melody for two guitars a third apart throughout
                Assert.Greater(song.Heavy.Drums.Count(hit => hit.drum == Drum.Snare), 1.5f * song.Calm.Drums.Count(hit => hit.drum == Drum.Snare));
                Assert.AreEqual(48, song.Calm.Leads[0].Count);
                foreach (var (low, high) in song.Calm.Leads[0].Zip(song.Calm.Leads[1], (low, high) => (low, high)))
                {
                    Assert.AreEqual(low.step, high.step);
                    Assert.That(high.pitch - low.pitch, Is.InRange(3, 4));
                }
                // more than a short loop: the riff comes back after the tremolo run, which is something else, as is the breakdown
                string Section(int index) => string.Join(" ", song.Heavy.Rhythm.Where(note => note.step / 64 == index).Select(note => $"{note.step % 64}:{note.pitch}:{note.strings.Length}:{note.muted}"));
                Assert.AreEqual(Section(0), Section(2));
                Assert.AreNotEqual(Section(0), Section(1));
                Assert.AreNotEqual(Section(0), Section(3));
                Assert.AreNotEqual(Section(1), Section(3));
            }
            Assert.AreEqual(3, songs.Select(song => song.Mode).Distinct().Count());
            Assert.Greater(songs.Select(song => song.Step).Distinct().Count(), 15);
            Assert.Greater(songs.Select(song => string.Join(" ", song.Heavy.Rhythm.Select(note => $"{note.step}:{note.pitch}:{note.muted}"))).Distinct().Count(), 35);
        }

        [Test]
        public void TheTwoArrangementsKeepTheSameTimeAndTheHeavyOneIsLouder()
        {
            Assert.AreEqual(2 * Heard.Song.Length, Heard.Stereo("calm music").Length);
            Assert.AreEqual(2 * Heard.Song.Length, Heard.Stereo("heavy music").Length);
            Assert.Greater(Measure.Rms(Heard.Mono("heavy music")), 1.5f * Measure.Rms(Heard.Mono("calm music")));
        }

        [TestCase("calm music", 1u)]
        [TestCase("heavy music", 1u)]
        [TestCase("another heavy song", 2u)]
        public void TheMusicKeepsTheTempoItWasWrittenAt(string name, uint seed)
        {
            float written = Riffs.Compose(seed).Tempo;
            Assert.AreEqual(written, Measure.Tempo(Heard.Mono(name), written), 0.015f * written);
        }

        [Test]
        public void AKickDrumIsMostlyLow()
        {
            var spectrum = Measure.Spectrum(Riffs.Kick());
            Assert.Greater(Measure.Band(spectrum, 0f, 300f), 0.75 * spectrum.Sum());
        }
    }

    // The parts the sounds are built from.
    public class SynthTests
    {
        [TestCase(EncounterCue.Emitter)]
        [TestCase(EncounterCue.Threat)]
        [TestCase(EncounterCue.Collapse)]
        [TestCase(EncounterCue.Footfall)]
        [TestCase(EncounterCue.ShellHit)]
        public void EncounterCuesHaveDistinctBoundedTakes(EncounterCue cue)
        {
            var a = Encounter(cue, 0);
            var b = Encounter(cue, 1);
            Assert.IsTrue(a.All(value => math.isfinite(value) && math.abs(value) <= 1));
            Assert.Greater(Measure.Rms(a), 0.015f);
            Assert.AreEqual(0, a.Length % 2);
            Assert.IsFalse(a.SequenceEqual(b));
        }

        [TestCase(61.74f, 600f)]
        [TestCase(61.74f, 4500f)]
        [TestCase(110f, 4500f)]
        [TestCase(440f, 4500f)]
        [TestCase(987.77f, 9000f)]
        public void APluckedStringIsInTune(float hertz, float tone)
        {
            var note = Pluck(2 * Rate, hertz, 5f, tone, 3);
            // the strongest pitch within a third of a semitone either way, looked for a cent at a time
            int cents = Enumerable.Range(-30, 61).OrderByDescending(offset => Measure.Strength(note, hertz * math.pow(2.0, offset / 1200.0))).First();
            Assert.That(cents, Is.InRange(-4, 4));
        }

        [Test]
        public void AMutedStringIsShorterAndDullerThanAnOpenOne()
        {
            float[] muted = Pluck(Rate, 65.4f, 0.25f, 600f, 3), open = Pluck(Rate, 65.4f, 5f, 4500f, 3);
            Assert.Less(Measure.Lasts(muted), 0.4f);
            Assert.Greater(Measure.Lasts(open), 0.9f);
            Assert.Less(Measure.Centroid(Measure.Spectrum(muted)), 0.5f * Measure.Centroid(Measure.Spectrum(open)));
        }

        // How much of a steady tone a filter lets through.
        static float Through(Biquad filter, float hertz)
        {
            var tone = Tone(Rate / 2, _ => hertz);
            return Measure.Rms(tone.Filter(filter).Skip(Rate / 4)) / Measure.Rms(Tone(Rate / 4, _ => hertz));
        }

        [Test]
        public void FiltersPassAndStopWhatTheySay()
        {
            Assert.AreEqual(1f, Through(Biquad.LowPass(1000f), 100f), 0.02f);
            Assert.AreEqual(0.707f, Through(Biquad.LowPass(1000f), 1000f), 0.02f);
            Assert.Less(Through(Biquad.LowPass(1000f), 8000f), 0.02f);
            Assert.AreEqual(1f, Through(Biquad.HighPass(1000f), 8000f), 0.02f);
            Assert.Less(Through(Biquad.HighPass(1000f), 100f), 0.02f);
            Assert.AreEqual(1f, Through(Biquad.BandPass(1000f, 4f), 1000f), 0.02f);
            Assert.Less(Through(Biquad.BandPass(1000f, 4f), 250f), 0.1f);
            Assert.Less(Through(Biquad.BandPass(1000f, 4f), 4000f), 0.1f);
            // six decibels is twice the size
            Assert.AreEqual(2f, Through(Biquad.Peak(1000f, 1f, 6f), 1000f), 0.04f);
            Assert.AreEqual(1f, Through(Biquad.Peak(1000f, 1f, 6f), 50f), 0.02f);
            // a filter built for twice the rate sits where it was asked to there
            Assert.AreEqual(0.707f, Measure.Rms(Tone(Rate, _ => 500f).Filter(Biquad.LowPass(1000f, 0.707f, 2 * Rate)).Skip(Rate / 2)) / Measure.Rms(Tone(Rate / 2, _ => 500f)), 0.02f);
        }

        [Test]
        public void ASweptFilterFollowsItsCorner()
        {
            float Left(float hertz, bool band) => Measure.Rms(Tone(Rate / 2, _ => hertz).Sweep(_ => 1000f, 4f, band).Skip(Rate / 4)) / 0.707f;
            Assert.AreEqual(1f, Left(100f, false), 0.02f);
            Assert.Less(Left(8000f, false), 0.02f);
            Assert.AreEqual(1f, Left(1000f, true), 0.02f);
            Assert.Less(Left(100f, true), 0.1f);
        }

        // An octave band two octaves up holds four times the power in white noise, the same in pink and a quarter in brown.
        [Test]
        public void NoiseIsTheColourItSays()
        {
            double Tilt(float[] noise)
            {
                var spectrum = Measure.Spectrum(noise);
                return 10 * math.log10(Measure.Band(spectrum, 2000f, 4000f) / Measure.Band(spectrum, 500f, 1000f));
            }
            Assert.AreEqual(6.0, Tilt(Noise(1 << 17, 5)), 1.0);
            Assert.AreEqual(0.0, Tilt(Noise(1 << 17, 5).Pink()), 1.0);
            Assert.AreEqual(-6.0, Tilt(Noise(1 << 17, 5).Brown()), 1.0);
        }

        // A saw has overtones without end; those above half the sample rate come back down as notes that were never played.
        [Test]
        public void ASawFoldsBackFarLessThanAPlainOne()
        {
            const int n = 1 << 15, bin = 1429;
            float hertz = bin * (float)Rate / n;
            double Stray(float[] saw)
            {
                var spectrum = Measure.Spectrum(saw);
                return spectrum.Where((_, i) => i % bin != 0).Sum() / spectrum.Sum();
            }
            var plain = new float[n];
            for (int i = 0; i < n; i++) plain[i] = 2f * (i * hertz / Rate % 1f) - 1f;
            Assert.Greater(Stray(plain), 0.02);
            Assert.Less(Stray(Tone(n, _ => hertz, Wave.Saw)), 0.2 * Stray(plain));
        }

        [Test]
        public void OverdriveAddsOvertonesAndABiasAddsEvenOnes()
        {
            float[] clean = Tone(Rate, _ => 200f), driven = Tone(Rate, _ => 200f).Drive(10f), lopsided = Tone(Rate, _ => 200f).Drive(2f, 0.5f);
            Assert.Less(Measure.Strength(clean, 600), 1e-4 * Measure.Strength(clean, 200));
            Assert.Greater(Measure.Strength(driven, 600), 0.05 * Measure.Strength(driven, 200));
            Assert.Less(Measure.Strength(driven, 400), 1e-4 * Measure.Strength(driven, 200));
            Assert.Greater(Measure.Strength(lopsided, 400), 0.005 * Measure.Strength(lopsided, 200));
            Assert.LessOrEqual(driven.Max(math.abs), 1f);
        }

        [Test]
        public void TheLimiterLeavesQuietAloneAndNeverPassesFullScale()
        {
            for (float x = -0.5f; x <= 0.5f; x += 0.05f) Assert.AreEqual(x, Limit(x));
            for (float x = 0.5f; x < 50f; x *= 1.1f)
            {
                Assert.LessOrEqual(Limit(x), 1f);
                Assert.Greater(Limit(x * 1.1f), Limit(x) - 1e-6f, "louder in is never quieter out");
                Assert.AreEqual(-Limit(x), Limit(-x));
            }
        }

        [Test]
        public void AnEnvelopeRisesThenFallsAsAsked()
        {
            Assert.AreEqual(0.5f, Hit(0.005f, 0.01f, 1f), 1e-5f);
            Assert.AreEqual(1f, Hit(0.01f, 0.01f, 1f), 1e-5f);
            // sixty decibels is a thousandth
            Assert.AreEqual(0.001f, Hit(1.01f, 0.01f, 1f), 1e-4f);
            Assert.AreEqual(1f, Adsr(0.1f, 0.1f, 0.2f, 0.6f, 1f, 0.5f), 1e-5f);
            Assert.AreEqual(0.6f, Adsr(0.95f, 0.1f, 0.2f, 0.6f, 1f, 0.5f), 0.01f);
            Assert.AreEqual(0.3f, Adsr(1.25f, 0.1f, 0.2f, 0.6f, 1f, 0.5f), 0.01f);
            Assert.AreEqual(0f, Adsr(1.6f, 0.1f, 0.2f, 0.6f, 1f, 0.5f));
        }

        [Test]
        public void ARoomDiesAwayAtTheRateAskedAndSoundsDifferentOnEachSide()
        {
            var click = new float[3 * Rate];
            click[0] = 1f;
            var (left, right) = Reverb(click, 1.5f);
            double Loud(float[] side, float from) => side.Skip(Samples(from)).Take(Rate / 5).Sum(sample => (double)sample * sample);
            // half the asked time on, the tail should be thirty decibels down
            Assert.AreEqual(-30.0, 10 * math.log10(Loud(left, 0.95f) / Loud(left, 0.2f)), 6.0);
            Assert.AreEqual(-30.0, 10 * math.log10(Loud(right, 0.95f) / Loud(right, 0.2f)), 6.0);
            Assert.Less(math.abs(left.Zip(right, (l, r) => (double)l * r).Sum()), 0.3 * left.Sum(sample => (double)sample * sample));
        }

        [Test]
        public void AWanderIsSlowNeverTheSameTwiceAndJoinsItsOwnStart()
        {
            // a power of two long, so its spectrum is exactly that of the loop it makes
            var curve = Wander(1 << 17, 3, 2f);
            Assert.AreEqual(1f, curve.Max(math.abs), 1e-4f);
            var spectrum = Measure.Spectrum(curve);
            Assert.Less(Measure.Band(spectrum, 8f, Rate), 0.01 * spectrum.Sum(), "faster than asked");
            Assert.Greater(Measure.Band(spectrum, 0.3f, 2f), 0.5 * spectrum.Sum(), "slower than asked");
            // unlike a wave, one half tells nothing of the other
            int half = curve.Length / 2;
            Assert.Less(math.abs(Enumerable.Range(0, half).Sum(i => (double)curve[i] * curve[i + half])), 0.5 * curve.Sum(sample => (double)sample * sample));
            Assert.Less(math.abs(curve[0] - curve[curve.Length - 1]), 2f * Enumerable.Range(1, curve.Length - 1).Max(i => math.abs(curve[i] - curve[i - 1])), "the join");
            Assert.Greater(curve.Zip(Wander(1 << 17, 4, 2f), (a, b) => math.abs(a - b)).Max(), 0.5f, "another seed");
        }

        [Test]
        public void AnEchoComesBackLaterQuieterAndDullerEachTime()
        {
            float[] left = new float[Rate], right = new float[Rate];
            left[0] = right[0] = 1f;
            Echo(left, right, 7, 0.1f, 0.5f, 0.5f);
            // the click comes back four times between a tenth and half a second on, once in each quarter of that
            float[] Quarter(float[] side, int k) => side.Skip(Samples(0.1f + 0.1f * k)).Take(Samples(0.1f)).ToArray();
            double Energy(int k) => Quarter(left, k).Concat(Quarter(right, k)).Sum(sample => (double)sample * sample);
            Assert.AreEqual(0f, Measure.Rms(left.Skip(1).Take(Samples(0.1f) - 1)));
            for (int k = 0; k < 4; k++)
            {
                Assert.Greater(Energy(k), 0.0, $"return {k}");
                if (k == 0) continue;
                Assert.Less(Energy(k), 0.5 * Energy(k - 1), $"return {k} is quieter");
                Assert.Less(Measure.Centroid(Measure.Spectrum(Quarter(left, k))), Measure.Centroid(Measure.Spectrum(Quarter(left, k - 1))), $"return {k} is duller");
                // and from the other side
                Assert.Less((Measure.Rms(Quarter(left, k)) - Measure.Rms(Quarter(right, k))) * (Measure.Rms(Quarter(left, k - 1)) - Measure.Rms(Quarter(right, k - 1))), 0f, $"return {k} is across from the last");
            }
        }

        [Test]
        public void AFilterOrARoomCanBeCarriedRoundALoop()
        {
            // filtered round the join, a loop comes out as it would from the middle of an endless run of itself
            var loop = Noise(4800, 9);
            var endless = loop.Concat(loop).Concat(loop).ToArray().Filter(Biquad.LowPass(300f)).Skip(9600).ToArray();
            var lapped = ((float[])loop.Clone()).Lap(x => x.Filter(Biquad.LowPass(300f)));
            Assert.Less(lapped.Zip(endless, (a, b) => math.abs(a - b)).Max(), 1e-4f);

            // a click just before the join is heard in the room just after it, given a lead-in and not otherwise
            var click = new float[Rate / 2];
            click[click.Length - 10] = 1f;
            Assert.AreEqual(0f, Measure.Rms(Reverb(click, 1f).left.Take(Rate / 10)));
            Assert.Greater(Measure.Rms(Reverb(click, 1f, lead: click.Length).left.Take(Rate / 10)), 0.005f);
        }
    }
}
