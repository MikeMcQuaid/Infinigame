using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace Infinigame
{
    // One offline voice for authored dialogue and generated replies on every platform.
    public class Speech : MonoBehaviour
    {
        public static bool Muted;
        public const string Choice = "cmu_us_slt_arctic_hts";
        const int Rate = 32000;
        public static Func<string, string, float[]> Render = Festival;
        static readonly object synthesis = new object();
#if UNITY_IOS && !UNITY_EDITOR
        const string Library = "__Internal";
#elif UNITY_EDITOR_OSX
        const string Library = "Build/Speech/macOS/libInfinigameSpeech.dylib";
#else
        const string Library = "InfinigameSpeech";
#endif
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr InfinigameSynthesise(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string words, [MarshalAs(UnmanagedType.LPUTF8Str)] string directory, out int count);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void InfinigameSpeechFree(IntPtr samples);

        public static float[] Festival(string words, string directory)
        {
            // SLT is an English voice. Normalise typography and accents before its text front end.
            words = Regex.Replace(words.Replace('’', '\'').Replace('“', '"').Replace('”', '"').Replace('–', '-').Replace('—', '-').Normalize(NormalizationForm.FormD), @"[^\x20-\x7e\s]", "");
            var samples = new System.Collections.Generic.List<float>();
            foreach (Match part in Regex.Matches(words, @".{1,400}(?:\s|$)|\S{1,400}", RegexOptions.Singleline))
            {
                IntPtr buffer = InfinigameSynthesise(part.Value, directory, out int count);
                try
                {
                    if (buffer == IntPtr.Zero || count <= 0) return null;
                    var chunk = new float[count];
                    Marshal.Copy(buffer, chunk, 0, count);
                    samples.AddRange(chunk);
                }
                finally { InfinigameSpeechFree(buffer); }
            }
            return samples.ToArray();
        }

        readonly Queue<(string words, Action start, bool caption)> lines = new Queue<(string, Action, bool)>();
        AudioSource source;
        bool paused;
        volatile int asked;
        int pending;
        float reading;
        public bool Speaking => pending != 0 || reading > 0 || source.isPlaying || paused && source.clip != null || lines.Count > 0;
        void Awake()
        {
            source = new GameObject("Companion voice").AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.ignoreListenerPause = true;
            source.minDistance = 5;
            source.dopplerLevel = 0;
        }
        void Update()
        {
            var game = GetComponent<Game>();
            source.mute = Muted;
            bool hold = game.Paused || game.Dead || game.ShowControls || game.Player.SystemView;
            source.mute = Muted;
            if (hold != paused)
            {
                paused = hold;
                if (hold) source.Pause(); else source.UnPause();
            }
            if (paused) return;
            bool nearby = !game.Hermit.Aboard && game.System.Reference == Core.SolarSystemDescriptor.Home
                && Unity.Mathematics.math.distance(game.Hermit.Position, game.Player.Position) < 30;
            source.spatialBlend = nearby ? 1 : 0;
            if (nearby) source.transform.position = game.Hermit.Position - game.System.Anchor + Unity.Mathematics.math.normalizesafe(game.Hermit.Position);
            reading = Mathf.Max(0, reading - Time.unscaledDeltaTime);
            if (pending != 0 || source.isPlaying || reading > 0) return;
            if (source.clip != null) { Destroy(source.clip); source.clip = null; }
            if (lines.Count > 0) PlayNext();
        }
        public void Silence()
        {
            asked++; pending = 0; reading = 0; lines.Clear();
            source.Stop(); Destroy(source.clip); source.clip = null;
        }
        void OnDestroy() => asked++;

        public void Say(string words, bool interrupt = false, Action start = null, bool caption = false)
        {
            if (string.IsNullOrWhiteSpace(words)) return;
            if (interrupt)
            {
                asked++; pending = 0;
                source.Stop(); Destroy(source.clip); source.clip = null;
                start?.Invoke();
                reading = caption ? 2 + words.Length / 18f : 0;
                if (!Muted) RenderLine(words, ++asked);
                return;
            }
            bool idle = !Speaking;
            lines.Enqueue((words, start, caption));
            if (idle) PlayNext();
        }
        void PlayNext()
        {
            var (words, start, caption) = lines.Dequeue();
            start?.Invoke();
            reading = caption ? 2 + words.Length / 18f : 0;
            if (!Muted) RenderLine(words, ++asked);
        }

        async void RenderLine(string words, int mine)
        {
            var render = Render;
            // Scene tests supply a stand-in; batch builds never invoke the real voice engine.
            if (Application.isBatchMode && render == Festival) return;
            pending = mine;
#if UNITY_EDITOR
            string directory = Path.GetFullPath("Build/Speech/Voice");
#else
            string directory = Path.Combine(Application.streamingAssetsPath, "Festival");
#endif
            float[] sound = null;
            try
            {
                sound = await Task.Run(() =>
                {
                    float[] samples;
                    lock (synthesis) samples = mine == asked ? render(words, directory) : null;
                    if (samples != null)
                        for (int i = 0; i < samples.Length; i++)
                            samples[i] *= 0.65f + 0.35f * (float)Math.Sin(i * (2 * Math.PI * 65 / Rate));
                    return samples;
                });
            }
            catch (Exception error) { Debug.LogWarning("Speech unavailable: " + error.Message); }
            if (pending == mine) pending = 0;
            if (this == null || mine != asked || sound == null || sound.Length == 0 || Muted) return;
            source.clip = AudioClip.Create("Speech", sound.Length, 1, Rate, false);
            source.clip.SetData(sound, 0);
            source.Play();
            if (paused) source.Pause();
        }
    }
}
