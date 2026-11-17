using System.Collections.Generic;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Infinigame.Core
{
    // Raiders are ships that hunt the player's ship; drones hover over the ground and hunt the player
    // on foot; the base is the raiders' station, which shoots at whatever comes near.
    public enum FoeKind { Raider, Drone, Base, Mind, Sentry }
    public enum MotileCaste { Stalker, Breacher, Spitter }
    public enum Alert { Calm, Searching, Alarmed }

    // Position is relative to the body's centre, like the player's.
    public struct Foe
    {
        public FoeKind kind;
        public MotileCaste caste;
        public float3 position, velocity, forward, burrowOrigin, burrowTarget;
        // how big a target it is, as the radius of a ball
        public float health, cooldown, size, maximumHealth;
        // A raider fires in bursts, and breaks away after each pass before turning back.
        public int burst;
        public bool away, scattered, friendly, stationary, guardian, outerGuard, protectedCore;
        public Alert alert;
        public float suspicion, recoil, attack, shield, burrowing;
        public bool clever;
        public bool patrol;
        public uint seed, random;
    }

    // A shot in flight, the player's or a foe's.
    public struct Bolt
    {
        public float3 position, velocity;
        // homing is how fast it can turn towards a foe, in radians a second; blast is how wide a hole it leaves
        public float life, damage, homing, blast;
        // level: a landing charge, which leaves the ground flat where it bursts instead of holed
        public bool hostile, level, airOnly, robot;
    }

    // Something the player fires.
    public struct Weapon
    {
        public string name;
        public float speed, damage, period, spread, homing, blast, life;
        // how many bolts leave at once, and which sound it makes
        public int pellets, voice;
        // A firearm also has rounds in its magazine, seconds to change one, degrees that a shot
        // throws the aim up by and a widening of its spread with each shot fired in a hurry.
        public int clip;
        public float reload, kick, bloom, charge;
        public bool beam, rail, melee, sweep, airOnly;
        public float energy;
        // what has to have been won before it can be used
        public Gear needs;
    }

    // Where a bolt ended: against a foe, the player or the ground.
    public struct Strike
    {
        public float3 at;
        public float blast;
        // ground: it struck the ground or the sea. level: it was a landing charge. Otherwise it
        // struck something's hull, a foe's when foe says which.
        public bool ground, level, shield, hostile, robot;
        public int foe;
    }

    // Fighting, as plain arithmetic: how foes move and shoot, what a bolt hits and what a fall,
    // a crash or heat costs. Foes are tougher the further the game has gone.
    public static class Combat
    {
        public const float ShipRadius = 4f, PersonRadius = 0.9f;
        const float RaiderBolt = 420f, DroneBolt = 32f, TurretBolt = 520f, RaiderReach = 650f, DroneReach = 38f, TurretReach = 2200f, Hover = 1.8f;
        // A blast this wide is a nuclear one: the only kind the raiders' citadel cannot shrug off.
        public const float Nuclear = 50f;

        // The ship's weapons, each won from the raiders in its turn: quick cannons, missiles that
        // chase, a lance that hits like a hammer and torpedoes that crack worlds.
        public static readonly Weapon[] Guns =
        {
            new Weapon { name = "Pulse cannons", speed = 900f, damage = 12f, period = 0.09f, blast = 2.5f, life = 3f, pellets = 1, voice = 0, needs = Gear.Ship },
            new Weapon { name = "Seeker missiles", speed = 420f, damage = 90f, period = 2.8f, airOnly = true, homing = 2.4f, blast = 9f, life = 7f, pellets = 2, voice = 1, needs = Gear.Missiles },
            new Weapon { name = "Beam", beam = true, speed = 3500f, damage = 45f, period = 0f, blast = 4f, life = 1.2f, pellets = 2, voice = 2, needs = Gear.Lance },
            new Weapon { name = "Railgun", damage = 650, pellets = 2, period = 2.8f, charge = 0.95f, rail = true, voice = 5, needs = Gear.ShipRail },
            new Weapon { name = "Nuclear torpedoes", speed = 260f, damage = 800f, period = 8f, blast = 60f, life = 30f, pellets = 1, voice = 10, needs = Gear.Nukes },
        };

        public static readonly Weapon[] Arms =
        {
            new Weapon { name = "Powered fists", speed = 100, damage = 70, period = 0.45f, blast = 2f, life = 0.04f, pellets = 1, voice = 4, melee = true, needs = Gear.Suit },
            new Weapon { name = "Gauntlet beam", speed = 1000, damage = 42, period = 0, life = 0.25f, voice = 2, beam = true, energy = 24, needs = Gear.Beams },
            new Weapon { name = "Back missiles", speed = 240, damage = 70, period = 3.4f, airOnly = true, homing = 3f, blast = 8f, life = 9, pellets = 1, voice = 1, energy = 15, needs = Gear.BackMissiles },
            new Weapon { name = "Rail gauntlet", speed = 10000, damage = 180, period = 2f, charge = 0.55f, life = 0.2f, voice = 5, rail = true, energy = 32, needs = Gear.Rail },
            new Weapon { name = "Hemisphere lasers", damage = 7, period = 0.12f, sweep = true, energy = 3.5f, voice = 0, needs = Gear.Nova },
        };

        public static void Hurt(ref Foe foe, float damage)
        {
            if (foe.protectedCore) return;
            float absorbed = math.min(foe.shield, damage);
            foe.shield -= absorbed; foe.health -= damage - absorbed;
        }

        public static string Name(Foe foe) => foe.stationary ? "Spine turret" : foe.kind == FoeKind.Raider ? "Harrower"
            : foe.kind == FoeKind.Drone ? "Skimmer" : foe.kind == FoeKind.Mind ? "Mind" : foe.kind == FoeKind.Base ? "Citadel" : foe.caste.ToString();

        public static float3 SweepDirection(int index, int count, float3 up)
        {
            float height = (index + 0.5f) / count, angle = index * 2.39996323f;
            float3 east = math.normalizesafe(math.cross(up, new float3(0, 0, 1)), new float3(1, 0, 0));
            return up * height + (east * math.cos(angle) + math.cross(up, east) * math.sin(angle)) * math.sqrt(1 - height * height);
        }

        public static Weapon CapturedWeapon(int kit, uint choice)
        {
            int count = 0;
            foreach (var weapon in Guns) if (weapon.blast < Nuclear && Campaign.Has(kit, weapon.needs)) count++;
            if (count == 0) return Guns[0];
            int chosen = (int)(choice % count);
            foreach (var weapon in Guns)
                if (weapon.blast < Nuclear && Campaign.Has(kit, weapon.needs) && chosen-- == 0) return weapon;
            return Guns[0];
        }

        // How much a foe can take and how hard it hits, at a level of threat that starts at 1.
        public static float Health(FoeKind kind, int depth, MotileCaste caste = MotileCaste.Stalker) =>
            (kind == FoeKind.Sentry ? caste == MotileCaste.Breacher ? 90f : caste == MotileCaste.Spitter ? 45f : 35f
            : kind == FoeKind.Raider ? 60f : kind == FoeKind.Drone ? 35f : kind == FoeKind.Mind ? 120f : 1500f);
        public static float Damage(FoeKind kind, int depth) => (kind == FoeKind.Raider ? 6f : kind == FoeKind.Drone ? 9f : 8f);

        public static MotileCaste Caste(int stage, int guard) => stage <= 2 ? MotileCaste.Stalker : (MotileCaste)((guard + stage - 2) % 3);

        // Hull or health lost each second to the sun's radiation out in deep space, without a shield against it.
        public const float Radiation = 12f;

        // Health lost to landing this hard on foot. A jump costs nothing; forty metres on the home world kills.
        public static float Fall(float impact) => math.max(0f, impact - 11f) * 6f;

        // Hull lost to hitting something this hard. A landing costs nothing; flying into a hillside is the end.
        public static float Crash(float impact) => math.max(0f, impact - 12f) * 1.6f;

        // Hull lost to striking a rock of a belt or a ring this hard: a dent, however fast, and never the last of a hull.
        public static float Graze(float impact, float hull) => math.min(math.min(2f + 0.1f * impact, 10f), math.max(0f, hull - 5f));

        // Hull or health lost each second to heat past what it can take, both in kelvin.
        public static float Scorch(float kelvin, float limit) => math.max(0f, kelvin - limit) * 0.04f;

        // size is how big a target the foe is: a raider or a drone has its own, the base takes its station's.
        public static Foe Spawn(FoeKind kind, float3 position, float3 facing, int depth, uint seed, float size = 0f, MotileCaste caste = MotileCaste.Stalker) => new Foe
        {
            kind = kind, caste = caste, position = position, forward = math.normalizesafe(facing, new float3(0, 0, 1)), health = Health(kind, depth, caste), maximumHealth = Health(kind, depth, caste), cooldown = 1.5f, seed = seed | 1u,
            size = kind == FoeKind.Raider ? 10f : kind == FoeKind.Drone || kind == FoeKind.Sentry ? 1.2f : kind == FoeKind.Mind ? 2f : size,
        };

        public static Bolt Fire(float3 from, float3 direction, float3 inherit, float speed, float damage, bool hostile, float blast = 1.5f) =>
            new Bolt { position = from, velocity = inherit + direction * speed, damage = damage, hostile = hostile, life = 3f, blast = blast };

        // The player pulls the trigger: one bolt, or a spread of them. wild widens the spread, for a
        // firearm fired fast, on the run or in the air.
        public static void Fire(in Weapon weapon, float3 from, float3 direction, float3 inherit, ref Random rnd, List<Bolt> into, float wild = 0f)
        {
            for (int i = 0; i < weapon.pellets; i++)
                into.Add(new Bolt
                {
                    position = from, velocity = inherit + math.normalize(direction + rnd.NextFloat3Direction() * ((weapon.spread + wild) * rnd.NextFloat())) * weapon.speed,
                    damage = weapon.damage, life = weapon.life, homing = weapon.homing, blast = weapon.blast, airOnly = weapon.airOnly,
                });
        }

        // Moves one foe a step towards its quarry and says whether it fired.
        public static bool Hunt(ref Foe foe, BodyDescriptor body, float3 target, float3 targetVelocity, int depth, float dt, out Bolt shot)
        {
            shot = default;
            var rnd = new Random(foe.random = ((foe.random == 0 ? foe.seed : foe.random) * 747796405u + 2891336453u) | 1u);
            float3 to = target - foe.position, up = body.kind == BodyKind.Station ? new float3(0, 1, 0) : math.normalizesafe(foe.position, new float3(0, 1, 0));
            float distance = math.max(math.length(to), 1e-3f);
            foe.cooldown -= dt;

            if (foe.kind == FoeKind.Mind) return false;
            if (foe.scattered && !foe.stationary)
            {
                foe.velocity = math.normalizesafe(foe.kind == FoeKind.Sentry ? Walker.Project(foe.position - target, up) : foe.position - target, foe.forward) * 12f;
                foe.position += foe.velocity * dt;
                float3 over = math.normalizesafe(foe.position, up);
                if (foe.kind == FoeKind.Sentry && body.kind == BodyKind.Rock) foe.position = over * (body.GroundRadius(over) + 0.9f);
                else foe.position = over * math.max(math.length(foe.position), Ship.Surface(body, over) + foe.size);
                return false;
            }
            if (foe.kind == FoeKind.Base || foe.stationary)
            {
                if (foe.stationary && body.kind == BodyKind.Rock) foe.position = up * (body.GroundRadius(up) + 1);
                // the station does not move: its guns track whatever is in reach and fire from its hull
                if (distance > TurretReach || foe.cooldown > 0f) return false;
                foe.cooldown = (foe.stationary ? 1.6f : 0.45f) + rnd.NextFloat(0.3f);
                float3 muzzle = foe.position + to / distance * foe.size;
                float flight = math.distance(target, muzzle) / TurretBolt;
                shot = Fire(muzzle, math.normalize(target + targetVelocity * flight - muzzle + rnd.NextFloat3Direction() * (0.012f * distance)), float3.zero, TurretBolt, Damage(foe.kind, depth), true, 3f);
                shot.life = 5f;
                if (foe.stationary) { shot.homing = 0.7f; shot.velocity *= 0.22f; }
                return true;
            }

            if (foe.kind == FoeKind.Drone || foe.kind == FoeKind.Sentry)
            {
                bool stalker = foe.kind == FoeKind.Sentry && foe.caste == MotileCaste.Stalker;
                bool breacher = foe.kind == FoeKind.Sentry && foe.caste == MotileCaste.Breacher;
                bool spitter = foe.kind == FoeKind.Sentry && foe.caste == MotileCaste.Spitter;
                // close to a dozen paces, then circle, never quite still
                if (body.kind == BodyKind.Station) up = new float3(0, 1, 0);
                float3 level = Walker.Project(to, up), toward = math.normalizesafe(level, foe.forward);
                float3 round = math.cross(up, toward) * (foe.seed % 2u == 0u ? 1f : -1f);
                float gap = math.length(level);
                float stand = stalker ? 1.7f : breacher ? 4 : spitter ? 28 : 12;
                float3 desired = toward * math.clamp((gap - stand) * 1.2f, -3, breacher ? 5 : spitter ? 7 : 10)
                    + round * ((stalker ? 0.8f : breacher ? 0.5f : 3.5f) * (1 - math.smoothstep(stand + 2, stand + 13, gap)));
                if (foe.clever && gap > stand + 5 && gap < 70)
                    desired = desired * (foe.seed % 3 == 0 ? 0.35f : 0.7f) + round * (stalker ? 6 : breacher ? 2.5f : 5);
                if (foe.clever && spitter && foe.cooldown > 1.5f) desired -= toward * 3;
                foe.velocity = Walker.MoveTowards(Walker.Project(foe.velocity, up), desired, 14f * dt);
                foe.position += foe.velocity * dt;
                up = math.normalize(foe.position);
                if (body.kind == BodyKind.Rock)
                {
                    float floor = foe.kind == FoeKind.Sentry ? body.GroundRadius(up) : Ship.Surface(body, up);
                    foe.position = up * (foe.kind == FoeKind.Sentry ? floor + 0.9f : math.max(floor + 0.8f, math.lerp(math.length(foe.position), floor + Hover, 1 - math.exp(-4 * dt))));
                }
                foe.forward = Turn(foe.forward, to / distance, 2.5f * dt);
                if (distance > (stalker ? 3.5f : spitter ? 70 : breacher ? 12 : DroneReach) || foe.cooldown > 0f) return false;
                foe.cooldown = (spitter ? 3 : breacher ? 2 : 1.4f) + rnd.NextFloat(0.8f);
                float speed = stalker ? 140 : spitter ? 24 : DroneBolt;
                shot = Fire(foe.position, math.normalize(to + targetVelocity * (distance / speed)), float3.zero, speed,
                    Damage(foe.kind, depth) * (breacher ? 2 : spitter ? 1.75f : 1), true, stalker ? 0 : spitter ? 3 : 1.2f);
                shot.life = stalker ? 0.05f : spitter ? 4 : 3;
                if (stalker) foe.velocity += toward * 6;
                return true;
            }

            float altitude = math.length(foe.position) - Ship.Surface(body, up);
            // a pass ends close in; the next begins once there is room to turn
            foe.away = foe.away ? distance < 500f : distance < 110f;
            float3 side = math.cross(up, to / distance) * (foe.seed % 2u == 0u ? 1f : -1f);
            // aim where the quarry will be when the bolt gets there
            float3 aim = math.normalize(to + (targetVelocity - foe.velocity) * (distance / RaiderBolt));
            float3 want = foe.away ? math.normalize(side * 0.7f + up * 0.35f - to / distance) : aim;
            if (altitude < 80f) want = math.normalize(want + up * ((80f - altitude) / 30f));
            foe.forward = Turn(foe.forward, want, 1.3f * dt);
            foe.velocity = Walker.MoveTowards(foe.velocity, foe.forward * (0.9f * Ship.MaxSpeed(body, altitude)), 90f * dt);
            foe.position += foe.velocity * dt;
            up = math.normalize(foe.position);
            foe.position = up * math.max(math.length(foe.position), Ship.Surface(body, up) + 10f);

            if (foe.away || distance > RaiderReach || foe.cooldown > 0f || math.dot(foe.forward, aim) < 0.998f) return false;
            if (foe.burst <= 0) foe.burst = 3;
            foe.cooldown = --foe.burst > 0 ? 0.14f : 1.2f + rnd.NextFloat(0.8f);
            // not every shot is true
            shot = Fire(foe.position + foe.forward * 12f, math.normalize(aim + rnd.NextFloat3Direction() * 0.02f), foe.velocity, RaiderBolt, Damage(foe.kind, depth), true, 2.5f);
            return true;
        }

        // Moves every bolt a step, takes health from the foes that the player's bolts hit and
        // drops the spent ones. Returns the damage done to the player, a sphere of this size and
        // motion. Where bolts ended against something goes into struck.
        public static float NuclearDamage(BodyDescriptor body, float3 blast, float3 target, float radius)
        {
            float atmosphere = body.radius + math.max(body.radius * 0.12f, body.terrain.MaxHeight * 1.45f);
            if (body.air > 0 && math.length(blast) < atmosphere && math.length(target) >= atmosphere) return 0;
            return 200 * math.saturate(1 - math.distance(blast, target) / math.max(1, radius));
        }

        public static float Step(List<Bolt> bolts, List<Foe> foes, BodyDescriptor body, float3 player, float3 playerVelocity, float playerRadius, float dt, List<Strike> struck, Shield shield = default, System.Func<float3, float3, bool> visible = null)
        {
            float hurt = 0f;
            for (int i = bolts.Count - 1; i >= 0; i--)
            {
                var bolt = bolts[i];
                if (bolt.homing > 0f)
                {
                    // a missile swings towards the nearest foe ahead of it
                    float speed = math.length(bolt.velocity), nearest = float.MaxValue;
                    float3 heading = bolt.velocity / speed, chase = heading;
                    if (bolt.hostile) chase = math.normalizesafe(player + playerVelocity * (math.distance(player, bolt.position) / speed) - bolt.position, heading);
                    foreach (var foe in foes)
                    {
                        if (bolt.hostile) break;
                        if (foe.friendly || foe.health <= 0 || bolt.airOnly && foe.kind != FoeKind.Raider && !foe.stationary) continue;
                        float3 to = foe.position - bolt.position;
                        float away = math.length(to);
                        if (away >= nearest || math.dot(to, heading) < 0.3f * away) continue;
                        nearest = away;
                        chase = math.normalize(to + foe.velocity * (away / speed));
                    }
                    bolt.velocity = Turn(heading, chase, bolt.homing * dt) * speed;
                }
                float3 step = bolt.velocity * dt, at = bolt.position + step;
                bool hit = shield.Blocks(bolt.position, step, out at), blocked = hit, ground = false;
                int struckFoe = -1;
                if (!hit && bolt.hostile)
                {
                    if (hit = Crosses(bolt.position, step - playerVelocity * dt, player, playerRadius)) hurt += bolt.damage;
                }
                else if (!hit)
                    for (int f = 0; f < foes.Count && !hit; f++)
                    {
                        var foe = foes[f];
                        if (foe.friendly || foe.health <= 0) continue;
                        if (!(hit = Crosses(bolt.position, step - foe.velocity * dt, foe.position, foe.size))) continue;
                        struckFoe = f;
                        // nothing short of a nuclear blast marks the citadel
                        if (foe.kind != FoeKind.Base || bolt.blast >= Nuclear) { Hurt(ref foe, bolt.damage * (bolt.blast >= Nuclear && foe.kind == FoeKind.Mind ? 0.2f : 1)); foe.recoil = 1; }
                        foes[f] = foe;
                        // it ends on the near side of what it hit
                        at = foe.position - math.normalizesafe(step) * math.min(foe.size, math.distance(bolt.position, foe.position));
                    }
                // the ground stops what misses
                if (!hit) hit = ground = math.length(at) < Ship.Surface(body, math.normalize(at));
                if (hit) struck?.Add(new Strike { at = at, blast = bolt.blast, ground = ground, level = bolt.level, foe = struckFoe, shield = blocked, hostile = bolt.hostile, robot = bolt.robot });
                if (hit && !blocked && !bolt.hostile && bolt.blast >= 5f)
                    // a big enough bang catches whatever else is near, less the further off it is
                    for (int f = 0; f < foes.Count; f++)
                    {
                        var foe = foes[f];
                        if (foe.friendly || foe.health <= 0 || shield.Blocks(at, foe.position - at, out _) || visible != null && !visible(at, foe.position)) continue;
                        float near = math.distance(foe.position, at) - foe.size;
                        if (near <= 0f || near >= bolt.blast || (foe.kind == FoeKind.Base && bolt.blast < Nuclear)) continue;
                        Hurt(ref foe, bolt.damage * (1f - near / bolt.blast) * (bolt.blast >= Nuclear && foe.kind == FoeKind.Mind ? 0.2f : 1));
                        foes[f] = foe;
                    }
                if (hit || (bolt.life -= dt) <= 0f)
                {
                    bolts[i] = bolts[bolts.Count - 1];
                    bolts.RemoveAt(bolts.Count - 1);
                    continue;
                }
                bolt.position = at;
                bolts[i] = bolt;
            }
            return hurt;
        }

        public static Alert Observe(ref Foe foe, float3 target, float noise, float light, bool visible, float dt)
        {
            float3 to = target - foe.position;
            float distance = math.length(to);
            bool heard = distance < noise * 90f, seen = visible && distance < 55f * light && math.dot(math.normalizesafe(to), foe.forward) > 0.4f;
            foe.suspicion = math.clamp(foe.suspicion + (seen ? 2f : heard ? 1f : -0.25f) * dt, 0, 2);
            return foe.alert = foe.suspicion >= 1f ? Alert.Alarmed : foe.suspicion > 0f ? Alert.Searching : Alert.Calm;
        }

        // Beam damage is supplied per step; a rail ray damages every hull before the ground.
        public static float3 Ray(List<Foe> foes, BodyDescriptor body, float3 from, float3 direction, float reach, float damage, bool pierce, Shield shield, List<Strike> struck)
        {
            direction = math.normalizesafe(direction, new float3(0, 0, 1));
            float3 end = from + direction * reach;
            if (shield.Blocks(from, direction * reach, out var stop))
            {
                reach = math.distance(from, stop);
                end = stop;
            }
            // Sample the same height function as movement.
            for (float distance = 0.5f; distance <= reach; distance += 0.5f)
            {
                float3 at = from + direction * distance;
                if (math.length(at) >= Ship.Surface(body, math.normalizesafe(at))) continue;
                reach = distance;
                end = at;
                struck?.Add(new Strike { at = at, ground = true, foe = -1, blast = 0.2f });
                break;
            }
            var hits = new List<(int index, float distance)>();
            for (int i = 0; i < foes.Count; i++)
            {
                var foe = foes[i];
                if (foe.health <= 0 || foe.friendly || !Crosses(from, direction * reach, foe.position, foe.size)) continue;
                float along = math.dot(foe.position - from, direction);
                float perpendicular = math.lengthsq(foe.position - from) - along * along;
                hits.Add((i, math.max(0, along - math.sqrt(math.max(0, foe.size * foe.size - perpendicular)))));
            }
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var foe = foes[hit.index];
                Hurt(ref foe, damage);
                if (damage > 0) foe.recoil = 1;
                foes[hit.index] = foe;
                float3 at = from + direction * hit.distance;
                struck?.Add(new Strike { at = at, foe = hit.index });
                if (!pierce) return at;
            }
            return end;
        }

        // Whether a step from a point passes through a sphere.
        public static bool Crosses(float3 from, float3 step, float3 centre, float radius)
        {
            float along = math.saturate(math.dot(centre - from, step) / math.max(math.lengthsq(step), 1e-9f));
            return math.distancesq(from + step * along, centre) < radius * radius;
        }

        public static int AssistedTarget(List<Foe> foes, float3 from, float3 forward, float reach, float degrees = 4)
        {
            int target = -1; float closest = math.cos(math.radians(degrees));
            for (int i = 0; i < foes.Count; i++)
            {
                var foe = foes[i];
                if (foe.friendly || foe.health <= 0 || foe.protectedCore || math.distance(from, foe.position) > reach) continue;
                float alignment = math.dot(forward, math.normalizesafe(foe.position - from));
                if (alignment > closest) { closest = alignment; target = i; }
            }
            return target;
        }

        // Swings one direction towards another by no more than an angle.
        public static float3 Turn(float3 from, float3 to, float radians)
        {
            float angle = math.acos(math.clamp(math.dot(from, to), -1f, 1f));
            if (angle <= radians) return to;
            float3 axis = math.cross(from, to);
            // facing dead away: any axis across the heading will do
            if (math.lengthsq(axis) < 1e-8f) axis = math.cross(from, math.abs(from.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0));
            return math.normalize(math.rotate(quaternion.AxisAngle(math.normalize(axis), radians), from));
        }
    }
}
