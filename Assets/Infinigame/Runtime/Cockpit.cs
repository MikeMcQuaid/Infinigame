using System.Collections.Generic;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // What the ship is doing, as its instruments need to know it, for each frame the pilot is seated.
    public struct Readings
    {
        public float speed;             // metres a second
        public float climb;             // metres a second, up positive
        public float range;             // metres to the surface that matters, and the name of that world
        public string ahead;
        public float altitude;          // metres above the ground or sea below
        public float heat, heatLimit;   // kelvin outside, and what the hull can take
        public float hull;              // what is left of it, 0 to 1
        public Vector3 thrust;          // what the pilot is asking for: x strafe, y lift, z forward, each -1 to 1
        public Vector3 turn;            // pitch, yaw and roll being asked for, each -1 to 1
        public float pitch, bank;       // the ship's attitude against the local horizon, radians
        public bool boost, assist, landing, landed, shield, autopilot, irradiated, firing, warp;   // warp: a warp drive is aboard and could be used
        public int weapon;              // index into Combat.Guns: cannons, missiles, lance, railgun, torpedoes
        public int weapons;             // bit i set when Combat.Guns[i] has been won
    }

    // The ship's instruments. Models.Ship builds them under "Cockpit" and names every part that
    // moves or lights; this is what each of them shows. A part eases to where it belongs, so
    // needles have a little inertia, switches click over and lamps fade up, and nothing jumps.
    public class Cockpit : MonoBehaviour
    {
        // How far a needle swings either side of noon, and how far the weapon selector turns from
        // one weapon to the next, in degrees.
        public const float Sweep = 120f, Detent = 40f;
        // The lamps of the ship's weapons, in the order of Combat.Guns.
        public static readonly string[] Racks = { "1 CANNON", "2 MISSILE", "3 BEAM", "4 RAIL", "5 NUKE" };

        // What the instruments are lettered in, and a lamp that is not lit.
        public static readonly Color Ink = new Color(0.55f, 0.95f, 1f), Dark = new Color(0.015f, 0.02f, 0.025f);
        static readonly Color Faint = Ink * 0.45f, Alarm = new Color(1f, 0.35f, 0.25f);
        static readonly Color Fine = new Color(0.3f, 1.6f, 2.2f), Safe = new Color(0.3f, 2f, 0.6f), Caution = new Color(1.8f, 0.7f, 0.08f), Danger = new Color(2.6f, 0.25f, 0.15f);

        static readonly string[] FlightLabels = { "BOOST", "AUTO", "LAND" };
        sealed class Part
        {
            public Transform transform;
            public TextMesh caption;
            public Material lamp;
            public Vector3 value, speed;
        }

        readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
        public Transform RadarSocket => At("Radar socket").transform;

        void Awake()
        {
            foreach (var label in GetComponentsInChildren<TextMesh>(true))
            {
                label.font = Shapes.Type;
                label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            }
        }
        public void Show(in Readings readings)
        {
            var r = readings;
            // the three screens: speed, the gap to the surface that matters and the heat outside
            bool near = r.range < 2000f;
            float load = r.heat / Mathf.Max(r.heatLimit, 1f), hot = math.smoothstep(0.7f, 0.9f, load);
            At("BOOST").caption.text = "BOOST\n" + DeviceControls.Key("SHIFT", "L3", "STICK ↑", "LT");
            At("AUTO").caption.text = "AUTO\n" + DeviceControls.Key("T", "D→", "RECALL", "R3");
            At("LAND").caption.text = "LAND\n" + DeviceControls.Use;
            foreach (string key in FlightLabels)
            {
                TextMesh text = At(key).caption;
                text.characterSize = 0.00068f * Mathf.Min(1, 5f / (text.text.Length - key.Length - 1));
            }
            Light("SHIELD", r.shield ? 1 : 0, Safe);
            At("SpeedScreen").caption.text = r.speed.ToString("0");
            At("RangeScreen").caption.text = near ? r.range.ToString("0") : (r.range / 1000f).ToString("0.0");
            At("RangeScreen/Legend").caption.text = $"{r.ahead?.ToUpperInvariant()}  {(near ? "M" : "KM")}";
            At("HeatScreen").caption.text = Climate.Celsius(r.heat).ToString("0");
            At("HeatScreen").caption.color = Color.Lerp(Ink, Alarm, hot);
            // a paused game holds every part where it is
            if (Time.deltaTime <= 0f) return;

            // the ball keeps the horizon where it is outside as the ship pitches and banks
            At("Attitude/Horizon").transform.localRotation = Quaternion.Euler(0f, 0f, math.degrees(r.bank)) * Quaternion.Euler(-math.degrees(r.pitch), 0f, 0f);
            // height and climb are a power of ten to a mark, so that a landing and an orbit both read
            Needle("ALTITUDE/Needle", Mathf.Log10(Mathf.Max(r.altitude, 1f)) / 6f);
            Needle("CLIMB/Needle", 0.5f + Mathf.Sign(r.climb) * Mathf.Log10(Mathf.Max(Mathf.Abs(r.climb), 1f)) / 6f);
            Needle("HULL/Needle", r.hull);
            Needle("TEMP/Needle", load / 1.2f);
            // the engines give half of what they have until boost asks for the rest
            Needle("THRUST/Needle", Mathf.Max(Mathf.Abs(r.thrust.x), Mathf.Abs(r.thrust.y), Mathf.Abs(r.thrust.z)) * (r.boost ? 1f : 0.5f));
            var spot = Ease("Thrusters/Spot", new Vector3(r.thrust.x, r.thrust.y), 0.1f);
            spot.transform.localPosition = spot.value;

            // the stick leans the way the nose is pushed and twists with the yaw; the throttle goes up with the engines, and past its gate for boost
            Turn("Stick", new Vector3(-12f * r.turn.x, 14f * r.turn.y, -12f * Mathf.Clamp(r.turn.y + r.turn.z, -1f, 1f)), 0.08f);
            Turn("Throttle", new Vector3(22f * r.thrust.z + (r.boost && r.thrust.z > 0f ? 12f : 0f), 0f, 0f), 0.1f);
            Light("Stick/Trigger", r.firing ? 1f : 0f, Danger);

            // the selector points at the weapon that is live and its lamp is lit; one not yet fitted is all but blank
            Turn("Selector", new Vector3(0f, 0f, (1.5f - r.weapon) * Detent), 0.07f);
            for (int i = 0; i < Racks.Length; i++)
            {
                var rack = Light(Racks[i], i == r.weapon ? 1f : 0f, Fine);
                if ((r.weapons >> i & 1) == 0) rack.caption.color = Faint * 0.3f;
            }

            Switch("ASSIST", r.assist, Fine);
            Switch("AUTO", r.autopilot, Fine);
            Switch("LAND", r.landing, Caution);
            // warnings come up as trouble nears, and throb while it is costing hull
            float throb = 0.75f + 0.25f * Mathf.Sin(6f * Time.time);
            Light("HEAT", hot * (load > 1f ? throb : 1f), Danger);
            Light("DAMAGE", (1f - math.smoothstep(0.25f, 0.5f, r.hull)) * (r.hull < 0.25f ? throb : 1f), Danger);
            Light("RAD", r.irradiated ? throb : 0f, Danger);
            Light("LANDED", r.landed ? 1f : 0f, Safe);
            Light("BOOST", r.boost ? 1f : 0f, Caution);
            Light("GATE", r.warp ? 1f : 0f, Fine);
        }

        // The named part, found once: a path down from "Cockpit", as Models.Ship laid it out.
        Part At(string name)
        {
            if (parts.TryGetValue(name, out var part)) return part;
            var found = transform.Find(name);
            return parts[name] = new Part { transform = found, caption = found.GetComponentInChildren<TextMesh>() };
        }

        // Moves a part's value towards where it should be, as anything with weight gets there.
        Part Ease(string name, Vector3 to, float lag)
        {
            var part = At(name);
            part.value = Vector3.SmoothDamp(part.value, to, ref part.speed, lag);
            return part;
        }

        // Turns a part to so many degrees about its x, y and z.
        void Turn(string name, Vector3 degrees, float lag)
        {
            var part = Ease(name, degrees, lag);
            part.transform.localRotation = Quaternion.Euler(part.value);
        }

        // A needle's share of its sweep, clockwise from seven o'clock to five.
        void Needle(string name, float share) => Turn(name, new Vector3(0f, 0f, Mathf.Lerp(Sweep, -Sweep, Mathf.Clamp01(share))), 0.25f);

        // Fades a lamp up or down. Its caption is pale on a dark lamp and dark on one lit at all brightly.
        Part Light(string name, float level, Color lit)
        {
            var part = Ease(name, new Vector3(level, 0f), 0.06f);
            if (part.lamp == null) part.lamp = part.transform.GetComponent<MeshRenderer>().material;
            part.lamp.color = Color.Lerp(Dark, lit, part.value.x);
            if (part.caption != null) part.caption.color = Color.Lerp(Faint, Color.black, 2.5f * part.value.x);
            return part;
        }

        // A switch on the overhead panel: its lever goes up as its lamp comes on.
        void Switch(string name, bool on, Color lit)
        {
            var part = Light(name, on ? 1f : 0f, lit);
            part.transform.Find("Switch").localRotation = Quaternion.Euler(Mathf.Lerp(-35f, 35f, part.value.x), 0f, 0f);
        }
    }
}
