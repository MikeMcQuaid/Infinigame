using System;
using System.Collections.Generic;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Infinigame
{
    // Everything drawn over the world: a spare status line with what is left of suit or hull, the
    // mission in hand, markers for places worth going to and things worth shooting, the interact
    // prompt, news, the scanner's card, conversation, the menus and the list of controls.
    // Built from code on one canvas in front of the camera, and quiet unless there is something to say.
    public class Hud : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.9f, 0.94f, 1f, 0.92f), Dim = new Color(0.9f, 0.94f, 1f, 0.5f), Faint = new Color(0.9f, 0.94f, 1f, 0.18f);
        static readonly Color Accent = new Color(0.42f, 0.86f, 0.98f, 0.95f), Glass = new Color(0.02f, 0.035f, 0.06f, 0.74f), Shade = new Color(0f, 0.01f, 0.02f, 0.8f);
        // a faint plate behind text that sits straight on the world, so it reads over snow and sand
        static readonly Color Plate = new Color(0.02f, 0.035f, 0.06f, 0.88f);
        static readonly Color Harm = new Color(1f, 0.25f, 0.2f, 0.95f);
        const float Typing = 70f, CardStays = 16f, BannerStays = 6f, NewsStays = 4.5f, BriefStays = 10f, Edge = 64f;

        static readonly (string heading, string[] rows)[] Help =
        {
            ("ON FOOT", new[]
            {
                "W A S D|Run", "Mouse|Look", "Space|Jump / hold to thrust", "W  aloft|Fly where you look", "Shift|Sprint", "F|Talk, take or board", "Click|Fire", "R|Toggle fitted cloak",
                "1 2 3 4 5|Select weapon", "B|Build at this site", "X|Scan what you face", "V|First or third person",
            }),
            ("IN THE SHIP", new[]
            {
                "W A S D|Thrust, reverse and strafe", "Mouse|Steer", "Space  Ctrl|Lift / descend", "Shift|Boost", "Q  E|Roll", "F|Land, then leave",
                "Click|Fire", "1 2 3 4 5|Select weapon", "T|Autopilot / recall", "Z|Flight assist", "V|Cockpit or chase view", "X|Scan the world ahead",
            }),
            ("ANYWHERE", new[]
            {
                "Tab|Expedition map", "Return|Talk to your robot", "Y|Dictate (model on)", "Esc|Pause / close a page",
                "Opt Return|Full screen or window",
            }),
        };

        struct Marker
        {
            public RectTransform at;
            // a diamond for a place, a box drawn round a foe, a line out to a name that had to go further off,
            // and an arrow for a mark held at the edge of the screen because its place is out of view
            public Image icon, box, arrow, health, healthTrack;
            public Text name, detail;
        }

        [NonSerialized] public Game game;

        internal static Material Overlay { get; private set; }
        Canvas canvas;
        public RectTransform ScreenPanel => (RectTransform)canvas.transform;
        static Sprite panel;
        public static Sprite Panel => panel != null ? panel : panel = Resources.Load<Sprite>("Visor/Panel");
        Sprite outline;
        readonly List<Foe> markerFoes = new List<Foe>();
        float3 markerEye;
        int Nearer(Foe a, Foe b) => math.distancesq(a.position, markerEye).CompareTo(math.distancesq(b.position, markerEye));
        CanvasGroup status, prompt, banner, card, talk, menu, controls, hint, run, news, chart, brief, asking, caption;
        Text place, readout, promptText, promptKey, bannerTitle, bannerSub, cardHead, cardTitle, cardBody, cardFoot, briefHead, briefTitle, briefTask, briefRows, briefWords, askText, captionText;
        Text speaker, line, draft, talkKeys, menuTitle, menuSub, menuNote, menuKeys, stats, runText, newsText;
        Text[] menuItems;
        Text lesson, cheatGlyph, dogLink;
        Image pageBackground, attributePlate, pointerFocus;
        RectTransform menuContent;
        readonly List<(Text label, Text action, Image divider, string key)> helpKeys = new List<(Text, Text, Image, string)>();
        Image reticle, menuBar, flash, murk, beacon, askMark;
        Sprite dot;
        // each of the oracle's voices' own badge, where the project has been given one
        Sprite[] badges;
        Text[] chartColumns;
        readonly List<int> chartWorlds = new List<int>();
        Text helpHint, controlsClose;
        Text escapeText;
        CanvasGroup escape;
        Text chartFoot, talkClose, heldWeapon;
        CanvasGroup mapBar, weaponCard;
        Text[] mapButtons, questions;
        Text attributes;
        RawImage mapImage;
        Canvas worldMarkers;
        Renderer surround;
        Material surroundInk;
        WeaponGlyph weaponGlyph;
        RectTransform lessonPlate, promptCap, statusPlate, promptPlate, captionPlate, missionPlate;
        Marker[] markers;
        // where this frame's marker names have been put, and the sides of its mark a name may go to, in order of preference
        readonly List<Rect> labels = new List<Rect>();
        readonly Vector3[] markerCorners = new Vector3[4];
        static readonly Vector2[] Places = { Vector2.up, Vector2.right, Vector2.left, Vector2.down, new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) };
        float fps, bannerSince = float.MinValue, cardHeight, briefSince = float.MinValue;
        int bannerBody = -1, used, briefed = -1;
        uint bannerSeed;
        Entry carded;

        void Awake()
        {
            var host = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            host.layer = LayerMask.NameToLayer("UI");
            canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            // Far enough out to be large in the world, so that it stays sharp even where the camera is a
            // long way from the origin and positions are coarse. Its shader draws over whatever is nearer.
            canvas.planeDistance = 100f;
            Overlay = new Material(Resources.Load<Shader>("Overlay"));
            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            Transform root = host.transform;
            outline = Resources.Load<Sprite>("Visor/Outline");
            dot = Resources.Load<Sprite>("Visor/Dot");
            var pointer = Resources.Load<Sprite>("Visor/Pointer");
            Vector2 centre = new Vector2(0.5f, 0.5f), bottom = new Vector2(0.5f, 0f), top = new Vector2(0.5f, 1f), topLeft = new Vector2(0f, 1f);

            // inside a giant's clouds there is nothing to see but the cloud
            murk = Fill(root, centre, Vector2.zero, Vector2.zero, Color.clear);
            Stretch(murk);
            // a knock reddens the whole view for a moment
            flash = Fill(root, centre, Vector2.zero, Vector2.zero, Color.clear);
            Stretch(flash);

            worldMarkers = new GameObject("World indicators", typeof(Canvas)).GetComponent<Canvas>();
            worldMarkers.transform.SetParent(transform, false);
            worldMarkers.gameObject.layer = host.layer;
            worldMarkers.renderMode = RenderMode.WorldSpace;
            mapImage = Rect(root, centre, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            Stretch(mapImage);
            mapImage.enabled = false;
            markers = new Marker[28];
            for (int i = 0; i < markers.Length; i++)
            {
                var at = Rect(root, Vector2.zero, Vector2.zero, new Vector2(240f, 60f));
                at.name = "Marker";
                at.pivot = centre;
                var icon = Fill(at, centre, Vector2.zero, new Vector2(8f, 8f), Ink);
                icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                markers[i] = new Marker
                {
                    at = at, icon = icon, healthTrack = Fill(at, centre, new Vector2(0, -25), new Vector2(64, 5), Shade), health = Fill(at, centre, new Vector2(0, -25), new Vector2(64, 5), Harm), box = Fill(at, centre, Vector2.zero, new Vector2(34f, 34f), Harm, outline),
                    arrow = Fill(at, centre, Vector2.zero, new Vector2(13f, 13f), Ink, pointer),
                    name = Label(at, centre, new Vector2(0f, 18f), new Vector2(240f, 18f), 13, TextAnchor.MiddleCenter, Ink),
                    detail = Label(at, centre, new Vector2(0f, -17f), new Vector2(240f, 16f), 12, TextAnchor.MiddleCenter, Dim),
                };
            }
            reticle = Fill(root, centre, Vector2.zero, new Vector2(5f, 5f), Ink, dot);

            status = Group(root, Vector2.zero, new Vector2(56f, 48f), new Vector2(900f, 112f));
            statusPlate = Fill(status.transform, Vector2.zero, new Vector2(-20f, -18f), Vector2.zero, Plate, Panel).rectTransform;
            place = Label(status.transform, topLeft, Vector2.zero, new Vector2(900f, 20f), 14, TextAnchor.UpperLeft, Dim);
            readout = Label(status.transform, topLeft, new Vector2(0f, -26f), new Vector2(900f, 44f), 34, TextAnchor.UpperLeft, Ink);
            // how deep the run has gone and how nearly the warp drive is charged
            run = Group(root, topLeft, new Vector2(56f, -48f), new Vector2(420f, 30f));
            missionPlate = Fill(run.transform, topLeft, new Vector2(-10, 8), new Vector2(1020, 50), Plate, Panel).rectTransform;
            runText = Label(run.transform, topLeft, Vector2.zero, new Vector2(1000f, 36f), 13, TextAnchor.UpperLeft, Dim);

            new GameObject("Globe radar").AddComponent<Radar>().game = game;

            weaponCard = Group(root, new Vector2(0, 0.5f), new Vector2(56, 35), new Vector2(340, 125));
            Stretch(Fill(weaponCard.transform, centre, Vector2.zero, Vector2.zero, Plate, Panel));
            heldWeapon = Label(weaponCard.transform, topLeft, new Vector2(15, -10), new Vector2(235, 26), 16, TextAnchor.UpperLeft, Ink);
            dogLink = Label(weaponCard.transform, Vector2.zero, new Vector2(15, 5), new Vector2(300, 26), 19, TextAnchor.LowerLeft, Accent);
            weaponGlyph = new GameObject("Weapon outline", typeof(RectTransform), typeof(WeaponGlyph)).GetComponent<WeaponGlyph>();
            weaponGlyph.transform.SetParent(weaponCard.transform, false); weaponGlyph.color = Ink; weaponGlyph.material = Overlay; weaponGlyph.raycastTarget = false;
            weaponGlyph.rectTransform.anchorMin = weaponGlyph.rectTransform.anchorMax = Vector2.zero;
            weaponGlyph.rectTransform.pivot = Vector2.zero; weaponGlyph.rectTransform.anchoredPosition = new Vector2(15, 12); weaponGlyph.rectTransform.sizeDelta = new Vector2(180, 50);
            pageBackground = Fill(root, centre, Vector2.zero, Vector2.zero, new Color(0.015f, 0.026f, 0.041f, 1));
            Stretch(pageBackground); pageBackground.enabled = false;
            mapBar = Group(root, top, new Vector2(0, -70), new Vector2(1300, 66));
            Stretch(Fill(mapBar.transform, centre, Vector2.zero, Vector2.zero, Plate, Panel));
            string[] mapActions = { "MAP", "SUMMARY", "SUIT ATTRIBUTES", "CLOSE ×", "FLEET ORDERS  ›", "BUILD", "SET AUTOPILOT", "CONTROLS", "CALL ROBODOG", "MAIN MENU" };
            mapButtons = new Text[mapActions.Length];
            for (int i = 0; i < mapActions.Length; i++)
            {
                var position = i < 3 ? new Vector2(-545 + i * 215, 0) : i == 7 ? new Vector2(110, 0)
                    : i == 9 ? new Vector2(335, 0) : i == 3 ? new Vector2(540, 0) : new Vector2(-465 + (i == 8 ? 3 : i - 4) * 310, -805);
                mapButtons[i] = Label(mapBar.transform, centre, position, new Vector2(i == 3 ? 150 : i < 3 || i == 7 || i == 9 ? 208 : 285, 58), 22, TextAnchor.MiddleCenter, Ink, mapActions[i]);
            }
            attributePlate = Fill(mapBar.transform, top, new Vector2(0, -110), new Vector2(1050, 520), new Color(0.025f, 0.045f, 0.065f, 1), Panel);
            attributes = Label(mapBar.transform, top, new Vector2(0, -110), new Vector2(1050, 520), 28, TextAnchor.MiddleCenter, Ink);
            attributes.lineSpacing = 1.6f;
            // The system map's table: every world at a glance.
            chart = Group(root, top, new Vector2(0, -150f), new Vector2(1328f, 420f));
            Stretch(Fill(chart.transform, centre, Vector2.zero, Vector2.zero, new Color(0.02f, 0.035f, 0.06f, 1), Panel));
            float[] columns = { 24f, 240f, 420f, 530f, 685f, 840f, 985f, 1130f, 0f, 0f };
            chartColumns = new Text[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                chartColumns[i] = Label(chart.transform, topLeft, new Vector2(columns[i], -22f), new Vector2(i == 0 ? 210 : i == 1 ? 175 : 150, 360f), 18, TextAnchor.UpperLeft, i == 0 ? Ink : Dim);
                chartColumns[i].lineSpacing = 1.45f;
            }
            chartFoot = Label(chart.transform, Vector2.zero, new Vector2(24f, 18f), new Vector2(1050f, 16f), 12, TextAnchor.LowerLeft, Dim);

            // The mission in hand, set out in full: over the map, and for a while whenever a new one begins.
            brief = Group(root, top, new Vector2(0, -592f), new Vector2(780f, 262f));
            Stretch(Fill(brief.transform, centre, Vector2.zero, Vector2.zero, new Color(0.006f, 0.012f, 0.02f, 1), Panel));
            Fill(brief.transform, Vector2.zero, Vector2.zero, new Vector2(3f, 0f), Accent).rectTransform.anchorMax = topLeft;
            briefHead = Label(brief.transform, topLeft, new Vector2(30f, -22f), new Vector2(720f, 18f), 13, TextAnchor.UpperLeft, Accent);
            briefTitle = Label(brief.transform, topLeft, new Vector2(30f, -44f), new Vector2(720f, 36f), 28, TextAnchor.UpperLeft, Ink);
            briefTask = Label(brief.transform, topLeft, new Vector2(30f, -88f), new Vector2(720f, 20f), 15, TextAnchor.UpperLeft, Ink);
            Label(brief.transform, topLeft, new Vector2(30f, -122f), new Vector2(90f, 70f), 14, TextAnchor.UpperLeft, Dim, "WHERE\nREWARD\nNEXT").lineSpacing = 1.45f;
            briefRows = Label(brief.transform, topLeft, new Vector2(128f, -122f), new Vector2(622f, 70f), 14, TextAnchor.UpperLeft, Ink);
            briefRows.lineSpacing = 1.45f;
            briefWords = Label(brief.transform, topLeft, new Vector2(30f, -198f), new Vector2(720f, 54f), 14, TextAnchor.UpperLeft, Dim);

            // Who is writing, while they are: the oracle's voice and its model, under the mission line.
            asking = Group(root, Vector2.one, new Vector2(-56f, -92f), new Vector2(420f, 22f));
            askMark = Fill(asking.transform, Vector2.one, new Vector2(0f, 1f), new Vector2(18f, 18f), Accent, dot);
            askText = Label(asking.transform, Vector2.one, new Vector2(-28f, 0f), new Vector2(600f, 18f), 13, TextAnchor.UpperRight, Accent);
            badges = Oracle.Voices.Select(voice => Resources.Load<Texture2D>($"Icons/{voice}") is Texture2D art && art != null
                ? Sprite.Create(art, new Rect(0f, 0f, art.width, art.height), Vector2.one * 0.5f) : null).ToArray();

            // What the copilot says unasked, written under the view as he says it.
            lessonPlate = Fill(root, bottom, Vector2.zero, Vector2.zero, Plate, Panel).rectTransform;
            lesson = Label(root, bottom, new Vector2(0, 330), new Vector2(1100, 64), 24, TextAnchor.MiddleCenter, Accent);
            cheatGlyph = Label(root, new Vector2(1, 0), new Vector2(-22, 22), new Vector2(48, 48), 16, TextAnchor.MiddleCenter, Dim, "◇");
            caption = Group(root, bottom, new Vector2(0f, 94f), new Vector2(1100f, 64f));
            captionPlate = Fill(caption.transform, centre, Vector2.zero, Vector2.zero, Plate, Panel).rectTransform;
            captionText = Label(caption.transform, centre, Vector2.zero, new Vector2(1040f, 60f), 19, TextAnchor.MiddleCenter, Ink);

            news = Group(root, top, new Vector2(0f, -258f), new Vector2(1400f, 30f));
            newsText = Label(news.transform, top, Vector2.zero, new Vector2(1400f, 26f), 17, TextAnchor.MiddleCenter, Accent);

            prompt = Group(root, bottom, new Vector2(0f, 176f), new Vector2(700f, 44f));
            promptPlate = Fill(prompt.transform, centre, Vector2.zero, Vector2.zero, Plate, Panel).rectTransform;
            promptCap = Fill(prompt.transform, centre, Vector2.zero, new Vector2(40f, 40f), Ink, outline).rectTransform;
            promptKey = Label(promptCap, centre, Vector2.zero, new Vector2(40f, 40f), 19, TextAnchor.MiddleCenter, Ink, "F");
            promptText = Label(prompt.transform, centre, Vector2.zero, new Vector2(600f, 40f), 22, TextAnchor.MiddleLeft, Ink);
            promptText.rectTransform.pivot = new Vector2(0f, 0.5f);

            hint = Group(root, new Vector2(1f, 0f), new Vector2(-56f, 48f), new Vector2(300f, 18f));
            helpHint = Label(hint.transform, new Vector2(1f, 0f), Vector2.zero, new Vector2(300f, 18f), 13, TextAnchor.LowerRight, Dim, "ESC   MENU");

            banner = Group(root, top, new Vector2(0f, -110f), new Vector2(1400f, 130f));
            bannerTitle = Label(banner.transform, top, Vector2.zero, new Vector2(1400f, 64f), 46, TextAnchor.MiddleCenter, Ink);
            Fill(banner.transform, top, new Vector2(0f, -74f), new Vector2(160f, 1f), Dim);
            bannerSub = Label(banner.transform, top, new Vector2(0f, -88f), new Vector2(1400f, 26f), 18, TextAnchor.MiddleCenter, Dim);

            card = Group(root, new Vector2(1f, 0.5f), new Vector2(-56f, 40f), new Vector2(500f, 300f));
            Stretch(Fill(card.transform, centre, Vector2.zero, Vector2.zero, Glass, Panel));
            var bar = Fill(card.transform, Vector2.zero, Vector2.zero, new Vector2(3f, 0f), Accent).rectTransform;
            bar.anchorMax = topLeft;
            cardHead = Label(card.transform, topLeft, new Vector2(30f, -24f), new Vector2(440f, 36f), 13, TextAnchor.UpperLeft, Accent);
            cardTitle = Label(card.transform, topLeft, new Vector2(30f, -68f), new Vector2(440f, 40f), 30, TextAnchor.UpperLeft, Ink);
            cardBody = Label(card.transform, topLeft, new Vector2(30f, -120f), new Vector2(440f, 200f), 19, TextAnchor.UpperLeft, Ink);
            cardBody.lineSpacing = 1.15f;
            cardFoot = Label(card.transform, Vector2.zero, new Vector2(30f, 20f), new Vector2(440f, 16f), 12, TextAnchor.LowerLeft, Dim);

            talk = Group(root, bottom, new Vector2(0f, 64f), new Vector2(1080f, 330f));
            Stretch(Fill(talk.transform, centre, Vector2.zero, Vector2.zero, new Color(0.012f, 0.022f, 0.033f, 0.985f), Panel));
            talkClose = Label(talk.transform, new Vector2(1, 1), new Vector2(-30, -18), new Vector2(130, 45), 20, TextAnchor.MiddleRight, Ink, "CLOSE ×");
            speaker = Label(talk.transform, topLeft, new Vector2(38f, -26f), new Vector2(1000f, 36f), 13, TextAnchor.UpperLeft, Accent);
            line = Label(talk.transform, topLeft, new Vector2(38f, -78f), new Vector2(1004f, 124f), 23, TextAnchor.UpperLeft, Ink);
            line.lineSpacing = 1.12f;
            Fill(talk.transform, Vector2.zero, new Vector2(38f, 62f), new Vector2(1004f, 1f), Faint);
            draft = Label(talk.transform, Vector2.zero, new Vector2(38f, 20f), new Vector2(760f, 30f), 20, TextAnchor.MiddleLeft, Ink);
            talkKeys = Label(talk.transform, new Vector2(1f, 0f), new Vector2(-38f, 26f), new Vector2(900f, 16f), 12, TextAnchor.LowerRight, Dim);

            questions = new Text[Hermit.Questions.Length];
            for (int i = 0; i < questions.Length; i++)
                questions[i] = Label(talk.transform, bottom, new Vector2((i - 1) * 340, 68), new Vector2(325, 62), 21, TextAnchor.MiddleCenter, Accent, (i + 1) + "  " + Hermit.Questions[i]);
            escape = Group(root, new Vector2(0.5f, 1), new Vector2(0, -105), new Vector2(650, 70));
            escape.name = "Emergency broadcast";
            Stretch(Fill(escape.transform, centre, Vector2.zero, Vector2.zero, Plate, Panel));
            escapeText = Label(escape.transform, centre, Vector2.zero, new Vector2(620, 50), 30, TextAnchor.MiddleCenter, Harm);
            escape.alpha = 0;
            controls = Group(root, centre, new Vector2(0f, 10f), new Vector2(1380f, 900f));
            Stretch(Fill(controls.transform, centre, Vector2.zero, Vector2.zero, new Color(0.015f, 0.03f, 0.045f, 1f), Panel));
            var help = DeviceControls.VR ? new (string heading, string[] rows)[]
            {
                ("SUIT", new[] { "Left stick|Move", "Right stick ◀ ▶|Snap turn", "Right stick ▲ ▼|Change weapon", "Left trigger|Jump / jetpack", "L stick click / grip|Sprint or boost", "Swing fist|Punch", "Right trigger|Fire / aim rail or missiles", "Release trigger|Fire aimed shot", "Right A|Take item / board", "R stick click|Engage cloak" }),
                ("COCKPIT", new[] { "Left stick ▲ ▼|Thrust / reverse", "Left stick ◀ ▶|Steer", "Right stick ▲ ▼|Pitch", "Right stick ◀ ▶|Roll", "Left trigger|Boost", "Left A|Descend", "Right trigger|Fire / aim rail", "Right B|Cycle weapon", "Right A|Land / leave", "R stick click|Autopilot", "Hold left B|Cockpit pause menu" }),
                ("MENUS", new[] { "Hold left B|Pause", "Either stick ▲ ▼|Move selection", "Either trigger / R A|Select row", "Point + trigger|Select button", "Right B|Go back", "Left / right stick|Pan / zoom map", "Tap left B|Expedition map", "Turn left wrist|Suit controls", "Wrist BUILD|Build at this site" }),
            } : Help;
            for (int column = 0; column < help.Length; column++)
            {
                float x = 36f + 450f * column;
                Label(controls.transform, topLeft, new Vector2(x, -36f), new Vector2(415f, 24f), 18, TextAnchor.UpperLeft, Accent, help[column].heading);
                if (column > 0) Fill(controls.transform, topLeft, new Vector2(x - 12, -36), new Vector2(2, 805), Faint);
                for (int row = 0; row < help[column].rows.Length; row++)
                {
                    var divider = Fill(controls.transform, topLeft, new Vector2(x, -66 - row * 65), new Vector2(426, 1), Faint);
                    string[] entry = help[column].rows[row].Split('|');
                    var key = Label(controls.transform, topLeft, new Vector2(x, -69 - row * 65), new Vector2(148, 60), 22, TextAnchor.MiddleLeft, Accent, entry[0]);
                    var action = Label(controls.transform, topLeft, new Vector2(x + 154, -69 - row * 65), new Vector2(272, 60), 22, TextAnchor.MiddleLeft, Dim, entry[1]);
                    helpKeys.Add((key, action, divider, entry[0]));
                }
            }
            controlsClose = Label(controls.transform, new Vector2(1, 0), new Vector2(-32, 18), new Vector2(280, 52), 24, TextAnchor.MiddleCenter, Ink, "CLOSE CONTROLS ×");
            var closePlate = Fill(controls.transform, new Vector2(1, 0), new Vector2(-32, 18), new Vector2(280, 52), Glass, Panel);
            closePlate.transform.SetSiblingIndex(controlsClose.transform.GetSiblingIndex());
            stats = Label(controls.transform, Vector2.zero, new Vector2(48f, 26f), new Vector2(900f, 16f), 12, TextAnchor.LowerLeft, Dim);

            menu = Group(root, centre, Vector2.zero, Vector2.zero);
            Stretch(menu.GetComponent<RectTransform>());
            // light is blended as light, not as the eye sees it, so this leaves the world behind at about half its brightness
            Stretch(Fill(menu.transform, centre, Vector2.zero, Vector2.zero, Shade));
            menuContent = Rect(menu.transform, centre, Vector2.zero, new Vector2(1500, 960));
            menuTitle = Label(menuContent, centre, new Vector2(0f, 250f), new Vector2(1600f, 90f), 62, TextAnchor.MiddleCenter, Ink);
            menuSub = Label(menuContent, centre, new Vector2(0f, 178f), new Vector2(1600f, 26f), 16, TextAnchor.MiddleCenter, Dim);
            menuNote = Label(menuContent, centre, new Vector2(0f, 124f), new Vector2(1600f, 68f), 16, TextAnchor.MiddleCenter, Dim);
            menuBar = Fill(menuContent, centre, Vector2.zero, new Vector2(3f, 30f), Accent);
            menuItems = new Text[16];
            for (int i = 0; i < menuItems.Length; i++)
                menuItems[i] = Label(menuContent, centre, new Vector2(0f, 64f - 60f * i), new Vector2(900f, 50f), 30, TextAnchor.MiddleCenter, Ink);
            menuKeys = Label(menuContent, bottom, new Vector2(0f, 64f), new Vector2(1200f, 18f), 12, TextAnchor.LowerCenter, Dim);

            foreach (var group in new[] { prompt, banner, card, talk, menu, controls, news, chart, brief, asking, caption, mapBar, weaponCard }) group.alpha = 0f;
            cheatGlyph.transform.SetAsLastSibling();
        }



        // Children are placed from one corner of their parent, given as the anchor, which is also their own pivot.
        static RectTransform Rect(Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject("Part", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.gameObject.layer = parent.gameObject.layer;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static void Stretch(Graphic graphic) => Stretch(graphic.rectTransform);

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static CanvasGroup Group(Transform parent, Vector2 anchor, Vector2 position, Vector2 size) => Rect(parent, anchor, position, size).gameObject.AddComponent<CanvasGroup>();

        static Image Fill(Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color colour, Sprite sprite = null)
        {
            var image = Rect(parent, anchor, position, size).gameObject.AddComponent<Image>();
            image.sprite = sprite != null ? sprite : size != Vector2.zero ? Panel : null;
            image.material = Overlay;
            image.type = Image.Type.Sliced;
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        static Text Label(Transform parent, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color colour, string words = "")
        {
            var text = Rect(parent, anchor, position, size).gameObject.AddComponent<Text>();
            text.font = Shapes.Type;
            text.material = Overlay;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = colour;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = words;
            // a soft shadow keeps pale text readable over snow, sand and cloud
            text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            return text;
        }

        // Capitals with air between them, for titles.
        static string Spaced(string words) => string.Join(" ", words.ToUpperInvariant().Select(letter => letter == ' ' ? "  " : letter.ToString()));

        static string ControlName(string key) => key switch
        {
            "W A S D" => DeviceControls.Move,
            "Mouse" => DeviceControls.Look,
            "Click" => DeviceControls.Fire,
            "F" => DeviceControls.Use,
            "Space" => DeviceControls.Key(key, "A / RB", "LIFT", "L TRIGGER"),
            "Space  Ctrl" => DeviceControls.Key(key, "RB / LB", "LIFT / DESCEND", "L TRIGGER / L A"),
            "W  aloft" => DeviceControls.Key(key, "LEFT STICK", "LEFT DRAG", "LEFT STICK"),
            "Shift" => DeviceControls.Key(key, "L3", "FULL DRAG", "EITHER GRIP"),
            "1 2 3 4 5" => DeviceControls.Key(key, "Y", "WEAPON", "R B / R STICK"),
            "X" => DeviceControls.Key(key, "LT", "··· SCAN", "WRIST SCAN"),
            "Tab" => DeviceControls.Key(key, "VIEW", "MAP", "LEFT B"),
            "T" => DeviceControls.Key(key, "D-PAD RIGHT", "RECALL", "RIGHT STICK"),
            "B" => DeviceControls.Key(key, "LT + DOWN", "BUILD", "WRIST BUILD"),
            "Return" => DeviceControls.Key(key, "LT + LEFT", "ROBOT", "WRIST ROBOT"),
            "Y" => DeviceControls.Key(key, "LT + R3", "HOLD MIC", "WRIST MIC"),
            "V" => DeviceControls.Key(key, "R3", "··· VIEW", "HEAD TRACKED"),
            "R" => DeviceControls.Key(key, "LT + X", "··· CLOAK", "R STICK CLICK"),
            "Esc" => DeviceControls.Key(key, "MENU / B", "MENU", "HOLD LEFT B"),
            "Z" => DeviceControls.Key(key, "LT + Y", "··· ASSIST", "SHIP CONTROLS"),
            "Q  E" => DeviceControls.Key(key, "LEFT STICK ◀ ▶", "AUTO", "LEFT STICK ◀ ▶"),
            _ => DeviceControls.Touch || DeviceControls.VR || DesktopControls.UsingPad || DeviceControls.Deck ? "SETTINGS" : key,
        };
        static string Distance(float metres) => metres < 1000f ? $"{metres:0} m" : $"{metres / 1000f:0.0} km";

        static void Show(CanvasGroup group, bool shown, float rate = 5f) => group.alpha = Mathf.MoveTowards(group.alpha, shown ? 1f : 0f, rate * Time.unscaledDeltaTime);

        // Which item of the menu on screen is under a point, or -1. Only the menu's own items count:
        // the labels below them are spare, and still in place on the frame a shorter menu opens.
        public bool Point(Vector3 origin, Vector3 direction, out Vector2 pointer, out Vector3 end)
        {
            var plane = new Plane(canvas.transform.forward, canvas.transform.position);
            bool hit = plane.Raycast(new Ray(origin, direction), out float distance) && distance < 8;
            end = origin + direction * (hit ? distance : 3);
            pointer = canvas.worldCamera.WorldToScreenPoint(end);
            return hit;
        }
        bool Hit(RectTransform rect, Vector2 pointer, out Vector2 local)
        {
            Rect bounds = rect.rect;
            if (canvas.renderMode == RenderMode.WorldSpace)
                return RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, pointer, canvas.worldCamera, out local) && bounds.Contains(local);
            // Intersect in pixels. At system-map distances a ray back into the canvas loses precision.
            Vector2 min = canvas.worldCamera.WorldToScreenPoint(rect.TransformPoint(bounds.min));
            Vector2 max = canvas.worldCamera.WorldToScreenPoint(rect.TransformPoint(bounds.max));
            Rect pixels = UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            local = UnityEngine.Rect.NormalizedToPoint(bounds, UnityEngine.Rect.PointToNormalized(pixels, pointer));
            return pixels.Contains(pointer);
        }
        public int MapControlAt(Vector2 pointer)
        {
            for (int i = 0; i < mapButtons.Length; i++) if (mapButtons[i].enabled && Hit(mapButtons[i].rectTransform, pointer, out _)) return i;
            return -1;
        }
        public int MapItemAt(Vector2 pointer)
        {
            if (!game.MapSummary) return -1;
            var label = chartColumns[0];
            if (!Hit(label.rectTransform, pointer, out Vector2 local)) return -1;
            float top = label.rectTransform.rect.yMax;
            int row = Mathf.FloorToInt((top - local.y) / (label.font.lineHeight * label.fontSize / label.font.fontSize * label.lineSpacing)) - 1;
            return row >= 0 && row < chartWorlds.Count ? chartWorlds[row] : -1;
        }
        public bool CloseControlsAt(Vector2 pointer) => Hit(controlsClose.rectTransform, pointer, out _);
        public bool DraftAt(Vector2 pointer) => Hit(draft.rectTransform, pointer, out _);
        void OnDestroy() { if (surroundInk != null) Destroy(surroundInk); }

        public int QuestionAt(Vector2 pointer) => Array.FindIndex(questions, item => item.enabled && Hit(item.rectTransform, pointer, out _));
        public bool CloseConversationAt(Vector2 pointer) => !Hit(talk.GetComponent<RectTransform>(), pointer, out _)
            || Hit(talkClose.rectTransform, pointer, out _);
        public bool BlocksFire(Vector2 pointer)
        {
            foreach (var group in new[] { status, weaponCard, prompt, run, caption, mapBar })
                if (group.alpha > 0.5f && Hit(group.GetComponent<RectTransform>(), pointer, out _)) return true;
            return false;
        }
        public bool CheatAt(Vector2 pointer) => Hit(cheatGlyph.rectTransform, pointer, out _);
        public int MenuItemAt(Vector2 pointer)
        {
            for (int i = 0, count = Mathf.Min(game.Items.Length, menuItems.Length); i < count; i++)
                if (menuItems[i].gameObject.activeSelf && Hit(menuItems[i].rectTransform, pointer, out _))
                    return i;
            return -1;
        }

        public bool HighlightPointer(Vector2? point)
        {
            Text target = null;
            int row = -1;
            if (point is Vector2 at)
            {
                if (game.Paused || game.Dead)
                {
                    int item = MenuItemAt(at);
                    target = item >= 0 ? menuItems[item] : cheatGlyph.enabled && CheatAt(at) ? cheatGlyph : null;
                }
                else if (game.Player.SystemView)
                {
                    int button = MapControlAt(at);
                    if (button >= 0) target = mapButtons[button];
                    else if (game.ShowControls && CloseControlsAt(at)) target = controlsClose;
                    else if ((row = chartWorlds.IndexOf(MapItemAt(at))) >= 0) target = chartColumns[0];
                }
                else if (game.ShowControls && CloseControlsAt(at)) target = controlsClose;
                else if (game.Hermit.Talking)
                {
                    int question = QuestionAt(at);
                    target = question >= 0 ? questions[question] : Hit(talkClose.rectTransform, at, out _) ? talkClose : null;
                }
            }
            if (target == null)
            {
                if (pointerFocus != null) pointerFocus.enabled = false;
                return false;
            }
            if (pointerFocus == null)
            {
                pointerFocus = Fill(target.transform, Vector2.one * 0.5f, Vector2.zero, Vector2.zero, new Color(0.25f, 0.91f, 1, 0.9f), outline);
                pointerFocus.name = "VR pointer focus";
                pointerFocus.raycastTarget = false;
            }
            pointerFocus.enabled = true;
            var focus = pointerFocus.rectTransform;
            if (focus.parent != target.transform) focus.SetParent(target.transform, false);
            var rect = target.rectTransform.rect;
            float height = row < 0 ? rect.height : target.font.lineHeight * target.fontSize / (float)target.font.fontSize * target.lineSpacing;
            focus.sizeDelta = new Vector2(rect.width, height);
            focus.anchoredPosition = row < 0 ? Vector2.zero : Vector2.up * (rect.height / 2 - (row + 1.5f) * height);
            return true;
        }

        Color DamageTint()
        {
            var player = game.Player;
            float left = player.flying ? player.hull / player.MaxHull : player.health / player.MaxHealth;
            return new Color(0.8f, 0.025f, 0.015f, game.Dead ? 0.45f : 0.4f * math.saturate(1 - (Time.time - player.HurtAt) / 0.6f) + 0.52f * math.saturate(1 - left * 4));
        }

        void LateUpdate()
        {
            if (DeviceControls.VR)
            {
                if (surround == null)
                {
                    surroundInk = new Material(Overlay) { renderQueue = 3990 };
                    surroundInk.SetFloat("_Solid", 1);
                    surroundInk.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Front);
                    var visor = Shapes.Part(Camera.main.transform, "Full field visor tint", Shapes.Orb(1, 24), surroundInk, Vector3.zero, Vector3.one * 3, false);
                    visor.layer = LayerMask.NameToLayer("UI");
                    surround = visor.GetComponent<Renderer>();
                }
                Color tint = game.Frontier.Whiteout > 0 ? new Color(1, 1, 1, game.Frontier.Whiteout)
                    : game.Paused ? Color.clear : DamageTint();
                surround.enabled = tint.a > 0;
                surroundInk.SetColor("_Tint", tint);
                flash.enabled = false;
            }
            var player = game.Player;
            var system = game.System;
            var body = system.ReferenceBody;
            var scanner = game.Scanner;
            var hermit = game.Hermit;
            float now = Time.unscaledTime;
            line.fontSize = DeviceControls.Touch || DeviceControls.Deck || DeviceControls.VR ? 32 : 26;
            captionText.fontSize = DeviceControls.Touch || DeviceControls.Deck || DeviceControls.VR ? 30 : 24;
            bool playing = !game.Paused && !game.Dead && !hermit.Talking && !player.SystemView && !game.ShowControls && !game.Fall.active, planetView = player.PlanetView;
            bool showVisor = playing && !game.GetComponent<DeviceControls>().ReadingWrist;
            // places are marked while playing and on the map of the world they are on
            bool charting = playing || (planetView && !game.Paused && !game.Dead && !game.ShowControls);
            var worlds = system.Descriptor.bodies;
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            // The further the camera is from the scene's origin, the coarser positions there are, and
            // over the map of the system it is a long way off: the canvas is kept big enough in the
            // world for that coarseness to stay far below a pixel, or its text would shiver.
            canvas.planeDistance = Mathf.Max(100f, Mathf.NextPowerOfTwo((int)(0.02f * canvas.worldCamera.transform.position.magnitude)));

            if (DeviceControls.VR)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                bool windscreen = player.Seated && (playing || game.Fall.active);
                var mount = windscreen ? player.Cockpit.transform : Camera.main.transform;
                canvas.transform.SetParent(mount, false);
                ScreenPanel.sizeDelta = new Vector2(1920, 1080);
                canvas.transform.localScale = Vector3.one * (windscreen ? 0.00070f : 0.00068f);
                canvas.transform.localPosition = windscreen ? new Vector3(0, 0.95f, 2.18f) : Vector3.forward * 0.85f;
                canvas.transform.localRotation = Quaternion.identity;
            }
            mapImage.enabled = DeviceControls.VR && player.SystemView && game.MapPage == 0 && player.MapCamera != null;
            if (mapImage.enabled) mapImage.texture = player.MapCamera.targetTexture;
            // where you are and how you are travelling; the cockpit has its own dials, so the big figures step aside there
            string craft = planetView ? "MAP" : player.SystemView ? "SYSTEM MAP" : !player.flying ? player.walker.swimming ? player.Has(Gear.Suit) ? "SUBMERGED" : "DROWNING" : player.cloaked ? "CLOAKED" : player.Has(Gear.Suit) ? "IN THE SUIT" : "ON FOOT"
                : player.ship.landed ? "SHIP  ·  LANDED" : player.landing ? "SHIP  ·  LANDING"
                : player.Autopilot ? $"AUTOPILOT TO {worlds[player.Destination].name.ToUpperInvariant()}" : $"SHIP  ·  ASSIST {(player.assist ? "ON" : "OFF")}";
            // how hot it is outside, in red once it is nearly more than hull or suit can take
            string outside = $"{Climate.Celsius(player.Heat):0} °C";
            if (player.Heat > 0.8f * player.HeatLimit) outside = $"<color=#ff5a40>{outside}</color>";
            place.text = $"{(system.Reference == 0 ? "DEEP SPACE" : body.name.ToUpperInvariant())}  ·  {craft}{(system.Rain > 0.05f ? "  ·  RAIN" : "")}  ·  {outside}"
                + (player.Irradiated ? "  ·  <color=#ff5a40>RADIATION</color>" : "");
            // in the ship: how fast, and how far to the surface that matters. On foot neither means much.
            readout.enabled = playing && player.flying && !player.Seated;
            if (readout.enabled)
            {
                string[] gap = Distance(player.Range).ToUpperInvariant().Split(' ');
                readout.text = $"{math.length(player.Velocity):0}<size=15>  M/S        </size>{gap[0]}<size=15>  {gap[1]} TO {worlds[player.Ahead].name.ToUpperInvariant()}</size>";
            }
            // what is in hand, and for a firearm what is left in it
            heldWeapon.text = player.Armed ? player.Weapon.name.ToUpperInvariant() : "UNARMED";
            heldWeapon.fontSize = DeviceControls.VR ? 24 : 20;
            heldWeapon.horizontalOverflow = HorizontalWrapMode.Overflow;
            var dog = game.Frontier.FollowingDog;
            dogLink.enabled = dog.HasValue;
            if (dog.HasValue) dogLink.text = "ROBODOG  ·  " + (system.ReferenceBody.Dry(player.Position) ? "AT ENTRANCE" : Distance(math.distance(player.Position, dog.Value)));
            weaponGlyph.Show(player.WeaponSlot);
            bool touchLayout = DeviceControls.HudActions && !DeviceControls.VR;
            var cardRect = weaponCard.GetComponent<RectTransform>();
            cardRect.pivot = cardRect.anchorMin = cardRect.anchorMax = Vector2.right;
            cardRect.sizeDelta = new Vector2(Mathf.Max(heldWeapon.preferredWidth + 78, dog.HasValue ? dogLink.preferredWidth + 24 : 0), dog.HasValue ? 65 : 38);
            cardRect.anchoredPosition = DeviceControls.VR ? new Vector2(player.Seated ? -470 : -185, 26) : new Vector2(-(Screen.width - Screen.safeArea.xMax) / canvas.scaleFactor - 12, (touchLayout ? Screen.safeArea.yMin + Screen.safeArea.height * 0.59f : Screen.safeArea.yMin) / canvas.scaleFactor + 8);
            heldWeapon.rectTransform.anchoredPosition = new Vector2(65, -5);
            weaponGlyph.rectTransform.localScale = Vector3.one * 0.32f;
            weaponGlyph.rectTransform.anchoredPosition = new Vector2(8, cardRect.sizeDelta.y - 28);
            Show(weaponCard, showVisor && !game.Buried && !game.GetComponent<DeviceControls>().Expanded);
            if (!DeviceControls.VR)
            {
                float centralWidth = ScreenPanel.rect.width - 2 * (touchLayout ? 310 : 420);
                bool narrow = centralWidth < 375;
                lesson.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(narrow ? ScreenPanel.rect.width - 48 : centralWidth, 100, 900), 88);
                lesson.rectTransform.anchoredPosition = new Vector2(0, narrow ? 518 : 320);
                lesson.resizeTextForBestFit = true; lesson.resizeTextMinSize = 16; lesson.resizeTextMaxSize = 24;
            }
            if (DeviceControls.VR)
            {
                lesson.rectTransform.anchoredPosition = new Vector2(0, 355);
                lesson.rectTransform.sizeDelta = new Vector2(1250, 80);
                banner.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -252);
                lesson.fontSize = 29;
                lesson.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            statusPlate.sizeDelta = new Vector2(Mathf.Max(place.preferredWidth, readout.enabled ? readout.preferredWidth : 0f, 290f) + 40f, readout.enabled ? 148f : playing ? 82f : 62f);
            statusPlate.anchoredPosition = new Vector2(-20f, readout.enabled ? -18f : playing ? 48f : 68f);
            // from the pilot's seat the dash says all of this itself
            status.GetComponent<RectTransform>().anchoredPosition = new Vector2(56, touchLayout ? 390 : 180);
            Show(status, !game.Fall.active && !game.Paused && !game.Dead && (player.flying || player.SystemView) && !player.Seated && !game.ShowControls && !game.GetComponent<DeviceControls>().Expanded);
            reticle.enabled = playing && !DeviceControls.VR && !player.Seated;
            // sunk into a giant, its cloud closes over the view
            float sunk = body.kind == BodyKind.Gas ? math.saturate((body.radius - math.length(player.Position)) / 60f) : 0f;
            // and under the ground there is only the dark, thinning as the earth is torn away
            murk.color = game.Buried && !DeviceControls.VR ? new Color(0f, 0f, 0f, 0.84f - 0.74f * Mathf.Pow((float)game.Dug / Game.Digs, 2.2f))
                : new Color(body.ground.x * 0.6f, body.ground.y * 0.6f, body.ground.z * 0.6f, 0.97f * sunk);
            flash.color = DamageTint();

            // the mission in hand, and how much of its guard has fallen
            runText.text = game.MissionAction; runText.color = Accent;
            bool mobile = DeviceControls.HudActions || DeviceControls.VR;
            runText.fontSize = DeviceControls.VR ? 30 : mobile ? 24 : 19;
            runText.horizontalOverflow = HorizontalWrapMode.Wrap;
            runText.rectTransform.sizeDelta = new Vector2(Mathf.Min(1000, ((RectTransform)canvas.transform).rect.width - 112), 74);
            runText.resizeTextForBestFit = true; runText.resizeTextMinSize = 14; runText.resizeTextMaxSize = runText.fontSize;
            missionPlate.sizeDelta = new Vector2(Mathf.Clamp(runText.preferredWidth, 220, 1000) + 24, runText.preferredHeight + 16);
            run.GetComponent<RectTransform>().anchoredPosition = new Vector2(DeviceControls.VR ? 255 : 56, DeviceControls.VR ? -142 : mobile ? -105 : -48);
            helpHint.text = DeviceControls.Key("ESC · MENU", "START · MENU", "MENU", "HOLD LEFT B · MENU");
            Show(run, showVisor && !game.Buried);
            escapeText.text = game.Fall.active ? game.Frontier.Ending : game.Outpost.Demolition > 0 ? $"BURROW DEMOLITION · {math.ceil(game.Outpost.Demolition):0}s" : $"CORE COLLAPSE · ESCAPE {math.ceil(game.Outpost.Escape):0}s";
            Show(escape, !game.Paused && (game.Fall.active || playing && (game.Outpost.Demolition > 0 || game.Stage == Campaign.Citadel && game.Outpost.Escape > 0)));


            // the mission in hand in full
            if (briefed != game.Stage) (briefed, briefSince) = (game.Stage, now);
            bool briefing = !game.Buried && game.Stage < Campaign.Won && !game.Paused && !game.Dead && !hermit.Talking
                && player.SystemView && game.MapSummary && !game.ShowControls;
            if (briefing)
            {
                var mission = game.Mission;
                int on = game.Outpost.World >= 0 ? game.Outpost.World : SolarSystemDescriptor.Home;
                float far = game.Goal is float3 goal ? math.distance(player.Position, goal)
                    : math.max(0f, Vector3.Distance(player.Position - system.Anchor, system.Bodies[on].transform.position) - worlds[on].radius);
                string Sentence(string words) => string.IsNullOrEmpty(words) ? "Establish a foothold" : char.ToUpperInvariant(words[0]) + words.Substring(1).ToLowerInvariant();
                briefHead.text = $"MISSION {game.Stage + 1} OF {Campaign.Missions.Length}";
                briefTitle.text = mission.name;
                briefTask.text = game.MissionAction;
                briefRows.text = $"{worlds[on].name}  ·  {Distance(far)} from you{(game.Goal != null ? "  ·  follow its mark" : on == system.Reference ? "" : "  ·  its mark is the bright one")}\n"
                    + $"{Sentence(Campaign.Spoils(game.Stage))}\n"
                    + (game.Stage < Campaign.Citadel ? $"{Campaign.Missions[game.Stage + 1].name}  ·  {Campaign.Spoils(game.Stage + 1).ToLowerInvariant()}" : "The way out of this system");
                briefWords.text = $"\"{game.Wording(mission.brief)}\"";
            }
            Show(brief, briefing, 4f);

            // who is writing the scanner's entry or the hermit's answer, while they are at it
            bool writing = (game.PreparingText || hermit.Thinking) && !game.Paused;
            if (writing)
            {
                var badge = badges[Oracle.Voice];
                askText.text = $"{Oracle.Voices[Oracle.Voice].ToUpperInvariant()}  ·  {Oracle.Model(Oracle.Voice).name.ToUpperInvariant()}{Game.Activity}";
                askMark.sprite = badge != null ? badge : dot;
                askMark.color = new Color(badge != null ? 1f : Accent.r, badge != null ? 1f : Accent.g, badge != null ? 1f : Accent.b, 0.55f + 0.45f * Mathf.Sin(now * 5f));
            }
            Show(asking, writing, 6f);

            if (hermit.Remarking)
            {
                float width = DeviceControls.VR ? 800 : Mathf.Clamp(((RectTransform)canvas.transform).rect.width * 0.47f, 480, 840);
                captionText.rectTransform.sizeDelta = new Vector2(width, 100);
                captionText.text = $"<color=#6bdcfa>{hermit.Name.ToUpperInvariant()}</color>     {hermit.Line}";
                captionPlate.sizeDelta = new Vector2(Mathf.Min(captionText.preferredWidth, width) + 48f, captionText.preferredHeight + 22f);
                caption.GetComponent<RectTransform>().anchoredPosition = new Vector2(DeviceControls.VR ? 120f : 0f, DeviceControls.VR ? 110f : DeviceControls.HudActions ? 232f : 172f);
            }
            Show(caption, game.Subtitles && hermit.Remarking && playing, 4f);
            string action = showVisor && !game.Buried && !game.Tutorial.Locked ? game.Prompt : null;
            string guidance = showVisor && !game.Buried ? game.Tutorial.Hint : "";
            lesson.text = guidance == runText.text || action != null && (game.Outpost.Near || game.Stage == Campaign.Recovery && player.NearShip) ? "" : guidance;
            if (showVisor && !player.flying && player.Has(Gear.Stealth) && (player.cloaked || Time.time - player.HurtAt < 4))
                lesson.text = player.cloaked ? game.Wording("cloakActive") : ControlName("R") + " · " + game.Wording("cloakHint");
            lessonPlate.gameObject.SetActive(DeviceControls.VR && lesson.text.Length > 0);
            if (lessonPlate.gameObject.activeSelf)
            {
                lessonPlate.sizeDelta = new Vector2(Mathf.Min(lesson.preferredWidth, lesson.rectTransform.rect.width) + 36, lesson.preferredHeight + 12);
                lessonPlate.anchoredPosition = new Vector2(0, lesson.rectTransform.anchoredPosition.y + (lesson.rectTransform.rect.height - lessonPlate.rect.height) * 0.5f);
            }
            var microphone = game.GetComponent<VoiceInput>();
            cheatGlyph.enabled = game.Paused && !game.Levelling;
            cheatGlyph.rectTransform.anchoredPosition = new Vector2(-22 - (Screen.width - Screen.safeArea.xMax) / canvas.scaleFactor, 22 + Screen.safeArea.yMin / canvas.scaleFactor);
            if (game.ShowControls) foreach (var binding in helpKeys)
            {
                binding.label.enabled = binding.action.enabled = binding.divider.enabled = (game.FactoryUnlocked || !(binding.key is "B" or "Wrist BUILD"))
                    && (!(Application.isMobilePlatform || DeviceControls.Touch) || !(binding.key is "M" or "[  ]" or "-  =" or "Opt Return"));
                binding.label.text = DeviceControls.VR ? binding.key : ControlName(binding.key);
                binding.label.fontSize = DeviceControls.VR ? 20 : DeviceControls.Deck ? 25 : 22;
            }
            newsText.text = game.Notice;
            Show(news, playing && now - game.NoticeAt < NewsStays, now - game.NoticeAt < NewsStays ? 6f : 1.2f);
            helpHint.fontSize = DeviceControls.VR ? 22 : 13;
            ((RectTransform)hint.transform).anchoredPosition = cardRect.anchoredPosition + Vector2.up * (cardRect.rect.height + 6);
            Show(hint, (now - game.Began < 14f || DeviceControls.VR && player.Seated) && playing && !game.ShowControls, 1f);

            // what the interact key would do
            if (action != null)
            {
                promptText.text = action;
                promptKey.text = DeviceControls.Use;
                float cap = Mathf.Max(40, promptKey.preferredWidth + 20);
                promptKey.rectTransform.sizeDelta = promptCap.sizeDelta = new Vector2(cap, 40);
                float width = cap + 14f + promptText.preferredWidth;
                promptCap.anchoredPosition = new Vector2(cap / 2 - width / 2f, 0f);
                promptPlate.sizeDelta = new Vector2(width + 36f, 60f);
                promptText.rectTransform.anchoredPosition = new Vector2(cap + 14 - width / 2f, 0f);
            }
            // from the pilot's seat it sits above the dash, clear of the dials
            prompt.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, DeviceControls.VR ? 475f : player.Seated ? 400f : hermit.Remarking && game.Subtitles ? 410f : 300f);
            Show(prompt, action != null, 8f);

            // the name of a world as you arrive in its sky
            if (bannerBody != system.Reference || bannerSeed != game.Seed)
            {
                bannerBody = system.Reference;
                bannerSeed = game.Seed;
                bannerSince = now;
                bannerTitle.text = Spaced(system.Reference == 0 ? "Deep space" : body.name);
                bannerSub.text = system.Reference == 0 ? $"in the light of {body.name}" : body.summary;
            }
            // under the ground there is no telling what world this is: only what to do about it
            if (game.Buried)
            {
                (bannerBody, bannerSince) = (-2, now);
                bannerTitle.text = Spaced("Emergency power");
                bannerSub.text = game.Mission.task;
            }
            // or as play begins there
            bool arriving = now - Mathf.Max(bannerSince, game.Began) < BannerStays;
            Show(banner, player.flying && arriving && playing, arriving ? 2f : 0.7f);

            // the scanner's card: the entry types itself out, then stays a while
            var entry = scanner.Result;
            bool carding = playing && entry != null && now - scanner.Since < CardStays;
            if (carding)
            {
                string subject = scanner.Subject.Split(',')[0].ToUpperInvariant();
                cardHead.text = $"SCANNER  ·  {subject}\n{scanner.Source}";
                if (carded != entry)
                {
                    carded = entry;
                    cardTitle.text = entry.title;
                    cardBody.text = entry.text;
                    cardHeight = 174f + cardBody.preferredHeight;
                }
                cardBody.text = entry.text.Substring(0, math.min(entry.text.Length, (int)((now - scanner.Since) * Typing)));
                int page = game.Journal.IndexOf(entry) + 1;
                cardFoot.text = page > 0 ? $"JOURNAL  ·  ENTRY {page} OF {game.Journal.Count}" : "";
                var rect = card.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(500f, Mathf.Lerp(rect.sizeDelta.y, cardHeight, 12f * Time.unscaledDeltaTime));
            }
            Show(card, carding, carding ? 6f : 1.5f);

            // talking with the hermit
            if (hermit.Talking)
            {
                speaker.text = $"{hermit.Name.ToUpperInvariant()}  ·  {(hermit.Aboard ? "YOUR COPILOT" : $"ROBOT OF {system.Descriptor.bodies[SolarSystemDescriptor.Home].name.ToUpperInvariant()}")}\n{hermit.Source}";
                line.text = hermit.Thinking ? new string('.', 1 + (int)(now * 3f) % 3) : hermit.Line.Substring(0, math.min(hermit.Line.Length, (int)((now - hermit.Spoke) * Typing)));
                draft.text = hermit.Thinking ? "" : $"<color=#6bdcfa>›</color>  {hermit.Draft}{((int)(now * 2f) % 2 == 0 ? "▏" : "")}";
                // until something is typed, the questions that a number key asks
                foreach (var option in questions) option.enabled = !hermit.Thinking && hermit.Draft.Length == 0;
                talkKeys.text = microphone.Listening ? "LISTENING · RELEASE TO SEND" : DeviceControls.VR ? "RIGHT A · FIRST QUESTION    LEFT A · SECOND    GRIP · THIRD    RIGHT B · LEAVE"
                    : hermit.Draft.Length > 0 ? $"{DeviceControls.Key("RETURN", "A", "KEYBOARD DONE", "RIGHT A")}   SAY IT        {DeviceControls.Key("ESC", "B", "CLOSE ×", "RIGHT B")}   LEAVE"
                    : DeviceControls.Touch ? "TAP A QUESTION OR TYPE BELOW" : "1 / 2 / 3 · ASK    OR TYPE BELOW";
            }
            Show(talk, hermit.Talking && !game.Paused, 6f);

            // the title page, the pause menu and the end of a run
            if (game.Paused || game.Dead)
            {
                var items = game.Items;
                bool paged = !game.Dead && !game.Confirming, voice = game.Standing != null && paged;
                bool settings = game.Page == Game.Settings && paged, cheats = game.Page == Game.Cheats && paged;
                string mission = game.Stage >= Campaign.Won ? "THE SYSTEM IS FREE" : game.Mission.name.ToUpperInvariant();
                menuTitle.text = game.Dead ? Spaced(player.flying ? "Ship lost" : "You died") : game.Confirming ? game.Confirmation : Spaced(game.Page ?? "Infinigame");
                menuSub.text = game.Levelling ? $"LEVEL {game.Progression.level + 1} · SELECT AN ATTRIBUTE, THEN CONFIRM" : game.Dead ? game.Epitaph.ToUpperInvariant() : game.Confirming ? "Choose Confirm to proceed or Cancel to return."
                    : cheats ? $"THE MISSION IN HAND IS {mission}"
                    : voice ? game.Standing
                    : game.Page == Game.Writers ? "EXPEDITION DIALOGUE AND GUIDANCE"
                    : game.Page == Game.WristAlignment ? "LEFT FOREARM" : game.Page == Game.HandAlignment ? "LEFT AND RIGHT GAUNTLETS"
                    : settings ? "HOW THE GAME LOOKS AND SOUNDS, AND WHO WRITES FOR IT THROUGH YOUR OWN ACCOUNT"
                    : game.Title ? "CHOOSE A SLOT TO PLAY   ·   EACH KEEPS A GAME AND A JOURNAL OF ITS OWN"
                    : $"THE {worlds[0].name.ToUpperInvariant()} SYSTEM   ·   {mission}   ·   DEPTH {game.Depth}   ·   {game.Journal.Count} JOURNAL ENTRIES";
                menuNote.text = game.Dead ? game.Tally : paged ? game.Hint : "";
                menuKeys.text = game.Levelling ? DeviceControls.Key("UP / DOWN · RETURN CHOOSES", "STICK / D-PAD · A CHOOSES", "TAP TO CHOOSE", "EITHER TRIGGER CHOOSES") : game.Dead ? DeviceControls.Key("RETURN", "A", "TAP", "TRIGGER") + " · TRY AGAIN" : DeviceControls.Key("UP / DOWN · RETURN CHOOSES · ESC BACK", "STICK / D-PAD · A CHOOSES · B BACK", "TAP TO CHOOSE", "EITHER STICK SELECTS · EITHER TRIGGER CHOOSES · RIGHT B BACK");
                var safe = ScreenPanel.rect.size;
                if (!DeviceControls.VR) safe = Vector2.Scale(safe, new Vector2(Screen.safeArea.width / Mathf.Max(1, Screen.width), Screen.safeArea.height / Mathf.Max(1, Screen.height)));
                menuContent.localScale = Vector3.one * Mathf.Min(1, safe.x / 1500f, safe.y / 960f);
                if (!DeviceControls.VR) menuContent.anchoredPosition = (Screen.safeArea.center - new Vector2(Screen.width, Screen.height) / 2) / canvas.scaleFactor;
                int rows = game.Page != "Missions" && items.Length > 9 ? (items.Length + 1) / 2 : items.Length;
                float spacing = Mathf.Min(60, 440f / Mathf.Max(1, rows - 1));
                for (int i = 0; i < menuItems.Length; i++)
                {
                    menuItems[i].gameObject.SetActive(i < items.Length);
                    if (i >= items.Length) continue;
                    float x = rows == items.Length ? 0 : i < rows ? -280 : 280;
                    menuItems[i].rectTransform.anchoredPosition = new Vector2(x, 64 - spacing * (i % rows));
                    menuItems[i].horizontalOverflow = game.Page == "Missions" ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap;
                    menuItems[i].resizeTextForBestFit = true;
                    menuItems[i].resizeTextMinSize = 16;
                    menuItems[i].resizeTextMaxSize = 30;
                    menuItems[i].rectTransform.sizeDelta = new Vector2(x == 0 ? 1100 : 530, Mathf.Min(50, spacing - 2));
                    bool submenu = items[i] == Game.Settings || items[i] == Game.Writers || items[i] == Game.Cheats || items[i] == "Controls" || items[i] == "Choose mission" || items[i] == "Expedition map" || items[i] == Game.HandAlignment || items[i] == Game.WristAlignment || game.Page == Game.Writers && items[i] != "Back" && items[i] != "Sign out";
                    menuItems[i].text = items[i] + (submenu ? "   ›" : "");
                    menuItems[i].color = items[i] == Game.Writers ? Accent : i == game.Selected ? Ink : Dim;
                }
                var chosen = menuItems[math.min(game.Selected, items.Length - 1)];
                menuBar.rectTransform.anchoredPosition = chosen.rectTransform.anchoredPosition - new Vector2(chosen.preferredWidth / 2f + 22f, 0f);
            }
            Show(menu, game.Paused || game.Dead, game.Dead ? 1.5f : 10f);

            // the system map's table
            if (player.SystemView)
            {
                var rows = Enumerable.Range(1, worlds.Length - 1).Where(i => system.Charted(i)
                    && (worlds[i].kind != BodyKind.Station || game.Stage >= Campaign.Citadel)).ToList();
                foreach (var column in chartColumns) column.fontSize = Mathf.Min(18, Mathf.FloorToInt(350f / (rows.Count + 1) / (column.lineSpacing * column.font.lineHeight / column.font.fontSize)));
                chartWorlds.Clear(); chartWorlds.AddRange(rows);
                string Column(string head, Func<int, string> cell) => head + "\n" + string.Join("\n", rows.Select(cell));
                chartColumns[0].text = Column("DESTINATION", row => (row == game.MapSelection ? "› " : "  ") + (worlds[row].kind == BodyKind.Station ? "CITADEL" : worlds[row].name.ToUpperInvariant()));
                chartColumns[1].text = Column("FOOTHOLD", row => !game.FactoryUnlocked ? "UNSECURED" : row == SolarSystemDescriptor.Home ? $"{game.Frontier.Total(row, front => front.factory ? 1 : 0):0}/3 FACTORIES" : game.Frontier.State(row).shipyard ? "SHIPYARD" : game.Frontier.State(row).factory ? "FACTORY" : $"{game.Frontier.State(row).kills}/{War.RequiredKills(game.Frontier.State(row))} kills");
                chartColumns[2].text = Column("ALIENS", row => $"{math.ceil(game.Frontier.Total(row, front => front.aliens)):0}");
                chartColumns[3].text = Column("ROBODOGS", row => $"{math.floor(game.Frontier.Total(row, front => front.dogs)):0}");
                chartColumns[4].text = Column("HOSTILE SHIPS", row => $"{math.ceil(game.Frontier.Total(row, front => front.alienShips)):0}");
                chartColumns[5].text = Column("OUR SHIPS", row => $"{math.floor(game.Frontier.Total(row, front => front.ships)):0}");
                chartColumns[6].text = Column("MIND", row => game.Frontier.Total(row, front => front.mindDead ? 0 : row == SolarSystemDescriptor.Home ? Campaign.Minds(Campaign.FirstBurrow + front.region) : 1) == 0 ? "SILENT" : $"{game.Frontier.Total(row, front => front.mindDead ? 0 : row == SolarSystemDescriptor.Home ? Campaign.Minds(Campaign.FirstBurrow + front.region) : 1):0} ACTIVE");
                chartColumns[7].text = Column("WAR", row => game.Frontier.Total(row, front => front.aliens) <= 0 && game.Frontier.Total(row, front => front.alienShips) <= 0 ? "WON" : "ACTIVE");
                chartColumns[8].text = "";
                chartColumns[9].text = "";
                chartFoot.text = $"PAUSED · {ControlName("W A S D")} SELECT · {ControlName("T")} AUTOPILOT · {ControlName("Tab")} CONTINUE · {game.GetComponent<FrameLog>().Status}";
            }
            Show(chart, player.SystemView && game.MapSummary && !game.ShowControls && !game.Paused && !game.Dead, 6f);
            Show(mapBar, player.SystemView && !game.Paused && !game.Dead, 10);
            pageBackground.enabled = !game.Paused && !game.Dead && (game.ShowControls || player.SystemView && game.MapPage != 0);
            float pageScale = Mathf.Min(1, (ScreenPanel.rect.width - 32) / 1380f, ScreenPanel.rect.height / 1080f);
            chart.transform.localScale = mapBar.transform.localScale = brief.transform.localScale = Vector3.one * pageScale;
            controls.transform.localScale = Vector3.one * pageScale * (player.SystemView ? 0.86f : 0.97f);
            controls.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, player.SystemView ? -48 : 0);
            mapButtons[7].color = game.MapPage == 3 ? Accent : Ink;
            for (int i = 0; i < 3; i++) mapButtons[i].color = game.MapPage == i ? Accent : Ink;
            mapButtons[4].enabled = game.MapSummary && game.Presence.fronts.Any(front => front.shipyard);
            mapButtons[5].text = game.Frontier.Construction.ToUpperInvariant();
            mapButtons[5].enabled = game.MapSummary && game.FactoryUnlocked && !player.flying;
            mapButtons[6].enabled = game.MapSummary && player.Has(Gear.Ship);
            mapButtons[8].enabled = game.MapSummary && game.Frontier.CanCallDogs;
            if (mapButtons[8].enabled) mapButtons[8].text = game.Escorts.pack ? "DISMISS ROBODOG" : "CALL ROBODOG";
            attributePlate.enabled = attributes.enabled = game.MapPage == 2 && !game.ShowControls;
            if (player.SystemView && attributes.enabled)
                attributes.text = $"LEVEL {game.Progression.level}   ·   SUIT ATTRIBUTES\n\n" + string.Join("\n", Enumerable.Range(0, game.Progression.ranks.Length).Select(i => game.Progression.Describe((Upgrade)i, player.Weapon.damage, false)));

            bool collapse = DeviceControls.VR && game.Frontier.Collapsing;
            if (collapse) escapeText.text = escapeText.text.Replace("SYSTEM CONSUMPTION ", "SYSTEM CONSUMPTION\n");
            escapeText.fontSize = collapse ? 68 : DeviceControls.VR ? 45 : 30;
            float inset = collapse ? 385 : DeviceControls.VR ? 225 : game.Fall.active && player.Seated ? ScreenPanel.rect.height * 0.23f : 105;
            escape.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -inset);
            escapeText.rectTransform.sizeDelta = new Vector2(collapse ? 1400 : 970, collapse ? 180 : 85);
            escape.GetComponent<RectTransform>().sizeDelta = escapeText.rectTransform.sizeDelta + new Vector2(40, 14);
            worldMarkers.enabled = !game.Paused && !game.Dead && !game.Fall.active && !game.ShowControls && !game.Hermit.Talking;
            if (game.ShowControls) stats.text = $"{ControlName("Esc")} TO CLOSE · {game.GetComponent<FrameLog>().Status}";
            controls.alpha = game.ShowControls && !game.Paused ? 1 : 0;
            if (game.ShowControls) { chart.alpha = brief.alpha = 0; mapImage.enabled = false; }

            // markers: other worlds, the ship when you have walked away from it, the camp, the last thing scanned
            used = 0;
            labels.Clear();
            void Reserve(RectTransform panel, Camera camera)
            {
                panel.GetWorldCorners(markerCorners);
                Vector2 low = RectTransformUtility.WorldToScreenPoint(camera, markerCorners[0]) / canvas.scaleFactor;
                Vector2 high = RectTransformUtility.WorldToScreenPoint(camera, markerCorners[2]) / canvas.scaleFactor;
                labels.Add(UnityEngine.Rect.MinMaxRect(Mathf.Min(low.x, high.x) - 6, Mathf.Min(low.y, high.y) - 6, Mathf.Max(low.x, high.x) + 6, Mathf.Max(low.y, high.y) + 6));
            }
            foreach (var group in new[] { weaponCard, run, status, caption })
            {
                if (group.alpha < 0.5f) continue;
                Reserve(group == run ? missionPlate : group == status ? statusPlate : group == caption ? captionPlate : group.GetComponent<RectTransform>(), canvas.worldCamera);
            }
            if (!DeviceControls.VR && game.GetComponent<SuitHud>().VisibleMeters is RectTransform meters)
                Reserve(meters, null);
            // over the map, names keep off the table of worlds and the mission's card as well as off each other
            if (player.SystemView && game.MapSummary && !planetView) labels.Add(new Rect(56f, 510f, 1328f, 420f));
            if (briefing) labels.Add(new Rect(56f, 226f, 780f, 262f));
            var view = player.SystemView && DeviceControls.VR && player.MapCamera != null ? player.MapCamera : canvas.worldCamera;
            Vector3 eye = view.transform.position, ground = system.Bodies[system.Reference].transform.position, self = player.Position - system.Anchor;
            Vector2 middle = ((RectTransform)canvas.transform).rect.size / 2f;
            // how far off another world is means something only from space: from the ground they are names in the sky
            bool inSpace = player.InSpace;
            if (!game.Fall.active && !game.Paused && !game.Dead && !hermit.Talking && !game.ShowControls && !game.Buried && !(player.SystemView && game.MapPage != 0))
            {
                // The world the mission is on stands out from the rest: or the planet it circles, from where a moon has no mark of its own.
                var outpost = game.Outpost;
                int goal = outpost.World >= 0 ? outpost.World : -1;
                if (goal >= 0 && !system.Charted(goal)) goal = system.Descriptor.Planet(goal);
                bool approaching = charting && player.flying && !player.SystemView && game.Stage >= Campaign.Tower && game.Stage < Campaign.Citadel
                    && outpost.World != system.Reference && outpost.World >= 0 && math.distance(self, system.Bodies[outpost.World].transform.position) < math.max(12000, 12 * worlds[outpost.World].radius);
                if (approaching && game.Goal is float3 landing)
                    Mark(landing - system.Anchor, "MISSION · " + worlds[outpost.World].name.ToUpperInvariant() + " LANDING SITE", Distance(math.distance(player.Position, landing)), Accent, false, -1, true);
                if (charting && !player.SystemView && game.Stage == Campaign.Won && game.Goal is float3 moonGoal)
                    Mark(moonGoal - system.Anchor, game.Fall.armed ? "MISSION · CLEAR THE MOON" : game.Frontier.CanBuild(2) ? "MISSION · BUILD MOON ENGINES HERE" : "MISSION · LAND ANYWHERE ON A MOON", Distance(math.distance(player.Position, moonGoal)), Accent, false, -1, true);
                if (charting && !player.SystemView && outpost.World == system.Reference && game.Stage == Campaign.Citadel && outpost.Rooms != null)
                {
                    Mark(outpost.Waypoint - system.Anchor, "MISSION · " + outpost.CitadelMark, Distance(math.distance(player.Position, outpost.Waypoint)), Accent, false, -1, true);
                    foreach (var foe in game.Battle.Foes)
                        if (foe.kind == FoeKind.Mind && foe.health > 0 && outpost.Rooms.InsideCitadel(outpost.Local(player.Position)))
                            Mark(foe.position - system.Anchor, foe.protectedCore ? "SHIELDED MIND" : "CITADEL MIND", Distance(math.distance(player.Position, foe.position)), Harm, true);
                }
                for (int i = 0; i < system.Bodies.Length && !planetView; i++)
                    if (!(approaching && i == outpost.World) && (inSpace || player.SystemView || i == goal && goal != system.Reference) && (i != system.Reference || player.SystemView) && system.Charted(i) && (worlds[i].kind != BodyKind.Station || game.Stage >= Campaign.Citadel))
                        // over the map the table has every distance, so the names stand alone and crowd each other less
                        Mark(system.Bodies[i].transform.position, (i == goal ? "MISSION · " : "") + (worlds[i].kind == BodyKind.Station ? "ENEMY CITADEL" : worlds[i].name.ToUpperInvariant()),
                            player.SystemView || !inSpace ? "" : worlds[i].kind == BodyKind.Station && game.Battle.Base <= 0f ? "DESTROYED"
                                : Distance(math.max(0f, Vector3.Distance(self, system.Bodies[i].transform.position) - worlds[i].radius)),
                            i == goal ? Accent : worlds[i].kind == BodyKind.Station ? Harm : Ink, false, i, i == goal && (i != system.Reference || inSpace));
                if (planetView)
                {
                    Mark(self, "YOU", "", Ink);
                    if (system.Reference == SolarSystemDescriptor.Home)
                        for (int region = 0; region < 3; region++)
                        {
                            var front = game.Frontier.State(SolarSystemDescriptor.Home, region);
                            float3 at = Campaign.Site(system.Descriptor, Campaign.FirstBurrow + region);
                            at *= body.GroundRadius(at) + 5;
                            Mark(at - system.Anchor, $"REGION {region + 1} · {(front.mindDead ? "MINDS SILENT" : $"{(game.Outpost.World == SolarSystemDescriptor.Home && game.Stage == Campaign.FirstBurrow + region ? game.Outpost.Left : Campaign.Minds(Campaign.FirstBurrow + region))} LINKED MINDS")}", !game.FactoryUnlocked ? "" : front.factory ? War.Controlled(front) ? "CONTROLLED" : "ROBODOGS CLEARING" : "FACTORY NEEDED", front.mindDead ? Accent : new Color(1, 0.3f, 0.8f), false, -1, true);
                        }
                    if (game.MapZoom <= 0.75f)
                    {
                        foreach (var foe in game.Battle.Foes)
                            if (foe.health > 0) Mark(foe.position - system.Anchor, Combat.Name(foe).ToUpperInvariant(), "", foe.kind == FoeKind.Mind ? new Color(1, 0.3f, 0.8f) : foe.kind == FoeKind.Raider ? new Color(0.65f, 0.45f, 1) : Harm, true);
                        foreach (var robot in game.Frontier.Contacts) Mark(robot.position - system.Anchor, robot.ship ? "ROBOT SHIP" : "ROBODOG", "", Accent);
                    }
                }
                // and on that world, the raiders' place itself, then what they kept there: marks that lead the way and are never out of sight
                if (charting && outpost.World == system.Reference && outpost.OuterLeft > 0)
                    Mark(outpost.Waypoint - system.Anchor, "MISSION · CLEAR LANDING ZONE", Distance(math.distance(player.Position, outpost.Waypoint)), Accent, false, -1, true);
                else if (charting && outpost.World == system.Reference && outpost.EquipmentReady)
                    Mark(outpost.Prize - system.Anchor, "MISSION · COLLECT EQUIPMENT", Distance(math.distance(player.Position, outpost.Prize)), Accent, false, -1, true);
                else if (charting && outpost.World == system.Reference && outpost.Recovered)
                {
                    float3 target = outpost.SiteCleared ? outpost.DeliverySite : outpost.Exit;
                    if (outpost.SiteCleared || !outpost.ClearOfDemolition)
                        Mark(target - system.Anchor, outpost.SiteCleared ? "MISSION · " + outpost.FactoryOrder.ToUpperInvariant() : "MISSION · BURROW EXIT", Distance(math.distance(player.Position, target)), Accent, false, -1, true);
                }
                else if (charting && outpost.World == system.Reference && outpost.Fallen && game.Stage != Campaign.Citadel)
                    Mark(ground + (Vector3)(outpost.Prize + math.normalizesafe(outpost.Prize) * 3f), "MISSION · ARM DEMOLITION",
                        Distance(math.distance(player.Position, outpost.Prize)), Accent, false, -1, true);
                else if (charting && outpost.World == system.Reference && outpost.OuterLeft == 0 && game.Stage < Campaign.Citadel)
                    Mark(ground + (Vector3)(game.Goal ?? outpost.Position), game.Stage == Campaign.FirstBurrow && outpost.Rooms != null && !outpost.Rooms.walls.Last().broken && math.distance(player.Position, outpost.Place(outpost.Rooms.entrance)) < 15 ? $"{DeviceControls.Fire} · PUNCH HERE" : player.flying && outpost.Shield.active ? "MISSION · LAND OUTSIDE THE SHIELD" : outpost.Entering ? "MISSION · BURROW ENTRANCE" : "MISSION · FOLLOW THE TUNNEL", Distance(math.distance(player.Position, game.Goal ?? outpost.Position)), Accent, false, -1, true);
                float3 walker = player.walker.position;
                if ((charting && (player.Has(Gear.Ship) || player.captive || game.Stage == Campaign.Recovery) || game.Tutorial.PointingAtShip) && !player.flying && math.distance(walker, player.ship.position) > 25f)
                    Mark(player.ship.position - system.Anchor, player.ShipOverhead ? "YOUR SHIP · IN ORBIT" : "SHIP", Distance(math.distance(walker, player.ship.position)), Ink, false, -1, true);
                // the hermit's fire, while he is still at it: the way to it is shown until he has been met
                float camp = math.distance(player.Position, hermit.Position);
                if (charting && !hermit.Aboard && system.Reference == SolarSystemDescriptor.Home && camp > 5f && (camp < 1500f || planetView))
                    Mark(ground + (Vector3)(hermit.Position + math.normalize(hermit.Position) * 2.4f), hermit.Name.ToUpperInvariant(), Distance(camp), Ink, false, -1, game.Stage == Campaign.Stranger);
                if (playing && dog.HasValue)
                    Mark(dog.Value - system.Anchor + math.normalizesafe(dog.Value) * 1.4f, "YOUR ROBODOG", Distance(math.distance(player.Position, dog.Value)), Accent);
                foreach (var factory in game.Colonies.Where(colony => colony.world == system.Reference))
                    if (!inSpace || planetView) Mark((Vector3)(factory.site - system.Anchor) + (Vector3)math.normalizesafe(factory.site) * 5, game.Frontier.State(factory.world, factory.region).shipyard ? "YOUR SHIPYARD" : "YOUR ROBODOG FACTORY", Distance(math.distance(factory.site, player.Position)), Dim);
                if (carding) Mark(scanner.Mark, "", "", Accent);
                // whatever is hunting you, and for a raider where to aim so the cannons meet it
                markerFoes.Clear();
                markerFoes.AddRange(game.Battle.Foes);
                markerEye = eye + (Vector3)system.Anchor;
                markerFoes.Sort(Nearer);
                foreach (var foe in markerFoes)
                {
                    float away = math.distance(foe.position, player.Position);
                    if (!playing) break;
                    if (foe.health <= 0) continue;
                    if (!inSpace && foe.outerGuard && outpost.World == system.Reference && away < 1500 && outpost.Visible((float3)eye + system.Anchor, foe.position))
                    {
                        Mark(foe.position - system.Anchor, foe.stationary ? "SPINE TURRET" : "HARROWER PATROL", Distance(away), Harm, true, -1, false, math.saturate(foe.health / math.max(1, foe.maximumHealth)));
                        continue;
                    }
                    if (inSpace && foe.kind != FoeKind.Raider || away > (inSpace ? 2500 : player.Powered ? 160 : 25)) continue;
                    if (!outpost.Visible((float3)eye + system.Anchor, foe.position)) continue;
                    Vector3 at = foe.position - system.Anchor, lead = at + (Vector3)((foe.velocity - player.Velocity) * (away / math.max(1, player.Weapon.speed)));
                    if (foe.kind != FoeKind.Base) Mark(at, Combat.Name(foe).ToUpperInvariant(), Distance(away), Harm, true, -1, false, math.saturate(foe.health / math.max(1, foe.maximumHealth)));
                    if (player.flying && foe.kind == FoeKind.Raider && away < 2500f && Vector3.Distance(view.WorldToScreenPoint(at), view.WorldToScreenPoint(lead)) > 14f)
                        Mark(lead, "", "", Accent);
                }
            }
            for (int i = used; i < markers.Length; i++) markers[i].at.gameObject.SetActive(false);

            // of is the body a mark stands for, which cannot hide itself. A guide leads the way and so is never
            // lost: with a world in the way it stands in the sky over the way there, and with its place out of
            // view it waits at the edge of the screen nearest it.
            void Mark(Vector3 world, string name, string detail, Color colour, bool boxed = false, int of = -1, bool guide = false, float health = -1)
            {
                Vector3 to = world - eye;
                // not through a world, the one underfoot or any other that stands in the way: only the maps see through them
                bool hidden = false;
                Vector3 shown = world;
                for (int i = 0; i < system.Bodies.Length && !hidden && !player.SystemView; i++)
                {
                    if (worlds[i].destroyed || worlds[i].kind == BodyKind.Gate || worlds[i].kind == BodyKind.Station) continue;
                    Vector3 centre = system.Bodies[i].transform.position - eye;
                    float along = Vector3.Dot(centre, to.normalized);
                    hidden = i != of && along > 0f && along < to.magnitude && (centre - to.normalized * along).magnitude < worlds[i].radius * 0.97f;
                    if (!hidden || !guide) continue;
                    Vector3 up = -centre.normalized;
                    Vector3 around = Vector3.ProjectOnPlane(to, up).normalized;
                    if (around.sqrMagnitude < 0.01f) around = Vector3.ProjectOnPlane(view.transform.up, up).normalized;
                    if (player.flying)
                    {
                        float radius = worlds[i].radius * 1.015f;
                        float cosine = Mathf.Clamp01(radius / Mathf.Max(radius, centre.magnitude));
                        shown = eye + centre + radius * (up * cosine + around * Mathf.Sqrt(1 - cosine * cosine));
                        detail = "FAR SIDE · " + detail;
                    }
                    else shown = eye + around * 1000;
                }
                Vector3 screen = view.WorldToScreenPoint(shown);
                // which way it lies from the middle of the view, which unlike where it projects to still means something abeam or astern
                Vector2 at = (Vector2)screen / canvas.scaleFactor, away = view.transform.InverseTransformPoint(shown);
                if (DeviceControls.VR)
                {
                    Vector3 uv = view.WorldToViewportPoint(shown);
                    at = Vector2.Scale(new Vector2(uv.x, uv.y), middle * 2);
                }
                bool astern = screen.z <= 0.01f, beyond = astern || Mathf.Abs(at.x - middle.x) > middle.x - Edge || Mathf.Abs(at.y - middle.y) > middle.y - Edge;
                bool mission = guide && (name.StartsWith("MISSION", StringComparison.Ordinal) || game.Stage == Campaign.Recovery || game.Stage == Campaign.Stranger);
                bool spatial = DeviceControls.VR && !player.SystemView && !(mission && (beyond || hidden));
                if (used == markers.Length || (hidden || astern) && (!guide || spatial)) return;
                var marker = markers[used++];
                marker.at.gameObject.SetActive(true);
                Transform parent = spatial ? worldMarkers.transform : canvas.transform;
                if (marker.at.parent != parent) marker.at.SetParent(parent, false);
                marker.arrow.enabled = !spatial && guide && beyond;
                if (marker.arrow.enabled)
                {
                    // dead astern, any edge will do
                    if (away.sqrMagnitude < 1e-6f) away = Vector2.down;
                    away *= Mathf.Min((middle.x - Edge) / Mathf.Max(Mathf.Abs(away.x), 1e-3f), (middle.y - Edge) / Mathf.Max(Mathf.Abs(away.y), 1e-3f));
                    at = middle + away;
                    if (!DeviceControls.VR)
                    {
                        Rect safe = Screen.safeArea;
                        at.x = Mathf.Clamp(at.x, safe.xMin / canvas.scaleFactor + Edge, safe.xMax / canvas.scaleFactor - Edge);
                        at.y = Mathf.Clamp(at.y, safe.yMin / canvas.scaleFactor + Edge, safe.yMax / canvas.scaleFactor - Edge);
                    }
                    if (player.Seated) at.y = Mathf.Max(at.y, middle.y * 0.64f);
                    marker.arrow.color = colour;
                    marker.arrow.rectTransform.anchoredPosition = away.normalized * 15f;
                    marker.arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg);
                }
                if (spatial)
                {
                    marker.at.SetPositionAndRotation(world, Quaternion.LookRotation(to, view.transform.up));
                    marker.at.localScale = Vector3.one * Mathf.Max(0.001f, to.magnitude * 0.00055f);
                }
                else
                {
                    marker.at.localScale = Vector3.one;
                    marker.at.localRotation = Quaternion.identity;
                    marker.at.anchoredPosition3D = at;
                }
                marker.icon.color = marker.name.color = colour;
                marker.icon.enabled = !boxed && !(spatial && (of >= 0 || name.StartsWith("YOUR SHIP") || name == "SHIP"));
                marker.box.enabled = boxed;
                marker.health.enabled = marker.healthTrack.enabled = health >= 0;
                marker.health.rectTransform.sizeDelta = new Vector2(64 * math.max(0, health), 5);
                marker.name.fontSize = guide ? 24 : DeviceControls.VR ? 18 : 13;
                marker.icon.rectTransform.sizeDelta = Vector2.one * (guide ? 14 : 7);
                marker.name.rectTransform.sizeDelta = new Vector2(550, 28);
                marker.name.text = name;
                marker.detail.text = detail;
                if (spatial)
                {
                    marker.name.enabled = marker.detail.enabled = true;
                    marker.name.rectTransform.anchoredPosition = Vector2.up * (of >= 0 || name.StartsWith("YOUR SHIP") ? 52 : 28);
                    marker.detail.rectTransform.anchoredPosition = Vector2.down * 39;
                    return;
                }
                // A name that would lie over another steps aside, and one with nowhere left to go gives
                // way: the mark itself stays. By choice the name sits over its mark and the distance
                // under it; failing that the two go together to one side of it or another.
                float wide = Mathf.Max(marker.name.preferredWidth, marker.detail.preferredWidth) + 10f, tall = detail.Length > 0 ? 36f : 18f;
                bool placed = name.Length + detail.Length == 0;

                // Every side close in is tried first; then, where worlds crowd together, a place in a
                // column off to the right of them, on the end of a line.
                for (int i = 0; i < (boxed ? 1 : Places.Length) && !placed; i++)
                {
                    bool astride = i == 0 && detail.Length > 0;
                    Vector2 side = Places[i], half = new Vector2(side.x * wide / 2f, side.y * tall / 2f);
                    Vector2 shift = astride ? Vector2.zero : half + new Vector2(side.x * 14f, side.y * 12f);
                    var box = new Rect(marker.at.anchoredPosition + shift - new Vector2(wide / 2f, astride ? 27f : tall / 2f), new Vector2(wide, astride ? 54f : tall));
                    bool free = !guide || box.xMin >= 0 && box.yMin >= 0 && box.xMax <= middle.x * 2 && box.yMax <= middle.y * 2;
                    foreach (var other in labels) free &= !other.Overlaps(box);
                    if (!free) continue;
                    labels.Add(box);
                    marker.name.rectTransform.anchoredPosition = shift + new Vector2(0f, astride ? 17.5f : detail.Length > 0 ? 9f : 0f);
                    marker.detail.rectTransform.anchoredPosition = shift - new Vector2(0f, astride ? 17.5f : 9f);
                    placed = true;
                }
                marker.name.enabled = marker.detail.enabled = placed;
            }
        }
    }
}
