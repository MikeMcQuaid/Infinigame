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
        public bool ReadingWrist => VR && forearm != null && forearm.Reading;
        float touchFireUntil, mapSince;
        bool mapHeld, consumedFire, consumedLift, wristMic, mouseMic;
        int micTouch = -1, fireTouch = -1;
        string tappedWeapon;
        public static bool Touch { get; private set; }
        public static string Key(string keyboard, string pad, string touch, string vr) => VR ? vr : Touch ? touch : (DesktopControls.UsingPad || Deck && !DesktopControls.KeyboardUsed) ? pad : keyboard;
        public static string Fire => Key("CLICK", "RT", "TAP FIRE", "PUNCH / TRIGGER");
        public static string Use => Key("F", "X", "USE", "RIGHT A");
        public static string Move => Key("W A S D", "LEFT STICK", "LEFT JOYSTICK", "LEFT STICK");
        public static string Look => Key("MOUSE", "RIGHT STICK", "SWIPE RIGHT SIDE", "TURN YOUR HEAD");
        readonly Image[] joysticks = new Image[2], thumbs = new Image[2];
        Vector2 moveOrigin, moveThumb;
        readonly int[] fingers = { -1, -1 };
        readonly Dictionary<int, (Vector2 at, float time)> taps = new Dictionary<int, (Vector2, float)>();
        bool trigger, leftTrigger, grip, menuHeld, primary, secondary, turning, cycling, leftPrimary, leftSecondary, stick;
        StickNavigation navigation;
        TouchScreenKeyboard keyboard;
        readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
#if UNITY_EDITOR
        public static bool SimulateHeadset;
        public static bool VR => SimulateHeadset || XRSettings.isDeviceActive;
#else
        public static bool VR => XRSettings.isDeviceActive;
#endif
        public static bool Deck =>
            System.Environment.GetEnvironmentVariable("SteamDeck") == "1" || SystemInfo.deviceModel.IndexOf("Jupiter", System.StringComparison.OrdinalIgnoreCase) >= 0 || SystemInfo.deviceModel.IndexOf("Galileo", System.StringComparison.OrdinalIgnoreCase) >= 0;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartWindowsDisplay()
        {
            var args = System.Environment.GetCommandLineArgs();
            bool requestVR = System.Array.IndexOf(args, "-vr") >= 0;
            if (Deck && !requestVR)
            {
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                return;
            }
            var manager = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager;
            if (manager == null) return;
            manager.InitializeLoaderSync();
            if (manager.activeLoader == null) return;
            manager.StartSubsystems();
            Application.quitting += () => { manager.StopSubsystems(); manager.DeinitializeLoader(); };
        }
#endif
        public static bool HudActions => Application.isMobilePlatform || Deck || Application.platform == RuntimePlatform.WindowsPlayer
            || Application.platform == RuntimePlatform.LinuxPlayer || Application.isEditor && Touch;
        public static bool Pointer => !VR && (Deck || Application.isMobilePlatform || HudActions && Keyboard.current?.altKey.isPressed == true);
        void Start()
        {
            if ((Application.isMobilePlatform || VR || Deck) && !PlayerPrefs.HasKey($"{Game.Prefs}.detail")) QualitySettings.SetQualityLevel(VR ? 1 : 0);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            Touch = Application.isMobilePlatform;
        }
        public Controls Poll() => Read();
        Controls Read()
        {
            var c = VR ? default : DesktopControls.Read(game.Player.flying, game.InvertShipPitch);
            if (game.Hermit.Talking && Keyboard.current?.yKey.isPressed == true && !game.GetComponent<VoiceInput>().Listening) c.voice = false;
            if (game.Paused || game.Hermit.Talking || game.ShowControls) c.menu |= Gamepad.current?.buttonEast.wasPressedThisFrame == true;
            if (Keyboard.current?.anyKey.wasPressedThisFrame == true || DesktopControls.UsingPad || Mouse.current?.delta.ReadValue().sqrMagnitude > 1 || Mouse.current?.leftButton.wasPressedThisFrame == true) Touch = false;
            if (VR) return ReadControllers(Controller.Read(XRNode.LeftHand), Controller.Read(XRNode.RightHand));
            if (game.Paused && keyboard != null || Touch && !game.GetComponent<VoiceInput>().Listening && game.Hermit.Talking && !game.Hermit.Thinking && (keyboard != null || Touchscreen.current?.primaryTouch.press.wasPressedThisFrame == true && game.GetComponent<Hud>().DraftAt(Touchscreen.current.primaryTouch.position.ReadValue())))
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
                if (Mouse.current.leftButton.isPressed)
                {
                    if (mouseMic) c.voice = true;
                    else { ScreenActions(Mouse.current.position.ReadValue(), Mouse.current.leftButton.wasPressedThisFrame, ref c); mouseMic = c.voice && !game.Hermit.Talking && Area(7).Contains(Mouse.current.position.ReadValue()); }
                }
                else mouseMic = false;
                if (Mouse.current.rightButton.isPressed && !game.Paused && !game.Player.SystemView) c.look += Mouse.current.delta.ReadValue() * DesktopControls.Sensitivity;
            }
            var screen = Touchscreen.current;
            currentTouches.Clear();
            moveThumb = Vector2.zero;
            bool playing = !game.Paused && !game.Hermit.Talking && !game.Dead && !game.ShowControls && !game.Player.SystemView;
            if (!playing) { fireTouch = fingers[0] = fingers[1] = -1; taps.Clear(); }
            Vector2? firstTouch = null, previousTouch = null;
            if (screen != null) foreach (var touch in screen.touches)
            {
                if (!touch.press.isPressed && taps.TryGetValue(touch.touchId.ReadValue(), out var tap))
                {
                    if (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Ended && playing && Time.unscaledTime - tap.time < 0.3f && Vector2.Distance(tap.at, touch.position.ReadValue()) < 20 * Unit) TapFire(ref c);
                    taps.Remove(touch.touchId.ReadValue());
                }
                if (!touch.press.isPressed) continue;
                Touch = true;
                Vector2 at = touch.position.ReadValue();
                int id = touch.touchId.ReadValue();
                bool pressed = !touching.Contains(id); currentTouches.Add(id);
                if (id == fireTouch) { c.automaticFire = c.fire = true; continue; }
                if (id == micTouch) { c.voice = true; continue; }
                if (game.Player.SystemView && !pressed)
                {
                    Vector2 previous = at - touch.delta.ReadValue();
                    if (firstTouch.HasValue)
                    {
                        float before = Vector2.Distance(previous, previousTouch.Value);
                        if (before > 2) c.mapPinch = Vector2.Distance(at, firstTouch.Value) / before;
                        c.mapDrag = Vector2.zero;
                    }
                    else { firstTouch = at; previousTouch = previous; c.mapDrag = touch.delta.ReadValue() / Mathf.Max(1, Screen.height); }
                }
                if (pressed) { c.pointer = at; c.click = true; }
                var start = touch.startPosition.ReadValue();
                if (id != fingers[0] && id != fingers[1] && ScreenActions(start, pressed, ref c))
                {
                    if (pressed && playing && c.automaticFire && Area(0).Contains(start)) { fireTouch = id; c.click = false; }
                    if (pressed && c.voice && Area(7).Contains(start)) { micTouch = id; c.click = false; }
                    continue;
                }
                if (!playing) continue;
                int side = start.x < Screen.safeArea.center.x ? 0 : 1;
                if (pressed && Screen.safeArea.Contains(start) && (StickArea(side).Contains(start) || !game.GetComponent<Hud>().BlocksFire(start)))
                {
                    taps[id] = (start, Time.unscaledTime);
                    if (fingers[side] < 0)
                    {
                        fingers[side] = id;
                        if (side == 0) moveOrigin = start;
                    }
                }
                if ((at - start).sqrMagnitude >= 400 * Unit * Unit) taps.Remove(id);
                if (fingers[side] != id) continue;
                if (side == 0)
                {
                    moveThumb = Vector2.ClampMagnitude((at - moveOrigin) / (60 * Unit), 1);
                    c.move += moveThumb;
                    c.sprint |= moveThumb.y >= 0.9f;
                }
                else c.look += touch.delta.ReadValue() * (150f / Mathf.Max(1, Screen.safeArea.height));
            }
            if (game.Paused || game.Dead || game.Hermit.Talking || game.ShowControls || game.Player.SystemView || c.menu || c.systemView || c.select != 0 || c.cycle != 0 || tappedWeapon != game.Player.Weapon.name) touchFireUntil = 0;
            c.fire |= Time.time < touchFireUntil;
            if (!currentTouches.Contains(fireTouch)) fireTouch = -1;
            if (!currentTouches.Contains(micTouch)) micTouch = -1;
            (touching, currentTouches) = (currentTouches, touching);
            for (int i = 0; i < 2; i++) if (!touching.Contains(fingers[i])) fingers[i] = -1;
            c.lift = c.jump ? 1 : c.lift;
            return c;
        }
        public struct Controller
        {
            public Vector2 axis;
            public bool trigger, grip, primary, secondary, click, menu;
            public static Controller Read(XRNode node)
            {
                var device = InputDevices.GetDeviceAtXRNode(node);
                var hand = new Controller();
                device.TryGetFeatureValue(CommonUsages.primary2DAxis, out hand.axis);
                device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out hand.click);
                device.TryGetFeatureValue(CommonUsages.triggerButton, out hand.trigger);
                device.TryGetFeatureValue(CommonUsages.gripButton, out hand.grip);
                device.TryGetFeatureValue(CommonUsages.primaryButton, out hand.primary);
                device.TryGetFeatureValue(CommonUsages.secondaryButton, out hand.secondary);
                device.TryGetFeatureValue(CommonUsages.menuButton, out hand.menu);
                return hand;
            }
        }
        public Controls ReadControllers(Controller left, Controller right)
        {
            var c = new Controls();
            c.move = left.axis * (game.Player.flying || left.axis.y >= 0.9f ? 1 : 0.45f);
            if (!game.Player.flying)
            {
                Vector3 up = Unity.Mathematics.math.normalizesafe(game.Player.Position);
                Vector3 facing = Vector3.ProjectOnPlane(Camera.main.transform.forward, up).normalized;
                Vector3 desired = facing * c.move.y + Vector3.Cross(up, facing) * c.move.x;
                Vector3 bodyForward = game.Player.walker.forward;
                c.move = new Vector2(Vector3.Dot(desired, Vector3.Cross(up, bodyForward)), Vector3.Dot(desired, bodyForward));
            }
            if (game.Player.flying)
            {
                c.look = new Vector2(left.axis.x, game.InvertShipPitch ? -right.axis.y : right.axis.y) * (90 * Time.deltaTime);
                c.move.x = 0;
                c.roll = right.axis.x;
            }
            else if (Mathf.Abs(right.axis.x) > 0.7f && Mathf.Abs(right.axis.x) >= Mathf.Abs(right.axis.y) && !turning) c.look.x = Mathf.Sign(right.axis.x) * 30;
            turning = Mathf.Abs(right.axis.x) > 0.4f;
            c.fire = right.trigger;
            bool pointing = game.Paused || game.Hermit.Talking || game.ShowControls || game.Player.SystemView || game.Dead;
            c.interact = right.primary && !primary && !pointing;
            if (left.secondary && !leftSecondary) { mapSince = Time.unscaledTime; mapHeld = false; }
            if (left.secondary && Time.unscaledTime - mapSince >= 0.55f) mapHeld = true;
            bool pause = left.menu || left.secondary && mapHeld;
            c.menu = pause && !menuHeld;
            c.jump = !game.Player.flying && left.trigger;
            c.lift = (c.jump ? 1 : 0) - (left.primary ? 1 : 0);
            c.sprint = right.grip || left.grip || left.click || (game.Player.flying ? left.trigger : left.axis.y >= 0.9f);
            bool selectTrigger = right.trigger && !trigger || left.trigger && !leftTrigger;
            c.confirm = pointing && (right.primary && !primary || selectTrigger);
            c.reload = right.click && !stick && !game.Player.flying;
            c.travel = right.click && !stick && (game.Player.flying || game.Player.SystemView);
            c.systemView = !left.secondary && leftSecondary && !mapHeld && (!pointing || game.Player.SystemView);
            c.cycle = right.secondary && !secondary && !pointing && !pause ? 1 : 0;
            if (!game.Player.flying && !pointing && !cycling && Mathf.Abs(right.axis.y) > 0.75f && Mathf.Abs(right.axis.y) > Mathf.Abs(right.axis.x))
                c.cycle = right.axis.y > 0 ? 1 : -1;
            cycling = Mathf.Abs(right.axis.y) > 0.4f;
            bool exploringMap = game.Player.SystemView && game.MapPage == 0 && !game.Paused;
            if (exploringMap)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                if (left.axis.sqrMagnitude > 0.0225f) c.mapDrag = -left.axis * (0.7f * dt);
                if (Mathf.Abs(right.axis.y) > 0.15f) c.mapPinch = Mathf.Exp(right.axis.y * 1.5f * dt);
            }
            int step = navigation.Read(pointing && !exploringMap ? left.axis.y : 0, pointing && !exploringMap ? right.axis.y : 0, Time.unscaledTime);
            c.up = step > 0;
            c.down = step < 0;
            if (pointing)
            {
                c.menu |= right.secondary && !secondary;
                c.fire = c.jump = c.sprint = false; c.interfaceActive = true;
                c.look = c.move = Vector2.zero; c.lift = c.roll = 0;
            }
            if (game.Hermit.Talking) c.typed = right.primary && !primary ? "1" : left.primary && !leftPrimary ? "2" : right.grip && !grip ? "3" : null;
            if (pointerRay == null)
            {
                pointerRay = new GameObject("Index menu pointer").AddComponent<LineRenderer>();
                pointerRay.transform.SetParent(transform, false); pointerRay.sharedMaterial = Shapes.Beam(Color.white);
                pointerRay.positionCount = 2; pointerRay.startWidth = pointerRay.endWidth = 0.004f;
            }
            bool tracked = game.Player.Aim(XRNode.RightHand, out var hand, out var rotation);
            pointerRay.enabled = false;
            pointerRay.startColor = pointerRay.endColor = new Color(0.25f, 0.8f, 1);
            var hud = game.GetComponent<Hud>();
            if (!tracked || !pointing) hud.HighlightPointer(null);
            if (!tracked || pointing) forearm?.ClearContact();
            if (tracked)
            {
                var ray = new Ray(hand, rotation * Vector3.forward);
                if (pointing)
                {
                    bool hit = hud.Point(hand, ray.direction, out c.pointer, out var end);
                    bool hovered = hud.HighlightPointer(hit ? c.pointer : (Vector2?)null);
                    pointerRay.enabled = true; pointerRay.SetPosition(0, hand); pointerRay.SetPosition(1, end);
                    if (hovered) pointerRay.startColor = pointerRay.endColor = new Color(0.65f, 1, 0.48f);
                    c.click = hit && selectTrigger && (hovered || game.Hermit.Talking);
                    if (c.click) c.confirm = false;
                }
                else
                {
                    bool touched = forearm != null && forearm.Touch(ray.GetPoint(0.1f), ref c);
                    if (forearm != null && forearm.Point(ray, out var end))
                    {
                        pointerRay.enabled = true; pointerRay.SetPosition(0, hand); pointerRay.SetPosition(1, end);
                        pointerRay.startColor = pointerRay.endColor = new Color(0.65f, 1, 0.48f);
                        if (selectTrigger && !touched) forearm.Activate(forearm.At(end), ref c);
                        c.interfaceActive = true;
                    }
                    c.interfaceActive |= touched;
                    if (c.interfaceActive) c.interact = false;
                    if (c.interfaceActive) c.fire = false;
                }
            }
            if (wristMic && (right.trigger || tracked && forearm != null && forearm.HoldingMic(hand + rotation * Vector3.forward * 0.1f))) c.voice = true;
            wristMic = c.voice;
            consumedFire = right.trigger && (consumedFire || pointing || c.interfaceActive);
            consumedLift = left.trigger && (consumedLift || pointing || c.interfaceActive);
            if (consumedFire) c.fire = false;
            if (consumedLift) { c.jump = false; c.lift = 0; }
            (trigger, leftTrigger, grip, menuHeld, primary, secondary, leftPrimary, leftSecondary, stick) = (right.trigger, left.trigger, right.grip, pause, right.primary, right.secondary, left.primary, left.secondary, right.click);
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
                if (i == 0)
                {
                    if (pressed) TapFire(ref c);
                    c.automaticFire = c.fire = true;
                }
                if (i == 1) c.jump = true;
                if (i == 2) c.interact |= pressed;
                if (i == 3 && pressed) expanded = !expanded;
                if (i == 4) c.menu |= pressed;
                if (i == 5) c.systemView |= pressed;
                if (i == 6) c.cycle = pressed ? 1 : c.cycle;
                if (i == 7 && Oracle.Enabled) c.voice = true;
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
            return false;
        }
        internal static float Unit => Mathf.Clamp(Screen.safeArea.height / 720f, 0.8f, 2.5f);
        Rect StickArea(int index)
        {
            float size = 140 * Unit;
            var centre = new Vector2(index == 0 ? Screen.safeArea.xMin + 245 * Unit : Screen.safeArea.xMax - 90 * Unit, Screen.safeArea.yMin + 170 * Unit);
            return new Rect(centre - Vector2.one * size * 0.5f, Vector2.one * size);
        }
        Rect Area(int index)
        {
            Rect safe = Screen.safeArea;
            float u = Unit;
            Vector2 centre = index switch
            {
                0 => new Vector2(safe.xMin + 90 * u, safe.yMin + 330 * u),
                1 => new Vector2(safe.xMax - 205 * u, safe.yMin + 55 * u),
                2 => new Vector2(safe.xMax - 90 * u, safe.yMin + 330 * u),
                3 => new Vector2(safe.xMin + 48 * u, safe.yMin + 48 * u),
                4 => new Vector2(safe.xMax - 45 * u, safe.yMax - 42 * u),
                5 => new Vector2(safe.xMax - 135 * u, safe.yMax - 42 * u),
                6 => new Vector2(safe.xMin + 132 * u, safe.yMin + 48 * u),
                8 => new Vector2(safe.xMin + 216 * u, safe.yMin + 48 * u),
                10 => new Vector2(safe.xMin + 300 * u, safe.yMin + 48 * u),
                12 => new Vector2(safe.xMin + 384 * u, safe.yMin + 48 * u),
                _ => new Vector2(safe.xMin + (48 + (index - 6) % 3 * 90) * u, safe.yMin + (132 + (index - 6) / 3 * 84) * u),
            };
            if (index > 6 && expanded) centre = new Vector2(safe.center.x + ((index - 6) % 4 - 1.5f) * 90 * u, safe.center.y + (1 - (index - 6) / 4) * 90 * u);
            float size = (index == 0 ? 104 : 84) * u;
            return new Rect(centre - Vector2.one * size * 0.5f, Vector2.one * size);
        }
        bool Visible(int i) => (!game.Fall.active && !game.Tutorial.Locked || i == 4) && !game.Paused && !game.Dead && !game.Hermit.Talking && (!game.ShowControls || i == 4)
            && !game.Player.SystemView && (i < 6 || i == 6 || expanded)
            && (i != 7 || Oracle.Enabled) && (i != 8 || game.Player.CanRecall)
            && (i != 13 || !game.Player.flying && game.Player.Has(Gear.Stealth))
            && (i != 15 || game.Player.Seated) && (i != 10 || !game.Player.flying && game.FactoryUnlocked)
            && (i != 6 || game.Player.Powered) && (i != 14 || game.Player.Has(Gear.Thrusters) || game.Player.flying)
            && (i != 1 || !game.Buried) && (i != 2 || game.Player.Prompt != null || game.Outpost.Near || game.Hermit.Near);
        void LateUpdate()
        {
            if ((VR && forearm == null || Application.isMobilePlatform && touchCanvas == null) && Camera.main != null)
            {
                var post = GameObject.Find("Global Volume");
                if (post != null) post.SetActive(false);
                Camera.main.allowHDR = false;
            }
            if (VR && forearm == null)
            {
                forearm = game.gameObject.AddComponent<ForearmConsole>(); forearm.game = game;
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                foreach (var display in displays) display.SetPreferredMirrorBlitMode(XRMirrorViewBlitMode.None);
                SubsystemManager.GetSubsystems(inputs);
                foreach (var input in inputs) input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
            }
            if (touchCanvas == null)
            {
                var host = new GameObject("Touch controls", typeof(Canvas));
                touchCanvas = host.GetComponent<Canvas>();
                touchCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                touchCanvas.sortingOrder = 30;
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var circle = Resources.Load<Sprite>("Visor/Touch");
                for (int i = 0; i < 2; i++)
                {
                    joysticks[i] = new GameObject(i == 0 ? "Move joystick" : "Look joystick", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    joysticks[i].transform.SetParent(host.transform, false); joysticks[i].sprite = circle;
                    joysticks[i].color = new Color(0.03f, 0.12f, 0.17f, 0.65f);
                    thumbs[i] = new GameObject("Thumb", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    thumbs[i].transform.SetParent(joysticks[i].transform, false); thumbs[i].sprite = circle;
                    thumbs[i].color = new Color(0.35f, 0.85f, 1, 0.6f);
                    var name = new GameObject("Stick label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                    name.transform.SetParent(thumbs[i].transform, false); name.font = font; name.text = i == 0 ? "MOVE" : "SWIPE";
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
                    labels[i].alignment = TextAnchor.MiddleCenter; labels[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                    labels[i].resizeTextForBestFit = true; labels[i].resizeTextMinSize = 11;
                    labels[i].color = new Color(0.55f, 0.92f, 1);
                    labels[i].rectTransform.anchorMin = Vector2.zero; labels[i].rectTransform.anchorMax = Vector2.one;
                    labels[i].rectTransform.offsetMin = new Vector2(8, 8); labels[i].rectTransform.offsetMax = new Vector2(-8, -8);
                    labels[i].text = new[] { "FIRE", "LIFT", "USE", "···", "MENU", "MAP", "ARMS", "MIC", "RECALL", "SCAN", "BUILD", "VIEW", "ROBOT", "CLOAK", "DESCEND", "ASSIST" }[i];
                }
            }
            touchCanvas.enabled = HudActions && !VR;
            for (int i = 0; i < 2; i++)
            {
                bool show = !game.Paused && !game.Hermit.Talking && !game.ShowControls && !game.Player.SystemView && !game.Buried && !game.Dead && !game.Fall.active;
                if (i == 1 && fingers[1] >= 0) show = false;
                joysticks[i].enabled = show; thumbs[i].gameObject.SetActive(show);
                var rect = joysticks[i].rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
                Rect area = StickArea(i);
                if (i == 0 && fingers[0] >= 0) area.center = moveOrigin;
                rect.anchoredPosition = area.position; rect.sizeDelta = area.size;
                thumbs[i].rectTransform.sizeDelta = Vector2.one * (60 * Unit);
                thumbs[i].rectTransform.anchoredPosition = i == 0 ? moveThumb * (40 * Unit) : Vector2.zero;
            }
            labels[8].text = game.Player.flying ? "AUTO" : "RECALL";
            labels[1].text = game.Player.Has(Gear.Thrusters) || game.Player.flying ? "THRUST" : "JUMP";
            for (int i = 0; i < labels.Length; i++)
            {
                plates[i].gameObject.SetActive(Visible(i));
                Rect area = Area(i);
                var rect = plates[i].rectTransform;
                rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = Vector2.zero;
                rect.anchoredPosition = area.position; rect.sizeDelta = area.size;
                labels[i].resizeTextMaxSize = labels[i].fontSize = Mathf.RoundToInt((i == 0 ? 24 : 20) * Unit);
            }
        }
        void OnDestroy() { if (touchCanvas != null) Destroy(touchCanvas.gameObject); }
    }
}
