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
        Image blackout, sweep;
        Text boot, integrity, experience, recovery;
        readonly Image[] meters = new Image[4];
        CanvasGroup glass;
        RectTransform instruments, metersPlate;
        internal RectTransform VisibleMeters => glass != null && glass.alpha >= 0.5f ? metersPlate : null;
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
            image.transform.SetParent(parent, false);
            if (size != Vector2.zero) { image.sprite = Hud.Panel; image.type = Image.Type.Sliced; }
            image.color = colour; image.raycastTarget = false;
            var rect = image.rectTransform; rect.anchorMin = rect.anchorMax = anchor; rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            return image;
        }
        void Start()
        {
            canvas = new GameObject("Suit glass", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.gameObject.layer = LayerMask.NameToLayer("UI");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            glass = new GameObject("Helmet instruments", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<CanvasGroup>(); glass.transform.SetParent(canvas.transform, false);
            instruments = glass.GetComponent<RectTransform>();
            instruments.pivot = instruments.anchorMin = Vector2.zero; instruments.anchorMax = Vector2.one;
            instruments.localScale = Vector3.one * 1.15f;
            instruments.offsetMin = instruments.offsetMax = Vector2.zero;
            var corner = Vector2.zero;
            metersPlate = Bar(glass.transform, corner, new Vector2(46, 24), new Vector2(269, 114), new Color(0.01f, 0.02f, 0.03f, 0.88f)).rectTransform;
            string[] names = { "HEALTH", "ENERGY", "XP", "RECHARGE" };
            Color[] colours = { new Color(0.45f, 0.94f, 0.62f), Color.cyan, new Color(1, 0.79f, 0.35f), new Color(0.58f, 0.80f, 1) };
            for (int i = 0; i < meters.Length; i++)
            {
                int y = 107 - i * 26;
                var label = Label(glass.transform, names[i], corner, new Vector2(60, y), new Vector2(135, 26), 18);
                label.text = names[i]; label.alignment = TextAnchor.MiddleLeft;
                if (i == 0) integrity = label;
                if (i == 2) experience = label;
                if (i == 3) recovery = label;
                var position = new Vector2(167, y + 8);
                Bar(glass.transform, corner, position, new Vector2(133, 10), new Color(0.21f, 0.26f, 0.3f));
                meters[i] = Bar(glass.transform, corner, position, new Vector2(133, 10), colours[i]);
            }
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
            if (!visible) { glass.alpha = 0; return; }
            if (DeviceControls.VR)
            {
                var screen = game.GetComponent<Hud>().ScreenPanel;
                if (canvas.transform.parent != screen)
                {
                    foreach (var graphic in canvas.GetComponentsInChildren<Graphic>()) graphic.material = Hud.Overlay;
                    canvas.renderMode = RenderMode.WorldSpace;
                    canvas.transform.SetParent(screen, false);
                    var rect = (RectTransform)canvas.transform;
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    rect.localPosition = Vector3.zero; rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one;
                    instruments.SetParent(screen, false);
                    instruments.localScale = Vector3.one * 1.25f;
                    instruments.offsetMin = new Vector2(185, 5);
                    instruments.offsetMax = Vector2.zero;
                }
            }
            else
            {
                // Leave the bottom row of touch buttons and the movement stick clear.
                Vector2 margin = Screen.safeArea.min;
                if (DeviceControls.HudActions)
                    margin += new Vector2(0, 94) * DeviceControls.Unit;
                instruments.offsetMin = margin / canvas.scaleFactor;
            }
            float age = game.Boot;
            if (age < 0.5f) spoke = false;
            if (!game.Paused && game.Buried && age > 3.5f && !spoke)
            {
                spoke = true;
                if (game.Stage == Campaign.Buried) game.Hermit.Rescue();
                game.Speech.Say(game.Wording("wake"), caption: game.Subtitles);
            }
            for (int i = 0; i < bootTicks.Length; i++) bootTicks[i].enabled = !game.Paused && game.Buried && age > 5 + i * 0.18f && age < 11.8f;
            glass.alpha = game.Paused || game.ShowControls || game.Hermit.Talking || game.Player.SystemView || game.GetComponent<DeviceControls>().ReadingWrist ? 0 : math.saturate((age - 8) / 4);
            blackout.enabled = !DeviceControls.VR && !game.Paused;
            blackout.color = game.Frontier.Whiteout > 0 ? new Color(1, 1, 1, game.Frontier.Whiteout) : new Color(0, 0, 0, age < 8 ? 1 : math.saturate((12 - age) / 4));
            sweep.enabled = !game.Paused && game.Buried && age >= 5 && age < 12;
            sweep.rectTransform.anchoredPosition = new Vector2(0, 400 - (age % 2) * 400);
            boot.enabled = !game.Paused && game.Buried && age < 12;
            boot.text = game.PreparingOpening && age < 1 ? "COMPANION LINK · PREPARING EXPEDITION" : age < 3.5f ? "" : age < 5 ? "YOU ARE NOT DEAD." : age < 6.5f ? "YOU ARE NOT DEAD.\n\nFIGHT." : age < 9 ? "NEURAL LINK RESTORED\nVISOR ONLINE  ·  ACTUATORS ONLINE\nREACTOR CRITICAL" : "EMERGENCY FISTS READY\n\nPUNCH UP THROUGH THE RUBBLE OVERHEAD\n\nTHRUSTERS AND WEAPON BUS OFFLINE";
            if (boot.enabled && spoke && game.Subtitles && game.Prepared("wake") && (age < 6.5f || game.Speech.Speaking)) boot.text = game.Wording("wake");
            var player = game.Player;
            float energy = math.saturate(player.energy / (Suit.Capacity + (player.Has(Gear.Capacitor) ? 50 : 0)));
            integrity.text = player.flying ? "HULL" : "HEALTH";
            experience.text = $"XP   LV {game.Progression.level}";
            Fill(0, player.flying ? player.hull / player.MaxHull : player.health / player.MaxHealth);
            var battle = game.Battle;
            recovery.text = battle.Sweeping || battle.Beaming ? "FIRING" : battle.Charging ? "CHARGING" : "RECHARGE";
            Fill(3, battle.Charging ? battle.RailCharge : battle.Sweeping || battle.Beaming ? 1 : 1 - battle.Cooldown / math.max(0.01f, battle.RecoveryPeriod));
            Fill(1, energy);
            Fill(2, (float)game.Progression.experience / game.Progression.Required);
        }
        void Fill(int row, float fraction)
        {
            meters[row].rectTransform.sizeDelta = new Vector2(133 * math.saturate(fraction), 10);
        }
        void OnDestroy() { if (glass != null) Destroy(glass.gameObject); if (canvas != null) Destroy(canvas.gameObject); }
    }
}
