using System.Collections.Generic;
using Infinigame.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace Infinigame
{
    // Device input fills the same frame of intent as desktop controls and tests.
    public class DeviceControls : MonoBehaviour
    {
        public Game game;
        Controls buttons;
        Vector2 origin;
        int finger = -1;
        bool trigger, grip, menu, primary, secondary, turning, navigating, leftPrimary, leftSecondary, stick;
        TouchScreenKeyboard keyboard;
        readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        public static bool VR => XRSettings.isDeviceActive;
        void Start()
        {
            if (!Application.isMobilePlatform && !VR) return;
            QualitySettings.SetQualityLevel(VR ? 1 : 0);
            game.Player.controls = Read;
            if (VR)
            {
                SubsystemManager.GetSubsystems(inputs);
                foreach (var input in inputs) input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
            }
        }
        Controls Read()
        {
            var c = buttons;
            buttons = default;
            if (VR)
            {
                var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 move);
                right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 turn);
                right.TryGetFeatureValue(CommonUsages.triggerButton, out bool fire);
                right.TryGetFeatureValue(CommonUsages.gripButton, out bool use);
                left.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool pause);
                left.TryGetFeatureValue(CommonUsages.primaryButton, out bool a);
                right.TryGetFeatureValue(CommonUsages.primaryButton, out bool b);
                right.TryGetFeatureValue(CommonUsages.secondaryButton, out bool next);
                left.TryGetFeatureValue(CommonUsages.secondaryButton, out bool map);
                left.TryGetFeatureValue(CommonUsages.gripButton, out bool modifier);
                right.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool travel);
                c.move = move * (game.Player.flying ? 1 : 0.45f);
                if (game.Player.flying)
                {
                    c.look = new Vector2(modifier ? 0 : turn.x, turn.y) * (90 * Time.deltaTime);
                    c.roll = modifier ? turn.x : 0;
                }
                else if (Mathf.Abs(turn.x) > 0.7f && !turning) c.look.x = Mathf.Sign(turn.x) * 30;
                turning = Mathf.Abs(turn.x) > 0.4f;
                c.fire = fire;
                c.interact = use && !grip && !modifier;
                c.menu = pause && !menu;
                c.jump = a && !modifier;
                c.lift = c.jump ? 1 : use && modifier ? -1 : 0;
                c.talk = a && !leftPrimary && modifier;
                c.ride = map && !leftSecondary && modifier;
                c.reload = next && !secondary && modifier;
                c.travel = travel && !stick && !modifier;
                if (travel && !stick && modifier) game.GetComponent<VoiceInput>().Toggle();
                c.confirm = b && !primary;
                c.cycle = next && !secondary && !modifier ? 1 : 0;
                c.scan = fire && !trigger && modifier;
                if (c.scan) c.fire = false;
                c.systemView = map && !modifier;
                c.up = turn.y > 0.7f && !navigating;
                c.down = turn.y < -0.7f && !navigating;
                navigating = Mathf.Abs(turn.y) > 0.4f;
                if (game.Paused || game.Hermit.Talking) c.menu |= next && !secondary;
                if (game.Hermit.Talking) c.typed = b && !primary ? "1" : a && !leftPrimary ? "2" : use && !grip ? "3" : null;
                (trigger, grip, menu, primary, secondary, leftPrimary, leftSecondary, stick) = (fire, use, pause, b, next, a, map, travel);
                return c;
            }
            if (game.Hermit.Talking && !game.Hermit.Thinking)
            {
                if (keyboard == null) keyboard = TouchScreenKeyboard.Open(game.Hermit.Draft, TouchScreenKeyboardType.Default, false, false, false, false, "Talk to the robot", 160);
                if (keyboard != null)
                {
                    c.replaceText = keyboard.text;
                    if (keyboard.status == TouchScreenKeyboard.Status.Done) { c.confirm = true; keyboard = null; }
                }
            }
            else if (keyboard != null) { keyboard.active = false; keyboard = null; }
            var screen = Touchscreen.current;
            if (screen == null) return c;
            foreach (var touch in screen.touches)
            {
                if (!touch.press.isPressed) { if (touch.touchId.ReadValue() == finger) finger = -1; continue; }
                Vector2 at = touch.position.ReadValue();
                c.pointer = at;
                c.click |= touch.press.wasPressedThisFrame;
                if (at.y < Screen.height * 0.28f || game.Paused || game.Hermit.Talking || game.Dead) continue;
                if (at.x < Screen.width * 0.4f)
                {
                    if (finger < 0) { finger = touch.touchId.ReadValue(); origin = at; }
                    if (touch.touchId.ReadValue() == finger) c.move = Vector2.ClampMagnitude((at - origin) / (Screen.height * 0.12f), 1);
                }
                else c.look += touch.delta.ReadValue() * 0.12f;
            }
            c.lift = c.jump ? 1 : c.lift;
            return c;
        }
        void OnGUI()
        {
            if (!Application.isMobilePlatform || Application.isBatchMode) return;
            float width = Screen.safeArea.width / 7, height = Screen.safeArea.height * 0.1f, bottom = Screen.height - Screen.safeArea.yMin - height;
            string[] labels = { game.Paused ? "Choose" : "Fire", "Lift", "Use", "Scan", "Weapon", "Map", "Menu" };
            for (int i = 0; i < labels.Length; i++)
            {
                var rect = new Rect(Screen.safeArea.xMin + i * width, bottom, width - 5, height);
                bool down = (i == 0 && !game.Paused || i == 1 || i == 5) ? GUI.RepeatButton(rect, labels[i]) : GUI.Button(rect, labels[i]);
                var above = new Rect(rect.x, rect.y - height * 0.6f, rect.width, height * 0.5f);
                string label = i == 6 ? "Robot" : i == 5 ? "Factory" : i == 4 ? "Mic" : i == 3 ? "Auto" : i == 2 ? "Down" : i == 1 ? "Cloak" : "View";
                bool pressed = i == 2 ? GUI.RepeatButton(above, label) : GUI.Button(above, label);
                if (down)
                {
                    if (i == 0) buttons.fire = buttons.confirm = true;
                    if (i == 1) buttons.jump = true;
                    if (i == 2) buttons.interact = true;
                    if (i == 3) buttons.scan = true;
                    if (i == 4) buttons.cycle = 1;
                    if (i == 5) buttons.systemView = true;
                    if (i == 6) buttons.menu = true;
                }
                if (!pressed) continue;
                if (i == 6) buttons.talk = true;
                if (i == 5) buttons.ride = true;
                if (i == 4) game.GetComponent<VoiceInput>().Toggle();
                if (i == 3) buttons.travel = true;
                if (i == 2) buttons.lift = -1;
                if (i == 1) buttons.reload = true;
                if (i == 0) buttons.toggleView = true;
            }
        }
    }
}
