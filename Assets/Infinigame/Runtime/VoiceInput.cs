using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_STANDALONE_WIN
using UnityEngine.Windows.Speech;
#endif

namespace Infinigame
{
    public class VoiceInput : MonoBehaviour
    {
        public Game game;
        public static Action<VoiceInput> Capture;
        public bool Listening { get; private set; }
        float began;
        bool held;
        string prefix;
        public string Transcript { get; private set; } = "";
        public void Hold(bool down)
        {
            if (down != held && (down || Listening)) Toggle();
            held = down;
        }
#if UNITY_IOS && !UNITY_EDITOR
        const string Library = "__Internal";
#else
        const string Library = "InfinigameVoice";
#endif
        [DllImport(Library)] static extern void InfinigameVoiceStart();
        [DllImport(Library)] static extern void InfinigameVoiceStop();
        [DllImport(Library)] static extern void InfinigameVoiceCancel();
        [DllImport(Library)] static extern int InfinigameVoiceState();
        [DllImport(Library)] static extern IntPtr InfinigameVoiceText();
        [DllImport(Library)] static extern void InfinigameVoiceFree(IntPtr text);
#if UNITY_STANDALONE_WIN
        DictationRecognizer dictation;
        string recognised;
#endif
        public void Toggle()
        {
            if (!Oracle.Enabled || game.Title || game.Buried || game.Tutorial.Locked || game.Hermit.Thinking) return;

            if (!Listening)
            {
                prefix = game.Hermit.Talking ? game.Hermit.Draft.TrimEnd() : "";
                game.Hermit.Dictate(prefix);
            }
            if (Capture != null) { Capture(this); return; }
            if (Application.isBatchMode) return;
            try
            {
#if UNITY_STANDALONE_WIN
                if (Listening) { Submit(Transcript); return; }
                dictation?.Dispose();
                dictation = new DictationRecognizer();
                recognised = "";
                dictation.DictationHypothesis += text => { if (Listening) Preview((recognised + " " + text).Trim()); };
                dictation.DictationResult += (text, confidence) =>
                {
                    if (!Listening) return;
                    recognised = (recognised + " " + text).Trim();
                    Preview(recognised);
                };
                dictation.DictationError += (error, code) => { Listening = false; game.Notify("VOICE UNAVAILABLE · TYPE WITH RETURN"); };
                dictation.Start();
#elif (UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR
                if (Listening) { InfinigameVoiceStop(); return; }
                InfinigameVoiceStart();
#else
                game.Notify("VOICE INPUT IS AVAILABLE IN DEVICE BUILDS · TYPE WITH RETURN");
                return;
#endif
#if UNITY_STANDALONE_WIN || ((UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR)
                Listening = true; Transcript = ""; began = Time.unscaledTime;
#endif
            }
            catch (Exception) { Listening = false; game.Notify("VOICE UNAVAILABLE · CHECK MICROPHONE PERMISSION OR TYPE WITH RETURN"); }
        }
        public void Preview(string text)
        {
            if (!Oracle.Enabled) return;
            Transcript = text ?? "";
            game.Hermit.Dictate(string.IsNullOrEmpty(prefix) ? Transcript : (prefix + " " + Transcript).TrimEnd());
        }

        public void Submit(string text)
        {
#if UNITY_STANDALONE_WIN
            if (dictation?.Status == SpeechSystemStatus.Running) dictation.Stop();
#endif
            Listening = false;
            if (!Oracle.Enabled || string.IsNullOrWhiteSpace(text)) return;
            Preview(text);
            game.Hermit.Converse(new Controls { confirm = true });
            prefix = "";
        }
        void Update()
        {
            if (Listening && (!Oracle.Enabled || !game.Hermit.Talking)) { Stop(); return; }
#if (UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR
            if (!Listening) return;
            int state = InfinigameVoiceState();
            if (state >= 2)
            {
                IntPtr value = InfinigameVoiceText(); Preview(Marshal.PtrToStringUTF8(value)); InfinigameVoiceFree(value);
                if (state == 3) { InfinigameVoiceCancel(); Submit(Transcript); }
            }
            else if (state < 0 || Time.unscaledTime - began > 30)
            {
                InfinigameVoiceCancel(); Listening = false;
                game.Notify("LOCAL DICTATION UNAVAILABLE OR TIMED OUT · TYPE WITH RETURN");
            }
#endif
        }
        void OnApplicationFocus(bool focused)
        {
#if (UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR
            if (!focused && Listening && InfinigameVoiceState() == 2) Stop();
#else
            if (!focused) Stop();
#endif
        }
        void OnDestroy() => Stop();
        void Stop()
        {
#if UNITY_STANDALONE_WIN
            if (dictation?.Status == SpeechSystemStatus.Running) dictation.Stop();
            dictation?.Dispose(); dictation = null;
#elif (UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR
            if (Listening) InfinigameVoiceCancel();
#endif
            Listening = false;
        }
    }
}
