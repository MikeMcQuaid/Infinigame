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
        static readonly Color Plate = new Color(0.02f, 0.035f, 0.06f, 0.5f);
        static readonly Color Harm = new Color(1f, 0.25f, 0.2f, 0.95f);
        const float Typing = 70f, CardStays = 16f, BannerStays = 6f, NewsStays = 4.5f, BriefStays = 10f, Edge = 64f;

        static readonly (string heading, string[] rows)[] Help =
        {
            ("ON FOOT", new[]
            {
                "W A S D|Run", "Mouse|Look", "Space|Jump, hold for thrusters", "W  aloft|Fly where you look", "Shift|Sprint", "F|Talk, take, board the ship", "Click|Fire", "R|Toggle fitted cloak",
                "1 2 3 4|Fists, beam, missiles, rail", "B|Colony and workshop", "Right click|Scan what you face", "V|First or third person",
            }),
            ("IN THE SHIP", new[]
            {
                "W A S D|Thrust and strafe", "Mouse|Steer", "Space  Ctrl|Climb, descend", "Shift|Boost", "Q  E|Roll", "F|Land, then leave",
                "Click|Fire", "1 2 3 4|Cannons, missiles, lance, torpedoes", "T|Autopilot to what you face", "G|Cross the powered gate", "Z|Flight assist", "V|Cockpit or chase view", "Right click|Scan the world ahead",
            }),
            ("ANYWHERE", new[]
            {
                "Tab|Hold for the map and the mission", "Return|Talk to your robot", "H|Show this", "Esc|Close this, pause", "M|Music on or off", "[  ]|Less or more detail", "-  =|Slower or faster look",
                "Opt Return|Full screen or window",
            }),
        };

        struct Marker
        {
            public RectTransform at;
            // a diamond for a place, a box drawn round a foe, a line out to a name that had to go further off,
            // and an arrow for a mark held at the edge of the screen because its place is out of view
            public Image icon, box, lead, arrow;
            public Text name, detail;
        }

        [NonSerialized] public Game game;

        static Material overlay;
        Canvas canvas;
        Sprite panel, outline;
        CanvasGroup status, prompt, banner, card, talk, menu, controls, hint, run, news, radar, chart, brief, asking, caption;
        Text place, readout, promptText, promptKey, bannerTitle, bannerSub, cardHead, cardTitle, cardBody, cardFoot, briefHead, briefTitle, briefTask, briefRows, briefWords, askText, captionText;
        Text speaker, line, draft, talkKeys, menuTitle, menuSub, menuNote, menuKeys, stats, vitalName, runText, newsText;
        Text[] menuItems;
        Image fuel, fuelTrack, vital, vitalTrack, charge, reticle, menuBar, flash, murk, beacon, askMark;
        Sprite dot;
        // each of the oracle's voices' own badge, where the project has been given one
        Sprite[] badges;
        // the radar's contacts: a blip each, on a stalk that shows how far above or below it is
        Image[] blips, stalks;
        Text[] chartColumns;
        Text chartFoot;
        RectTransform promptCap, statusPlate, promptPlate, captionPlate;
        Marker[] markers;
        // where this frame's marker names have been put, and the sides of its mark a name may go to, in order of preference
        readonly List<Rect> labels = new List<Rect>();
        static readonly Vector2[] Places = { Vector2.up, Vector2.right, Vector2.left, Vector2.down, new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) };
        float fps, bannerSince = float.MinValue, cardHeight, briefSince = float.MinValue;
        int bannerBody = -1, used, briefed = -1;
        uint bannerSeed;
        Entry carded;

        void Awake()
        {
            var host = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            // Far enough out to be large in the world, so that it stays sharp even where the camera is a
            // long way from the origin and positions are coarse. Its shader draws over whatever is nearer.
            canvas.planeDistance = 100f;
            overlay = new Material(Resources.Load<Shader>("Overlay"));
            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            Transform root = host.transform;
            panel = Rounded(0f);
            outline = Rounded(2f);
            dot = Rounded(0f, 16, 8f);
            var pointer = Pointer();
            Vector2 centre = new Vector2(0.5f, 0.5f), bottom = new Vector2(0.5f, 0f), top = new Vector2(0.5f, 1f), topLeft = new Vector2(0f, 1f);

            // inside a giant's clouds there is nothing to see but the cloud
            murk = Fill(root, centre, Vector2.zero, Vector2.zero, Color.clear);
            Stretch(murk);
            // a knock reddens the whole view for a moment
            flash = Fill(root, centre, Vector2.zero, Vector2.zero, Color.clear);
            Stretch(flash);

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
                    at = at, icon = icon, box = Fill(at, centre, Vector2.zero, new Vector2(34f, 34f), Harm, outline), lead = Fill(at, centre, Vector2.zero, Vector2.zero, Dim),
                    arrow = Fill(at, centre, Vector2.zero, new Vector2(13f, 13f), Ink, pointer),
                    name = Label(at, centre, new Vector2(0f, 18f), new Vector2(240f, 18f), 13, TextAnchor.MiddleCenter, Ink),
                    detail = Label(at, centre, new Vector2(0f, -17f), new Vector2(240f, 16f), 12, TextAnchor.MiddleCenter, Dim),
                };
            }
            reticle = Fill(root, centre, Vector2.zero, new Vector2(5f, 5f), Ink, dot);

            status = Group(root, Vector2.zero, new Vector2(56f, 48f), new Vector2(900f, 112f));
            statusPlate = Fill(status.transform, Vector2.zero, new Vector2(-20f, -18f), Vector2.zero, Plate, panel).rectTransform;
            place = Label(status.transform, topLeft, Vector2.zero, new Vector2(900f, 20f), 14, TextAnchor.UpperLeft, Dim);
            readout = Label(status.transform, topLeft, new Vector2(0f, -26f), new Vector2(900f, 44f), 34, TextAnchor.UpperLeft, Ink);
            // suit or hull, and under it the jetpack's fuel
            vitalTrack = Fill(status.transform, Vector2.zero, new Vector2(0f, 12f), new Vector2(220f, 6f), Faint);
            vital = Fill(status.transform, Vector2.zero, new Vector2(0f, 12f), new Vector2(220f, 6f), Ink);
            vitalName = Label(status.transform, Vector2.zero, new Vector2(232f, 7f), new Vector2(200f, 16f), 12, TextAnchor.LowerLeft, Dim);
            fuelTrack = Fill(status.transform, Vector2.zero, Vector2.zero, new Vector2(220f, 3f), Faint);
            fuel = Fill(status.transform, Vector2.zero, Vector2.zero, new Vector2(220f, 3f), Accent);

            // how deep the run has gone and how nearly the warp drive is charged
            run = Group(root, Vector2.one, new Vector2(-56f, -48f), new Vector2(420f, 30f));
            runText = Label(run.transform, Vector2.one, Vector2.zero, new Vector2(1000f, 36f), 13, TextAnchor.UpperRight, Dim);
            Fill(run.transform, Vector2.one, new Vector2(0f, -26f), new Vector2(220f, 3f), Faint);
            charge = Fill(run.transform, Vector2.one, new Vector2(0f, -26f), new Vector2(220f, 3f), Accent);

            radar = Group(root, new Vector2(1f, 0f), new Vector2(-56f, 48f), new Vector2(320f, 190f));
            new GameObject("Globe radar").AddComponent<Radar>().game = game;

            // The system map's table: every world at a glance.
            chart = Group(root, topLeft, new Vector2(56f, -150f), new Vector2(1120f, 420f));
            Stretch(Fill(chart.transform, centre, Vector2.zero, Vector2.zero, Plate, panel));
            float[] columns = { 24f, 190f, 340f, 440f, 540f, 650f, 750f, 850f, 920f, 1000f };
            chartColumns = new Text[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                chartColumns[i] = Label(chart.transform, topLeft, new Vector2(columns[i], -22f), new Vector2(170f, 360f), 14, TextAnchor.UpperLeft, i == 0 ? Ink : Dim);
                chartColumns[i].lineSpacing = 1.45f;
            }
            chartFoot = Label(chart.transform, Vector2.zero, new Vector2(24f, 18f), new Vector2(1050f, 16f), 12, TextAnchor.LowerLeft, Dim);

            // The mission in hand, set out in full: over the map, and for a while whenever a new one begins.
            brief = Group(root, topLeft, new Vector2(56f, -592f), new Vector2(780f, 262f));
            Stretch(Fill(brief.transform, centre, Vector2.zero, Vector2.zero, Glass, panel));
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
            caption = Group(root, bottom, new Vector2(0f, 236f), new Vector2(1100f, 64f));
            captionPlate = Fill(caption.transform, centre, Vector2.zero, Vector2.zero, Plate, panel).rectTransform;
            captionText = Label(caption.transform, centre, Vector2.zero, new Vector2(1040f, 60f), 19, TextAnchor.MiddleCenter, Ink);

            news = Group(root, top, new Vector2(0f, -258f), new Vector2(1400f, 30f));
            newsText = Label(news.transform, top, Vector2.zero, new Vector2(1400f, 26f), 17, TextAnchor.MiddleCenter, Accent);

            prompt = Group(root, bottom, new Vector2(0f, 176f), new Vector2(700f, 44f));
            promptPlate = Fill(prompt.transform, centre, Vector2.zero, Vector2.zero, Plate, panel).rectTransform;
            promptCap = Fill(prompt.transform, centre, Vector2.zero, new Vector2(40f, 40f), Ink, outline).rectTransform;
            promptKey = Label(promptCap, centre, Vector2.zero, new Vector2(40f, 40f), 19, TextAnchor.MiddleCenter, Ink, "F");
            promptText = Label(prompt.transform, centre, Vector2.zero, new Vector2(600f, 40f), 22, TextAnchor.MiddleLeft, Ink);
            promptText.rectTransform.pivot = new Vector2(0f, 0.5f);

            hint = Group(root, new Vector2(1f, 0f), new Vector2(-56f, 48f), new Vector2(300f, 18f));
            Label(hint.transform, new Vector2(1f, 0f), Vector2.zero, new Vector2(300f, 18f), 13, TextAnchor.LowerRight, Dim, "H   CONTROLS");

            banner = Group(root, top, new Vector2(0f, -110f), new Vector2(1400f, 130f));
            bannerTitle = Label(banner.transform, top, Vector2.zero, new Vector2(1400f, 64f), 46, TextAnchor.MiddleCenter, Ink);
            Fill(banner.transform, top, new Vector2(0f, -74f), new Vector2(160f, 1f), Dim);
            bannerSub = Label(banner.transform, top, new Vector2(0f, -88f), new Vector2(1400f, 26f), 18, TextAnchor.MiddleCenter, Dim);

            card = Group(root, new Vector2(1f, 0.5f), new Vector2(-56f, 40f), new Vector2(500f, 300f));
            Stretch(Fill(card.transform, centre, Vector2.zero, Vector2.zero, Glass, panel));
            var bar = Fill(card.transform, Vector2.zero, Vector2.zero, new Vector2(3f, 0f), Accent).rectTransform;
            bar.anchorMax = topLeft;
            cardHead = Label(card.transform, topLeft, new Vector2(30f, -24f), new Vector2(440f, 36f), 13, TextAnchor.UpperLeft, Accent);
            cardTitle = Label(card.transform, topLeft, new Vector2(30f, -68f), new Vector2(440f, 40f), 30, TextAnchor.UpperLeft, Ink);
            cardBody = Label(card.transform, topLeft, new Vector2(30f, -120f), new Vector2(440f, 200f), 19, TextAnchor.UpperLeft, Ink);
            cardBody.lineSpacing = 1.15f;
            cardFoot = Label(card.transform, Vector2.zero, new Vector2(30f, 20f), new Vector2(440f, 16f), 12, TextAnchor.LowerLeft, Dim);

            talk = Group(root, bottom, new Vector2(0f, 64f), new Vector2(1080f, 274f));
            Stretch(Fill(talk.transform, centre, Vector2.zero, Vector2.zero, Glass, panel));
            speaker = Label(talk.transform, topLeft, new Vector2(38f, -26f), new Vector2(1000f, 36f), 13, TextAnchor.UpperLeft, Accent);
            line = Label(talk.transform, topLeft, new Vector2(38f, -78f), new Vector2(1004f, 124f), 23, TextAnchor.UpperLeft, Ink);
            line.lineSpacing = 1.12f;
            Fill(talk.transform, Vector2.zero, new Vector2(38f, 62f), new Vector2(1004f, 1f), Faint);
            draft = Label(talk.transform, Vector2.zero, new Vector2(38f, 20f), new Vector2(760f, 30f), 20, TextAnchor.MiddleLeft, Ink);
            talkKeys = Label(talk.transform, new Vector2(1f, 0f), new Vector2(-38f, 26f), new Vector2(900f, 16f), 12, TextAnchor.LowerRight, Dim);

            controls = Group(root, centre, new Vector2(0f, 10f), new Vector2(1240f, 520f));
            Stretch(Fill(controls.transform, centre, Vector2.zero, Vector2.zero, Glass, panel));
            for (int column = 0; column < Help.Length; column++)
            {
                float x = 48f + 400f * column;
                Label(controls.transform, topLeft, new Vector2(x, -36f), new Vector2(340f, 18f), 13, TextAnchor.UpperLeft, Accent, Help[column].heading);
                var keys = Label(controls.transform, topLeft, new Vector2(x, -74f), new Vector2(120f, 400f), 16, TextAnchor.UpperLeft, Ink, string.Join("\n", Help[column].rows.Select(row => row.Split('|')[0])));
                var actions = Label(controls.transform, topLeft, new Vector2(x + 132f, -74f), new Vector2(250f, 400f), 16, TextAnchor.UpperLeft, Dim, string.Join("\n", Help[column].rows.Select(row => row.Split('|')[1])));
                keys.lineSpacing = actions.lineSpacing = 1.5f;
            }
            stats = Label(controls.transform, Vector2.zero, new Vector2(48f, 26f), new Vector2(900f, 16f), 12, TextAnchor.LowerLeft, Dim);

            menu = Group(root, centre, Vector2.zero, Vector2.zero);
            Stretch(menu.GetComponent<RectTransform>());
            // light is blended as light, not as the eye sees it, so this leaves the world behind at about half its brightness
            Stretch(Fill(menu.transform, centre, Vector2.zero, Vector2.zero, Shade));
            menuTitle = Label(menu.transform, centre, new Vector2(0f, 250f), new Vector2(1600f, 90f), 62, TextAnchor.MiddleCenter, Ink);
            menuSub = Label(menu.transform, centre, new Vector2(0f, 178f), new Vector2(1600f, 26f), 16, TextAnchor.MiddleCenter, Dim);
            menuNote = Label(menu.transform, centre, new Vector2(0f, 124f), new Vector2(1600f, 68f), 16, TextAnchor.MiddleCenter, Dim);
            menuBar = Fill(menu.transform, centre, Vector2.zero, new Vector2(3f, 30f), Accent);
            menuItems = new Text[12];
            for (int i = 0; i < menuItems.Length; i++)
                menuItems[i] = Label(menu.transform, centre, new Vector2(0f, 64f - 60f * i), new Vector2(900f, 50f), 30, TextAnchor.MiddleCenter, Ink);
            menuKeys = Label(menu.transform, bottom, new Vector2(0f, 64f), new Vector2(1200f, 18f), 12, TextAnchor.LowerCenter, Dim);

            foreach (var group in new[] { prompt, banner, card, talk, menu, controls, news, radar, chart, brief, asking, caption }) group.alpha = 0f;
        }

        // A white arrowhead pointing right.
        static Sprite Pointer()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color(1f, 1f, 1f, math.saturate(0.5f * (size - x - 0.5f) - math.abs(y + 0.5f - size / 2f) + 0.5f));
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), Vector2.one * 0.5f);
        }

        // A white rounded square whose corners survive stretching, filled or as a line this thick.
        static Sprite Rounded(float line, int size = 32, float radius = 7f)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float2 corner = math.abs(new float2(x, y) + 0.5f - size / 2f) - (size / 2f - radius);
                // how far inside the shape this pixel is: from the rounded corner, or from the nearest straight edge
                float inside = math.min(radius - math.length(math.max(corner, 0f)), math.cmin(size / 2f - math.abs(new float2(x, y) + 0.5f - size / 2f)));
                pixels[y * size + x] = new Color(1f, 1f, 1f, math.saturate(line > 0f ? line - math.abs(inside - line) + 0.5f : inside + 0.5f));
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, Vector4.one * (size * 0.4f));
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
            image.sprite = sprite;
            image.material = overlay;
            image.type = Image.Type.Sliced;
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        static Text Label(Transform parent, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color colour, string words = "")
        {
            var text = Rect(parent, anchor, position, size).gameObject.AddComponent<Text>();
            text.font = Shapes.Type;
            text.material = overlay;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = colour;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = words;
            // a soft shadow keeps pale text readable over snow, sand and cloud
            text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.45f);
            return text;
        }

        // Capitals with air between them, for titles.
        static string Spaced(string words) => string.Join(" ", words.ToUpperInvariant().Select(letter => letter == ' ' ? "  " : letter.ToString()));

        static string Distance(float metres) => metres < 1000f ? $"{metres:0} m" : $"{metres / 1000f:0.0} km";

        static void Show(CanvasGroup group, bool shown, float rate = 5f) => group.alpha = Mathf.MoveTowards(group.alpha, shown ? 1f : 0f, rate * Time.unscaledDeltaTime);

        // Which item of the menu on screen is under a point, or -1. Only the menu's own items count:
        // the labels below them are spare, and still in place on the frame a shorter menu opens.
        public int MenuItemAt(Vector2 pointer)
        {
            for (int i = 0; i < game.Items.Length; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(menuItems[i].rectTransform, pointer, canvas.worldCamera))
                    return i;
            return -1;
        }

        void LateUpdate()
        {
            var player = game.Player;
            var system = game.System;
            var body = system.ReferenceBody;
            var scanner = game.Scanner;
            var hermit = game.Hermit;
            float now = Time.unscaledTime;
            bool playing = !game.Paused && !game.Dead && !hermit.Talking && !player.SystemView, planetView = player.PlanetView;
            // places are marked while playing and on the map of the world they are on
            bool charting = playing || (planetView && !game.Paused && !game.Dead);
            var worlds = system.Descriptor.bodies;
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            // The further the camera is from the scene's origin, the coarser positions there are, and
            // over the map of the system it is a long way off: the canvas is kept big enough in the
            // world for that coarseness to stay far below a pixel, or its text would shiver.
            canvas.planeDistance = Mathf.Max(100f, Mathf.NextPowerOfTwo((int)(0.02f * canvas.worldCamera.transform.position.magnitude)));

            if (DeviceControls.VR)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.transform.localScale = Vector3.one * 0.0008f;
                float3 up = math.normalizesafe(player.Position);
                Quaternion facing = player.flying ? (Quaternion)player.ship.rotation : Quaternion.LookRotation(player.walker.forward, up);
                canvas.transform.SetPositionAndRotation((Vector3)(player.Position - system.Anchor) + facing * new Vector3(0, player.flying ? 0.8f : 1.6f, 2), facing);
            }
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
            string[] gap = Distance(player.Range).ToUpperInvariant().Split(' ');
            readout.text = $"{math.length(player.Velocity):0}<size=15>  M/S        </size>{gap[0]}<size=15>  {gap[1]} TO {worlds[player.Ahead].name.ToUpperInvariant()}</size>";
            readout.enabled = playing && player.flying && !player.Seated;
            float left = player.flying ? player.hull / player.MaxHull : player.health / player.MaxHealth;
            vital.rectTransform.sizeDelta = new Vector2(220f * math.saturate(left), 6f);
            vital.color = Color.Lerp(Harm, Ink, math.smoothstep(0.25f, 0.5f, left));
            // what is in hand, and for a firearm what is left in it
            vitalName.text = $"{(player.flying ? "HULL" : "SUIT")}     " + (!player.Armed ? "UNARMED" : player.Weapon.name.ToUpperInvariant()
                + (player.flying ? "" : $"   {player.energy:0} ENERGY"));
            vital.enabled = vitalTrack.enabled = vitalName.enabled = playing;
            fuel.rectTransform.sizeDelta = new Vector2(220f * player.energy / (Suit.Capacity + (player.Has(Gear.Capacitor) ? 50 : 0)), 3f);
            fuel.enabled = fuelTrack.enabled = playing && !player.flying;
            statusPlate.sizeDelta = new Vector2(Mathf.Max(place.preferredWidth, readout.enabled ? readout.preferredWidth : 0f, 290f) + 40f, readout.enabled ? 148f : playing ? 82f : 62f);
            statusPlate.anchoredPosition = new Vector2(-20f, readout.enabled ? -18f : playing ? 48f : 68f);
            // from the pilot's seat the dash says all of this itself
            Show(status, !game.Paused && !game.Dead && !player.Seated);
            reticle.enabled = playing;
            // sunk into a giant, its cloud closes over the view
            float sunk = body.kind == BodyKind.Gas ? math.saturate((body.radius - math.length(player.Position)) / 60f) : 0f;
            // and under the ground there is only the dark, thinning as the earth is torn away
            murk.color = game.Buried ? new Color(0f, 0f, 0f, 1f - 0.9f * Mathf.Pow((float)game.Dug / Game.Digs, 2.2f))
                : new Color(body.ground.x * 0.6f, body.ground.y * 0.6f, body.ground.z * 0.6f, 0.97f * sunk);
            flash.color = new Color(Harm.r, Harm.g, Harm.b, game.Dead ? 0.3f : 0.4f * math.saturate(1f - (Time.time - player.HurtAt) / 0.6f) + 0.25f * math.saturate(1f - left * 4f));

            // the mission in hand, and how much of its guard has fallen
            runText.text = game.Objective;
            charge.rectTransform.sizeDelta = new Vector2(game.Outpost.World < 0 ? 0f : 220f * (1f - (float)game.Outpost.Left / game.Mission.count), 3f);
            Show(run, playing);

            Show(radar, playing, 4f);

            // the mission in hand in full
            if (briefed != game.Stage) (briefed, briefSince) = (game.Stage, now);
            bool briefing = !game.Buried && game.Stage < Campaign.Won && !game.Paused && !game.Dead && !hermit.Talking
                && (player.SystemView || now - Mathf.Max(briefSince, game.Began) < BriefStays);
            if (briefing)
            {
                var mission = game.Mission;
                int on = game.Outpost.World >= 0 ? game.Outpost.World : SolarSystemDescriptor.Home;
                float far = game.Goal is float3 goal ? math.distance(player.Position, goal)
                    : math.max(0f, Vector3.Distance(player.Position - system.Anchor, system.Bodies[on].transform.position) - worlds[on].radius);
                string Sentence(string words) => char.ToUpperInvariant(words[0]) + words.Substring(1).ToLowerInvariant();
                briefHead.text = $"MISSION {game.Stage + 1} OF {Campaign.Missions.Length}";
                briefTitle.text = mission.name;
                briefTask.text = game.Objective.Substring(game.Objective.IndexOf('·') + 1).Trim();
                briefRows.text = $"{worlds[on].name}  ·  {Distance(far)} from you{(game.Goal != null ? "  ·  follow its mark" : on == system.Reference ? "" : "  ·  its mark is the bright one")}\n"
                    + $"{Sentence(Campaign.Spoils(game.Stage))}\n"
                    + (game.Stage < Campaign.Citadel ? $"{Campaign.Missions[game.Stage + 1].name}  ·  {Campaign.Spoils(game.Stage + 1).ToLowerInvariant()}" : "The way out of this system");
                briefWords.text = $"\"{mission.brief}\"";
            }
            Show(brief, briefing, 4f);

            // who is writing the scanner's entry or the hermit's answer, while they are at it
            bool writing = (scanner.Busy || hermit.Thinking) && !game.Paused;
            if (writing)
            {
                var badge = badges[Oracle.Voice];
                askText.text = $"{Oracle.Voices[Oracle.Voice].ToUpperInvariant()}  ·  {Oracle.Model(Oracle.Voice).name.ToUpperInvariant()}";
                askMark.sprite = badge != null ? badge : dot;
                askMark.color = new Color(badge != null ? 1f : Accent.r, badge != null ? 1f : Accent.g, badge != null ? 1f : Accent.b, 0.55f + 0.45f * Mathf.Sin(now * 5f));
            }
            Show(asking, writing, 6f);

            if (hermit.Remarking)
            {
                captionText.text = $"<color=#6bdcfa>{hermit.Name.ToUpperInvariant()}</color>     {hermit.Line}";
                captionPlate.sizeDelta = new Vector2(Mathf.Min(captionText.preferredWidth, 1040f) + 48f, captionText.preferredHeight + 22f);
                caption.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, player.Seated ? 470f : 236f);
            }
            Show(caption, hermit.Remarking && playing, 4f);
            newsText.text = game.Notice;
            Show(news, playing && now - game.NoticeAt < NewsStays, now - game.NoticeAt < NewsStays ? 6f : 1.2f);
            Show(hint, now - game.Began < 14f && playing && !game.ShowControls, 1f);

            // what the interact key would do
            string action = playing ? game.Prompt : null;
            if (action != null)
            {
                promptText.text = action;
                promptKey.text = action == "Colony and workshop" ? "B" : "F";
                float width = 54f + promptText.preferredWidth;
                promptCap.anchoredPosition = new Vector2(20f - width / 2f, 0f);
                promptPlate.sizeDelta = new Vector2(width + 36f, 60f);
                promptText.rectTransform.anchoredPosition = new Vector2(54f - width / 2f, 0f);
            }
            // from the pilot's seat it sits above the dash, clear of the dials
            prompt.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, player.Seated ? 400f : 176f);
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
                bannerTitle.text = Spaced("Buried");
                bannerSub.text = game.Mission.task;
            }
            // or as play begins there
            bool arriving = now - Mathf.Max(bannerSince, game.Began) < BannerStays;
            Show(banner, arriving && playing, arriving ? 2f : 0.7f);

            // the scanner's card: the entry types itself out, then stays a while
            var entry = scanner.Result;
            bool carding = playing && (scanner.Busy || (entry != null && now - scanner.Since < CardStays));
            if (carding)
            {
                string subject = scanner.Subject.Split(',')[0].ToUpperInvariant();
                cardHead.text = $"SCANNER  ·  {subject}\n{scanner.Source}";
                if (scanner.Busy)
                {
                    cardTitle.text = "Analysing" + new string('.', 1 + (int)(now * 3f) % 3);
                    cardBody.text = "";
                    cardFoot.text = "";
                    cardHeight = 130f;
                    carded = null;
                }
                else
                {
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
                }
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
                talkKeys.text = hermit.Draft.Length > 0 ? "RETURN   SAY IT        ESC   LEAVE"
                    : string.Join("     ", Hermit.Questions.Select((question, i) => $"{i + 1}   {question.ToUpperInvariant()}")) + "        OR TYPE        ESC   LEAVE";
            }
            Show(talk, hermit.Talking && !game.Paused, 6f);

            // the title page, the pause menu and the end of a run
            if (game.Paused || game.Dead)
            {
                var items = game.Items;
                bool paged = !game.Dead && !game.Confirming, voice = game.Standing != null && paged;
                bool settings = game.Page == Game.Settings && paged, cheats = game.Page == Game.Cheats && paged;
                string mission = game.Stage >= Campaign.Won ? "THE SYSTEM IS FREE" : game.Mission.name.ToUpperInvariant();
                menuTitle.text = game.Dead ? Spaced(player.flying ? "Ship lost" : "You died") : game.Confirming ? "Quit to desktop?" : Spaced(game.Page ?? "Infinigame");
                menuSub.text = game.Dead ? game.Epitaph.ToUpperInvariant() : game.Confirming ? "All you have won, your journal and the mission you are on are already saved."
                    : cheats ? $"THE MISSION IN HAND IS {mission}"
                    : voice ? game.Standing
                    : settings ? "HOW THE GAME LOOKS AND SOUNDS, AND WHO WRITES FOR IT THROUGH YOUR OWN ACCOUNT"
                    : game.Title ? "CHOOSE A SLOT TO PLAY   ·   EACH KEEPS A GAME AND A JOURNAL OF ITS OWN"
                    : $"THE {worlds[0].name.ToUpperInvariant()} SYSTEM   ·   {mission}   ·   DEPTH {game.Depth}   ·   {game.Journal.Count} JOURNAL ENTRIES";
                menuNote.text = game.Dead ? game.Tally : paged ? game.Hint : "";
                menuKeys.text = game.Dead ? "RETURN   TRY AGAIN" : "UP  DOWN   SELECT        RETURN   CHOOSE        ESC   BACK";
                for (int i = 0; i < menuItems.Length; i++)
                {
                    menuItems[i].gameObject.SetActive(i < items.Length);
                    if (i >= items.Length) continue;
                    // a long page is set closer
                    menuItems[i].rectTransform.anchoredPosition = new Vector2(0f, 64f - (items.Length > 9 ? 42f : items.Length > 7 ? 52f : 60f) * i);
                    menuItems[i].text = items[i];
                    menuItems[i].color = i == game.Selected ? Ink : Dim;
                }
                var chosen = menuItems[game.Selected];
                menuBar.rectTransform.anchoredPosition = chosen.rectTransform.anchoredPosition - new Vector2(chosen.preferredWidth / 2f + 22f, 0f);
            }
            Show(menu, game.Paused || game.Dead, game.Dead ? 1.5f : 10f);

            // the system map's table
            if (player.SystemView)
            {
                var rows = new List<int> { 0 };
                rows.AddRange(system.Descriptor.Planets);
                rows.Insert(math.min(5, rows.Count), system.Station);
                if (system.Gate >= 0) rows.Add(system.Gate);
                rows.AddRange(Enumerable.Range(1, worlds.Length - 1).Where(i => worlds[i].kind == BodyKind.Sun));
                string Column(string head, Func<int, BodyDescriptor, string> cell) => head + "\n" + string.Join("\n", rows.Select(row => cell(row, worlds[row])));
                int here = system.Descriptor.Planet(system.Reference);
                chartColumns[0].text = Column("WORLD", (row, world) => (row == here ? "›  " : "    ") + world.name.ToUpperInvariant());
                chartColumns[1].text = Column("WHAT", (row, world) => world.kind == BodyKind.Sun ? "star" : world.destroyed ? "rubble" : world.kind == BodyKind.Gate ? "gate" : world.kind == BodyKind.Station ? "citadel"
                    : world.kind == BodyKind.Gas ? world.heat < 100f ? "ice giant" : "gas giant" : world.SeaRadius > 0f ? "living world" : world.air > 0f ? "rock, with air" : "airless rock");
                chartColumns[2].text = Column("RADIUS", (row, world) => Distance(world.radius));
                chartColumns[3].text = Column("GRAVITY", (row, world) => world.kind == BodyKind.Station ? "none" : $"{world.surfaceGravity / 9.81f:0.0} g");
                chartColumns[4].text = Column("WARMTH", (row, world) => world.kind == BodyKind.Station ? "" : $"{Climate.Celsius(world.heat):#,0} °C");
                chartColumns[5].text = Column("DAY", (row, world) => world.spinPeriod == 0 ? "" : Scan.Span(world.spinPeriod));
                chartColumns[6].text = Column("YEAR", (row, world) => row == 0 ? "" : Scan.Span(world.orbit.period));
                // as astronomers count it: a world that turns backwards is one tipped right over
                chartColumns[7].text = Column("TILT", (row, world) => world.spinPeriod == 0 ? "" : $"{(world.spinPeriod < 0 ? 180f - math.degrees(world.tilt) : math.degrees(world.tilt)):0}°");
                chartColumns[8].text = Column("MOONS", (row, world) => system.Descriptor.Moons(row) is int moons && moons > 0 ? moons.ToString() : "");
                chartColumns[9].text = Column("FROM YOU", (row, world) => Distance(math.max(0f, Vector3.Distance(player.Position - system.Anchor, system.Bodies[row].transform.position) - world.radius)));
                chartFoot.text = $"PRESENCE {game.Presence.Reach(system.Reference):0%}  ·  {game.Objective}   ·   FACE A WORLD AND PRESS T TO FLY THERE";
            }
            Show(chart, player.SystemView && !planetView && !game.Paused && !game.Dead, 6f);

            if (game.ShowControls) stats.text = "ESC TO CLOSE";
            Show(controls, game.ShowControls && !game.Paused, 8f);

            // markers: other worlds, the ship when you have walked away from it, the camp, the last thing scanned
            used = 0;
            labels.Clear();
            // over the map, names keep off the table of worlds and the mission's card as well as off each other
            if (player.SystemView && !planetView) labels.Add(new Rect(56f, 510f, 1120f, 420f));
            if (briefing) labels.Add(new Rect(56f, 226f, 780f, 262f));
            var view = canvas.worldCamera;
            Vector3 eye = view.transform.position, ground = system.Bodies[system.Reference].transform.position, self = player.Position - system.Anchor;
            Vector2 middle = ((RectTransform)canvas.transform).rect.size / 2f;
            // how far off another world is means something only from space: from the ground they are names in the sky
            bool inSpace = player.flying && (system.Reference == 0 || player.Altitude > Player.Ceiling);
            if (!game.Paused && !game.Dead && !hermit.Talking && !game.Buried)
            {
                // The world the mission is on stands out from the rest: or the planet it circles, from where a moon has no mark of its own.
                var outpost = game.Outpost;
                int goal = outpost.World >= 0 ? outpost.World : -1;
                if (goal >= 0 && !system.Charted(goal)) goal = system.Descriptor.Planet(goal);
                for (int i = 0; i < system.Bodies.Length && !planetView; i++)
                    if ((i != system.Reference || player.SystemView) && system.Charted(i))
                        // over the map the table has every distance, so the names stand alone and crowd each other less
                        Mark(system.Bodies[i].transform.position, worlds[i].name.ToUpperInvariant(),
                            player.SystemView || !inSpace ? "" : worlds[i].kind == BodyKind.Station && game.Battle.Base <= 0f ? "DESTROYED"
                                : Distance(math.max(0f, Vector3.Distance(self, system.Bodies[i].transform.position) - worlds[i].radius)),
                            worlds[i].kind == BodyKind.Station ? Harm : i == goal ? Accent : Ink, false, i, i == goal && inSpace);
                if (planetView) Mark(self, "YOU", "", Ink);
                // and on that world, the raiders' place itself, then what they kept there: marks that lead the way and are never out of sight
                if (charting && outpost.World == system.Reference && outpost.Fallen)
                    Mark(ground + (Vector3)(outpost.Prize + math.normalizesafe(outpost.Prize) * 3f), Campaign.Name(game.Mission.wins[0]) + (game.Mission.wins.Length > 1 ? " AND MORE" : ""),
                        Distance(math.distance(player.Position, outpost.Prize)), Accent, false, -1, true);
                else if (charting && outpost.World == system.Reference && game.Stage < Campaign.Citadel)
                    Mark(ground + (Vector3)(outpost.Position + math.normalize(outpost.Position) * 20f), game.Mission.name.ToUpperInvariant(), Distance(math.distance(player.Position, outpost.Position)), Harm, false, -1, true);
                float3 walker = player.walker.position;
                if (charting && !player.flying && player.Has(Gear.Ship) && math.distance(walker, player.ship.position) > 25f)
                    Mark(player.ship.position - system.Anchor, "SHIP", Distance(math.distance(walker, player.ship.position)), Ink, false, -1, true);
                // the hermit's fire, while he is still at it: the way to it is shown until he has been met
                float camp = math.distance(player.Position, hermit.Position);
                if (charting && !hermit.Aboard && system.Reference == SolarSystemDescriptor.Home && camp > 12f && (camp < 1500f || planetView))
                    Mark(ground + (Vector3)(hermit.Position + math.normalize(hermit.Position) * 2.4f), hermit.Name.ToUpperInvariant(), Distance(camp), Ink, false, -1, game.Stage == Campaign.Stranger);
                if (carding) Mark(scanner.Mark, "", "", Accent);
                // whatever is hunting you, and for a raider where to aim so the cannons meet it
                foreach (var foe in game.Battle.Foes)
                {
                    float away = math.distance(foe.position, player.Position);
                    if (!playing) break;
                    if (!outpost.Visible((float3)eye + system.Anchor, foe.position)) continue;
                    Vector3 at = foe.position - system.Anchor, lead = at + (Vector3)((foe.velocity - player.Velocity) * (away / math.max(1, player.Weapon.speed)));
                    if (foe.kind != FoeKind.Base) Mark(at, foe.kind == FoeKind.Raider ? "RAIDER" : foe.kind == FoeKind.Mind ? "MIND" : foe.kind == FoeKind.Sentry ? foe.caste.ToString().ToUpperInvariant() : "DRONE", Distance(away), Harm, true);
                    if (player.flying && foe.kind == FoeKind.Raider && away < 2500f && Vector3.Distance(view.WorldToScreenPoint(at), view.WorldToScreenPoint(lead)) > 14f)
                        Mark(lead, "", "", Accent);
                }
            }
            for (int i = used; i < markers.Length; i++) markers[i].at.gameObject.SetActive(false);

            // of is the body a mark stands for, which cannot hide itself. A guide leads the way and so is never
            // lost: with a world in the way it stands in the sky over the way there, and with its place out of
            // view it waits at the edge of the screen nearest it.
            void Mark(Vector3 world, string name, string detail, Color colour, bool boxed = false, int of = -1, bool guide = false)
            {
                Vector3 to = world - eye;
                // not through a world, the one underfoot or any other that stands in the way: only the maps see through them
                bool hidden = false;
                for (int i = 0; i < system.Bodies.Length && !hidden && !player.SystemView; i++)
                {
                    if (worlds[i].destroyed || worlds[i].kind == BodyKind.Gate || worlds[i].kind == BodyKind.Station) continue;
                    Vector3 centre = system.Bodies[i].transform.position - eye;
                    float along = Vector3.Dot(centre, to.normalized);
                    hidden = i != of && along > 0f && along < to.magnitude && (centre - to.normalized * along).magnitude < worlds[i].radius * 0.97f;
                }
                Vector3 shown = hidden && guide ? eye + Vector3.ProjectOnPlane(to, eye - ground).normalized * 1000f : world, screen = view.WorldToScreenPoint(shown);
                // which way it lies from the middle of the view, which unlike where it projects to still means something abeam or astern
                Vector2 at = (Vector2)screen / canvas.scaleFactor, away = view.transform.InverseTransformPoint(shown);
                bool astern = screen.z <= 0.01f, beyond = astern || Mathf.Abs(at.x - middle.x) > middle.x - Edge || Mathf.Abs(at.y - middle.y) > middle.y - Edge;
                if ((hidden || astern) && !guide || used == markers.Length) return;
                var marker = markers[used++];
                marker.at.gameObject.SetActive(true);
                marker.arrow.enabled = guide && beyond;
                if (marker.arrow.enabled)
                {
                    // dead astern, any edge will do
                    if (away.sqrMagnitude < 1e-6f) away = Vector2.down;
                    away *= Mathf.Min((middle.x - Edge) / Mathf.Max(Mathf.Abs(away.x), 1e-3f), (middle.y - Edge) / Mathf.Max(Mathf.Abs(away.y), 1e-3f));
                    at = middle + away;
                    marker.arrow.color = colour;
                    marker.arrow.rectTransform.anchoredPosition = away.normalized * 15f;
                    marker.arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg);
                }
                marker.at.anchoredPosition = at;
                marker.icon.color = marker.name.color = colour;
                marker.icon.enabled = !boxed;
                marker.box.enabled = boxed;
                marker.name.text = name;
                marker.detail.text = detail;
                // A name that would lie over another steps aside, and one with nowhere left to go gives
                // way: the mark itself stays. By choice the name sits over its mark and the distance
                // under it; failing that the two go together to one side of it or another.
                float wide = Mathf.Max(marker.name.preferredWidth, marker.detail.preferredWidth) + 10f, tall = detail.Length > 0 ? 36f : 18f;
                bool placed = name.Length + detail.Length == 0;
                marker.lead.enabled = false;
                // Every side close in is tried first; then, where worlds crowd together, a place in a
                // column off to the right of them, on the end of a line.
                for (int i = 0; i < Places.Length + 16 && !placed; i++)
                {
                    bool astride = i == 0 && detail.Length > 0, far = i >= Places.Length;
                    Vector2 side = far ? Vector2.right : Places[i], half = new Vector2(side.x * wide / 2f, side.y * tall / 2f);
                    Vector2 shift = astride ? Vector2.zero : far ? new Vector2(110f + wide / 2f, 150f - 20f * (i - Places.Length)) : half + new Vector2(side.x * 14f, side.y * 12f);
                    var box = new Rect(marker.at.anchoredPosition + shift - new Vector2(wide / 2f, astride ? 27f : tall / 2f), new Vector2(wide, astride ? 54f : tall));
                    bool free = true;
                    foreach (var other in labels) free &= !other.Overlaps(box);
                    if (!free) continue;
                    labels.Add(box);
                    marker.name.rectTransform.anchoredPosition = shift + new Vector2(0f, astride ? 17.5f : detail.Length > 0 ? 9f : 0f);
                    marker.detail.rectTransform.anchoredPosition = shift - new Vector2(0f, astride ? 17.5f : 9f);
                    placed = true;
                    // the line runs from just clear of the mark to the nearest edge of the name
                    Vector2 end = shift - half;
                    marker.lead.enabled = far;
                    marker.lead.rectTransform.pivot = new Vector2(0f, 0.5f);
                    marker.lead.rectTransform.anchoredPosition = end.normalized * 7f;
                    marker.lead.rectTransform.sizeDelta = new Vector2(end.magnitude - 10f, 1f);
                    marker.lead.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(end.y, end.x) * Mathf.Rad2Deg);
                }
                marker.name.enabled = marker.detail.enabled = placed;
            }
        }
    }
}
