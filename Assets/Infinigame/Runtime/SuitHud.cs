using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Infinigame
{
    // Helmet instrumentation and the cold start share the same glass.
    public class SuitHud : MonoBehaviour
    {
        public Game game;
        Canvas canvas;
        Image blackout, sweep, reactor;
        Text boot, telemetry, systems, radio;
        CanvasGroup glass;
        readonly Image[] bootTicks = new Image[32];
        bool spoke;
        Text Label(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, int font)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = font;
            text.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);
            text.color = new Color(0.4f, 0.9f, 0.95f); text.raycastTarget = false;
            var rect = text.rectTransform; rect.anchorMin = rect.anchorMax = anchor; rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            return text;
        }
        Image Bar(Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color colour)
        {
            var image = new GameObject("Visor circuit", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false); image.color = colour; image.raycastTarget = false;
            var rect = image.rectTransform; rect.anchorMin = rect.anchorMax = anchor; rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            return image;
        }
        void Start()
        {
            canvas = new GameObject("Suit glass", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            glass = new GameObject("Helmet instruments", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<CanvasGroup>(); glass.transform.SetParent(canvas.transform, false);
            var rect = glass.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            Bar(glass.transform, new Vector2(0, 1), new Vector2(46, -25), new Vector2(525, 126), new Color(0.01f, 0.02f, 0.03f, 0.88f));
            telemetry = Label(glass.transform, "Telemetry", new Vector2(0, 1), new Vector2(60, -38), new Vector2(500, 130), 19);
            systems = Label(glass.transform, "Weapon bus", new Vector2(0, 0.5f), new Vector2(64, 0), new Vector2(340, 180), 17);
            radio = Label(glass.transform, "Radio", new Vector2(1, 0), new Vector2(-65, 45), new Vector2(380, 95), 17); radio.alignment = TextAnchor.LowerRight;
            reactor = Bar(glass.transform, new Vector2(0, 1), new Vector2(60, -142), new Vector2(300, 4), Color.cyan);
            blackout = Bar(canvas.transform, Vector2.zero, Vector2.zero, Vector2.zero, Color.black);
            blackout.rectTransform.anchorMax = Vector2.one; blackout.rectTransform.offsetMin = blackout.rectTransform.offsetMax = Vector2.zero;
            for (int i = 0; i < bootTicks.Length; i++)
            {
                float angle = i * math.PI * 2 / bootTicks.Length;
                bootTicks[i] = Bar(canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(math.sin(angle), math.cos(angle)) * 220, new Vector2(18, 3), Color.cyan);
                bootTicks[i].rectTransform.localRotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg);
            }
            sweep = Bar(canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500, 2), Color.cyan);
            boot = Label(canvas.transform, "Suit boot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(950, 270), 28); boot.alignment = TextAnchor.MiddleCenter;
        }
        void LateUpdate()
        {
            if (canvas == null) return;
            bool visible = !game.Title && !game.Dead;
            canvas.enabled = visible;
            if (!visible) return;
            if (DeviceControls.VR)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.transform.SetParent(Camera.main.transform, false);
                canvas.transform.localPosition = Vector3.forward * 1.5f; canvas.transform.localRotation = Quaternion.identity; canvas.transform.localScale = Vector3.one * 0.001f;
            }
            float age = game.Boot;
            if (age < 0.5f) spoke = false;
            if (game.Buried && age > 3.5f && !spoke)
            {
                spoke = true;
                game.Speech.Say("You are not dead. You are not dead. Fight. Emergency power. Punch your way out.");
            }
            for (int i = 0; i < bootTicks.Length; i++) bootTicks[i].enabled = game.Buried && age > 5 + i * 0.18f && age < 11.8f;
            glass.alpha = game.Paused || game.ShowControls || game.Hermit.Talking || game.Player.SystemView || game.Player.flying || game.Frontier.Collapsing ? 0 : math.saturate((age - 8) / 4);
            blackout.enabled = !DeviceControls.VR || game.Frontier.Whiteout > 0;
            blackout.color = game.Frontier.Whiteout > 0 ? new Color(1, 1, 1, game.Frontier.Whiteout) : new Color(0, 0, 0, age < 8 ? 1 : math.saturate((12 - age) / 4));
            sweep.enabled = game.Buried && age >= 5 && age < 12;
            sweep.rectTransform.anchoredPosition = new Vector2(0, 400 - (age % 2) * 400);
            boot.enabled = game.Buried && age < 12;
            boot.text = age < 3.5f ? "" : age < 5 ? "YOU ARE NOT DEAD." : age < 6.5f ? "YOU ARE NOT DEAD.\n\nFIGHT." : age < 9 ? "NEURAL LINK RESTORED\nVISOR ONLINE  ·  ACTUATORS ONLINE\nREACTOR CRITICAL" : "EMERGENCY FISTS READY\n\nPUNCH YOUR WAY OUT\n\nTHRUSTERS AND WEAPON BUS OFFLINE";
            telemetry.enabled = systems.enabled = radio.enabled = reactor.enabled = !game.Paused && !game.Hermit.Talking && !game.Player.SystemView && !game.ShowControls;
            telemetry.fontSize = DeviceControls.Touch ? 26 : 19;
            telemetry.text = $"FOUNDRY // SEALED ARMOUR\nINTEGRITY {100 * math.saturate(game.Player.health / game.Player.MaxHealth):0}%    REACTOR {100 * game.Player.energy / (Suit.Capacity + (game.Player.Has(Gear.Capacitor) ? 50 : 0)):0}%\nALT {game.Player.Altitude:0} m    SPEED {math.length(game.Player.Velocity):0} m/s";
            if (game.Player.InSpace && (game.Player.Has(Gear.Ship) || game.Player.captive)) telemetry.text += $"\nSHIP {math.distance(game.Player.Position, game.Player.ship.position):0} m   RELATIVE SPEED {math.length(game.Player.Velocity - game.Player.ship.velocity):0} m/s";
            reactor.rectTransform.sizeDelta = new Vector2(300 * math.saturate(game.Player.energy / (Suit.Capacity + (game.Player.Has(Gear.Capacitor) ? 50 : 0))), 4);
            reactor.color = game.Player.Powered ? Color.cyan : new Color(1, 0.65f, 0.1f);
            systems.text = "";
            radio.text = "";

        }
        void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
    }
}
