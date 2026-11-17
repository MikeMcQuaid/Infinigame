using System;
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
        public static bool Muted, Robot;
        public static float RobotStrength = 1;
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

        AudioSource source;
        bool paused, preview;
        volatile int asked;
        int pending;
        public bool Speaking => pending != 0 || source != null && (source.isPlaying || paused && source.clip != null);
        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.ignoreListenerPause = true;
        }
        void Update()
        {
            var game = GetComponent<Game>();
            bool hold = !preview && (game.Paused || game.Dead || game.ShowControls || game.Player.SystemView);
            if (paused == hold) return;
            paused = hold;
            if (hold) source.Pause(); else source.UnPause();
        }
        public void Silence()
        {
            asked++; pending = 0; source.Stop(); Destroy(source.clip); source.clip = null;
        }
        void OnDestroy() { asked++; }

        public async void Say(string words, bool preview = false)
        {
            Silence();
            this.preview = preview;
            if (Muted || string.IsNullOrWhiteSpace(words)) return;
            var render = Render;
            // Scene tests supply a stand-in; batch builds never invoke the real voice engine.
            if (Application.isBatchMode && render == Festival) return;
            int mine = asked;
            pending = mine;
#if UNITY_EDITOR
            string directory = Path.GetFullPath("Build/Speech/Voice");
#else
            string directory = Path.Combine(Application.streamingAssetsPath, "Festival");
#endif
            float strength = Robot ? Mathf.Clamp01(RobotStrength) : 0;
            float[] sound = null;
            try
            {
                sound = await Task.Run(() =>
                {
                    lock (synthesis) return mine == asked ? render(words, directory) : null;
                });
            }
            catch (Exception error) { Debug.LogWarning("Speech unavailable: " + error.Message); }
            if (pending == mine) pending = 0;
            if (this == null || mine != asked || sound == null || sound.Length == 0 || Muted) return;
            if (strength > 0)
                for (int i = 0; i < sound.Length; i++) sound[i] *= 1 - 0.35f * strength + 0.35f * strength * Mathf.Sin(i * 2 * Mathf.PI * 65 / Rate);
            source.clip = AudioClip.Create("Speech", sound.Length, 1, Rate, false);
            source.clip.SetData(sound, 0);
            source.Play();
            if (paused && !preview) source.Pause();
        }
    }
}
