using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Infinigame
{
    // The hermit's voice. On a Mac the system's own speech reads his lines: it writes the sound
    // to a file, and the game plays that with its other sounds so that it is mixed, muted and
    // paused along with them. Where there is no such speech he is only read.
    public class Speech : MonoBehaviour
    {
        public static bool Muted;
        // The command that speaks, and the voices asked for in turn until it knows one: an older
        // man's, then whichever the system would use. Tests point the command at a stand-in.
        public static string Tool = "/usr/bin/say";
        static readonly string[] Voices = { "Daniel (Enhanced)", "Daniel", null };
        // which of those this computer turned out to have
        static int known;

        AudioSource source;
        int asked;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            // Lower and lightly modulated to suit the machine voice.
            source.pitch = 0.82f;
        }

        // Says these words, in place of whatever was being said.
        public async void Say(string words)
        {
            int mine = ++asked;
            source.Stop();
            if (Muted || !File.Exists(Tool) || string.IsNullOrWhiteSpace(words)) return;
            string folder = Path.Combine(Application.temporaryCachePath, "speech");
            float[] sound = null;
            int rate = 22050;
            try
            {
                sound = await Task.Run(() =>
                {
                    Directory.CreateDirectory(folder);
                    string script = Path.Combine(folder, $"{mine}.txt"), spoken = Path.Combine(folder, $"{mine}.wav");
                    // read from a file, so that nothing in his words can be taken for an instruction to the command
                    File.WriteAllText(script, words);
                    for (; known < Voices.Length; known++)
                    {
                        var start = new ProcessStartInfo { FileName = Tool, UseShellExecute = false, CreateNoWindow = true };
                        foreach (string argument in new[] { "-r", "185", "-o", spoken, $"--data-format=LEI16@{rate}", "-f", script }) start.ArgumentList.Add(argument);
                        if (Voices[known] != null)
                        {
                            start.ArgumentList.Add("-v");
                            start.ArgumentList.Add(Voices[known]);
                        }
                        using var process = Process.Start(start);
                        if (!process.WaitForExit(20000))
                        {
                            process.Kill();
                            continue;
                        }
                        // a voice it does not have leaves an error or, on some systems, only silence
                        var samples = process.ExitCode == 0 && File.Exists(spoken) ? Samples(File.ReadAllBytes(spoken)) : new float[0];
                        if (samples.Length > 0) return samples;
                    }
                    return null;
                });
            }
            catch (Exception)
            {
                // no speech on this computer after all
            }
            // he may have been given something else to say in the meantime
            if (mine != asked || sound == null || sound.Length == 0) return;
            Destroy(source.clip);
            source.clip = AudioClip.Create("Speech", sound.Length, 1, rate, false);
            for (int i = 0; i < sound.Length; i++) sound[i] *= 0.75f + 0.25f * Mathf.Sin(2 * Mathf.PI * 45 * i / rate);
            source.clip.SetData(sound, 0);
            source.Play();
        }

        // The sound in a WAV file of 16-bit samples, wherever among its chunks the data lies.
        public static float[] Samples(byte[] wav)
        {
            for (int at = 12; at + 8 <= wav.Length;)
            {
                int length = BitConverter.ToInt32(wav, at + 4);
                if (wav[at] == 'd' && wav[at + 1] == 'a' && wav[at + 2] == 't' && wav[at + 3] == 'a')
                {
                    var samples = new float[Math.Min(length, wav.Length - at - 8) / 2];
                    for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(wav, at + 8 + 2 * i) / 32768f;
                    return samples;
                }
                at += 8 + length + (length & 1);
            }
            return new float[0];
        }
    }
}
