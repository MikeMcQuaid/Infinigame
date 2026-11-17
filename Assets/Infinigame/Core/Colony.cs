using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Infinigame.Core
{
    public enum Building { Depot, Mine, Drill, Turret, Radar, Workshop, Robot, Factory, ShipFactory }

    [Serializable]
    public class Colony
    {
        public int world, region, buildings, robots = 1;
        public float ore, ice, salvage, raid, fabrication;
        public float3 site;
        public bool Has(Building building) => (buildings & 1 << (int)building) != 0;
        public static float3 Cost(Building building) => Narrative.Costs[(int)building];
        public bool Spend(float3 cost)
        {
            if (math.any(cost < 0) || ore < cost.x || ice < cost.y || salvage < cost.z) return false;
            ore -= cost.x;
            ice -= cost.y;
            salvage -= cost.z;
            return true;
        }
        public bool Build(Building building)
        {
            if (building != Building.Robot && Has(building) || building != Building.Depot && !Has(Building.Depot) || !Spend(Cost(building))) return false;
            if (building == Building.Robot) robots++;
            else buildings |= 1 << (int)building;
            return true;
        }
        public void Step(float dt, float pressure)
        {
            if (dt <= 0f) return;
            if (Has(Building.Mine)) ore += robots * dt / 12f;
            if (Has(Building.Drill)) ice += robots * dt / 18f;
            if (Has(Building.Workshop) && robots > 0 && robots < 32)
            {
                fabrication += dt;
                if (fabrication >= 120 && Build(Building.Robot)) fabrication = 0;
                fabrication = math.min(fabrication, 120);
            }
            if (pressure <= 0f) return;
            raid += dt * pressure * (Has(Building.Radar) ? 0.65f : 1);
            while (raid >= 180f)
            {
                raid -= 180f;
                if (Has(Building.Turret) && Spend(new float3(2, 0, 0))) salvage += 4f;
                else if (robots > 0) robots--;
            }
        }
    }

    [Serializable]
    public class Presence
    {
        public float played;
        public List<Front> fronts = new List<Front>();
        public List<int> worlds = new List<int>();
        public List<float> freed = new List<float>();
        public void Step(float dt) => played += math.max(0f, dt);
        public float Reach(int world)
        {
            int i = worlds.IndexOf(world);
            return math.saturate(i < 0 ? 0.15f + played / 7200f - world * 0.012f : (played - freed[i]) / 7200f);
        }
        public void Liberate(int world)
        {
            int i = worlds.IndexOf(world);
            if (i < 0) { worlds.Add(world); freed.Add(played); }
            else freed[i] = played;
        }
    }

    [Serializable]
    public class Moonfall
    {
        public const float Assembly = 5, Ignition = 4, Consumption = 20, Supernova = 4;
        public int moon = -1, world = -1;
        public double began, duration = 90;
        public bool active, destroyed;
        // The target keeps moving while the moon leaves its orbit; the initial position is exact.
        public double3 Position(SolarSystemDescriptor system, double time)
        {
            double t = math.saturate((time - began - Assembly - Ignition) / math.max(1, duration - Assembly - Ignition));
            double3 start = system.OrbitalPosition(moon, time), target = system.OrbitalPosition(world, time);
            double3 impact = target + math.normalizesafe(start - target) * (system.bodies[world].radius + system.bodies[moon].radius);
            return math.lerp(start, impact, t * t * (3 - 2 * t));
        }
        public bool Finished(double time) => active && time >= began + duration;
    }
}
