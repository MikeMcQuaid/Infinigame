using Infinigame.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Infinigame
{
    public class ForearmConsole : MonoBehaviour
    {
        public Game game;
        const float DisplayScale = 0.00027f;
        Canvas canvas;
        Transform cuff, sleeve;
        Text heading;
        readonly Text[] labels = new Text[12];
        readonly Image[] plates = new Image[12];
        bool building;
        int touching = -1, pointed = -1;
        static readonly string[] actions = { "BUILD", "MAP", "RECALL", "ROBOT", "SCAN", "CLOAK", "WEAPON", "MENU", "THRUST", "DESCEND", "VOICE", "PACK" };
        public bool Reading
        {
            get
            {
                if (canvas == null || !canvas.isActiveAndEnabled) return false;
                var eye = Camera.main.transform;
                return touching >= 0 || pointed >= 0 || Vector3.Dot(eye.forward, (canvas.transform.position - eye.position).normalized) > 0.88f;
            }
        }
        public void Init()
        {
            if (canvas != null) return;
            canvas = new GameObject("Forearm console", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var back = canvas.gameObject.AddComponent<Image>();
            back.sprite = Hud.Panel; back.type = Image.Type.Sliced;
            back.color = new Color(0.005f, 0.015f, 0.025f, 0.99f);
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(480, 620);
            canvas.transform.localScale = Vector3.one * DisplayScale;
            var bracer = Models.Parts("Wrist housing");
            Shapes.Part(canvas.transform, "Wrist display casing", bracer[0], Shapes.Solid(Surface.Metal, new Color(0.07f, 0.12f, 0.15f)), Vector3.zero, Vector3.one);
            cuff = Shapes.Part(canvas.transform, "Gauntlet cuff", bracer[1], Shapes.Solid(Surface.Metal, new Color(0.16f, 0.21f, 0.24f)), Vector3.zero, Vector3.one).transform;
            sleeve = Shapes.Part(canvas.transform, "Forearm pressure sleeve", Shapes.Orb(1, 24),
                Shapes.Solid(Surface.Seal, new Color(0.21f, 0.23f, 0.25f)), Vector3.zero, Vector3.one).transform;
            var bezel = Shapes.Solid(Surface.Metal, new Color(0.22f, 0.29f, 0.32f));
            foreach (int side in new[] { -1, 1 })
            {
                Shapes.Part(canvas.transform, "Bezel rail", Shapes.Box(), bezel, new Vector3(side * 248, 0, 0), new Vector3(16, 637, 18));
                Shapes.Part(canvas.transform, "Bezel lip", Shapes.Box(), bezel, new Vector3(0, side * 319, 0), new Vector3(494, 18, 18));
            }
            foreach (int x in new[] { -1, 1 })
                foreach (int y in new[] { -1, 1 })
                {
                    var guard = Shapes.Part(canvas.transform, "Knuckle guard", bracer[0], bezel,
                        new Vector3(x * 239, y * 309, -5), new Vector3(0.072f, 0.043f, 0.3f));
                    guard.transform.localRotation = Quaternion.Euler(0, 0, -x * y * 45);
                }
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Text Label(Transform parent, string text, Vector2 at, Vector2 size, int fontSize)
            {
                var label = new GameObject(text, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                label.transform.SetParent(parent, false); label.font = font; label.text = text;
                label.alignment = TextAnchor.MiddleCenter; label.fontSize = fontSize; label.color = new Color(0.4f, 0.95f, 1);
                label.rectTransform.anchoredPosition = at; label.rectTransform.sizeDelta = size; return label;
            }
            heading = Label(canvas.transform, "SUIT LINK", new Vector2(0, 280), new Vector2(480, 55), 25);
            for (int i = 0; i < labels.Length; i++)
            {
                var plate = new GameObject(actions[i], typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                plate.sprite = Hud.Panel; plate.type = Image.Type.Sliced;
                plate.transform.SetParent(canvas.transform, false); plate.color = new Color(0.01f, 0.04f, 0.06f, 0.98f);
                plate.rectTransform.anchoredPosition = new Vector2(i % 2 == 0 ? -118 : 118, 207 - i / 2 * 91);
                plate.rectTransform.sizeDelta = new Vector2(224, 79); plates[i] = plate;
                labels[i] = Label(plate.transform, actions[i], Vector2.zero, new Vector2(220, 76), 30);
            }
            foreach (var part in canvas.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = LayerMask.NameToLayer("UI");
            Refresh();
        }
        public void Place(Vector3 hand, Quaternion rotation)
        {
            Init();
            var glove = game.GlovePose(0, hand, rotation);
            var fit = game.WristFit;
            var mount = glove.rotation * Quaternion.AngleAxis(fit.z, Vector3.forward);
            canvas.transform.SetPositionAndRotation(glove.position + mount * new Vector3(0, fit.y, -fit.x) * 0.01f,
                mount * Quaternion.LookRotation(Vector3.down, Vector3.forward) * Quaternion.AngleAxis(fit.w, Vector3.forward));
            cuff.localPosition = Vector3.forward * ((fit.y - Game.DefaultWrist.y) * 0.01f / DisplayScale);
            cuff.localRotation = Quaternion.AngleAxis(-fit.w, Vector3.forward);
            // The sleeve runs under the clasp and inside the gauntlet as the display moves.
            const float heel = 0.18f;
            float halfLength = (fit.x * 0.01f + 0.06f - heel) * 0.5f;
            sleeve.SetPositionAndRotation(glove.position - glove.forward * (heel + halfLength), glove.rotation);
            sleeve.localScale = new Vector3(0.072f, 0.057f, halfLength) / DisplayScale;
        }
        public int At(Vector3 world)
        {
            if (canvas == null || !canvas.enabled) return -1;
            Vector3 local = canvas.transform.InverseTransformPoint(world);
            if (Mathf.Abs(local.z) > 60) return -1;
            for (int i = 0; i < plates.Length; i++)
                if (plates[i].gameObject.activeSelf && plates[i].rectTransform.rect.Contains(plates[i].transform.InverseTransformPoint(world))) return i;
            return -1;
        }
        public bool HoldingMic(Vector3 finger)
        {
            if (canvas == null || !Oracle.Enabled) return false;
            Vector3 local = plates[10].transform.InverseTransformPoint(finger);
            return Mathf.Abs(local.z) <= 60 && plates[10].rectTransform.rect.Contains(local);
        }
        public bool Point(Ray ray, out Vector3 end)
        {
            pointed = -1;
            end = ray.GetPoint(2);
            if (canvas == null || !canvas.enabled || !new Plane(canvas.transform.forward, canvas.transform.position).Raycast(ray, out float distance) || distance > 2) return false;
            end = ray.GetPoint(distance);
            pointed = At(end);
            return pointed >= 0;
        }
        public void Activate(int item, ref Controls c)
        {
            Init();
            c.interfaceActive = true;
            if (building)
            {
                if (item == 3) { building = false; Refresh(); }
                else if (item >= 0 && item < 3)
                {
                    if (game.Player.flying || !game.FactoryUnlocked || game.System.ReferenceBody.Dry(game.Player.Position)) game.Notify("LEAVE THE SHIP AND BURROW TO BUILD AT THE REGIONAL SITE");
                    else game.Frontier.BuildFactory(item);
                }
                return;
            }
            if (item == 0) { building = game.FactoryUnlocked; Refresh(); }
            if (item == 1) c.systemView = true;
            if (item == 2) c.travel = true;
            if (item == 3) c.talk = true;
            if (item == 4) c.scan = true;
            if (item == 5) c.reload = true;
            if (item == 6) c.cycle = 1;
            if (item == 7) c.menu = true;
            if (item == 8) c.jump = true;
            if (item == 9) c.lift = -1;
            if (item == 10) c.voice = true;
            if (item == 11) game.Frontier.CallDogs();
        }
        public bool Touch(Vector3 finger, ref Controls c)
        {
            int item = At(finger);
            if (item >= 0 && (item != touching || !building && (item == 8 || item == 9 || item == 10))) Activate(item, ref c);
            touching = item;
            return item >= 0;
        }
        public void ClearContact() => touching = pointed = -1;
        void LateUpdate()
        {
            if (!DeviceControls.VR) return;
            Init();
            bool tracked = game.Player.Hand(XRNode.LeftHand, out var hand, out var rotation);
            if (tracked) Place(hand, rotation);
            bool worn = tracked && !game.Player.flying && !game.Title && !game.Dead && !game.Buried && !game.Paused && !game.Hermit.Talking && !game.Player.SystemView;
            bool fitting = tracked && game.Page == Game.WristAlignment;
            canvas.enabled = fitting || worn && Vector3.Dot(-canvas.transform.forward, (Camera.main.transform.position - canvas.transform.position).normalized) > 0.55f;
            canvas.gameObject.SetActive(worn || fitting);
            if (!canvas.enabled) { ClearContact(); return; }
            Refresh();
        }
        public void Home()
        {
            building = false;
            if (canvas != null) Refresh();
        }
        public void Refresh()
        {
            if (!game.FactoryUnlocked) building = false;
            heading.text = building ? $"FACTORY / {game.Frontier.Local.kills}/{War.RequiredKills(game.Frontier.Local)} KILLS" : "SUIT LINK · LEFT TRIGGER TO LIFT";
            for (int i = 0; i < labels.Length; i++)
            {
                plates[i].gameObject.SetActive(building ? i == 3 || i < 3 && game.Frontier.VisibleBuild(i) : (i != 10 || Oracle.Enabled) && (i != 0 || game.FactoryUnlocked) && (i != 2 || game.Player.CanRecall) && (i != 5 || game.Player.Has(Gear.Stealth)) && (i != 8 && i != 9 || game.Player.Has(Gear.Thrusters)) && (i != 11 || game.Frontier.CanCallDogs));
                labels[i].text = building ? i == 0 ? game.Frontier.BuildLabel.ToUpperInvariant() : i == 1 ? "SHIPYARD" : i == 2 ? "MOON DRIVE" : "BACK" : i == 11 ? game.Escorts.pack ? "DISMISS DOG" : "CALL ROBODOG" : actions[i];
                bool focus = i == touching || i == pointed;
                plates[i].color = focus ? new Color(0.14f, 0.78f, 0.85f, 1) : new Color(0.01f, 0.04f, 0.06f, 0.98f);
                labels[i].color = focus ? new Color(0.01f, 0.06f, 0.08f) : new Color(0.4f, 0.95f, 1);
            }
        }
        void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
    }
}
