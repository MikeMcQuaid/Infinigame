using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Infinigame.Core
{
    public enum Building { Depot, Mine, Drill, Turret, Radar, Workshop, Robot }

    [Serializable]
    public class Colony
    {
        public int world, buildings, robots = 1;
        public float ore, ice, salvage, raid;
        public float3 site;
        public bool Has(Building building) => (buildings & 1 << (int)building) != 0;
        public static float3 Cost(Building building) => building switch
        {
            Building.Depot => new float3(10, 0, 5), Building.Mine => new float3(15, 0, 5), Building.Drill => new float3(15, 5, 5),
            Building.Turret => new float3(20, 5, 15), Building.Radar => new float3(10, 5, 10), Building.Workshop => new float3(25, 10, 15),
            _ => new float3(10, 5, 5),
        };
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
            if (pressure <= 0f) return;
            raid += dt * pressure;
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
        public int moon = -1, world = -1;
        public double began, duration = 90;
        public bool active, destroyed;
        // The target keeps moving while the moon leaves its orbit; the initial position is exact.
        public double3 Position(SolarSystemDescriptor system, double time)
        {
            double t = math.saturate((time - began) / duration);
            return math.lerp(system.OrbitalPosition(moon, time), system.OrbitalPosition(world, time), t * t * (3 - 2 * t));
        }
        public bool Finished(double time) => active && time >= began + duration;
    }
}
