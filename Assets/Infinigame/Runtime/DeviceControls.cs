using System.Collections.Generic;
using Infinigame.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.UI;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace Infinigame
{
    // Device input fills the same frame of intent as desktop controls and tests.
    public class DeviceControls : MonoBehaviour
    {
        public Game game;
        Canvas touchCanvas;
        LineRenderer pointerRay;
        readonly Text[] labels = new Text[16];
        readonly Image[] plates = new Image[16];
        HashSet<int> touching = new HashSet<int>(), currentTouches = new HashSet<int>();
        bool expanded;
        public bool Expanded => expanded && HudActions && !VR;
        ForearmConsole forearm;
        int cockpitTouch = -1;
        float touchFireUntil;
        string tappedWeapon;
        public static bool Touch { get; private set; }
        public static string Key(string keyboard, string pad, string touch, string vr) => VR ? vr : Touch ? touch : DesktopControls.UsingPad ? pad : keyboard;
        public static string Fire => Key("CLICK", "RT", "TAP FIRE", "PUNCH / TRIGGER");
        public static string Use => Key("F", "X", "USE", "RIGHT GRIP");
        public static string Move => Key("W A S D", "LEFT STICK", "LEFT JOYSTICK", "LEFT STICK");
        public static string Look => Key("MOUSE", "RIGHT STICK", "RIGHT JOYSTICK", "TURN YOUR HEAD");
        readonly Image[] joysticks = new Image[2], thumbs = new Image[2];
        readonly Vector2[] deflections = new Vector2[2];
        readonly int[] fingers = { -1, -1 };
        bool trigger, grip, menu, primary, secondary, turning, navigating, leftPrimary, leftSecondary, stick;
        TouchScreenKeyboard keyboard;
        readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        public static bool VR => XRSettings.isDeviceActive;
        public static bool Deck =>
#if INFINIGAME_DECK
            true;
#else
            false;
#endif
        public static bool HudActions => Application.isMobilePlatform || Deck || Application.platform == RuntimePlatform.WindowsPlayer
            || Application.platform == RuntimePlatform.LinuxPlayer || Application.isEditor && Touch;
        public static bool Pointer => !VR && (Deck || Application.isMobilePlatform || HudActions && Keyboard.current?.altKey.isPressed == true);
        void Start()
        {
            if ((Application.isMobilePlatform || VR || Deck) && !PlayerPrefs.HasKey($"{Game.Prefs}.detail")) QualitySettings.SetQualityLevel(VR ? 1 : 0);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            Touch = Application.isMobilePlatform;
            if (VR)
            {
                forearm = game.gameObject.AddComponent<ForearmConsole>(); forearm.game = game;
                SubsystemManager.GetSubsystems(inputs);
                foreach (var input in inputs) input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
            }
        }
        public Controls Poll() => Read();
        Controls Read()
        {
            var c = DesktopControls.Read();
            if (game.Paused || game.Hermit.Talking || game.ShowControls) c.menu |= Gamepad.current?.buttonEast.wasPressedThisFrame == true;
            if (Keyboard.current?.anyKey.wasPressedThisFrame == true || DesktopControls.UsingPad || Mouse.current?.delta.ReadValue().sqrMagnitude > 1 || Mouse.current?.leftButton.wasPressedThisFrame == true) Touch = false;
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
                c.voice = travel && modifier;
                c.confirm = b && !primary;
                c.cycle = next && !secondary && !modifier ? 1 : 0;
                c.scan = fire && !trigger && modifier;
                if (c.scan) c.fire = false;
                c.systemView = map && !leftSecondary && !modifier;
                c.up = turn.y > 0.7f && !navigating;
                c.down = turn.y < -0.7f && !navigating;
                navigating = Mathf.Abs(turn.y) > 0.4f;
                if (game.Paused || game.Hermit.Talking) c.menu |= next && !secondary;
                if (game.Hermit.Talking) c.typed = b && !primary ? "1" : a && !leftPrimary ? "2" : use && !grip ? "3" : null;
                bool pointing = game.Paused || game.Hermit.Talking || game.ShowControls || game.Player.SystemView;
                if (pointerRay == null)
                {
                    pointerRay = new GameObject("Index menu pointer").AddComponent<LineRenderer>();
                    pointerRay.transform.SetParent(transform, false); pointerRay.sharedMaterial = Shapes.Material("Glow", new Color(0.25f, 0.8f, 1));
                    pointerRay.positionCount = 2; pointerRay.startWidth = pointerRay.endWidth = 0.004f;
                }
                bool tracked = game.Player.Hand(XRNode.RightHand, out var hand, out var rotation);
                pointerRay.enabled = false;
                if (tracked)
                {
                    var ray = new Ray(hand, rotation * Vector3.forward);
                    if (pointing)
                    {
                        bool hit = game.GetComponent<Hud>().Point(hand, ray.direction, out c.pointer, out var end);
                        pointerRay.enabled = true; pointerRay.SetPosition(0, hand); pointerRay.SetPosition(1, end);
                        c.click = hit && fire && !trigger; c.interfaceActive = true; c.fire = false;
                    }
                    else
                    {
                        bool touched = forearm != null && forearm.Touch(ray.GetPoint(0.1f), ref c);
                        if (forearm != null && forearm.Point(ray, out var end))
                        {
                            pointerRay.enabled = true; pointerRay.SetPosition(0, hand); pointerRay.SetPosition(1, end);
                            if (fire && !trigger && !touched) forearm.Activate(forearm.At(end), ref c);
                            c.interfaceActive = true;
                        }
                        c.interfaceActive |= touched;
                        if (c.interfaceActive) c.interact = false;
                        int control = game.Player.Seated ? game.Player.Cockpit.TouchControl(ray.GetPoint(0.1f)) : -1;
                        if (!c.interfaceActive && control >= 0)
                        {
                            c.interact = false;
                            if (control != cockpitTouch || control == 9) Cockpit.Activate(control, ref c);
                            c.interfaceActive = control != 9;
                        }
                        cockpitTouch = control;
                        if (c.interfaceActive) c.fire = false;
                    }
                }
                (trigger, grip, menu, primary, secondary, leftPrimary, leftSecondary, stick) = (fire, use, pause, b, next, a, map, travel);
                return c;
            }
            if (game.Paused && keyboard != null || Touch && game.Hermit.Talking && !game.Hermit.Thinking)
            {
                if (keyboard == null) keyboard = TouchScreenKeyboard.Open(game.Hermit.Draft, TouchScreenKeyboardType.Default, false, false, false, false, "Talk to the robot", 160);
                if (keyboard != null)
                {
                    if (game.Paused) c.typed = keyboard.text; else c.replaceText = keyboard.text;
                    if (keyboard.status == TouchScreenKeyboard.Status.Done) { c.confirm = !game.Title; keyboard = null; }
                }
            }
            else if (keyboard != null) { keyboard.active = false; keyboard = null; }
            if (HudActions && Cursor.lockState != CursorLockMode.Locked && Mouse.current != null)
            {
                if (Mouse.current.leftButton.isPressed) ScreenActions(Mouse.current.position.ReadValue(), Mouse.current.leftButton.wasPressedThisFrame, ref c);
                if (Mouse.current.rightButton.isPressed && !game.Paused && !game.Player.SystemView) c.look += Mouse.current.delta.ReadValue() * DesktopControls.Sensitivity;
            }
            var screen = Touchscreen.current;
            currentTouches.Clear();
            deflections[0] = deflections[1] = Vector2.zero;
            if (screen != null) foreach (var touch in screen.touches)
            {
                if (!touch.press.isPressed) continue;
                Touch = true;
                Vector2 at = touch.position.ReadValue();
                int id = touch.touchId.ReadValue();
                bool pressed = !touching.Contains(id); currentTouches.Add(id);
                c.pointer = at;
                c.click |= pressed;
                bool button = ScreenActions(at, pressed, ref c);
                if (button || game.Paused || game.Hermit.Talking || game.Dead || game.Player.SystemView) continue;
                for (int stickIndex = 0; stickIndex < 2; stickIndex++)
                {
                    if (pressed && fingers[stickIndex] < 0 && StickArea(stickIndex).Contains(at)) fingers[stickIndex] = id;
                    if (fingers[stickIndex] != id) continue;
                    deflections[stickIndex] = Vector2.ClampMagnitude((at - StickArea(stickIndex).center) / (60 * Unit), 1);
                    if (stickIndex == 0) c.move += deflections[stickIndex];
                    else c.look += new Vector2(deflections[1].x, deflections[1].y) * (110 * Time.unscaledDeltaTime);
                }
            }
            if (game.Paused || game.Dead || game.Hermit.Talking || game.ShowControls || game.Player.SystemView || c.menu || c.systemView || c.select != 0 || c.cycle != 0 || tappedWeapon != game.Player.Weapon.name) touchFireUntil = 0;
            c.fire |= Time.time < touchFireUntil;
            (touching, currentTouches) = (currentTouches, touching);
            for (int i = 0; i < 2; i++) if (!touching.Contains(fingers[i])) fingers[i] = -1;
            c.lift = c.jump ? 1 : c.lift;
            return c;
        }
        void TapFire(ref Controls c)
        {
            c.fire = true;
            var weapon = game.Player.Weapon; tappedWeapon = weapon.name;
            touchFireUntil = Time.time + (weapon.rail ? weapon.charge + 0.05f : weapon.beam ? 0.35f : 0);
        }
        bool ScreenActions(Vector2 at, bool pressed, ref Controls c)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                if (!Visible(i) || !Area(i).Contains(at)) continue;
                if (i == 0 && pressed) TapFire(ref c);
                if (i == 1) c.jump = true;
                if (i == 2) c.interact |= pressed;
                if (i == 3 && pressed) expanded = !expanded;
                if (i == 4) c.menu |= pressed;
                if (i == 5) c.systemView |= pressed;
                if (i == 6) c.cycle = pressed ? 1 : c.cycle;
                if (i == 7) c.voice = true;
                if (i == 8) c.travel |= pressed;
                if (i == 9) c.scan |= pressed;
                if (i == 10) c.ride |= pressed;
                if (i == 11) c.toggleView |= pressed;
                if (i == 12) c.talk |= pressed;
                if (i == 13) c.reload |= pressed;
                if (i == 14) c.lift = -1;
                if (i == 15) c.toggleAssist |= pressed;
                return true;
            }
            if (!game.Paused && !game.ShowControls && !game.Player.SystemView && !game.Hermit.Talking && game.Player.Seated)
            {
                int control = game.Player.Cockpit.ScreenControl(at, Camera.main);
                if (control >= 0)
                {
                    if (pressed) { Cockpit.Activate(control, ref c); if (control == 9) TapFire(ref c); }
                    return true;
                }
            }
            return false;
        }
        float Unit => Mathf.Clamp(Screen.safeArea.height / 720f, 0.8f, 2.5f);
        Rect StickArea(int index)
        {
            float size = 140 * Unit;
            var centre = new Vector2(index == 0 ? Screen.safeArea.xMin + 140 * Unit : Screen.safeArea.xMax - 285 * Unit, Screen.safeArea.yMin + 170 * Unit);
            return new Rect(centre - Vector2.one * size * 0.5f, Vector2.one * size);
        }
        Rect Area(int index)
        {
            Rect safe = Screen.safeArea;
            float u = Unit;
            Vector2 centre = index switch
            {
                0 => new Vector2(safe.xMax - 92 * u, safe.yMin + 128 * u),
                1 => new Vector2(safe.xMax - 205 * u, safe.yMin + 55 * u),
                2 => new Vector2(safe.xMax - 90 * u, safe.yMin + 260 * u),
                3 => new Vector2(safe.xMin + 48 * u, safe.yMin + 48 * u),
                4 => new Vector2(safe.xMax - 45 * u, safe.yMax - 42 * u),
                5 => new Vector2(safe.xMax - 135 * u, safe.yMax - 42 * u),
                6 => new Vector2(safe.xMin + 132 * u, safe.yMin + 48 * u),
                8 => new Vector2(safe.xMin + 216 * u, safe.yMin + 48 * u),
                10 => new Vector2(safe.xMin + 300 * u, safe.yMin + 48 * u),
                12 => new Vector2(safe.xMin + 384 * u, safe.yMin + 48 * u),
                _ => new Vector2(safe.xMin + (48 + (index - 6) % 3 * 90) * u, safe.yMin + (132 + (index - 6) / 3 * 84) * u),
            };
            if (index >= 6 && expanded) centre = new Vector2(safe.center.x + ((index - 6) % 4 - 1.5f) * 90 * u, safe.center.y + (1 - (index - 6) / 4) * 90 * u);
            float size = (index == 0 ? 104 : 72) * u;
            return new Rect(centre - Vector2.one * size * 0.5f, Vector2.one * size);
        }
        bool Visible(int i) => (!game.Fall.active && !game.Tutorial.Locked || i == 4) && !game.Paused && !game.Dead && !game.Hermit.Talking && (!game.ShowControls || i == 4)
            && (!game.Player.SystemView || i == 5 || i == 4) && (i < 6 || i == 6 || expanded)
            && (i != 8 || game.Player.Has(Gear.Ship) || game.Stage == 4)
            && (i != 13 || !game.Player.flying && game.Player.Has(Gear.Stealth))
            && (i != 15 || game.Player.Seated) && (i != 10 || !game.Player.flying && game.Player.Powered)
            && (i != 6 || game.Player.Powered) && (i != 14 || game.Player.Has(Gear.Thrusters) || game.Player.flying)
            && (i != 1 || !game.Buried) && (i != 2 || game.Player.Prompt != null || game.Outpost.Near || game.Hermit.Near);
        void LateUpdate()
        {
            if (touchCanvas == null)
            {
                var host = new GameObject("Touch controls", typeof(Canvas));
                touchCanvas = host.GetComponent<Canvas>();
                touchCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                touchCanvas.sortingOrder = 30;
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var texture = new Texture2D(64, 64);
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(31.5f - Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)))));
                texture.Apply();
                var circle = Sprite.Create(texture, new Rect(0, 0, 64, 64), Vector2.one * 0.5f);
                for (int i = 0; i < 2; i++)
                {
                    joysticks[i] = new GameObject(i == 0 ? "Move joystick" : "Look joystick", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    joysticks[i].transform.SetParent(host.transform, false); joysticks[i].sprite = circle;
                    joysticks[i].color = new Color(0.03f, 0.12f, 0.17f, 0.65f);
                    thumbs[i] = new GameObject("Thumb", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    thumbs[i].transform.SetParent(joysticks[i].transform, false); thumbs[i].sprite = circle;
                    thumbs[i].color = new Color(0.35f, 0.85f, 1, 0.6f);
                    var name = new GameObject("Stick label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                    name.transform.SetParent(thumbs[i].transform, false); name.font = font; name.text = i == 0 ? "MOVE" : "LOOK";
                    name.fontSize = 12; name.alignment = TextAnchor.MiddleCenter; name.color = Color.white;
                    name.rectTransform.sizeDelta = new Vector2(54, 32);
                }
                for (int i = 0; i < labels.Length; i++)
                {
                    var plate = new GameObject("Touch " + i, typeof(RectTransform), typeof(Image));
                    plate.transform.SetParent(host.transform, false);
                    plates[i] = plate.GetComponent<Image>(); plates[i].sprite = circle;
                    plates[i].color = new Color(0.02f, 0.08f, 0.12f, 0.84f);
                    var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
                    label.transform.SetParent(plate.transform, false);
                    labels[i] = label.GetComponent<Text>(); labels[i].font = font;
                    labels[i].alignment = TextAnchor.MiddleCenter; labels[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                    labels[i].resizeTextForBestFit = true; labels[i].resizeTextMinSize = 11;
                    labels[i].color = new Color(0.55f, 0.92f, 1);
                    labels[i].rectTransform.anchorMin = Vector2.zero; labels[i].rectTransform.anchorMax = Vector2.one;
                    labels[i].rectTransform.offsetMin = new Vector2(8, 8); labels[i].rectTransform.offsetMax = new Vector2(-8, -8);
                    labels[i].text = new[] { "FIRE", "LIFT", "USE", "···", "MENU", "MAP", "ARMS", "MIC", "RECALL", "SCAN", "BUILD", "VIEW", "ROBOT", "CLOAK", "DOWN", "ASSIST" }[i];
                }
            }
            touchCanvas.enabled = HudActions && !VR;
            for (int i = 0; i < 2; i++)
            {
                bool show = !game.Paused && !game.Hermit.Talking && !game.ShowControls && !game.Player.SystemView && !game.Buried && !game.Dead && !game.Fall.active;
                joysticks[i].enabled = show; thumbs[i].gameObject.SetActive(show);
                var rect = joysticks[i].rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
                rect.anchoredPosition = StickArea(i).position; rect.sizeDelta = StickArea(i).size;
                thumbs[i].rectTransform.sizeDelta = Vector2.one * (60 * Unit);
                thumbs[i].rectTransform.anchoredPosition = deflections[i] * (40 * Unit);
            }
            labels[8].text = game.Player.flying ? "AUTO" : "RECALL";
            for (int i = 0; i < labels.Length; i++)
            {
                plates[i].gameObject.SetActive(Visible(i));
                Rect area = Area(i);
                var rect = plates[i].rectTransform;
                rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = Vector2.zero;
                rect.anchoredPosition = area.position; rect.sizeDelta = area.size;
                labels[i].resizeTextMaxSize = labels[i].fontSize = Mathf.RoundToInt((i == 0 ? 22 : 17) * Unit);
            }
        }
        void OnDestroy() { if (touchCanvas != null) Destroy(touchCanvas.gameObject); }
    }
}
