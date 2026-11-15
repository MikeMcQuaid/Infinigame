using System;
using System.Collections.Generic;
using Unity.Mathematics;
using static Infinigame.Core.Biquad;
using static Infinigame.Core.Synth;
using Random = Unity.Mathematics.Random;

namespace Infinigame.Core
{
    public enum Drum { Kick, Snare, Hat, Open, Crash, HighTom, LowTom, Clank }

    // One note for a guitar: when it starts and how long it lasts, both in sixteenths; how many
    // semitones above the song's root; which strings of the chord; and whether the palm rests on them.
    public struct Note
    {
        public int step, steps, pitch;
        public int[] strings;
        public bool muted;
    }

    // What the band plays at one intensity: the rhythm guitars, which the bass follows an octave
    // down, up to two lead guitars each with a line of its own and the drums.
    public class Part
    {
        public readonly List<Note> Rhythm = new List<Note>();
        public readonly List<Note>[] Leads = { new List<Note>(), new List<Note>() };
        public readonly List<(int step, Drum drum, float gain)> Drums = new List<(int, Drum, float)>();
    }

    // A piece of music as written down, before anyone plays it: sixteen bars arranged twice, calm
    // and heavy, in one key and at one tempo so that either can fade into the other.
    public class Song
    {
        public uint Seed;
        public string Mode;
        // The MIDI note of the guitars' lowest string and how many samples a sixteenth note lasts.
        public int Root, Step;
        public readonly Part Calm = new Part(), Heavy = new Part();
        public float Tempo => Rate * 15f / Step;
        public int Length => Riffs.Bars * 16 * Step;
    }

    // Metal, written and played by arithmetic. Compose turns a seed into a song: a key, a tempo and
    // four sections of four bars. The heavy arrangement is the dark one, for a fight: a riff, a run
    // of tremolo picking, the riff again under a lead, a breakdown. The calm one is for flying on
    // in peace: a gallop through brighter chords under a melody for two guitars. Render plays
    // either: plucked-string guitars through a simulated amplifier, a bass and a drum kit, mixed
    // so the end runs into the start.
    public static class Riffs
    {
        public const int Bars = 16;

        static readonly int[] Fifth = { 0, 7 }, Power = { 0, 7, 12 }, Tritone = { 0, 6, 12 }, Single = { 0 };

        static readonly (string name, int[] scale)[] Modes =
        {
            ("phrygian", new[] { 0, 1, 3, 5, 7, 8, 10 }),
            ("locrian", new[] { 0, 1, 3, 5, 6, 8, 10 }),
            ("harmonic minor", new[] { 0, 2, 3, 5, 7, 8, 11 }),
        };

        // Ways to split a bar of sixteen into uneven groups, which is where a riff's lurch comes from.
        static readonly int[][] Groupings =
        {
            new[] { 3, 3, 2, 3, 3, 2 }, new[] { 3, 3, 3, 3, 2, 2 }, new[] { 4, 4, 4, 4 }, new[] { 2, 3, 3, 2, 3, 3 },
            new[] { 3, 3, 4, 3, 3 }, new[] { 6, 6, 4 }, new[] { 4, 3, 3, 3, 3 }, new[] { 3, 5, 3, 5 },
        };

        // For each drum: how loud it sits, where across the stereo field and how much of it the room hears.
        static readonly float[] Levels = { 0.8f, 0.8f, 0.28f, 0.3f, 0.55f, 0.6f, 0.6f, 0.4f };
        static readonly float[] Pans = { 0f, 0f, 0.3f, 0.3f, -0.35f, 0.4f, -0.4f, -0.2f };
        static readonly float[] Rooms = { 0.03f, 0.3f, 0.05f, 0.1f, 0.25f, 0.3f, 0.3f, 0.5f };

        public static Song Compose(uint seed)
        {
            // one system's seed is the last one's plus one, so they are well stirred before anything is drawn from them
            var rnd = Random.CreateFromIndex(seed);
            var (mode, scale) = Modes[rnd.NextInt(Modes.Length)];
            // tuned down to between B and D below a guitar's usual low E, at a pace somewhere between a march and a sprint
            var song = new Song { Seed = seed, Mode = mode, Root = rnd.NextInt(35, 39), Step = Rate * 15 / rnd.NextInt(144, 184) };
            Part calm = song.Calm, heavy = song.Heavy;

            // Two bars of rhythm guitar: 0 a riff, 1 tremolo picking, 2 a breakdown.
            List<Note> Phrase(int feel)
            {
                var notes = new List<Note>();
                int[] groups = Groupings[rnd.NextInt(Groupings.Length)];
                // where the riff goes besides the root: the mode's second, the tritone and one other
                int[] haunts = { scale[1], 6, scale[rnd.NextInt(2, 7)] };
                int density = rnd.NextInt(3), pitch = 0;
                // a riff keeps coming home: about half its chords are the root
                var chords = new int[groups.Length];
                for (int i = 1; i < chords.Length; i++) chords[i] = rnd.NextBool() ? 0 : haunts[rnd.NextInt(3)];
                for (int bar = 0; bar < 2; bar++)
                {
                    int at = bar * 16;
                    if (feel == 1)
                    {
                        // one chord every half bar, picked as fast as the hand will go
                        for (int i = 0; i < 16; i++)
                        {
                            if (i % 8 == 0 && bar + i > 0) pitch = haunts[rnd.NextInt(3)];
                            notes.Add(new Note { step = at + i, pitch = pitch, strings = Power });
                        }
                        continue;
                    }
                    for (int g = 0; g < groups.Length; g++)
                    {
                        int group = groups[g];
                        if (feel == 0)
                        {
                            // each group opens on a ringing chord and the second bar answers the first by ending somewhere else;
                            // chugs on the low string fill in behind: every sixteenth, a gallop or plain eighths
                            notes.Add(new Note { step = at, pitch = bar == 1 && g >= groups.Length - 2 ? haunts[rnd.NextInt(3)] : chords[g], strings = Power });
                            for (int i = at + 1; i < at + group; i++)
                                if (density == 0 || (density == 1 ? i % 4 != 1 : i % 2 == 0)) notes.Add(new Note { step = i, strings = Fifth, muted = true });
                        }
                        // a breakdown is short bursts of chugs with air between them and a tritone left hanging at the end of the bar
                        else if (at + group == bar * 16 + 16) notes.Add(new Note { step = at, strings = Tritone });
                        else
                            for (int i = 0; i < group; i++)
                                if (i == 0 || (i == 1 && group > 2) || (i == 3 && group > 4)) notes.Add(new Note { step = at + i, strings = Fifth, muted = true });
                        at += group;
                    }
                }
                // a chord rings until the next stroke; a chug is choked at once
                for (int i = 0; i < notes.Count; i++)
                {
                    var note = notes[i];
                    note.steps = note.muted ? 1 : (i + 1 < notes.Count ? notes[i + 1].step : 32) - note.step;
                    notes[i] = note;
                }
                return notes;
            }

            var riff = Phrase(0);
            var phrases = new[] { riff, Phrase(1), riff, Phrase(2) };

            // The chords of the calm arrangement, a bar each: out from the root by way of the sixth and
            // the third and home again, which lifts where the riffs brood. The harmonic minor has no
            // flat seventh to climb back by, so it goes by the fifth.
            int climb = scale[6] == 10 ? 10 : 7;
            var walks = new[] { new[] { 0, 8, 3, climb }, new[] { 0, 8, climb, 0 }, new[] { 8, climb, 0, 0 }, new[] { 0, 3, 8, climb } };
            var verse = walks[rnd.NextInt(4)];
            var roots = new[] { verse, walks[rnd.NextInt(4)], verse, walks[rnd.NextInt(4)] };
            // A step of the mode as semitones, carrying on up through the octaves.
            int Degree(int index) => 12 * (index / 7) + scale[index % 7];
            for (int section = 0; section < 4; section++)
            {
                int first = section * 64;
                // the phrase twice, the last half bar given over to a turnaround
                for (int repeat = 0; repeat < 2; repeat++)
                    foreach (var note in phrases[section])
                        if (repeat == 0 || note.step < 24)
                            heavy.Rhythm.Add(new Note
                            {
                                step = first + repeat * 32 + note.step, steps = math.min(note.steps, (repeat == 0 ? 32 : 24) - note.step),
                                pitch = note.pitch, strings = note.strings, muted = note.muted,
                            });
                // out of a breakdown, one tritone left to ring; otherwise four chords walking down the mode to its second, which wants the root that follows
                if (section == 3) heavy.Rhythm.Add(new Note { step = first + 56, steps = 8, strings = Tritone });
                else
                    for (int i = 0; i < 4; i++) heavy.Rhythm.Add(new Note { step = first + 56 + 2 * i, steps = 2, pitch = scale[4 - i], strings = Power });
                // over the riff's return, a lead that keeps to the notes that grate
                for (int i = 0; section == 2 && i < 8; i++)
                    heavy.Leads[0].Add(new Note { step = first + 8 * i, steps = 8, pitch = 36 + new[] { scale[1], 6, scale[5], 11, 12 + scale[1] }[rnd.NextInt(5)], strings = Single });

                for (int bar = 0; bar < 4; bar++)
                {
                    int start = first + bar * 16, chord = roots[section][bar];
                    // the calm arrangement strikes its chord and gallops on under it
                    calm.Rhythm.Add(new Note { step = start, steps = 4, pitch = chord, strings = Power });
                    for (int i = 4; i < 16; i++)
                        if (i % 4 != 1) calm.Rhythm.Add(new Note { step = start + i, steps = 1, pitch = chord, strings = Fifth, muted = true });
                    // over it two guitars sing a third apart: a long note of the chord (its third, fifth or octave), then two short ones moving off it
                    int tune = Array.IndexOf(scale, chord) + new[] { 2, 4, 7 }[rnd.NextInt(3)];
                    int[] line = { tune, tune + (rnd.NextBool() ? 1 : -1), tune + (rnd.NextBool() ? 2 : -2) };
                    for (int i = 0; i < 3; i++)
                        for (int twin = 0; twin < 2; twin++)
                            calm.Leads[twin].Add(new Note { step = start + (i == 0 ? 0 : 4 + 4 * i), steps = i == 0 ? 8 : 4, pitch = 24 + Degree(line[i] + 2 * twin), strings = Single });

                    for (int i = 0; i < 16; i++)
                    {
                        int step = start + i;
                        // calm drums: the kick gallops with the guitars, the snare falls once a bar at half the pace, an open hat keeps time
                        if (calm.Rhythm.Exists(note => note.step == step)) calm.Drums.Add((step, Drum.Kick, 0.8f));
                        if (i == 8) calm.Drums.Add((step, Drum.Snare, 0.9f));
                        if (i % 4 == 0) calm.Drums.Add((step, bar + i == 0 ? Drum.Crash : Drum.Open, 0.6f));
                        if (bar == 3 && i >= 12) calm.Drums.Add((step, i < 14 ? Drum.HighTom : Drum.LowTom, 0.7f));

                        if (bar == 3 && i >= 8)
                        {
                            // a roll down the kit into the next section
                            heavy.Drums.Add((step, i < 12 ? Drum.Snare : i < 14 ? Drum.HighTom : Drum.LowTom, 0.6f + 0.05f * (i - 8)));
                            if (i % 4 == 0) heavy.Drums.Add((step, Drum.Kick, 1f));
                            continue;
                        }
                        // the kick lands with every stroke of the guitars, which in the fast passages is both feet flat out
                        if (heavy.Rhythm.Exists(note => note.step == step)) heavy.Drums.Add((step, Drum.Kick, 1f));
                        // the snare on two and four, joined by an anvil once the riff returns; in the breakdown
                        // the anvil keeps those beats and the snare falls on three alone
                        if (section == 3 ? i == 8 : i % 8 == 4) heavy.Drums.Add((step, Drum.Snare, 1f));
                        if (section >= 2 && i % 8 == 4) heavy.Drums.Add((step, Drum.Clank, 1f));
                        if (i == 0 && bar % 2 == 0) heavy.Drums.Add((step, Drum.Crash, bar == 0 ? 1f : 0.7f));
                        // time is kept on closed hats in eighths at first, then on an open hat in quarters
                        if (section == 0 ? i % 2 == 0 : i % 4 == 0) heavy.Drums.Add((step, section == 0 ? Drum.Hat : Drum.Open, i % 4 == 0 ? 0.8f : 0.5f));
                    }
                }
            }
            return song;
        }

        public static float[] Render(Song song, bool heavy)
        {
            int n = song.Length;
            var part = heavy ? song.Heavy : song.Calm;
            var rnd = new Random(math.hash(new uint2(song.Seed, heavy ? 2u : 1u)) | 1u);
            float[] left = new float[n], right = new float[n], room = new float[n];

            // Adds a sound to the mix from wherever it starts, running round the end into the start, sending some of it to the room.
            void Mix(float[] sound, int start, float gain, float pan, float wet)
            {
                float angle = (pan + 1f) * math.PI / 4f;
                for (int i = 0; i < sound.Length; i++)
                {
                    int at = (start + i) % n;
                    left[at] += sound[i] * gain * math.cos(angle);
                    right[at] += sound[i] * gain * math.sin(angle);
                    room[at] += sound[i] * gain * wet;
                }
            }

            // the rhythm part is played twice, one take hard to each side, as metal records are made
            for (int take = 0; take < 2; take++)
            {
                var track = new float[n * 2];
                foreach (var note in part.Rhythm) Strike(track, song, note, 2, ref rnd);
                Mix(Amp(track, heavy ? 30f : 10f, heavy ? 5000f : 3800f).Loud(heavy ? 0.2f : 0.12f), 0, 1f, take == 0 ? -0.9f : 0.9f, heavy ? 0.05f : 0.15f);
            }
            // each lead guitar has an amplifier to itself, or their two notes would grind against each other in one
            for (int twin = 0; twin < 2; twin++)
            {
                if (part.Leads[twin].Count == 0) continue;
                var solo = new float[n * 2];
                foreach (var note in part.Leads[twin]) Strike(solo, song, note, 2, ref rnd);
                Mix(Amp(solo, heavy ? 40f : 12f, heavy ? 5500f : 4500f).Level(), 0, 0.3f, twin - 0.5f, heavy ? 0.3f : 0.5f);
            }

            var bass = new float[n];
            foreach (var note in part.Rhythm)
                Strike(bass, song, new Note { step = note.step, steps = note.steps, pitch = note.pitch - 12, strings = Single, muted = note.muted }, 1, ref rnd);
            Mix(bass.Lap(x => x.Level().Drive(heavy ? 4f : 1.5f).Filter(LowPass(heavy ? 1800f : 700f)), Rate / 4).Loud(heavy ? 0.15f : 0.1f), 0, 1f, 0f, 0f);

            // two kicks, one for each foot, so a fast run is not one sample over and over
            var kit = new[]
            {
                Kick(1), Snare(), Cymbal(0.07f, 6000f, 3), Cymbal(0.45f, 5000f, 4), Cymbal(2.2f, 3000f, 5), Tom(190f), Tom(120f),
                Ring(0.3f, 520f, 6, 1f, 1.34f, 1.87f, 2.56f, 3.42f, 4.38f).Add(Burst(0.05f, 7, BandPass(2500f, 0.7f), 0.0005f), 0.5f).Drive(2f).Level(),
                Kick(2),
            };
            foreach (var (step, drum, gain) in part.Drums)
                Mix(kit[drum == Drum.Kick && step % 2 == 1 ? 8 : (int)drum], step * song.Step, gain * Levels[(int)drum] * rnd.NextFloat(0.85f, 1f), Pans[(int)drum], Rooms[(int)drum]);

            // a tight room for the heavy mix; a hall for the calm one
            var (l, r) = Reverb(room, heavy ? 0.9f : 2.5f, heavy ? 1f : 1.6f, heavy ? 5000f : 4000f, Rate * (heavy ? 2 : 4));
            return Loop(left.Add(l), right.Add(r), heavy ? 1.3f : 0.55f);
        }

        // Plays one note into a track that wraps round: each string of the chord is plucked a moment
        // after the last and damped by the hand when the note is over.
        static void Strike(float[] track, Song song, Note note, int oversample, ref Random rnd)
        {
            int rate = Rate * oversample, length = note.steps * song.Step * oversample;
            for (int s = 0; s < note.strings.Length; s++)
            {
                // no two strokes are quite in tune or quite in time, which is what makes two takes sound like two guitars
                float hertz = 440f * math.pow(2f, (song.Root + note.pitch + note.strings[s] - 69 + rnd.NextFloat(-0.04f, 0.04f)) / 12f);
                // a thin string keeps its overtones as long as a fat one, so the loss sits further up for higher notes
                var pluck = Pluck(length, hertz, note.muted ? 0.45f : 5f, note.muted ? 800f : 4500f * math.sqrt(hertz / 65f), rnd.NextUInt(), rate).Level();
                int start = note.step * song.Step * oversample + (int)(rate * (rnd.NextFloat(0.004f) + 0.003f * s));
                for (int i = 0; i < length; i++) track[(start + i) % track.Length] += pluck[i] * math.min(1f, (length - i) / (0.008f * rate));
            }
        }

        // A valve amplifier turned right up and the speaker cabinet it drives. It works at twice the
        // sample rate so that the overtones the distortion makes do not fold back down as fizz, and
        // hands back every other sample once the cabinet has taken the top off.
        static float[] Amp(float[] track, float gain, float top)
        {
            const int rate = Rate * 2;
            track.Lap(x => x.Level()
                // bass is what turns to mud once it clips, so before the valves most of it is held back and the mids are pushed
                .Filter(HighPass(110f, 0.707f, rate)).Filter(Peak(900f, 0.7f, 6f, rate))
                .Drive(gain, 0.15f).Filter(HighPass(70f, 0.707f, rate)).Drive(2.5f)
                // the cabinet: nothing below the cone's reach or much above it, a thump low down and the nasal part of the middle eased
                .Filter(HighPass(80f, 0.9f, rate)).Filter(Peak(120f, 0.8f, 7f, rate)).Filter(Peak(1500f, 0.7f, -3f, rate))
                .Filter(LowPass(top, 0.54f, rate)).Filter(LowPass(top, 1.31f, rate)), rate / 4);
            var heard = new float[track.Length / 2];
            for (int i = 0; i < heard.Length; i++) heard[i] = track[2 * i];
            return heard;
        }

        // Sets how loud a whole track is, by its root mean square: the measure the band is balanced by.
        static float[] Loud(this float[] x, float rms)
        {
            double sum = 0;
            foreach (float sample in x) sum += sample * sample;
            float gain = rms / (float)math.sqrt(sum / x.Length + 1e-12);
            for (int i = 0; i < x.Length; i++) x[i] *= gain;
            return x;
        }

        // The kick: a drumhead dropping in pitch as it settles and the click of the beater that lets a fast run be heard.
        public static float[] Kick(uint foot = 1) =>
            Thump(0.18f, 150f + 6f * foot, 48f, 55f, 0.001f).Drive(1.5f).Add(Burst(0.03f, foot, BandPass(3500f, 0.8f), 0.0003f), 0.8f).Level();

        // The snare: the wires rattling, over two notes of the head.
        static float[] Snare() =>
            Burst(0.25f, 8, BandPass(3000f, 0.5f), 0.0005f).Add(Thump(0.15f, 245f, 185f, 40f, 0.0005f), 0.6f).Add(Thump(0.09f, 331f, 331f, 0f, 0.0005f), 0.3f).Drive(1.5f).Level();

        static float[] Tom(float hertz) => Thump(0.35f, hertz * 1.5f, hertz, 25f, 0.001f).Add(Burst(0.03f, 9, LowPass(2500f), 0.0005f), 0.3f).Level();

        // Hats and crashes: six square waves at pitches with nothing in common, as the early drum
        // machines did it, in a wash of noise, with all but the top taken away.
        static float[] Cymbal(float seconds, float floor, uint seed)
        {
            var x = Noise(Samples(seconds), seed);
            foreach (float hertz in new[] { 310f, 461f, 559f, 790f, 816f, 1209f }) x.Add(Tone(x.Length, _ => hertz, Wave.Square));
            return x.Filter(HighPass(floor)).Filter(LowPass(11000f)).Level().Shape(t => Hit(t, 0.001f, seconds));
        }
    }
}
