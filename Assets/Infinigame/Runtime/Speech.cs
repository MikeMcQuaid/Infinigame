using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Infinigame
{
    // The hermit's voice. On a Mac the system's own speech reads his lines: it writes the sound
    // to a file, and the game plays that with its other sounds. Speech can continue during menus. Where there is no such speech he is only read.
    public class Speech : MonoBehaviour
    {
        public static bool Muted;
#if UNITY_STANDALONE_WIN
        public static string Tool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe");
#elif UNITY_IOS && !UNITY_EDITOR
        public static string Tool = "native-ios";
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void InfinigameSpeak(string text, bool robot);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern bool InfinigameSpeaking();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void InfinigameSilence();
#else
        public static string Tool = "/usr/bin/say";
#endif
        public static string Choice = "Daniel (English (UK))";

        AudioSource source;
        int asked, pending;
        public bool Speaking
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                if (Tool == "native-ios" && !Application.isBatchMode) return InfinigameSpeaking();
#endif
                return pending != 0 || source != null && source.isPlaying;
            }
        }
        public void Silence()
        {
            asked++; pending = 0; source.Stop();
#if UNITY_IOS && !UNITY_EDITOR
            if (Tool == "native-ios" && !Application.isBatchMode) InfinigameSilence();
#endif
        }

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.pitch = 1f;
            // Voice previews and colony explanations are heard while their menus are open.
            source.ignoreListenerPause = true;
        }

        // Says these words, in place of whatever was being said.
        public async void Say(string words)
        {
            int mine = ++asked;
            pending = 0; source.Stop();
            if (Muted || string.IsNullOrWhiteSpace(words)) return;
#if UNITY_IOS && !UNITY_EDITOR
            if (Tool == "native-ios" && !Application.isBatchMode) { InfinigameSpeak(words, Choice == "Zarvox"); return; }
#endif
            if (!File.Exists(Tool)) return;
            pending = mine;
            string folder = Path.Combine(Application.temporaryCachePath, "speech");
            string voice = Choice;
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
                    foreach (string chosen in string.IsNullOrEmpty(voice) ? new[] { "" } : new[] { voice, "" })
                    {
                        var start = new ProcessStartInfo { FileName = Tool, UseShellExecute = false, CreateNoWindow = true };
                        if (Tool.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string command = Path.Combine(folder, $"{mine}.ps1");
                            File.WriteAllText(command, @"param($TextFile, $WaveFile)
Add-Type -AssemblyName System.Speech
$speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
$british = $speaker.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Culture.Name -eq 'en-GB' } | Select-Object -First 1
if ($british) { $speaker.SelectVoice($british.VoiceInfo.Name) }
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$speaker.SetOutputToWaveFile($WaveFile, $format)
$speaker.Speak([System.IO.File]::ReadAllText($TextFile))
$speaker.Dispose()");
                            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", command, script, spoken }) start.ArgumentList.Add(argument);
                        }
                        else foreach (string argument in new[] { "-r", "185", "-o", spoken, $"--data-format=LEI16@{rate}", "-f", script }) start.ArgumentList.Add(argument);
                        if (chosen.Length > 0 && !Tool.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            start.ArgumentList.Add("-v");
                            start.ArgumentList.Add(chosen);
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
            if (pending == mine) pending = 0;
            // he may have been given something else to say in the meantime
            if (this == null || mine != asked || sound == null || sound.Length == 0) return;
            if (Muted) return;
            if (voice == "Zarvox" && Tool.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase))
                for (int i = 0; i < sound.Length; i++) sound[i] *= 0.82f + 0.18f * Mathf.Sin(i * 2 * Mathf.PI * 65 / rate);
            Destroy(source.clip);
            source.clip = AudioClip.Create("Speech", sound.Length, 1, rate, false);
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
