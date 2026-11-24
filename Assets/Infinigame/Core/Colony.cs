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
        public float ore, ice, raid, fabrication;
        public float3 site;
        public bool Has(Building building) => (buildings & 1 << (int)building) != 0;
        public static float2 Cost(Building building) => Narrative.Costs[(int)building];
        public bool Spend(float2 cost)
        {
            if (math.any(cost < 0) || ore < cost.x || ice < cost.y) return false;
            ore -= cost.x;
            ice -= cost.y;
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
                if (!(Has(Building.Turret) && Spend(new float2(2, 0))) && robots > 0) robots--;
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
        public const float Assembly = 1, Ignition = 1, Transit = 10, Consumption = 10, Infall = 3, WhiteHole = 4;
        public int moon = -1, world = -1;
        public double began, duration = Assembly + Ignition + Transit;
        public bool active, destroyed;
        public double TravelFraction(double time)
        {
            double transit = math.max(1, duration - Assembly - Ignition);
            double seconds = math.clamp(time - began - Assembly - Ignition, 0, transit);
            // Accelerate from rest for one second, then hold that speed until contact.
            return (seconds < 1 ? 0.5 * seconds * seconds : seconds - 0.5) / (transit - 0.5);
        }
        public double3 Position(SolarSystemDescriptor system, double time)
        {
            if (time < began) return system.OrbitalPosition(moon, time);
            double3 outward = system.OrbitalPosition(moon, began) - system.OrbitalPosition(world, began);
            double contact = system.bodies[world].radius + system.bodies[moon].radius;
            double clearance = math.max(20000, math.length(outward) - contact);
            return system.OrbitalPosition(world, time) + math.normalize(outward) * (contact + clearance * (1 - TravelFraction(time)));
        }
        public bool Finished(double time) => active && time >= began + duration;
    }
}
