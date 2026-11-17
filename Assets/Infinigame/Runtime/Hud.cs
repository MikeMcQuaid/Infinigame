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
                "W A S D|Run", "Mouse|Look", "Space|Jump, hold for thrusters", "W  aloft|Fly where you look", "Shift|Sprint", "F|Talk, take, board the ship", "Click|Fire", "R|Toggle fitted cloak",
                "1 2 3 4 5|Select weapon", "B|Factory uplink", "Right click|Scan what you face", "V|First or third person",
            }),
            ("IN THE SHIP", new[]
            {
                "W A S D|Thrust and strafe", "Mouse|Steer", "Space  Ctrl|Climb, descend", "Shift|Boost", "Q  E|Roll", "F|Land, then leave",
                "Click|Fire", "1 2 3 4 5|Select weapon", "T|Autopilot / recall on foot", "Z|Flight assist", "V|Cockpit or chase view", "Right click|Scan the world ahead",
            }),
            ("ANYWHERE", new[]
            {
                "Tab|Toggle map and mission", "Return|Talk to your robot", "Y|Hold to talk; release to send", "H|Show this", "Esc|Close this, pause", "M|Music on or off", "[  ]|Less or more detail", "-  =|Slower or faster look",
                "Opt Return|Full screen or window",
            }),
        };

        struct Marker
        {
            public RectTransform at;
            // a diamond for a place, a box drawn round a foe, a line out to a name that had to go further off,
            // and an arrow for a mark held at the edge of the screen because its place is out of view
            public Image icon, box, lead, arrow, health, healthTrack;
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
        Text lesson, transcript, cheatGlyph;
        readonly List<(Text label, string key)> helpKeys = new List<(Text, string)>();
        Image fuel, fuelTrack, vital, vitalTrack, charge, reticle, menuBar, flash, murk, beacon, askMark;
        Sprite dot;
        // each of the oracle's voices' own badge, where the project has been given one
        Sprite[] badges;
        // the radar's contacts: a blip each, on a stalk that shows how far above or below it is
        Image[] blips, stalks;
        Text[] chartColumns;
        readonly List<int> chartWorlds = new List<int>();
        Text helpHint;
        Text escapeText;
        CanvasGroup escape;
        Text chartFoot, talkClose, heldWeapon;
        CanvasGroup mapBar, weaponCard;
        Text[] mapButtons;
        WeaponGlyph weaponGlyph;
        RectTransform promptCap, statusPlate, promptPlate, captionPlate, missionPlate;
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
                    at = at, icon = icon, healthTrack = Fill(at, centre, new Vector2(0, -25), new Vector2(64, 5), Shade), health = Fill(at, centre, new Vector2(0, -25), new Vector2(64, 5), Harm), box = Fill(at, centre, Vector2.zero, new Vector2(34f, 34f), Harm, outline), lead = Fill(at, centre, Vector2.zero, Vector2.zero, Dim),
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
            missionPlate = Fill(run.transform, Vector2.one, new Vector2(10, 8), new Vector2(1020, 50), Plate, panel).rectTransform;
            runText = Label(run.transform, Vector2.one, Vector2.zero, new Vector2(1000f, 36f), 13, TextAnchor.UpperRight, Dim);
            Fill(run.transform, Vector2.one, new Vector2(0f, -26f), new Vector2(220f, 3f), Faint);
            charge = Fill(run.transform, Vector2.one, new Vector2(0f, -26f), new Vector2(220f, 3f), Accent);

            radar = Group(root, new Vector2(1f, 0f), new Vector2(-56f, 48f), new Vector2(320f, 190f));
            new GameObject("Globe radar").AddComponent<Radar>().game = game;

            weaponCard = Group(root, new Vector2(0, 0.5f), new Vector2(56, 35), new Vector2(265, 100));
            Stretch(Fill(weaponCard.transform, centre, Vector2.zero, Vector2.zero, Plate, panel));
            heldWeapon = Label(weaponCard.transform, topLeft, new Vector2(15, -10), new Vector2(235, 26), 16, TextAnchor.UpperLeft, Ink);
            weaponGlyph = new GameObject("Weapon outline", typeof(RectTransform), typeof(WeaponGlyph)).GetComponent<WeaponGlyph>();
            weaponGlyph.transform.SetParent(weaponCard.transform, false); weaponGlyph.color = Ink; weaponGlyph.material = overlay; weaponGlyph.raycastTarget = false;
            weaponGlyph.rectTransform.anchorMin = weaponGlyph.rectTransform.anchorMax = Vector2.zero;
            weaponGlyph.rectTransform.pivot = Vector2.zero; weaponGlyph.rectTransform.anchoredPosition = new Vector2(15, 12); weaponGlyph.rectTransform.sizeDelta = new Vector2(180, 50);
            mapBar = Group(root, top, new Vector2(0, -70), new Vector2(1300, 66));
            Stretch(Fill(mapBar.transform, centre, Vector2.zero, Vector2.zero, Plate, panel));
            string[] mapActions = { "MAP", "SUMMARY", "ZOOM −", "ZOOM +", "AUTOPILOT", "CONTINUE" };
            mapButtons = new Text[mapActions.Length];
            for (int i = 0; i < mapActions.Length; i++) mapButtons[i] = Label(mapBar.transform, centre, new Vector2((i - 2.5f) * 210, 0), new Vector2(200, 58), 22, TextAnchor.MiddleCenter, Ink, mapActions[i]);
            // The system map's table: every world at a glance.
            chart = Group(root, topLeft, new Vector2(56f, -150f), new Vector2(1328f, 420f));
            Stretch(Fill(chart.transform, centre, Vector2.zero, Vector2.zero, new Color(0.02f, 0.035f, 0.06f, 1), panel));
            float[] columns = { 24f, 240f, 420f, 530f, 685f, 840f, 985f, 1130f, 0f, 0f };
            chartColumns = new Text[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                chartColumns[i] = Label(chart.transform, topLeft, new Vector2(columns[i], -22f), new Vector2(i == 0 ? 210 : i == 1 ? 175 : 150, 360f), 18, TextAnchor.UpperLeft, i == 0 ? Ink : Dim);
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
            lesson = Label(root, bottom, new Vector2(0, 330), new Vector2(1100, 64), 24, TextAnchor.MiddleCenter, Accent);
            transcript = Label(root, bottom, new Vector2(0, 410), new Vector2(1000, 80), 22, TextAnchor.MiddleCenter, Ink);
            cheatGlyph = Label(root, new Vector2(1, 0), new Vector2(-22, 22), new Vector2(48, 48), 16, TextAnchor.MiddleCenter, Dim, "◇");
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
            helpHint = Label(hint.transform, new Vector2(1f, 0f), Vector2.zero, new Vector2(300f, 18f), 13, TextAnchor.LowerRight, Dim, "H   CONTROLS");

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
            talkClose = Label(talk.transform, new Vector2(1, 1), new Vector2(-30, -18), new Vector2(130, 45), 20, TextAnchor.MiddleRight, Ink, "CLOSE ×");
            speaker = Label(talk.transform, topLeft, new Vector2(38f, -26f), new Vector2(1000f, 36f), 13, TextAnchor.UpperLeft, Accent);
            line = Label(talk.transform, topLeft, new Vector2(38f, -78f), new Vector2(1004f, 124f), 23, TextAnchor.UpperLeft, Ink);
            line.lineSpacing = 1.12f;
            Fill(talk.transform, Vector2.zero, new Vector2(38f, 62f), new Vector2(1004f, 1f), Faint);
            draft = Label(talk.transform, Vector2.zero, new Vector2(38f, 20f), new Vector2(760f, 30f), 20, TextAnchor.MiddleLeft, Ink);
            talkKeys = Label(talk.transform, new Vector2(1f, 0f), new Vector2(-38f, 26f), new Vector2(900f, 16f), 12, TextAnchor.LowerRight, Dim);

            escape = Group(root, new Vector2(0.5f, 1), new Vector2(0, -105), new Vector2(650, 70));
            Stretch(Fill(escape.transform, centre, Vector2.zero, Vector2.zero, Plate, panel));
            escapeText = Label(escape.transform, centre, Vector2.zero, new Vector2(620, 50), 30, TextAnchor.MiddleCenter, Harm);
            escape.alpha = 0;
            controls = Group(root, centre, new Vector2(0f, 10f), new Vector2(1240f, 840f));
            Stretch(Fill(controls.transform, centre, Vector2.zero, Vector2.zero, new Color(0.015f, 0.03f, 0.045f, 1f), panel));
            for (int column = 0; column < Help.Length; column++)
            {
                float x = 48f + 400f * column;
                Label(controls.transform, topLeft, new Vector2(x, -36f), new Vector2(340f, 18f), 13, TextAnchor.UpperLeft, Accent, Help[column].heading);
                for (int row = 0; row < Help[column].rows.Length; row++)
                {
                    string[] entry = Help[column].rows[row].Split('|');
                    helpKeys.Add((Label(controls.transform, topLeft, new Vector2(x, -74 - row * 60), new Vector2(132, 56), 24, TextAnchor.UpperLeft, Accent, entry[0]), entry[0]));
                    Label(controls.transform, topLeft, new Vector2(x + 138, -74 - row * 60), new Vector2(220, 56), 24, TextAnchor.UpperLeft, Dim, entry[1]);
                }
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
            menuItems = new Text[16];
            for (int i = 0; i < menuItems.Length; i++)
                menuItems[i] = Label(menu.transform, centre, new Vector2(0f, 64f - 60f * i), new Vector2(900f, 50f), 30, TextAnchor.MiddleCenter, Ink);
            menuKeys = Label(menu.transform, bottom, new Vector2(0f, 64f), new Vector2(1200f, 18f), 12, TextAnchor.LowerCenter, Dim);

            foreach (var group in new[] { prompt, banner, card, talk, menu, controls, news, radar, chart, brief, asking, caption, mapBar, weaponCard }) group.alpha = 0f;
            cheatGlyph.transform.SetAsLastSibling();
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
            "Space" => DeviceControls.Key(key, "A / RB", "LIFT", "LEFT A"),
            "Space  Ctrl" => DeviceControls.Key(key, "RB / LB", "LIFT / DOWN", "A / BOTH GRIPS"),
            "W  aloft" => DeviceControls.Key(key, "LEFT STICK", "LEFT DRAG", "LEFT STICK"),
            "Shift" => DeviceControls.Key(key, "L3", "FULL DRAG", "LEFT STICK"),
            "1 2 3 4 5" => DeviceControls.Key(key, "Y", "WEAPON", "RIGHT B"),
            "Right click" => DeviceControls.Key(key, "LT", "··· SCAN", "L GRIP + RT"),
            "Tab" => DeviceControls.Key(key, "VIEW", "MAP", "LEFT B"),
            "T" => DeviceControls.Key(key, "D-PAD RIGHT", "RECALL", "RIGHT STICK"),
            "B" => DeviceControls.Key(key, "LT + DOWN", "BUILD", "WRIST BUILD"),
            "Return" => DeviceControls.Key(key, "LT + LEFT", "ROBOT", "WRIST ROBOT"),
            "Y" => DeviceControls.Key(key, "LT + R3", "HOLD MIC", "L GRIP + R3"),
            "V" => DeviceControls.Key(key, "R3", "··· VIEW", "HEAD TRACKED"),
            "R" => DeviceControls.Key(key, "LT + X", "··· CLOAK", "L GRIP + R B"),
            "H" => DeviceControls.Key(key, "D-PAD LEFT", "MENU", "MENU"),
            "Esc" => DeviceControls.Key(key, "MENU / B", "MENU", "LEFT STICK"),
            "Z" => DeviceControls.Key(key, "LT + Y", "··· ASSIST", "ASSIST SWITCH"),
            "Q  E" => DeviceControls.Key(key, "AUTO", "AUTO", "L GRIP + STICK"),
            _ => DeviceControls.Touch || DeviceControls.VR || DesktopControls.UsingPad ? "SETTINGS" : key,
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
        public int MapControlAt(Vector2 pointer)
        {
            for (int i = 0; i < mapButtons.Length; i++) if (RectTransformUtility.RectangleContainsScreenPoint(mapButtons[i].rectTransform, pointer, canvas.worldCamera)) return i;
            return -1;
        }
        public int MapItemAt(Vector2 pointer)
        {
            if (!game.MapSummary) return -1;
            var label = chartColumns[0];
            if (!RectTransformUtility.RectangleContainsScreenPoint(label.rectTransform, pointer, canvas.worldCamera)) return -1;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(label.rectTransform, pointer, canvas.worldCamera, out Vector2 local);
            float top = label.rectTransform.rect.yMax;
            int row = Mathf.FloorToInt((top - local.y) / (label.font.lineHeight * label.fontSize / label.font.fontSize * label.lineSpacing)) - 1;
            return row >= 0 && row < chartWorlds.Count ? chartWorlds[row] : -1;
        }
        public bool CloseConversationAt(Vector2 pointer) => !RectTransformUtility.RectangleContainsScreenPoint(talk.GetComponent<RectTransform>(), pointer, canvas.worldCamera)
            || RectTransformUtility.RectangleContainsScreenPoint(talkClose.rectTransform, pointer, canvas.worldCamera);
        public bool CheatAt(Vector2 pointer) => RectTransformUtility.RectangleContainsScreenPoint(cheatGlyph.rectTransform, pointer, canvas.worldCamera);
        public int MenuItemAt(Vector2 pointer)
        {
            for (int i = 0; i < game.Items.Length; i++)
                if (i < menuItems.Length && RectTransformUtility.RectangleContainsScreenPoint(menuItems[i].rectTransform, pointer, canvas.worldCamera))
                    return i;
            return -1;
        }

        void LateUpdate()
        {
            canvas.enabled = !game.Buried || game.Boot >= 4;
            if (!canvas.enabled) return;
            var player = game.Player;
            var system = game.System;
            var body = system.ReferenceBody;
            var scanner = game.Scanner;
            var hermit = game.Hermit;
            float now = Time.unscaledTime;
            bool playing = !game.Paused && !game.Dead && !hermit.Talking && !player.SystemView && !game.ShowControls && !game.Fall.active, planetView = player.PlanetView;
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
                var head = Camera.main.transform;
                canvas.transform.SetPositionAndRotation(head.position + head.forward * 1.5f, head.rotation);
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
            heldWeapon.text = player.Armed ? player.Weapon.name.ToUpperInvariant() : "UNARMED";
            weaponGlyph.Show(player.Weapon);
            Show(weaponCard, playing && !game.Buried && !game.GetComponent<DeviceControls>().Expanded);
            bool touchLayout = DeviceControls.HudActions && !DeviceControls.VR;
            weaponCard.GetComponent<RectTransform>().anchorMin = weaponCard.GetComponent<RectTransform>().anchorMax = touchLayout ? new Vector2(0, 1) : new Vector2(0, 0.5f);
            weaponCard.GetComponent<RectTransform>().anchoredPosition = touchLayout ? new Vector2(56, -235) : new Vector2(56, 35);
            vitalName.text = $"{(player.flying ? "HULL" : "SUIT")}     " + (!player.Armed ? "UNARMED" : player.Weapon.name.ToUpperInvariant()
                + (player.flying ? "" : $"   {player.energy:0} ENERGY"));
            vital.enabled = vitalTrack.enabled = vitalName.enabled = playing;
            fuel.rectTransform.sizeDelta = new Vector2(220f * player.energy / (Suit.Capacity + (player.Has(Gear.Capacitor) ? 50 : 0)), 3f);
            fuel.enabled = fuelTrack.enabled = playing && !player.flying;
            statusPlate.sizeDelta = new Vector2(Mathf.Max(place.preferredWidth, readout.enabled ? readout.preferredWidth : 0f, 290f) + 40f, readout.enabled ? 148f : playing ? 82f : 62f);
            statusPlate.anchoredPosition = new Vector2(-20f, readout.enabled ? -18f : playing ? 48f : 68f);
            // from the pilot's seat the dash says all of this itself
            status.GetComponent<RectTransform>().anchorMin = status.GetComponent<RectTransform>().anchorMax = touchLayout ? new Vector2(0, 1) : Vector2.zero;
            status.GetComponent<RectTransform>().anchoredPosition = new Vector2(56, touchLayout ? -150 : 48);
            Show(status, !game.Paused && !game.Dead && !player.Seated && !game.ShowControls && !game.GetComponent<DeviceControls>().Expanded);
            reticle.enabled = playing;
            // sunk into a giant, its cloud closes over the view
            float sunk = body.kind == BodyKind.Gas ? math.saturate((body.radius - math.length(player.Position)) / 60f) : 0f;
            // and under the ground there is only the dark, thinning as the earth is torn away
            murk.color = game.Buried ? new Color(0f, 0f, 0f, 0.84f - 0.74f * Mathf.Pow((float)game.Dug / Game.Digs, 2.2f))
                : new Color(body.ground.x * 0.6f, body.ground.y * 0.6f, body.ground.z * 0.6f, 0.97f * sunk);
            flash.color = new Color(Harm.r, Harm.g, Harm.b, game.Dead ? 0.3f : 0.4f * math.saturate(1f - (Time.time - player.HurtAt) / 0.6f) + 0.25f * math.saturate(1f - left * 4f));

            // the mission in hand, and how much of its guard has fallen
            runText.text = game.Objective; runText.color = Accent;
            bool mobile = Application.isMobilePlatform || DeviceControls.Touch;
            runText.fontSize = mobile ? 30 : 17;
            missionPlate.sizeDelta = new Vector2(Mathf.Min(1000, runText.preferredWidth) + 20, mobile ? 86 : 50);
            run.GetComponent<RectTransform>().anchoredPosition = new Vector2(-56, mobile ? -190 : -48);
            runText.rectTransform.sizeDelta = new Vector2(1000, mobile ? 72 : 36);
            helpHint.text = ControlName("H") + " · CONTROLS";
            charge.rectTransform.sizeDelta = new Vector2(game.Outpost.World < 0 ? 0f : 220f * (1f - (float)game.Outpost.Left / (game.System.ReferenceBody.kind == BodyKind.Station ? 3 : 1)), 3f);
            Show(run, playing);
            escapeText.text = game.Fall.active ? game.Frontier.Ending : game.Outpost.Demolition > 0 ? $"BURROW DEMOLITION · ESCAPE {math.ceil(game.Outpost.Demolition):0}s" : $"CORE COLLAPSE · ESCAPE {math.ceil(game.Outpost.Escape):0}s";
            Show(escape, !game.Paused && (game.Fall.active || playing && (game.Outpost.Demolition > 0 || game.Stage == Campaign.Citadel && game.Outpost.Escape > 0)));
            if (game.Stage == Campaign.Citadel && game.Outpost.Escape > 0) runText.text = $"ESCAPE · {math.ceil(game.Outpost.Escape):0} SECONDS";

            Show(radar, playing, 4f);

            // the mission in hand in full
            if (briefed != game.Stage) (briefed, briefSince) = (game.Stage, now);
            bool briefing = !game.Buried && game.Stage < Campaign.Won && !game.Paused && !game.Dead && !hermit.Talking
                && player.SystemView && game.MapSummary;
            if (briefing)
            {
                var mission = game.Mission;
                int on = game.Outpost.World >= 0 ? game.Outpost.World : SolarSystemDescriptor.Home;
                float far = game.Goal is float3 goal ? math.distance(player.Position, goal)
                    : math.max(0f, Vector3.Distance(player.Position - system.Anchor, system.Bodies[on].transform.position) - worlds[on].radius);
                string Sentence(string words) => string.IsNullOrEmpty(words) ? "Establish a foothold" : char.ToUpperInvariant(words[0]) + words.Substring(1).ToLowerInvariant();
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
                caption.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, player.Seated ? 470f : 140f);
            }
            Show(caption, game.Subtitles && hermit.Remarking && playing, 4f);
            lesson.text = playing ? game.Tutorial.Hint : "";
            var microphone = game.GetComponent<VoiceInput>();
            transcript.text = microphone.Listening ? "LISTENING · RELEASE TO SEND\n" + microphone.Transcript : "";
            cheatGlyph.enabled = game.Paused;
            cheatGlyph.rectTransform.anchoredPosition = new Vector2(-22 - (Screen.width - Screen.safeArea.xMax) / canvas.scaleFactor, 22 + Screen.safeArea.yMin / canvas.scaleFactor);
            foreach (var binding in helpKeys) binding.label.text = ControlName(binding.key);
            newsText.text = game.Notice;
            Show(news, playing && now - game.NoticeAt < NewsStays, now - game.NoticeAt < NewsStays ? 6f : 1.2f);
            Show(hint, now - game.Began < 14f && playing && !game.ShowControls, 1f);

            // what the interact key would do
            string action = playing && !game.Tutorial.Locked ? game.Prompt : null;
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
            prompt.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, player.Seated ? 400f : hermit.Remarking && game.Subtitles ? 260f : 176f);
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
                bannerTitle.text = Spaced(game.Boot < 7 ? "" : "Emergency power");
                bannerSub.text = game.Boot < 7 ? "" : game.Mission.task;
            }
            // or as play begins there
            bool arriving = now - Mathf.Max(bannerSince, game.Began) < BannerStays;
            Show(banner, player.flying &&  arriving && playing, arriving ? 2f : 0.7f);

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
                talkKeys.text = DeviceControls.VR ? "RIGHT A · FIRST QUESTION    LEFT A · SECOND    GRIP · THIRD    RIGHT B · LEAVE"
                    : hermit.Draft.Length > 0 ? $"{DeviceControls.Key("RETURN", "A", "KEYBOARD DONE", "RIGHT A")}   SAY IT        {DeviceControls.Key("ESC", "B", "CLOSE ×", "RIGHT B")}   LEAVE"
                    : string.Join("     ", Hermit.Questions.Select((question, i) => $"{i + 1}   {question.ToUpperInvariant()}")) + $"        OR TYPE        {DeviceControls.Key("ESC", "B", "CLOSE ×", "RIGHT B")}   LEAVE";
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
                menuSub.text = game.Dead ? game.Epitaph.ToUpperInvariant() : game.Confirming ? "Choose Confirm to proceed or Cancel to return."
                    : cheats ? $"THE MISSION IN HAND IS {mission}"
                    : voice ? game.Standing
                    : game.Page == "Voice" ? "LOCAL SPEECH · NO ACCOUNT OR SUBSCRIPTION"
                    : settings ? "HOW THE GAME LOOKS AND SOUNDS, AND WHO WRITES FOR IT THROUGH YOUR OWN ACCOUNT"
                    : game.Title ? "CHOOSE A SLOT TO PLAY   ·   EACH KEEPS A GAME AND A JOURNAL OF ITS OWN"
                    : $"THE {worlds[0].name.ToUpperInvariant()} SYSTEM   ·   {mission}   ·   DEPTH {game.Depth}   ·   {game.Journal.Count} JOURNAL ENTRIES";
                menuNote.text = game.Dead ? game.Tally : paged ? game.Hint : "";
                menuKeys.text = game.Dead ? DeviceControls.Key("RETURN", "A", "TAP", "TRIGGER") + " · TRY AGAIN" : DeviceControls.Key("UP / DOWN · RETURN CHOOSES · ESC BACK", "STICK / D-PAD · A CHOOSES · B BACK", "TAP TO CHOOSE", "POINT + TRIGGER · B BACK");
                for (int i = 0; i < menuItems.Length; i++)
                {
                    menuItems[i].gameObject.SetActive(i < items.Length);
                    if (i >= items.Length) continue;
                    // a long page is set closer
                    menuItems[i].rectTransform.anchoredPosition = new Vector2(0f, 64f - (items.Length > 11 ? 32f : items.Length > 9 ? 42f : items.Length > 7 ? 52f : 60f) * i);
                    menuItems[i].fontSize = items.Length > 11 ? 22 : 30;
                    menuItems[i].rectTransform.sizeDelta = new Vector2(1100, items.Length > 11 ? 30 : items.Length > 9 ? 40 : 50);
                    menuItems[i].text = items[i];
                    menuItems[i].color = game.Page == Game.ColonyPage && i < 3 && !game.Frontier.CanBuild(i) ? new Color(0.35f, 0.4f, 0.43f) : i == game.Selected ? Ink : Dim;
                }
                var chosen = menuItems[game.Selected];
                menuBar.rectTransform.anchoredPosition = chosen.rectTransform.anchoredPosition - new Vector2(chosen.preferredWidth / 2f + 22f, 0f);
            }
            Show(menu, game.Paused || game.Dead, game.Dead ? 1.5f : 10f);

            // the system map's table
            if (player.SystemView)
            {
                var rows = Enumerable.Range(1, worlds.Length - 1).Where(i => system.Charted(i)
                    && (worlds[i].kind != BodyKind.Station || game.Stage >= Campaign.Citadel)).ToList();
                int selected = rows.IndexOf(game.MapSelection);
                if (selected >= 9) rows = rows.Skip(selected - 8).Take(9).ToList(); else rows = rows.Take(9).ToList();
                chartWorlds.Clear(); chartWorlds.AddRange(rows);
                string Column(string head, Func<int, string> cell) => head + "\n" + string.Join("\n", rows.Select(cell));
                chartColumns[0].text = Column("DESTINATION", row => (row == game.MapSelection ? "› " : "  ") + (worlds[row].kind == BodyKind.Station ? "CITADEL" : worlds[row].name.ToUpperInvariant()));
                chartColumns[1].text = Column("FOOTHOLD", row => game.Frontier.State(row).shipyard ? "SHIPYARD" : game.Frontier.State(row).factory ? "FACTORY" : $"{game.Frontier.State(row).kills}/{War.RequiredKills(game.Frontier.State(row))} kills");
                chartColumns[2].text = Column("ALIENS", row => $"{math.ceil(game.Frontier.State(row).aliens):0}");
                chartColumns[3].text = Column("ROBODOGS", row => $"{math.floor(game.Frontier.State(row).dogs):0}");
                chartColumns[4].text = Column("HOSTILE SHIPS", row => $"{math.ceil(game.Frontier.State(row).alienShips):0}");
                chartColumns[5].text = Column("OUR SHIPS", row => $"{math.floor(game.Frontier.State(row).ships):0}");
                chartColumns[6].text = Column("MIND", row => game.Frontier.State(row).mindDead ? "SILENT" : "ACTIVE");
                chartColumns[7].text = Column("WAR", row => game.Frontier.State(row).aliens <= 0 && game.Frontier.State(row).alienShips <= 0 ? "WON" : "ACTIVE");
                chartColumns[8].text = "";
                chartColumns[9].text = "";
                chartFoot.text = $"PAUSED · {ControlName("W A S D")} SELECT · {ControlName("T")} AUTOPILOT · {ControlName("Tab")} CONTINUE · {game.GetComponent<FrameLog>().Status}";
            }
            Show(chart, player.SystemView && game.MapSummary && !game.Paused && !game.Dead, 6f);
            Show(mapBar, player.SystemView && !game.Paused && !game.Dead, 10);
            mapButtons[0].color = game.MapSummary ? Dim : Ink; mapButtons[1].color = game.MapSummary ? Ink : Dim;

            if (game.ShowControls) stats.text = $"{ControlName("Esc")} TO CLOSE · {game.GetComponent<FrameLog>().Status}";
            Show(controls, game.ShowControls && !game.Paused, 8f);

            // markers: other worlds, the ship when you have walked away from it, the camp, the last thing scanned
            used = 0;
            labels.Clear();
            // over the map, names keep off the table of worlds and the mission's card as well as off each other
            if (player.SystemView && game.MapSummary && !planetView) labels.Add(new Rect(56f, 510f, 1328f, 420f));
            if (briefing) labels.Add(new Rect(56f, 226f, 780f, 262f));
            var view = canvas.worldCamera;
            Vector3 eye = view.transform.position, ground = system.Bodies[system.Reference].transform.position, self = player.Position - system.Anchor;
            Vector2 middle = ((RectTransform)canvas.transform).rect.size / 2f;
            // how far off another world is means something only from space: from the ground they are names in the sky
            bool inSpace = player.InSpace;
            if (!game.Paused && !game.Dead && !hermit.Talking && !game.Buried && !(player.SystemView && game.MapSummary))
            {
                // The world the mission is on stands out from the rest: or the planet it circles, from where a moon has no mark of its own.
                var outpost = game.Outpost;
                int goal = outpost.World >= 0 ? outpost.World : -1;
                if (goal >= 0 && !system.Charted(goal)) goal = system.Descriptor.Planet(goal);
                for (int i = 0; i < system.Bodies.Length && !planetView; i++)
                    if ((inSpace || player.SystemView) && (i != system.Reference || player.SystemView) && system.Charted(i) && (worlds[i].kind != BodyKind.Station || game.Stage >= Campaign.Citadel))
                        // over the map the table has every distance, so the names stand alone and crowd each other less
                        Mark(system.Bodies[i].transform.position, worlds[i].kind == BodyKind.Station ? "ENEMY CITADEL" : worlds[i].name.ToUpperInvariant(),
                            player.SystemView || !inSpace ? "" : worlds[i].kind == BodyKind.Station && game.Battle.Base <= 0f ? "DESTROYED"
                                : Distance(math.max(0f, Vector3.Distance(self, system.Bodies[i].transform.position) - worlds[i].radius)),
                            worlds[i].kind == BodyKind.Station ? Harm : i == goal ? Accent : Ink, false, i, i == goal && inSpace);
                if (planetView) Mark(self, "YOU", "", Ink);
                // and on that world, the raiders' place itself, then what they kept there: marks that lead the way and are never out of sight
                if (charting && outpost.World == system.Reference && outpost.Fallen)
                    Mark(ground + (Vector3)(outpost.Prize + math.normalizesafe(outpost.Prize) * 3f), Campaign.Name(game.Mission.wins[0]) + (game.Mission.wins.Length > 1 ? " AND MORE" : ""),
                        Distance(math.distance(player.Position, outpost.Prize)), Accent, false, -1, true);
                else if (charting && outpost.World == system.Reference && game.Stage < Campaign.Citadel)
                    Mark(ground + (Vector3)((game.Goal ?? outpost.Position) + math.normalize(outpost.Position) * 3f), game.Stage == 2 && !outpost.Rooms.walls.Last().broken ? $"{DeviceControls.Fire} · PUNCH HERE" : game.Mission.name.ToUpperInvariant(), Distance(math.distance(player.Position, game.Goal ?? outpost.Position)), Accent, false, -1, true);
                float3 walker = player.walker.position;
                if (charting && !player.flying && (player.Has(Gear.Ship) || player.captive) && math.distance(walker, player.ship.position) > 25f)
                    Mark(player.ship.position - system.Anchor, player.captive ? "UNMANNED SHIP" : "SHIP", Distance(math.distance(walker, player.ship.position)), Ink, false, -1, true);
                // the hermit's fire, while he is still at it: the way to it is shown until he has been met
                float camp = math.distance(player.Position, hermit.Position);
                if (charting && !hermit.Aboard && system.Reference == SolarSystemDescriptor.Home && camp > 5f && (camp < 1500f || planetView))
                    Mark(ground + (Vector3)(hermit.Position + math.normalize(hermit.Position) * 2.4f), hermit.Name.ToUpperInvariant(), Distance(camp), Ink, false, -1, game.Stage == Campaign.Stranger);
                foreach (var factory in game.Colonies.Where(colony => colony.world == system.Reference))
                    if (!inSpace || player.SystemView) Mark((Vector3)(factory.site - system.Anchor) + (Vector3)math.normalizesafe(factory.site) * 5, game.Frontier.State(factory.world).shipyard ? "YOUR SHIPYARD" : "YOUR ROBODOG FACTORY", Distance(math.distance(factory.site, player.Position)), Accent, false, -1, true);
                if (carding) Mark(scanner.Mark, "", "", Accent);
                // whatever is hunting you, and for a raider where to aim so the cannons meet it
                foreach (var foe in game.Battle.Foes)
                {
                    float away = math.distance(foe.position, player.Position);
                    if (!playing) break;
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
                marker.health.enabled = marker.healthTrack.enabled = health >= 0;
                marker.health.rectTransform.sizeDelta = new Vector2(64 * math.max(0, health), 5);
                marker.name.fontSize = guide ? 19 : 13;
                marker.name.rectTransform.sizeDelta = new Vector2(550, 28);
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
