using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace Infinigame
{
    // The hermit's voice. On a Mac the system's own speech reads his lines: it writes the sound
    // to a file, and the game plays that with its other sounds. Speech can continue during menus. Where there is no such speech he is only read.
    public class Speech : MonoBehaviour
    {
        public static bool Muted, Robot;
        public static float RobotStrength = 1;
#if UNITY_STANDALONE_WIN
        public static string Tool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe");
#elif UNITY_IOS && !UNITY_EDITOR
        public static string Tool = "native-ios";
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void InfinigameSpeak(string text, string voice, float strength);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern IntPtr InfinigameVoices();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void InfinigameVoiceFree(IntPtr text);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern bool InfinigameSpeaking();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void InfinigameSilence();
#else
        public static string Tool = "/usr/bin/say";
#endif
        public static string Choice = "Daniel (English (UK))";

        [Serializable] public class Voice
        {
            public string id, name, language;
        }
        [Serializable] class Catalogue { public Voice[] voices; }

        public static async Task<Voice[]> InstalledVoices()
        {
            string tool = Tool;
            try
            {
                Voice[] voices;
#if UNITY_IOS && !UNITY_EDITOR
                if (Tool == "native-ios" && !Application.isBatchMode)
                {
                    IntPtr json = InfinigameVoices();
                    try { voices = JsonUtility.FromJson<Catalogue>(System.Runtime.InteropServices.Marshal.PtrToStringUTF8(json)).voices; }
                    finally { InfinigameVoiceFree(json); }
                }
                else
#endif
                {
                    if (!File.Exists(tool)) return Array.Empty<Voice>();
                    bool windows = tool.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase);
                    string output = await Task.Run(() =>
                    {
                        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                        foreach (string argument in windows ? new[] { "-NoProfile", "-NonInteractive", "-Command", @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Speech
$speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
@{ voices = @($speaker.GetInstalledVoices() | Where-Object { $_.Enabled } | ForEach-Object { @{ id = $_.VoiceInfo.Name; name = $_.VoiceInfo.Name; language = $_.VoiceInfo.Culture.Name } }) } | ConvertTo-Json -Depth 3 -Compress
$speaker.Dispose()" } : new[] { "-v", "?" }) start.ArgumentList.Add(argument);
                        using var process = Process.Start(start);
                        var read = process.StandardOutput.ReadToEndAsync();
                        process.BeginErrorReadLine();
                        if (!process.WaitForExit(5000)) { process.Kill(); return ""; }
                        return process.ExitCode == 0 ? read.GetAwaiter().GetResult() : "";
                    });
                    voices = windows ? JsonUtility.FromJson<Catalogue>(output.Trim('\uFEFF', '\r', '\n', ' '))?.voices
                        : Regex.Matches(output, @"^(.+?)\s+([a-z]{2,3}[_-][A-Za-z0-9_-]+)\s+#", RegexOptions.Multiline).Cast<Match>()
                            .Select(match => new Voice { id = match.Groups[1].Value.Trim(), name = match.Groups[1].Value.Trim(), language = match.Groups[2].Value.Replace('_', '-') }).ToArray();
                }
                return (voices ?? Array.Empty<Voice>()).Where(voice => !string.IsNullOrWhiteSpace(voice.id) && !string.IsNullOrWhiteSpace(voice.name))
                    .GroupBy(voice => voice.id).Select(group => group.First())
                    .OrderBy(voice => voice.language == "en-GB" ? 0 : voice.language?.StartsWith("en") == true ? 1 : 2)
                    .ThenBy(voice => voice.name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception) { return Array.Empty<Voice>(); }
        }

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
            if (Tool == "native-ios" && !Application.isBatchMode) { InfinigameSpeak(words, Choice, Robot ? RobotStrength : 0); return; }
#endif
            if (!File.Exists(Tool)) return;
            pending = mine;
            string folder = Path.Combine(Application.temporaryCachePath, "speech");
            string voice = Choice, tool = Tool;
            float strength = Robot ? Mathf.Clamp01(RobotStrength) : 0;
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
                        var start = new ProcessStartInfo { FileName = tool, UseShellExecute = false, CreateNoWindow = true };
                        if (tool.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string command = Path.Combine(folder, $"{mine}.ps1");
                            File.WriteAllText(command, @"param($TextFile, $WaveFile, $VoiceName)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
if ($VoiceName) { $speaker.SelectVoice($VoiceName) }
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$speaker.SetOutputToWaveFile($WaveFile, $format)
$speaker.Speak([System.IO.File]::ReadAllText($TextFile))
$speaker.Dispose()");
                            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", command, script, spoken }) start.ArgumentList.Add(argument);
                            if (chosen.Length > 0) start.ArgumentList.Add(chosen);
                        }
                        else foreach (string argument in new[] { "-r", "185", "-o", spoken, $"--data-format=LEI16@{rate}", "-f", script }) start.ArgumentList.Add(argument);
                        if (chosen.Length > 0 && !tool.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase))
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
            if (strength > 0)
                for (int i = 0; i < sound.Length; i++) sound[i] *= 1 - 0.35f * strength + 0.35f * strength * Mathf.Sin(i * 2 * Mathf.PI * 65 / rate);
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
