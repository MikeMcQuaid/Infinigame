using System.Collections.Generic;
using Infinigame.Core;
using UnityEngine;

namespace Infinigame
{
    public static class Models
    {
        public static readonly Vector3 PilotEye = new Vector3(0, 0.95f, 1.2f);
        public static readonly Vector3 WingPort = new Vector3(1.7f, -0.3f, 2.5f);
        public static int LimbCount(MotileCaste caste) => caste == MotileCaste.Breacher ? 8 : caste == MotileCaste.Spitter ? 4 : 6;

        public static Vector3 LimbRoot(MotileCaste caste, int index)
        {
            int pairs = LimbCount(caste) / 2;
            return new Vector3((index < pairs ? -1 : 1) * (caste == MotileCaste.Breacher ? 0.62f : 0.4f), 0,
                (index % pairs / (float)(pairs - 1) - 0.5f) * (caste == MotileCaste.Spitter ? 0.7f : 0.9f));
        }

        public static Transform Gauntlet() => Prefab("Gauntlet");
        public static Transform Traveller() => Prefab("Traveller");
        public static Transform Robot() => Prefab("Robot");
        public static Transform Ship() => Prefab("Ship");
        public static Transform Building(Building kind) => Prefab(kind.ToString());
        public static Transform Equipment(bool demolition) => Prefab(demolition ? "Demolition core" : "Equipment case");
        public static Transform Gate() => Prefab("Gate");
        public static Transform CitadelCarapace() => LivingRoom("Citadel carapace");
        public static Transform Nest() => LivingRoom("Presence nest");
        public static Transform CitadelRoof(bool crown) => LivingRoom(crown ? "Citadel sensory chamber" : "Citadel vault");

        static readonly Dictionary<Material, Material> matteFinishes = new Dictionary<Material, Material>();

        static Transform LivingRoom(string name)
        {
            var room = Prefab(name);
            if (Shapes.MatteCombat)
                foreach (var part in room.GetComponentsInChildren<MeshRenderer>())
                {
                    var material = part.sharedMaterial;
                    if (!material.HasProperty("_Organic") || material.GetFloat("_Organic") == 0) continue;
                    if (!matteFinishes.TryGetValue(material, out var matte) || matte == null)
                        matteFinishes[material] = matte = Shapes.Carapace(material.color, material.GetFloat("_Size"), material.GetColor("_Glow"));
                    part.sharedMaterial = matte;
                }
            return room;
        }

        static string Tier => Application.isMobilePlatform || LodSettings.Tier == 0 ? "Mobile/" : "Desktop/";
        static Transform Prefab(string name)
        {
            var model = Object.Instantiate(Resources.Load<GameObject>("Models/" + Tier + name));
            model.name = name;
            return model.transform;
        }

        internal static Mesh[] Parts(string name) => Resources.Load<ModelParts>("Models/" + Tier + name).meshes;
        public static (Mesh hull, Mesh lights) Raider() { var m = Parts("Harrower"); return (m[0], m[1]); }
        public static (Mesh shell, Mesh lights) Drone() { var m = Parts("Skimmer"); return (m[0], m[1]); }
        public static (Mesh body, Mesh lights) Mind() { var m = Parts("Mind"); return (m[0], m[1]); }
        public static (Mesh body, Mesh lights, Mesh leg) Sentry(MotileCaste caste = MotileCaste.Stalker, int detail = 0)
        {
            var parts = Parts(caste + " " + detail);
            return (parts[0], parts[1], parts[2]);
        }
        public static ModelParts Wreck(string name) => Resources.Load<ModelParts>("Models/" + Tier + name + " wreck");
        public static Mesh[] SpineLauncher() => Parts("Turret emplacement");
        public static Mesh Missile() => Parts("Missile")[0];
        public static Mesh Drifter() => Parts("Drifter")[0];
        public static (Mesh body, Mesh lights) Strider() { var m = Parts("Strider"); return (m[0], m[1]); }
        public static Mesh[] Crystals() => Parts("Crystals");
        public static Mesh TubeGrove() => Parts("Tube grove")[0];
        public static Transform Camp()
        {
            var root = new GameObject("Robot shelter").transform;
            var robot = Robot(); robot.name = "Hermit"; robot.SetParent(root, false);
            var workshop = Building(Core.Building.Workshop); workshop.SetParent(root, false); workshop.localPosition = new Vector3(5, 0, -3);
            var fire = new GameObject("Fire").transform; fire.SetParent(root, false); fire.localPosition = new Vector3(-3, 0, 2);
            return root;
        }

    }
}
