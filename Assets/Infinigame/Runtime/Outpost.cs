using System;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // The place the mission in hand is about: a fort of the raiders' on some world's ground, the
    // guards that turn out of it when the player comes near, and what they guard. Killing them
    // all leaves that to be taken, and taking it wins the mission: by hand from its crate, or,
    // for what is too heavy to carry, by landing the ship beside it. The citadel is such a place
    // too, with itself for a guard and its warp drive left adrift in the wreckage.
    public class Outpost : MonoBehaviour
    {
        // How near a prize has to be to take it by hand, by landing beside it or, adrift, by flying up to it; and how near its guards turn out.
        const float Arm = 5f, Hoist = 30f, Grapple = 80f, Picket = 170f, Patrol = 1200f;

        [NonSerialized] public Game game;
        // The body it stands on and where, relative to that body's centre: no body at all while the
        // mission has no such place.
        public int World { get; private set; } = -1;
        public float3 Position { get; private set; }
        // How many of its guards are still to be killed.
        public int Left { get; private set; }
        // The guard is dead and what it kept is there for the taking, here.
        public bool Fallen => World >= 0 && Left == 0;
        public float3 Prize { get; private set; }
        // The player is on foot and within arm's reach of it: a ship that is the prize is bigger than a crate.
        public bool Near => Fallen && game.Stage < Campaign.Citadel && World == game.System.Reference && !game.Player.flying
            && math.distance(game.Player.walker.position, Prize) < (game.Mission.wins.Contains(Gear.Ship) ? 9f : Arm);
        // What is left to do once the guard is dead.
        public string Errand => game.Stage == Campaign.Citadel ? "FLY TO THE BEACON IN THE WRECKAGE FOR ITS WARP DRIVE"
            : Hauled ? $"LAND BESIDE THE BEACON TO HAUL {Campaign.Spoils(game.Stage)} ABOARD" : $"TAKE {Campaign.Spoils(game.Stage)}";

        // Raiders guard what only a ship could carry off.
        bool Hauled => game.Mission.guards == FoeKind.Raider;

        Transform fort, crate, beam;
        float muster;
        bool roused;

        // The beam is as wide as it takes to be seen: a hair's breadth from beside it, and from across a valley no thinner than a line.
        void Update()
        {
            if (crate == null || !crate.gameObject.activeSelf) return;
            float wide = Mathf.Clamp(0.004f * Vector3.Distance(Camera.main.transform.position, beam.position), 0.04f, 6f);
            beam.localScale = new Vector3(wide, 1f, wide);
        }

        // Raises the fort for the mission the game has reached, with its full guard and its prize put by.
        public void Settle()
        {
            if (fort != null) Destroy(fort.gameObject);
            if (crate != null) Destroy(crate.gameObject);
            var system = game.System.Descriptor;
            World = game.Stage > Campaign.Stranger && game.Stage <= Campaign.Citadel ? Campaign.World(system, game.Stage) : -1;
            roused = false;
            if (World < 0) return;
            var body = system.bodies[World];
            float3 site = Campaign.Site(system, game.Stage), aside = math.normalize(math.cross(site, new float3(0.2f, 0.9f, 0.4f)));
            Left = game.Mission.count;
            crate = Crate();
            beam = crate.Find("Beam");
            crate.SetParent(game.System.Bodies[World].transform, false);
            crate.gameObject.SetActive(false);
            if (game.Stage == Campaign.Citadel)
            {
                // nothing to raise: the citadel is its own fort, and what it leaves drifts clear of where it stood
                (Position, Prize) = (float3.zero, site * (1.4f * body.radius));
                crate.localPosition = Prize;
                return;
            }
            Position = site * body.GroundRadius(site);
            fort = Fort();
            fort.SetParent(game.System.Bodies[World].transform, false);
            // bedded a little into the ground it stands on
            fort.localPosition = Position - site * 0.4f;
            fort.localRotation = Quaternion.LookRotation(Walker.Project(new float3(0.2f, 0.9f, 0.4f), site), site);
            // what it guards stands in the open beside it: a few steps off for someone on foot, further where a ship has to land by it
            float3 beside = math.normalize(Position + aside * (Hauled ? 36f : 22f));
            Prize = beside * body.GroundRadius(beside);
            crate.localPosition = Prize - beside * 0.1f;
            crate.localRotation = Quaternion.LookRotation(Walker.Project(aside, beside), beside);
        }

        // Keeps the guard turned out while the player is near enough to fight it: drones for
        // someone on foot, raiders for a ship or for someone with a launcher to answer them. Once
        // the guard is dead, watches for a ship come to take what is too heavy to carry.
        public void Tick()
        {
            var player = game.Player;
            bool drones = game.Mission.guards == FoeKind.Drone;
            if (World != game.System.Reference) return;
            if (Fallen)
            {
                // the ship is the prize where it is one, and needs no crate
                crate.gameObject.SetActive(!game.Mission.wins.Contains(Gear.Ship));
                if (game.Stage == Campaign.Citadel ? player.flying && math.distance(player.Position, Prize) < Grapple
                    : Hauled && player.flying && player.ship.landed && math.distance(player.Position, Prize) < Hoist) game.Advance();
                return;
            }
            if (game.Stage == Campaign.Citadel || (drones ? player.flying : !player.flying && !player.Has(Gear.Launcher)) || math.distance(player.Position, Position) > (drones ? Picket : Patrol)) return;
            if (!roused)
            {
                roused = true;
                game.Notify($"{game.Mission.name.ToUpperInvariant()}  ·  {Left} {(drones ? "DRONES" : "RAIDERS")} TO CLEAR");
                game.Sounds.Alarm();
            }
            int afield = 0;
            foreach (var foe in game.Battle.Foes)
                if (foe.kind == game.Mission.guards)
                    afield++;
            // a few at a time, the next as the last ones fall
            if (afield >= math.min(Left, drones ? 4 : 3) || (muster -= Time.deltaTime) > 0f) return;
            muster = 1.2f;
            float3 up = math.normalize(Position), round = math.rotate(quaternion.AxisAngle(up, 2.4f * (Left + afield)), math.normalize(math.cross(up, new float3(0.2f, 0.9f, 0.4f))));
            float3 over = math.normalize(Position + round * (drones ? 16f : 500f));
            game.Battle.Add(game.Mission.guards, over * (Ship.Surface(game.System.ReferenceBody, over) + (drones ? 2f : 350f)));
        }

        // One of the guards has been killed. The last of them leaves the prize unguarded.
        public void Killed(FoeKind kind)
        {
            if (World != game.System.Reference || Left <= 0 || kind != game.Mission.guards) return;
            game.Notify(--Left > 0 ? $"{Left} LEFT" : game.Stage == Campaign.Citadel ? $"THE CITADEL IS DESTROYED  ·  {Errand}" : $"THE GUARD IS DEAD  ·  {Errand}");
            if (Left == 0) game.Sounds.Chime();
        }

        // What the guard kept: an iron-bound crate with a band of light round it, under a beam
        // that stands up into the sky to be found by, a metre across until Update has sized it.
        static Transform Crate()
        {
            var root = new GameObject("Prize").transform;
            MeshKit box = new MeshKit(), light = new MeshKit();
            box.Box(new Vector3(0f, 0.6f, 0f), new Vector3(1.7f, 1.2f, 1.15f), 0.08f);
            foreach (float x in new[] { -0.6f, 0.6f }) box.Box(new Vector3(x, 0.6f, 0f), new Vector3(0.14f, 1.26f, 1.21f), 0.03f);
            light.Box(new Vector3(0f, 0.6f, 0f), new Vector3(1.73f, 0.1f, 1.18f));
            var shaft = new MeshKit();
            shaft.Rod(new Vector3(0f, 1.25f, 0f), new Vector3(0f, 140f, 0f), 0.5f, 8, 0.3f);
            var metal = Shapes.Material("Props", new Color(0.2f, 0.23f, 0.27f));
            metal.SetFloat("_Metallic", 0.5f);
            metal.SetFloat("_Smoothness", 0.5f);
            Shapes.Part(root, "Box", box.Mesh("Box"), metal, Vector3.zero, Vector3.one);
            var glow = Shapes.Material("Glow", new Color(0.7f, 3.4f, 4.2f));
            Shapes.Part(root, "Light", light.Mesh("Light"), glow, Vector3.zero, Vector3.one, false);
            Shapes.Part(root, "Beam", shaft.Mesh("Beam"), glow, Vector3.zero, Vector3.one, false);
            return root;
        }

        // The fort: a squat iron keep on a skirt, a tower at each corner with a spike for a roof,
        // and a furnace showing through its slits.
        static Transform Fort()
        {
            var root = new GameObject("Outpost").transform;
            MeshKit iron = new MeshKit(), furnace = new MeshKit();
            iron.Box(new Vector3(0f, 0.7f, 0f), new Vector3(15f, 1.4f, 15f), 0.3f);
            iron.Box(new Vector3(0f, 4f, 0f), new Vector3(9f, 5.4f, 9f), 0.5f);
            iron.Box(new Vector3(0f, 7.8f, 0f), new Vector3(5.5f, 2.4f, 5.5f), 0.4f);
            iron.Spike(new Vector3(0f, 8.8f, 0f), new Vector3(0f, 18f, 0f), 0.9f, 6);
            foreach (float x in new[] { -5.4f, 5.4f })
            foreach (float z in new[] { -5.4f, 5.4f })
            {
                iron.Rod(new Vector3(x, 0f, z), new Vector3(x, 8.5f, z), 1.4f, 8, 1.05f);
                iron.Spike(new Vector3(x, 8.5f, z), new Vector3(x * 1.2f, 13.5f, z * 1.2f), 1.15f, 6);
            }
            for (int side = 0; side < 4; side++)
                furnace.Place(MeshKit.Turn(0f, 90f * side, 0f), () =>
                {
                    furnace.Box(new Vector3(0f, 4.2f, 4.52f), new Vector3(0.5f, 3f, 0.1f));
                    foreach (float x in new[] { -2.2f, 2.2f }) furnace.Box(new Vector3(x, 4.6f, 4.52f), new Vector3(0.3f, 1.6f, 0.1f));
                });
            var metal = Shapes.Material("Props", new Color(0.3f, 0.26f, 0.25f));
            metal.SetFloat("_Metallic", 0.4f);
            metal.SetFloat("_Smoothness", 0.45f);
            Shapes.Part(root, "Iron", iron.Mesh("Iron"), metal, Vector3.zero, Vector3.one);
            Shapes.Part(root, "Furnace", furnace.Mesh("Furnace"), Shapes.Material("Glow", new Color(9f, 2.4f, 0.35f)), Vector3.zero, Vector3.one, false);
            return root;
        }
    }
}
