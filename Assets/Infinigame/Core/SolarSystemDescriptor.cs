using System;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Infinigame.Core
{
    // Speeds the brief's lap times depend on. BriefTests holds generation to them.
    public static class Tuning
    {
        public const float WalkSpeed = 5f, SprintSpeed = 8f, ShipCruiseSpeed = 150f, ShipSpaceSpeed = 600f;
    }

    public enum BodyKind { Sun, Planet, Moon }

    // A circular orbit on rails around a parent body: a pure function of time.
    [Serializable]
    public struct Orbit
    {
        public int parent; // body index, -1 for a body that does not move
        public double radius, period, phase;
        public float inclination; // tilt of the orbital plane about the x axis, radians

        public double3 Position(double time) => parent < 0 ? double3.zero : Tilt(Angle(time), radius);

        public double3 Velocity(double time) =>
            parent < 0 ? double3.zero : Tilt(Angle(time) + math.PI_DBL / 2, radius * 2 * math.PI_DBL / period);

        double Angle(double time) => phase + 2 * math.PI_DBL * time / period;

        double3 Tilt(double angle, double length)
        {
            double z = math.sin(angle), tilt = inclination;
            return new double3(math.cos(angle), -z * math.sin(tilt), z * math.cos(tilt)) * length;
        }
    }

    [Serializable]
    public class BodyDescriptor
    {
        public string name;
        public BodyKind kind;
        public float radius, surfaceGravity, soiRadius;
        public float hue; // palette seed, 0 to 1
        public Orbit orbit;
        public TerrainParams terrain;
        // One turn every spinPeriod seconds, in the same sense as the orbits, about an axis tilted from +y. Zero is no spin.
        public double spinPeriod, spinPhase;
        public float tilt;

        public quaternion Rotation(double time) => spinPeriod == 0 ? quaternion.identity : math.mul(quaternion.RotateX(tilt),
            quaternion.RotateY(-(float)math.fmod(spinPhase + 2 * math.PI_DBL * time / spinPeriod, 2 * math.PI_DBL)));

        // Angular velocity of the spin, in the inertial frame.
        public float3 Spin => spinPeriod == 0 ? float3.zero : math.rotate(quaternion.RotateX(tilt), new float3(0, -(float)(2 * math.PI_DBL / spinPeriod), 0));

        public float SeaRadius => terrain.seaDepth > 0f ? radius : 0f;

        public float GroundRadius(float3 direction) => radius + TerrainNoise.Height(direction, terrain);

        public float3 GroundNormal(float3 direction)
        {
            float3 a = math.normalize(math.cross(direction, math.abs(direction.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0)));
            float3 b = math.cross(direction, a), da = math.normalize(direction + a * (0.5f / radius)), db = math.normalize(direction + b * (0.5f / radius));
            float3 origin = direction * GroundRadius(direction);
            float3 normal = math.normalize(math.cross(da * GroundRadius(da) - origin, db * GroundRadius(db) - origin));
            return math.dot(normal, direction) < 0f ? -normal : normal;
        }

        // A gentle spot above the waterline to stand or park on.
        public float3 FindLanding(uint seed)
        {
            var rnd = new Random(math.max(1u, seed));
            for (int i = 0; i < 5000; i++)
            {
                float3 direction = rnd.NextFloat3Direction();
                float height = GroundRadius(direction) - radius;
                if ((SeaRadius == 0f || (height > 3f && height < 30f)) && math.dot(GroundNormal(direction), direction) > 0.98f)
                    return direction;
            }
            return new float3(0, 1, 0);
        }

        // Inverse-square above the surface, constant below it.
        public float3 GravityAt(float3 fromCentre)
        {
            float r = math.length(fromCentre);
            return r < 1e-3f ? float3.zero : -fromCentre / r * surfaceGravity * math.square(radius / math.max(r, radius));
        }
    }

    // Everything needed to rebuild a system. Later this is what an LLM authors as JSON.
    [Serializable]
    public class SolarSystemDescriptor
    {
        public int schemaVersion = 1;
        public uint seed;
        public BodyDescriptor[] bodies;

        public double3 PositionAt(int body, double time)
        {
            double3 p = double3.zero;
            for (int b = body; b >= 0; b = bodies[b].orbit.parent) p += bodies[b].orbit.Position(time);
            return p;
        }

        public double3 VelocityAt(int body, double time)
        {
            double3 v = double3.zero;
            for (int b = body; b >= 0; b = bodies[b].orbit.parent) v += bodies[b].orbit.Velocity(time);
            return v;
        }

        // The innermost body whose sphere of influence holds the point. The current
        // reference gets a margin so a traveller on the boundary does not flicker.
        public int ReferenceBody(double3 position, double time, int current)
        {
            int best = 0;
            for (int i = 1; i < bodies.Length; i++)
                if (bodies[i].soiRadius < bodies[best].soiRadius
                    && math.distance(position, PositionAt(i, time)) < bodies[i].soiRadius * (i == current ? 1.15f : 1f))
                    best = i;
            return best;
        }

        // Each body carries its own frame, centred on it and turning with it. Local means in that frame.
        public double3 ToInertial(int body, double time, float3 local) => PositionAt(body, time) + math.rotate(bodies[body].Rotation(time), local);

        public float3 ToLocal(int body, double time, double3 inertial) =>
            math.rotate(math.inverse(bodies[body].Rotation(time)), (float3)(inertial - PositionAt(body, time)));

        // The velocity of something moving in a body's frame: its own, plus being carried along and round.
        public double3 InertialVelocity(int body, double time, float3 local, float3 velocity)
        {
            quaternion rotation = bodies[body].Rotation(time);
            return VelocityAt(body, time) + math.rotate(rotation, velocity) + math.cross(bodies[body].Spin, math.rotate(rotation, local));
        }

        // Re-expresses a position and velocity held in one body's frame in another's.
        public void Rebase(int from, int to, double time, ref float3 position, ref float3 velocity)
        {
            double3 inertial = ToInertial(from, time, position), motion = InertialVelocity(from, time, position, velocity);
            position = ToLocal(to, time, inertial);
            velocity = math.rotate(math.inverse(bodies[to].Rotation(time)),
                (float3)(motion - VelocityAt(to, time)) - math.cross(bodies[to].Spin, (float3)(inertial - PositionAt(to, time))));
        }

        // How a direction held in one body's frame turns when re-expressed in another's.
        public quaternion Turn(int from, int to, double time) => math.mul(math.inverse(bodies[to].Rotation(time)), bodies[from].Rotation(time));

        // Sizes stay inside ranges that keep the brief's lap times.
        public static SolarSystemDescriptor Generate(uint seed)
        {
            var rnd = new Random(math.max(1u, math.hash(new uint2(seed, 0x9E3779B9u))));
            float planetRadius = rnd.NextFloat(850f, 1100f), moonRadius = rnd.NextFloat(130f, 180f), riverFreq = rnd.NextFloat(2f, 3.2f);
            double planetOrbit = rnd.NextDouble(26000, 34000), moonOrbit = rnd.NextDouble(3500, 5000);
            var system = new SolarSystemDescriptor
            {
                seed = seed,
                bodies = new[]
                {
                    new BodyDescriptor
                    {
                        name = "Sun", kind = BodyKind.Sun, radius = 2500f, surfaceGravity = 20f, soiRadius = 1e9f,
                        hue = rnd.NextFloat(), orbit = new Orbit { parent = -1 },
                    },
                    new BodyDescriptor
                    {
                        name = "Planet", kind = BodyKind.Planet, radius = planetRadius,
                        surfaceGravity = rnd.NextFloat(8f, 11f), soiRadius = (float)planetOrbit * 0.45f, hue = rnd.NextFloat(),
                        orbit = new Orbit { parent = 0, radius = planetOrbit, period = rnd.NextDouble(3600, 5400), phase = rnd.NextDouble(0, 2 * math.PI_DBL) },
                        terrain = new TerrainParams
                        {
                            offset = rnd.NextFloat3(-100f, 100f),
                            seaDepth = 0.06f * planetRadius, seaBias = rnd.NextFloat(-0.12f, 0.05f),
                            continentFreq = rnd.NextFloat(0.9f, 1.5f), warp = 0.35f,
                            plains = 0.008f * planetRadius, hillFreq = rnd.NextFloat(5f, 8f), hillAmp = 0.012f * planetRadius,
                            mountainFreq = rnd.NextFloat(2.5f, 4f), mountainAmp = rnd.NextFloat(0.1f, 0.14f) * planetRadius,
                            riverFreq = riverFreq, riverWidth = 0.037f, riverBank = 1.6f * riverFreq / planetRadius, riverDepth = 0.003f * planetRadius,
                            polarCap = 0.06f * planetRadius,
                        },
                    },
                    new BodyDescriptor
                    {
                        name = "Moon", kind = BodyKind.Moon, radius = moonRadius,
                        surfaceGravity = rnd.NextFloat(1.3f, 2.2f), soiRadius = moonRadius * 3f, hue = rnd.NextFloat(),
                        orbit = new Orbit
                        {
                            parent = 1, radius = moonOrbit, period = rnd.NextDouble(1200, 1800),
                            phase = rnd.NextDouble(0, 2 * math.PI_DBL), inclination = rnd.NextFloat(-0.3f, 0.3f),
                        },
                        terrain = new TerrainParams
                        {
                            offset = rnd.NextFloat3(-100f, 100f),
                            hillFreq = 4f, hillAmp = 0.03f * moonRadius,
                            craterFreq = rnd.NextFloat(3f, 5f), craterDepth = 0.04f * moonRadius,
                        },
                    },
                },
            };

            // The planet turns in about ten minutes, with a tilt for seasons over its hour-long year.
            var planet = system.bodies[1];
            planet.spinPeriod = rnd.NextDouble(480, 720);
            planet.spinPhase = rnd.NextDouble(0, 2 * math.PI_DBL);
            planet.tilt = rnd.NextFloat(0.2f, 0.45f);
            // The moon is tidally locked: it turns once per orbit, in the plane of that orbit, so one face
            // always looks at the planet.
            var moon = system.bodies[2];
            moon.spinPeriod = moon.orbit.period;
            moon.spinPhase = moon.orbit.phase;
            moon.tilt = moon.orbit.inclination;
            return system;
        }
    }
}
