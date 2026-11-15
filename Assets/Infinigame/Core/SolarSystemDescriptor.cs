using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Infinigame.Core
{
    // Speeds the brief's lap times depend on. BriefTests holds generation to them.
    public static class Tuning
    {
        public const float WalkSpeed = 8f, SprintSpeed = 12f, BikeSpeed = 28f, ShipCruiseSpeed = 150f, ShipSpaceSpeed = 600f;
    }

    // A station is something built: it has a frame and a place on the map like any world, but no ground.
    public enum BodyKind { Sun, Rock, Gas, Station, Gate }

    // An orbit on rails around a parent body: a pure function of time. It is an ellipse with the
    // parent at one focus, swept faster near the parent than far from it as Kepler found, in a
    // plane of its own tilted against the plane the home world circles in.
    [Serializable]
    public struct Orbit
    {
        public int parent; // body index, -1 for a body that does not move
        // radius is the mean distance (the ellipse's semi-major axis); a negative period goes round
        // backwards; phase is how far round it starts, as an angle that grows evenly with time
        public double radius, period, phase;
        // how far from a circle: 0 is one, and the distance swings between radius x (1 - e) and radius x (1 + e)
        public float eccentricity;
        // The plane: how steeply it is tilted, which way the tilt faces (an angle about +y) and how
        // far round from there the closest approach lies.
        public float inclination, node, periapsis;

        public double Nearest => radius * (1 - eccentricity);
        public double Furthest => radius * (1 + eccentricity);

        public double3 Position(double time) => parent < 0 ? double3.zero : Place(time, false);

        public double3 Velocity(double time) => parent < 0 ? double3.zero : Place(time, true);

        double3 Place(double time, bool velocity)
        {
            // Kepler's equation, by Newton's method: where on the ellipse an evenly growing angle has got to
            double rate = 2 * math.PI_DBL / period, mean = math.fmod(phase + rate * time, 2 * math.PI_DBL), e = eccentricity, around = mean + e * math.sin(mean);
            for (int i = 0; i < 6; i++) around -= (around - e * math.sin(around) - mean) / (1 - e * math.cos(around));
            double flat = math.sqrt(1 - e * e), turning = rate / (1 - e * math.cos(around));
            // in the orbit's own plane, measured from its closest approach
            double2 at = velocity ? radius * turning * new double2(-math.sin(around), flat * math.cos(around)) : radius * new double2(math.cos(around) - e, flat * math.sin(around));
            math.sincos((double)periapsis, out double sinP, out double cosP);
            double x = at.x * cosP - at.y * sinP, z = at.x * sinP + at.y * cosP;
            // tilted about the x axis, then swung about y to face the way the plane does
            math.sincos((double)inclination, out double sinI, out double cosI);
            math.sincos((double)node, out double sinN, out double cosN);
            double y = -z * sinI;
            z *= cosI;
            return new double3(x * cosN + z * sinN, y, z * cosN - x * sinN);
        }
    }

    [Serializable]
    public class BodyDescriptor
    {
        public string name, summary;
        public bool destroyed;
        public float blight;
        public BodyKind kind;
        public float radius, surfaceGravity, soiRadius;
        public float hue; // a star's colour, 0 to 1
        // The colour of bare ground, or of a gas giant's cloud tops. Worlds with seas are grassed over instead.
        public float3 ground;
        // Air: how thick it is next to the home world's, zero for none, and its colour by day and at dusk.
        public float air;
        public float3 sky, dusk;
        // The share of sky under cloud, or how boldly a gas giant is banded.
        public float cloud;
        // Rings in the equatorial plane, as distances from the centre, and how solid they look. Zero is none.
        // A star's ring is its asteroid belt.
        public float ringInner, ringOuter, ringDensity;
        // The radius of the biggest rocks in the rings and half the thickness of their sheet, in metres.
        public float ringRock = 7f, ringThick = 12f;
        // Seconds for the rings to turn once as a whole, for a belt whose rocks keep station with each
        // other. Zero leaves every band to circle at the speed of an orbit at its own distance.
        public double ringPeriod;
        // How warm it is: the mean at the surface or the cloud tops in kelvin, and how far midday and
        // midnight swing either side of it.
        public float heat, swing;
        // How far from the sun the real world this one stands for is, in astronomical units. Sunlight is
        // worked out from that, because the game's own distances are far too small to be to scale.
        public float au;
        public Orbit orbit;
        public TerrainParams terrain;
        // The volcanoes that stand on its ground, if it has any.
        public Volcano[] volcanoes = Array.Empty<Volcano>();
        // One turn every spinPeriod seconds, in the same sense as the orbits, about an axis tilted from +y
        // and leaning the way heading says (an angle about +y). Zero is no spin and a negative period
        // turns backwards.
        public double spinPeriod, spinPhase;
        public float tilt, heading;

        // What blasts have made of the ground since the system was built, oldest first: craters,
        // and the pads that landing bombs level. Replaced whole, never changed in place, so other
        // threads can read it.
        [NonSerialized] public Blast[] blasts = Array.Empty<Blast>();
        // Where each of those is: a direction from the centre and a radius in metres. Replaced whole too.
        [NonSerialized] public float4[] craters = Array.Empty<float4>();

        // How the axis is set: +y tilted over, then swung round to its heading.
        quaternion Axis => math.mul(quaternion.RotateY(heading), quaternion.RotateX(tilt));

        public quaternion Rotation(double time) => spinPeriod == 0 ? quaternion.identity : math.mul(Axis,
            quaternion.RotateY(-(float)math.fmod(spinPhase + 2 * math.PI_DBL * time / spinPeriod, 2 * math.PI_DBL)));

        // Angular velocity of the spin, in the inertial frame.
        public float3 Spin => spinPeriod == 0 ? float3.zero : math.rotate(Axis, new float3(0, -(float)(2 * math.PI_DBL / spinPeriod), 0));

        public float SeaRadius => terrain.seaDepth > 0f ? radius : 0f;

        // The ground, a giant's cloud tops, or the solid heart of a station inside its spires.
        // natural is the ground as it was made, whatever has been blasted out of it since.
        public float GroundRadius(float3 direction, bool natural = false)
        {
            if (kind == BodyKind.Station || kind == BodyKind.Gate) return 0f;
            float height = TerrainNoise.Height(direction, terrain);
            // x: the cone that stands here, y: how far it has smoothed away the land under it
            float2 cone = 0f;
            foreach (var volcano in volcanoes) cone = math.max(cone, TerrainNoise.Cone(direction, volcano, radius, out _));
            height = height * (1f - cone.y) + cone.x;
            if (natural) return radius + height;
            var left = blasts;
            float pool = float.MinValue;
            float2 flat = 0f;
            for (int i = 0; i < left.Length; i++) TerrainNoise.Shape(direction, left[i], radius, ref height, ref pool, ref flat);
            return radius + math.lerp(math.max(height, pool), flat.x, flat.y);
        }

        // What a blast leaves of the ground at a spot. The sea boils wherever it covers the ground;
        // the rest goes by what the ground was made of, so a second blast does as the first did.
        public Scar Blasted(float3 direction) => GroundRadius(direction) < SeaRadius ? Scar.Steam : Scan.Terrain(this, direction, true) switch
        {
            "snow" or "polar ice" => Scar.Melt,
            "cliff rock" or "mountain rock" or "bare rock face" or "volcano flank" or "caldera floor" or "lava lake" => Scar.Lava,
            _ => Scar.Char,
        };

        // Blows a crater this many metres across its radius, at this time on the clock of whoever
        // digs it. Only ground can be dug, and only so many times.
        public void Dig(float3 direction, float size, float now = 0f)
        {
            if (kind != BodyKind.Rock) return;
            direction = math.normalize(direction);
            var blast = new Blast { crater = new float4(direction, size), scar = Blasted(direction), born = now, level = float.MinValue };
            float3 east = math.normalize(math.cross(direction, math.abs(direction.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0))), north = math.cross(direction, east);
            float centre = GroundRadius(direction) - radius;
            // the lie of the land in rings round the middle, each this far out as a share of the crater's radius
            for (float ring = blast.scar == Scar.Melt ? 0.5f : blast.scar == Scar.Lava ? 0.35f : 2f; ring <= (blast.scar == Scar.Lava ? 0.65f : 1f); ring += blast.scar == Scar.Lava ? 0.1f : 0.25f)
            {
                float lowest = float.MaxValue;
                for (int i = 0; i < 8; i++)
                {
                    math.sincos(i * math.PI / 4f, out float sine, out float cosine);
                    float3 outward = east * cosine + north * sine, at = math.normalize(direction + outward * (ring * size / radius));
                    float ground = GroundRadius(at) - radius, none = 0f;
                    float2 flat = 0f;
                    // molten rock runs the way the land falls from where the rim will be
                    if (ring == 1f) blast.downhill -= outward * ((ground - centre) / (4f * size));
                    TerrainNoise.Shape(at, blast, radius, ref ground, ref none, ref flat);
                    lowest = math.min(lowest, ground);
                }
                // Meltwater stands in as much of the bowl as can hold it: out to whichever ring is
                // highest at its lowest point, and no higher than that or it would spill.
                if (blast.scar == Scar.Melt && lowest - 0.02f * size > blast.level) (blast.level, blast.pool) = (math.min(lowest - 0.02f * size, centre - 0.18f * size), ring);
                if (blast.scar == Scar.Lava && lowest - 0.04f * size > blast.level) (blast.level, blast.pool) = (math.min(lowest - 0.04f * size, centre - 0.25f * size), ring);
            }
            Leave(blast);
        }

        // Levels a disc of ground this many metres in radius at the height it has at its centre,
        // though never below the sea, and chars it as a blast would: somewhere to set a ship down.
        // It blends into the ground round it, and nothing dug before or after disturbs it.
        public void Level(float3 direction, float size)
        {
            if (kind != BodyKind.Rock) return;
            direction = math.normalize(direction);
            // ground that a pad as big has already levelled needs no more
            foreach (var old in blasts)
                if (old.scar == Scar.Pad && math.distance(direction, old.crater.xyz) * radius + size <= old.crater.w)
                    return;
            Leave(new Blast { crater = new float4(direction, size), scar = Scar.Pad, level = math.max(GroundRadius(direction), SeaRadius + 1f) - radius });
        }

        // Only so many blasts are remembered: the oldest pad goes when there are too many of those,
        // the oldest crater when there is too much of everything.
        void Leave(Blast blast)
        {
            var left = new List<Blast>(blasts) { blast };
            bool pads = left.FindAll(old => old.scar == Scar.Pad).Count > MaxPads;
            if (pads || left.Count > MaxCraters) left.RemoveAt(left.FindIndex(old => old.scar == Scar.Pad == pads));
            blasts = left.ToArray();
            craters = Array.ConvertAll(blasts, old => old.crater);
        }

        public const int MaxCraters = 256, MaxPads = 32;

        public float3 GroundNormal(float3 direction, bool natural = false)
        {
            float3 a = math.normalize(math.cross(direction, math.abs(direction.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0)));
            float3 b = math.cross(direction, a), da = math.normalize(direction + a * (0.5f / radius)), db = math.normalize(direction + b * (0.5f / radius));
            float3 origin = direction * GroundRadius(direction, natural);
            float3 normal = math.normalize(math.cross(da * GroundRadius(da, natural) - origin, db * GroundRadius(db, natural) - origin));
            return math.dot(normal, direction) < 0f ? -normal : normal;
        }

        // How deep in the woods a spot is: Scatter grows trees where this runs above -0.15.
        float Woods(float3 direction) => noise.snoise(direction * 9f + terrain.offset);

        // A gentle spot above the waterline to stand or park on. On a living world it is out in
        // the open, well clear of the woods, with somewhere to camp beside it.
        public float3 FindLanding(uint seed)
        {
            var rnd = new Random(math.max(1u, seed));
            for (int i = 0; i < 5000; i++)
            {
                float3 direction = rnd.NextFloat3Direction();
                float height = GroundRadius(direction) - radius;
                if ((SeaRadius == 0f || (height > 3f && height < 30f && Woods(direction) < -0.35f && !FindCamp(direction).Equals(direction)))
                    && math.dot(GroundNormal(direction), direction) > 0.98f)
                    return direction;
            }
            return new float3(0, 1, 0);
        }

        // Somewhere to pitch a camp: level, dry, open ground some twenty paces from the landing
        // site or as little further as it takes, clear of where the ship is parked.
        public float3 FindCamp(float3 landing)
        {
            float3 ahead = math.normalize(math.cross(landing, new float3(0.2f, 0.9f, 0.4f)));
            for (int i = 0; i < 40; i++)
            {
                float3 direction = math.normalize(landing + math.rotate(quaternion.AxisAngle(landing, 2.2f + 0.35f * i), ahead) * ((22f + 3f * i) / radius));
                if (GroundRadius(direction) - radius >= 2.5f && math.dot(GroundNormal(direction), direction) >= 0.95f && Woods(direction) < -0.27f) return direction;
            }
            return landing;
        }

        // The nearest dry, gentle ground to a spot: somewhere to build on.
        public float3 FindSite(float3 near)
        {
            float3 across = math.normalize(math.cross(near, new float3(0.2f, 0.9f, 0.4f)));
            for (int i = 0; i < 1500; i++)
            {
                // an ever wider spiral of tries about the spot
                float3 direction = math.normalize(near + math.rotate(quaternion.AxisAngle(near, 2.4f * i), across) * (12f * math.sqrt(i) / radius));
                if ((SeaRadius == 0f || GroundRadius(direction) - radius > 3f) && math.dot(GroundNormal(direction), direction) > 0.95f) return direction;
            }
            return FindLanding(1);
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
        internal readonly Dictionary<int, float3> missionSites = new Dictionary<int, float3>();
        public uint seed;
        public BodyDescriptor[] bodies;
        public Moonfall moonfall;
        public bool generated;

        public double3 PositionAt(int body, double time) => moonfall != null && moonfall.active && body == moonfall.moon ? moonfall.Position(this, time) : OrbitalPosition(body, time);

        public double3 OrbitalPosition(int body, double time)
        {
            double3 p = double3.zero;
            foreach (var star in bodies) if (star.kind == BodyKind.Sun && star.orbit.parent == 0) p -= star.orbit.Position(time) * 0.5;
            for (int b = body; b >= 0; b = bodies[b].orbit.parent) p += bodies[b].orbit.Position(time);
            return p;
        }

        public double3 VelocityAt(int body, double time)
        {
            if (moonfall != null && moonfall.active && body == moonfall.moon) return (PositionAt(body, time + 0.01) - PositionAt(body, time - 0.01)) / 0.02;
            double3 v = double3.zero;
            foreach (var star in bodies) if (star.kind == BodyKind.Sun && star.orbit.parent == 0) v -= star.orbit.Velocity(time) * 0.5;
            for (int b = body; b >= 0; b = bodies[b].orbit.parent) v += bodies[b].orbit.Velocity(time);
            return v;
        }

        // The innermost body whose sphere of influence holds the point. The current
        // reference gets a margin so a traveller on the boundary does not flicker.
        public int ReferenceBody(double3 position, double time, int current)
        {
            int best = 0;
            for (int i = 1; i < bodies.Length; i++)
                if (!bodies[i].destroyed && bodies[i].soiRadius < bodies[best].soiRadius
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

        // The home world and its moon are always these bodies.
        public const int Home = 3, Moon = 4;

        // The bodies that circle the star and are worlds: the planets, in order outwards.
        public IEnumerable<int> Planets
        {
            get
            {
                for (int i = 0; i < bodies.Length; i++)
                    if (bodies[i].orbit.parent == 0 && bodies[i].kind != BodyKind.Station && bodies[i].kind != BodyKind.Gate && bodies[i].kind != BodyKind.Sun)
                        yield return i;
            }
        }

        // The planet a body is or circles, and how many moons a body has.
        public int Planet(int body) => bodies[body].orbit.parent <= 0 ? body : bodies[body].orbit.parent;

        public int Moons(int body)
        {
            int moons = 0;
            foreach (var other in bodies)
                if (other.orbit.parent == body && body != 0)
                    moons++;
            return moons;
        }

        // Real worlds, with the Earth's radius as the unit: how big, how heavy at the surface
        // (m/s2), how far from the sun (au) and how warm (kelvin). The planets and their bigger moons
        // here keep these sizes relative to each other. Only the star does not: to scale it would be a
        // hundred times the home world across and fill the sky.
        const float Mercury = 0.383f, Venus = 0.949f, Mars = 0.532f, Jupiter = 11.21f, Saturn = 9.45f, Uranus = 4.01f, Neptune = 3.88f;

        // A small copy of our own solar system: four rocky worlds, an asteroid belt with a raider
        // base in it, then four giants, each with its moons. The home world and its moon stay inside
        // sizes that keep the brief's lap times, which is why that one moon is a little under scale.
        public static SolarSystemDescriptor Generate(uint seed)
        {
            var rnd = new Random(math.max(1u, math.hash(new uint2(seed, 0x9E3779B9u))));
            var bodies = new List<BodyDescriptor>();
            var names = new HashSet<string>();
            float home = rnd.NextFloat(850f, 1100f);
            // how far out the system is spoken for, starting well clear of the sun
            float star = 1.2f * Jupiter * home;
            double reach = 4.6 * star;

            // The worlds carry the names of the ones they are copies of. Whatever has no such name
            // is given a made-up one, and one is made up regardless so that every seed's system
            // stays the system it was before the worlds were named.
            BodyDescriptor Add(BodyDescriptor body, string name = null)
            {
                string made;
                do made = Name(ref rnd); while (!names.Add(made));
                body.name = name ?? made;
                body.terrain.offset = rnd.NextFloat3(-100f, 100f);
                bodies.Add(body);
                return body;
            }

            // Distances here are far too small to be to scale, and so is the year. The home world's
            // day, about ten minutes, is the clock: every other day and every month is the real one
            // counted in those days, so the moon takes 27 of them to go round. Every year is the real
            // one counted in home years, each of which is cut to Year days.
            double day = rnd.NextDouble(480, 720);

            // Each orbit keeps a gap beyond the last sphere of influence even at its closest, so no two
            // ever touch. Its shape and the tilt of its plane are the real planet's; which way the
            // plane faces is the system's own. A world's axis is tilted as the real one's is, in a
            // direction of its own.
            int Planet(string name, BodyDescriptor body, double gap, float eccentricity, float inclination, double years, double days, float tilt)
            {
                double orbit = (reach + gap + body.soiRadius) / (1 - eccentricity);
                reach = orbit * (1 + eccentricity) + body.soiRadius;
                body.orbit = new Orbit
                {
                    parent = 0, radius = orbit, period = day * Year * years, phase = rnd.NextDouble(0, 2 * math.PI_DBL),
                    eccentricity = eccentricity, inclination = math.radians(inclination), node = rnd.NextFloat(0f, 2f * math.PI), periapsis = rnd.NextFloat(0f, 2f * math.PI),
                };
                body.spinPeriod = day * days;
                body.spinPhase = rnd.NextDouble(0, 2 * math.PI_DBL);
                body.tilt = math.radians(tilt);
                body.heading = rnd.NextFloat(0f, 2f * math.PI);
                Add(body, name);
                return bodies.Count - 1;
            }

            // A moon takes its real month, in days, and keeps one face towards its planet as real moons
            // do: it turns once per orbit, about that orbit's own axis. A negative month goes round
            // backwards. Most moons circle over their planet's equator, in the plane of its rings; lean
            // is how far off it this one's orbit is tipped, in degrees.
            void Satellite(int planet, string name, string summary, float size, float gravity, double orbit, double month, float3 colour, float heat, float swing, TerrainParams terrain,
                float eccentricity = 0f, float lean = 0f)
            {
                var parent = bodies[planet];
                var moon = Add(new BodyDescriptor
                {
                    summary = summary, kind = BodyKind.Rock, radius = size, surfaceGravity = gravity, soiRadius = 3f * size, ground = colour,
                    heat = heat, swing = swing, au = parent.au, terrain = terrain,
                    orbit = new Orbit
                    {
                        parent = planet, radius = orbit, period = day * month, phase = rnd.NextDouble(0, 2 * math.PI_DBL), eccentricity = eccentricity,
                        inclination = parent.tilt + math.radians(lean), node = parent.heading, periapsis = rnd.NextFloat(0f, 2f * math.PI),
                    },
                }, name);
                moon.spinPeriod = moon.orbit.period;
                moon.spinPhase = moon.orbit.phase + moon.orbit.periapsis;
                moon.tilt = moon.orbit.inclination;
                moon.heading = moon.orbit.node;
            }

            // Volcanoes are dealt by a generator of their own, so that every seed's system is otherwise
            // the one it was before it had any. Each is a share of its world's radius across, the
            // first the biggest, and aspect times that tall, clear of any polar ice and of the others.
            // It stands a little short of the tallest the world's terrain allows for.
            var fire = new Random(math.max(1u, math.hash(new uint2(seed, 0x51ED270Bu))));
            void Erupt(BodyDescriptor body, int count, float2 across, float aspect, Volcano shape, float active)
            {
                var dealt = new List<Volcano>();
                for (int tries = 0; dealt.Count < count && tries < 2000; tries++)
                {
                    var volcano = shape;
                    volcano.direction = fire.NextFloat3Direction();
                    volcano.radius = body.radius * (dealt.Count == 0 ? across.y : fire.NextFloat(across.x, math.lerp(across.x, across.y, 0.7f)));
                    volcano.height = math.min(aspect * volcano.radius, 0.9f * body.terrain.volcanoHeight);
                    volcano.lava = fire.NextFloat() < active ? 1f : 0f;
                    if ((body.terrain.polarCap == 0f || math.abs(volcano.direction.y) < 0.55f)
                        && !dealt.Exists(other => math.distance(volcano.direction, other.direction) * body.radius < 0.9f * (volcano.radius + other.radius)))
                        dealt.Add(volcano);
                }
                body.volcanoes = dealt.ToArray();
            }

            // Cratered and airless, like most moons: the bigger the craters, the older the face.
            TerrainParams Cratered(float size, float relief, float craters) => new TerrainParams
            {
                hillFreq = 4f, hillAmp = 0.04f * relief * size, mountainFreq = 2.2f, mountainAmp = 0.14f * relief * size,
                craterFreq = rnd.NextFloat(1.6f, 2.6f), craterDepth = 0.11f * craters * size,
            };

            Add(new BodyDescriptor
            {
                summary = "the star at the heart of the system", kind = BodyKind.Sun, radius = star, surfaceGravity = 20f, soiRadius = 1e9f,
                hue = rnd.NextFloat(), heat = Climate.Photosphere, au = Climate.SunRadius, orbit = new Orbit { parent = -1 },
            }, "Sol");

            float size = home * Mercury;
            Planet("Mercury", new BodyDescriptor
            {
                summary = "a scorched, cratered rock close to its star", kind = BodyKind.Rock, radius = size,
                surfaceGravity = 3.7f, soiRadius = 3f * size, ground = new float3(0.62f, 0.57f, 0.52f), heat = 400f, swing = 300f, au = 0.387f,
                terrain = new TerrainParams
                {
                    hillFreq = 4f, hillAmp = 0.03f * size, mountainFreq = 2.4f, mountainAmp = 0.08f * size,
                    craterFreq = rnd.NextFloat(1.8f, 2.6f), craterDepth = 0.09f * size,
                },
                // the star's pull has locked its spin to its orbit: three turns to every two years
            }, rnd.NextDouble(1500, 2500), 0.206f, 7f, 0.241, 0.241 * Year * 2 / 3, 0.03f);

            size = home * Venus;
            // it turns backwards, and so slowly that its day outlasts its year
            int venus = Planet("Venus", new BodyDescriptor
            {
                summary = "a furnace world of volcanic rock under an unbroken yellow shroud", kind = BodyKind.Rock, radius = size,
                surfaceGravity = 8.9f, soiRadius = 3f * size, ground = new float3(0.8f, 0.5f, 0.28f), heat = 737f, au = 0.723f,
                air = 3f, sky = new float3(0.95f, 0.72f, 0.35f), dusk = new float3(0.9f, 0.35f, 0.1f), cloud = 1f,
                terrain = new TerrainParams { hillFreq = 5f, hillAmp = 0.02f * size, mountainFreq = 2f, mountainAmp = 0.09f * size, volcanoHeight = 0.085f * size },
            }, rnd.NextDouble(3000, 4000), 0.007f, 3.4f, 0.615, -0.615 * Year * 1.08, 2.6f);
            // broad low shields, no higher than its ranges and far under the cloud, a few of them still alight
            Erupt(bodies[venus], 7, new float2(0.2f, 0.3f), 0.24f, new Volcano { caldera = 0.2f, scarp = 0.12f, steepening = 1f }, 0.35f);

            // The moon would be 0.27 of the home world across. The brief wants it walked round in five
            // minutes, which holds it to about a fifth.
            float moon = rnd.NextFloat(185f, 200f);
            double moonOrbit = rnd.NextDouble(3500, 5000);
            float riverFreq = rnd.NextFloat(2f, 3.2f);
            // Its orbit is the plane every other is measured against. It turns in about ten minutes, with a tilt for seasons over its year.
            Planet("Earth", new BodyDescriptor
            {
                summary = "a living world of seas, forests and snow-capped mountains", kind = BodyKind.Rock, radius = home,
                surfaceGravity = 9.8f, soiRadius = (float)moonOrbit * 1.055f + 3f * moon + 900f, heat = 288f, swing = 8f, au = 1f,
                air = 1f, sky = new float3(0.25f, 0.5f, 1f), dusk = new float3(1f, 0.45f, 0.2f), cloud = 0.48f,
                terrain = new TerrainParams
                {
                    seaDepth = 0.1f * home, seaBias = rnd.NextFloat(-0.12f, 0.05f),
                    continentFreq = rnd.NextFloat(0.9f, 1.5f), warp = 0.35f,
                    plains = 0.012f * home, hillFreq = rnd.NextFloat(4f, 7f), hillAmp = 0.02f * home,
                    mountainFreq = rnd.NextFloat(1.8f, 2.8f), mountainAmp = rnd.NextFloat(0.2f, 0.28f) * home,
                    riverFreq = riverFreq, riverWidth = 0.045f, riverBank = 1.6f * riverFreq / home, riverDepth = 0.005f * home,
                    polarCap = 0.06f * home,
                },
            }, rnd.NextDouble(3000, 4000), 0.017f, 0f, 1, 1, 23.4f);
            // unlike most moons it keeps near the plane of the planets, not over its own planet's equator
            Satellite(Home, "Luna", "an airless grey moon, cratered all over", moon, 1.62f, moonOrbit, 27.32, new float3(0.6f, 0.6f, 0.6f), 250f, 140f, Cratered(moon, 0.7f, 0.75f), 0.055f, -18.3f);

            size = home * Mars;
            // low ranges, and ice caps wide enough to bury the polar plains. Its sunsets are blue.
            int mars = Planet("Mars", new BodyDescriptor
            {
                summary = "a cold rust-red desert of giant volcanoes, with ice at its poles", kind = BodyKind.Rock, radius = size,
                surfaceGravity = 3.7f, soiRadius = 5.2f * size, ground = new float3(0.72f, 0.36f, 0.2f), heat = 210f, swing = 60f, au = 1.524f,
                air = 0.3f, sky = new float3(0.8f, 0.6f, 0.45f), dusk = new float3(0.35f, 0.55f, 0.9f),
                terrain = new TerrainParams
                {
                    hillFreq = 5f, hillAmp = 0.025f * size, mountainFreq = 1.2f, mountainAmp = 0.1f * size,
                    craterFreq = 3f, craterDepth = 0.02f * size, polarCap = 0.21f * size, volcanoHeight = 0.17f * size,
                },
            }, rnd.NextDouble(2000, 3000), 0.093f, 1.85f, 1.881, 1.03, 25.2f);
            // A few vast shield volcanoes, long dead, each ringed by a cliff and far taller than anything
            // else on the planet, though short of the height where its ice begins.
            Erupt(bodies[mars], 4, new float2(0.28f, 0.46f), 0.33f, new Volcano { caldera = 0.17f, scarp = 0.15f, steepening = 1f }, 0f);
            // Its two moons are captured rubble a few metres across at this scale, drawn large enough to
            // find and land on. The inner one laps the planet three times a day, so it rises in the west.
            Satellite(mars, "Phobos", "a dark lump of captured rubble, barely a moon", 46f, 1f, 2.6 * size, 0.319, new float3(0.34f, 0.31f, 0.29f), 233f, 60f, Cratered(46f, 4f, 1.6f), 0.015f, 1.1f);
            Satellite(mars, "Deimos", "a smaller, smoother lump of captured rubble", 32f, 1f, 3.9 * size, 1.263, new float3(0.42f, 0.39f, 0.36f), 233f, 60f, Cratered(32f, 3f, 0.8f));

            // The asteroid belt circles the star between the rocky worlds and the giants, turning as one
            // so that its rocks keep station with each other and with the raiders' base among them.
            double beltInner = reach + 4000, beltOuter = beltInner + 22000, beltYear = day * Year * 4.6;
            bodies[0].ringInner = (float)beltInner;
            bodies[0].ringOuter = (float)beltOuter;
            bodies[0].ringDensity = 0.3f;
            bodies[0].ringRock = 90f;
            bodies[0].ringThick = 450f;
            bodies[0].ringPeriod = beltYear;
            Add(new BodyDescriptor
            {
                summary = "the raiders' base: a spiked iron citadel hanging in the asteroid belt", kind = BodyKind.Station, radius = 260f, soiRadius = 2600f,
                ground = new float3(0.08f, 0.07f, 0.08f), au = 2.8f,
                orbit = new Orbit { parent = 0, radius = (beltInner + beltOuter) / 2, period = beltYear, phase = rnd.NextDouble(0, 2 * math.PI_DBL) },
            });
            reach = beltOuter + 4000;

            // Giants have no ground: their radius is the cloud tops, and below those it only gets hotter.
            int Giant(string name, string summary, float scale, float gravity, float3 clouds, float banding, float eccentricity, float inclination, double years, double days, float tilt,
                float influence, float heat, float au, float ringInner = 0f, float ringOuter = 0f, float ringDensity = 0f)
            {
                size = home * scale;
                return Planet(name, new BodyDescriptor
                {
                    summary = summary, kind = BodyKind.Gas, radius = size, surfaceGravity = gravity, soiRadius = influence * size, heat = heat, au = au,
                    ground = clouds, air = 2f, sky = math.lerp(clouds, 1f, 0.4f), dusk = clouds * 0.7f, cloud = banding,
                    ringInner = ringInner * size, ringOuter = ringOuter * size, ringDensity = ringDensity,
                }, rnd.NextDouble(4000, 6000), eccentricity, inclination, years, days, tilt);
            }

            int giant = Giant("Jupiter", "a banded gas giant with a great storm, king of the planets", Jupiter, 24.8f, Hsv(rnd.NextFloat(0.06f, 0.1f), 0.5f, 0.9f), 1f,
                0.049f, 1.3f, 11.86, 0.414, 3.1f, 8.2f, 165f, 5.2f);
            float r = bodies[giant].radius;
            Satellite(giant, "Io", "a sulphur-yellow moon of restless volcanoes", 0.286f * home, 1.8f, 3.2 * r, 1.769, new float3(0.85f, 0.75f, 0.3f), 110f, 20f,
                new TerrainParams { hillFreq = 6f, hillAmp = 0.012f * home, mountainFreq = 3f, mountainAmp = 0.02f * home, volcanoHeight = 0.036f * home });
            // many small steep cones over its sulphur plains, most of them with lava in their wide calderas
            Erupt(bodies[bodies.Count - 1], 16, new float2(0.11f, 0.2f), 0.55f, new Volcano { caldera = 0.4f, scarp = 0f, steepening = 1.6f }, 0.65f);
            Satellite(giant, "Europa", "a smooth white moon of cracked ice over a hidden ocean", 0.245f * home, 1.3f, 4.3 * r, 3.551, new float3(0.9f, 0.9f, 0.88f), 102f, 20f,
                new TerrainParams { hillFreq = 9f, hillAmp = 0.006f * home, mountainFreq = 5f, mountainAmp = 0.008f * home }, 0.009f);
            Satellite(giant, "Ganymede", "the largest moon there is, grooved ice and dark old ground", 0.413f * home, 1.43f, 5.6 * r, 7.155, new float3(0.55f, 0.52f, 0.48f), 110f, 30f, Cratered(0.413f * home, 0.5f, 0.5f));
            Satellite(giant, "Callisto", "a dark moon cratered from pole to pole", 0.378f * home, 1.24f, 7.2 * r, 16.69, new float3(0.36f, 0.33f, 0.3f), 134f, 30f, Cratered(0.378f * home, 0.4f, 1.1f), 0.007f);

            giant = Giant("Saturn", "a pale gold gas giant circled by broad bright rings", Saturn, 10.4f, Hsv(rnd.NextFloat(0.1f, 0.13f), 0.4f, 0.95f), 0.55f,
                0.057f, 2.5f, 29.46, 0.444, 26.7f, 9f, 134f, 9.58f, 1.25f, 2.3f, 0.9f);
            r = bodies[giant].radius;
            Satellite(giant, "Mimas", "a small icy moon with one enormous crater", 0.031f * home, 0.6f, 2.8 * r, 0.942, new float3(0.8f, 0.8f, 0.8f), 64f, 10f, Cratered(0.031f * home, 2f, 2.2f), 0.02f, 1.6f);
            Satellite(giant, "Enceladus", "a brilliant white moon that vents ice into space", 0.04f * home, 0.6f, 3.3 * r, 1.37, new float3(0.95f, 0.96f, 1f), 75f, 10f, Cratered(0.04f * home, 1f, 0.3f));
            Satellite(giant, "Tethys", "an ice moon split by a great canyon", 0.083f * home, 0.6f, 3.9 * r, 1.888, new float3(0.82f, 0.82f, 0.8f), 86f, 10f, Cratered(0.083f * home, 1f, 1f));
            Satellite(giant, "Dione", "an ice moon streaked with bright cliffs", 0.088f * home, 0.6f, 4.6 * r, 2.737, new float3(0.78f, 0.78f, 0.76f), 87f, 10f, Cratered(0.088f * home, 0.8f, 0.9f));
            Satellite(giant, "Rhea", "a heavily cratered ball of dirty ice", 0.12f * home, 0.6f, 5.4 * r, 4.518, new float3(0.7f, 0.69f, 0.67f), 76f, 20f, Cratered(0.12f * home, 0.6f, 1.2f));
            Satellite(giant, "Titan", "a great moon with thick orange air and lakes of methane", 0.404f * home, 1.35f, 6.6 * r, 15.95, new float3(0.6f, 0.42f, 0.2f), 94f, 2f,
                new TerrainParams { hillFreq = 5f, hillAmp = 0.01f * home, mountainFreq = 2f, mountainAmp = 0.03f * home }, 0.029f);
            var hazy = bodies[bodies.Count - 1];
            hazy.air = 2.5f;
            hazy.sky = new float3(0.85f, 0.55f, 0.2f);
            hazy.dusk = new float3(0.5f, 0.3f, 0.12f);
            hazy.cloud = 1f;
            Satellite(giant, "Iapetus", "a two-faced moon, one side bright and one dark", 0.115f * home, 0.6f, 8 * r, 79.32, new float3(0.5f, 0.45f, 0.4f), 110f, 30f, Cratered(0.115f * home, 1.4f, 1f), 0.028f, 15.5f);

            // it lies on its side and turns backwards
            giant = Giant("Uranus", "a pale cyan ice giant tipped on its side, with thin dark rings", Uranus, 8.9f, Hsv(rnd.NextFloat(0.48f, 0.52f), 0.4f, 0.92f), 0.15f,
                0.046f, 0.77f, 84.0, -0.718, 82.2f, 7.6f, 76f, 19.2f, 1.64f, 2f, 0.3f);
            r = bodies[giant].radius;
            Satellite(giant, "Miranda", "a small moon of jumbled cliffs, as if broken and remade", 0.037f * home, 0.6f, 2.6 * r, 1.413, new float3(0.6f, 0.6f, 0.62f), 60f, 10f, Cratered(0.037f * home, 5f, 1f), 0f, 4.3f);
            Satellite(giant, "Ariel", "a bright moon cut by rift valleys", 0.091f * home, 0.6f, 3.4 * r, 2.52, new float3(0.62f, 0.62f, 0.64f), 60f, 10f, Cratered(0.091f * home, 1.5f, 0.6f));
            Satellite(giant, "Umbriel", "a dark, ancient, cratered moon", 0.092f * home, 0.6f, 4.3 * r, 4.144, new float3(0.34f, 0.34f, 0.36f), 75f, 10f, Cratered(0.092f * home, 0.6f, 1.2f));
            Satellite(giant, "Titania", "a moon of faults and frost-floored canyons", 0.124f * home, 0.6f, 5.4 * r, 8.706, new float3(0.55f, 0.53f, 0.52f), 70f, 15f, Cratered(0.124f * home, 1.2f, 0.8f));
            Satellite(giant, "Oberon", "a reddish moon with one lone mountain", 0.119f * home, 0.6f, 6.6 * r, 13.46, new float3(0.5f, 0.42f, 0.4f), 75f, 15f, Cratered(0.119f * home, 0.8f, 1.1f));

            giant = Giant("Neptune", "a deep blue ice giant of fast winds, the last world out", Neptune, 11.2f, Hsv(rnd.NextFloat(0.6f, 0.64f), 0.7f, 0.85f), 0.4f,
                0.01f, 1.77f, 164.8, 0.671, 28.3f, 6f, 72f, 30.05f);
            r = bodies[giant].radius;
            Satellite(giant, "Proteus", "a dark, lumpy inner moon", 0.033f * home, 0.6f, 2.6 * r, 1.122, new float3(0.3f, 0.3f, 0.32f), 51f, 5f, Cratered(0.033f * home, 3f, 1.5f));
            // it circles the wrong way, a captured wanderer
            Satellite(giant, "Triton", "a pink moon of nitrogen frost and geysers, circling backwards", 0.212f * home, 0.78f, 4.5 * r, -5.877, new float3(0.8f, 0.68f, 0.66f), 38f, 4f,
                new TerrainParams { hillFreq = 7f, hillAmp = 0.012f * home, mountainFreq = 3f, mountainAmp = 0.015f * home, craterFreq = 3f, craterDepth = 0.004f * home }, 0f, 23f);

            return new SolarSystemDescriptor { seed = seed, bodies = bodies.ToArray() };
        }

        // A separate stream leaves the original system's random draws untouched.
        public static SolarSystemDescriptor Beyond(uint seed)
        {
            var rnd = new Random(math.hash(new uint2(seed, 0x7531u)) | 1u);
            var original = Generate(seed);
            var bodies = new List<BodyDescriptor> { original.bodies[0] };
            var names = new HashSet<string>();
            double reach = 100000;
            BodyDescriptor Rock(float radius, bool living) => new BodyDescriptor
            {
                kind = BodyKind.Rock, radius = radius, surfaceGravity = math.max(0.6f, radius / 100f), soiRadius = radius * 5,
                heat = living ? 288 : rnd.NextFloat(40, 650), swing = living ? 12 : 50,
                ground = rnd.NextFloat3(0.2f, 0.8f), sky = new float3(0.3f, 0.6f, 0.9f), dusk = new float3(1, 0.3f, 0.1f),
                air = living ? 1 : 0, cloud = living ? 0.45f : 0, spinPeriod = rnd.NextFloat(480, 900),
                terrain = new TerrainParams { hillFreq = 6, hillAmp = radius * 0.025f, mountainFreq = 3, mountainAmp = radius * 0.06f,
                    seaDepth = living ? radius * 0.02f : 0, offset = rnd.NextFloat3(-100, 100) },
            };
            void Planet(BodyDescriptor body)
            {
                reach += body.soiRadius * 2 + rnd.NextDouble(16000, 40000);
                body.au = (float)(reach / 180000);
                body.orbit = new Orbit { parent = 0, radius = reach, period = reach * 0.08, phase = rnd.NextDouble(0, 6.28), eccentricity = 0.02f };
                bodies.Add(body);
            }
            Planet(Rock(rnd.NextFloat(350, 600), false));
            Planet(Rock(rnd.NextFloat(400, 900), rnd.NextBool()));
            Planet(Rock(rnd.NextFloat(850, 1100), true));
            void Moon(int parent, bool living)
            {
                var moon = Rock(rnd.NextFloat(110, 230), living);
                moon.orbit = new Orbit { parent = parent, radius = bodies[parent].radius * (3.2 + (bodies.Count % 3) * 0.6), period = 1800 + bodies.Count * 170, phase = rnd.NextDouble(0, 6.28) };
                moon.soiRadius = moon.radius * 1.8f;
                bodies.Add(moon);
            }
            Moon(Home, true);
            int count = rnd.NextInt(1, 8);
            for (int i = 0; i < count; i++)
            {
                var planet = Rock(rnd.NextFloat(300, 1000), rnd.NextBool());
                if (rnd.NextBool())
                {
                    planet.kind = BodyKind.Gas;
                    planet.radius *= 5;
                    planet.soiRadius = planet.radius * 8;
                    planet.terrain = default;
                    planet.air = 2;
                    planet.cloud = 0.6f;
                    planet.ringInner = planet.radius * 1.4f;
                    planet.ringOuter = planet.radius * 2.1f;
                    planet.ringDensity = 0.6f;
                }
                Planet(planet);
                int parent = bodies.Count - 1;
                int moons = rnd.NextInt(0, 4);
                for (int m = 0; m < moons; m++) Moon(parent, rnd.NextBool());
            }
            // The finale always has a small moon and an uninhabited target of its own.
            Planet(Rock(650, false));
            Moon(bodies.Count - 1, false);
            var station = Array.Find(original.bodies, body => body.kind == BodyKind.Station);
            station.orbit.radius = reach + 18000;
            bodies.Add(station);
            if (rnd.NextBool())
            {
                bodies.Add(new BodyDescriptor { kind = BodyKind.Sun, radius = bodies[0].radius * 0.7f, soiRadius = bodies[0].radius * 2,
                    hue = 0.6f, heat = 4800, orbit = new Orbit { parent = 0, radius = 45000, period = 1400, phase = math.PI } });
            }
            foreach (var body in bodies)
            {
                do body.name = Name(ref rnd); while (!names.Add(body.name));
                body.summary = body.kind == BodyKind.Sun ? "a star beyond the gate" : body.kind == BodyKind.Station ? "the presence's citadel" : body.SeaRadius > 0 ? "a living world beyond the gate" : "an uncharted world";
            }
            return new SolarSystemDescriptor { seed = seed, bodies = bodies.ToArray(), generated = true };
        }

        // Days in the home world's year. The real 365 would take sixty hours to play through; at
        // twelve, two hours or so, the planets can be seen to move and the seasons to turn.
        public const double Year = 12;

        // Two or three made-up syllables.
        public static string Name(ref Random rnd)
        {
            string[] starts = { "Ka", "Ve", "Tho", "Mi", "Su", "Ory", "Zel", "Bra", "Ny", "Qua", "Il", "Eso", "Dra", "Fen", "Hal", "Usk" };
            string[] middles = { "", "", "", "ra", "li", "to", "ne", "va", "shi", "du" };
            string[] ends = { "n", "s", "ra", "th", "lix", "mos", "nia", "ri", "x", "ro", "lune", "ta" };
            return starts[rnd.NextInt(starts.Length)] + middles[rnd.NextInt(middles.Length)] + ends[rnd.NextInt(ends.Length)];
        }

        public static float3 Hsv(float hue, float saturation, float value) =>
            math.lerp(1f, math.saturate(math.abs(math.frac(hue + new float3(1f, 2f / 3f, 1f / 3f)) * 6f - 3f) - 1f), saturation) * value;
    }
}
