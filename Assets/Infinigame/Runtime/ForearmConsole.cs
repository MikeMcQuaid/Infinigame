using Infinigame.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Infinigame
{
    public class ForearmConsole : MonoBehaviour
    {
        public Game game;
        Canvas canvas;
        Text heading;
        readonly Text[] labels = new Text[8];
        readonly Image[] plates = new Image[8];
        bool building;
        int touching = -1;
        static readonly string[] actions = { "BUILD", "MAP", "RECALL", "ROBOT", "SCAN", "CLOAK", "WEAPON", "MENU" };
        public void Init()
        {
            if (canvas != null) return;
            canvas = new GameObject("Forearm console", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.gameObject.AddComponent<Image>().color = new Color(0.005f, 0.015f, 0.025f, 0.99f);
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(480, 440);
            canvas.transform.localScale = Vector3.one * 0.0005f;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Text Label(Transform parent, string text, Vector2 at, Vector2 size, int fontSize)
            {
                var label = new GameObject(text, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                label.transform.SetParent(parent, false); label.font = font; label.text = text;
                label.alignment = TextAnchor.MiddleCenter; label.fontSize = fontSize; label.color = new Color(0.4f, 0.95f, 1);
                label.rectTransform.anchoredPosition = at; label.rectTransform.sizeDelta = size; return label;
            }
            heading = Label(canvas.transform, "FOUNDRY / SUIT LINK", new Vector2(0, 190), new Vector2(480, 55), 25);
            for (int i = 0; i < labels.Length; i++)
            {
                var plate = new GameObject(actions[i], typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                plate.transform.SetParent(canvas.transform, false); plate.color = new Color(0.01f, 0.04f, 0.06f, 0.98f);
                plate.rectTransform.anchoredPosition = new Vector2(i % 2 == 0 ? -118 : 118, 117 - i / 2 * 91);
                plate.rectTransform.sizeDelta = new Vector2(224, 79); plates[i] = plate;
                labels[i] = Label(plate.transform, actions[i], Vector2.zero, new Vector2(220, 76), 30);
            }
        }
        public void Place(Vector3 hand, Quaternion rotation)
        {
            Init();
            canvas.transform.SetPositionAndRotation(hand + rotation * new Vector3(0, 0.07f, -0.19f), rotation * Quaternion.Euler(90, 0, 0));
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
        public bool Point(Ray ray, out Vector3 end)
        {
            end = ray.GetPoint(2);
            if (canvas == null || !canvas.enabled || !new Plane(canvas.transform.forward, canvas.transform.position).Raycast(ray, out float distance) || distance > 2) return false;
            end = ray.GetPoint(distance);
            return At(end) >= 0;
        }
        public void Activate(int item, ref Controls c)
        {
            c.interfaceActive = true;
            if (building)
            {
                if (item == 3) building = false;
                else if (item >= 0 && item < 3)
                {
                    if (game.Player.flying || !game.Player.Powered || game.System.ReferenceBody.Dry(game.Player.Position)) game.Notify("LEAVE THE SHIP AND BURROW TO BUILD WITH A POWERED SUIT");
                    else game.Frontier.BuildFactory(item);
                }
                return;
            }
            if (item == 0) building = true;
            if (item == 1) c.systemView = true;
            if (item == 2) c.travel = true;
            if (item == 3) c.talk = true;
            if (item == 4) c.scan = true;
            if (item == 5) c.reload = true;
            if (item == 6) c.cycle = 1;
            if (item == 7) c.menu = true;
        }
        public bool Touch(Vector3 finger, ref Controls c)
        {
            int item = At(finger);
            if (item >= 0 && item != touching) Activate(item, ref c);
            touching = item;
            return item >= 0;
        }
        void LateUpdate()
        {
            if (!DeviceControls.VR) return;
            Init();
            bool tracked = game.Player.Hand(XRNode.LeftHand, out var hand, out var rotation);
            canvas.enabled = tracked && !game.Title && !game.Dead && !game.Buried && !game.Paused && !game.Hermit.Talking && !game.Player.SystemView;
            if (!canvas.enabled) { touching = -1; return; }
            Place(hand, rotation);
            heading.text = building ? $"FACTORY / {game.Frontier.Local.kills}/{War.RequiredKills(game.Frontier.Local)} KILLS" : "FOUNDRY / SUIT LINK";
            for (int i = 0; i < labels.Length; i++)
            {
                plates[i].gameObject.SetActive(!building || i < 4);
                labels[i].text = building ? i == 0 ? "ROBODOGS" : i == 1 ? "SHIPYARD" : i == 2 ? "MOON DRIVE" : "BACK" : actions[i];
                plates[i].color = i == touching ? new Color(0.04f, 0.25f, 0.3f, 1) : new Color(0.01f, 0.04f, 0.06f, 0.98f);
            }
        }
        void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
    }
}
