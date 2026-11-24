using System;
using System.IO;
using System.Text;
using Infinigame.Core;
using UnityEngine;

namespace Infinigame.Editor
{
    static class AudioWorkshop
    {
        public static void Export(string directory)
        {
            Directory.CreateDirectory(directory);
            void Write(string name, float[] samples)
            {
                using var writer = new BinaryWriter(File.Create(Path.Combine(directory, name + ".wav")));
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)2); writer.Write(Synth.Rate);
                writer.Write(Synth.Rate * 4); writer.Write((short)4); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
                foreach (float sample in samples) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1, 1) * 32767));
            }
            void Takes(string name, int count, Func<int, float[]> make)
            {
                for (int i = 0; i < count; i++) Write(name + " " + i, make(i));
            }
            Write("Engine", Synth.Engine()); Write("Whine", Synth.Whine()); Write("Wind", Synth.Wind());
            Write("Rain", Synth.Rain()); Write("Jetpack", Synth.Jetpack()); Write("Beam", Synth.Beam());
            Write("Charge", Synth.Charge()); Write("Mind", Synth.Mind()); Write("Bike", Synth.Bike());
            Write("Chime", Synth.Chime()); Write("Warp", Synth.Warp()); Write("Alarm", Synth.Alarm());
            Write("Engage", Synth.Engage()); Write("Hurt", Synth.Hurt()); Write("Reload", Synth.Reload());
            Write("Dry fire", Synth.DryFire()); Write("Nuke", Synth.Nuke());
            Takes("Geiger", 3, Synth.Geiger);
            foreach (EncounterCue cue in Enum.GetValues(typeof(EncounterCue)))
                Takes("Encounter " + cue, 3, take => Synth.Encounter(cue, take));
            for (int i = 0; i < Mix.Takes.Length; i++) Takes("Shot " + i, Mix.Takes[i], take => Synth.Shot(i, take));
            Takes("Step", Mix.Steps, Synth.Step); Takes("Thud", Mix.Thuds, Synth.Thud); Takes("Dig", Mix.Digs, Synth.Dig);
            Takes("Boom", Mix.Booms, take => Synth.Boom(false, take)); Takes("Heavy boom", Mix.Booms, take => Synth.Boom(true, take));
        }
    }
}
