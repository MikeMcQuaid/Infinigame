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
#endif
        public void Toggle()
        {
            if (game.Title || game.Buried || game.Hermit.Thinking) return;
            if (Capture != null) { Capture(this); return; }
            if (Application.isBatchMode) return;
            try
            {
#if UNITY_STANDALONE_WIN
                if (Listening) { dictation.Stop(); Listening = false; return; }
                dictation?.Dispose();
                dictation = new DictationRecognizer();
                dictation.DictationResult += (text, confidence) => Submit(text);
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
                Listening = true; began = Time.unscaledTime;
                game.Notify("LISTENING · PRESS Y OR MIC TO FINISH");
#endif
            }
            catch (Exception) { Listening = false; game.Notify("VOICE UNAVAILABLE · CHECK MICROPHONE PERMISSION OR TYPE WITH RETURN"); }
        }
        public void Submit(string text)
        {
#if UNITY_STANDALONE_WIN
            dictation?.Stop();
#endif
            Listening = false;
            if (string.IsNullOrWhiteSpace(text)) return;
            game.Hermit.Greet();
            game.Hermit.Say(text);
            game.Notify("YOU: " + text);
        }
        void Update()
        {
            if (Keyboard.current?.yKey.wasPressedThisFrame == true && (Listening || !game.Hermit.Talking)) Toggle();
#if (UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR
            if (!Listening) return;
            int state = InfinigameVoiceState();
            if (state == 3)
            {
                IntPtr value = InfinigameVoiceText(); string text = Marshal.PtrToStringUTF8(value); InfinigameVoiceFree(value);
                InfinigameVoiceCancel(); Submit(text);
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
            if (Listening) dictation?.Stop(); dictation?.Dispose(); dictation = null;
#elif (UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR
            if (Listening) InfinigameVoiceCancel();
#endif
            Listening = false;
        }
        void OnGUI()
        {
            if (!Application.isMobilePlatform || game.Title || Application.isBatchMode) return;
            if (GUI.Button(new Rect(Screen.safeArea.xMax - 90, Screen.height - Screen.safeArea.yMax + 80, 80, 50), Listening ? "Stop mic" : "Mic")) Toggle();
        }
    }
}
