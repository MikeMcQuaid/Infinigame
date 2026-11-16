using System;
using System.Collections.Generic;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // Point at something and scan it: the nearest plant, rock or animal on the line of sight, else
    // the ground, else a world across space. The oracle writes its journal entry the first time,
    // from the plain facts the game knows; after that the journal remembers.
    public class Scanner : MonoBehaviour
    {
        const string Persona =
            "You are the field scanner of a small exploration ship, in a game set in a toy-sized solar system. " +
            "Its worlds really are tiny, a kilometre or so across with days of a few minutes, and nobody there finds that odd. " +
            "You are told what the scanner is pointed at and where. Write its journal entry: on the first line an invented name " +
            "for the subject, two or three words with no punctuation, then two or three sentences that a ship's naturalist would " +
            "write about it. Keep to the facts given and invent the rest vividly. Plain text only.";

        [NonSerialized] public Game game;

        // What was last scanned, in a few words, and where it is in the scene.
        public string Subject { get; private set; }
        public Vector3 Mark { get; private set; }
        // Its journal entry, once written. While Busy the oracle is still writing.
        public Entry Result { get; private set; }
        public string Source { get; private set; } = "";
        public bool Busy { get; private set; }
        // When Result last changed, for the HUD to reveal and later put away.
        public float Since { get; private set; } = float.MinValue;

        readonly List<Scannable> things = new List<Scannable>();

        public async void Scan()
        {
            if (Busy) return;
            var system = game.System;
            var view = Camera.main.transform;
            var body = system.Bodies[system.Reference];
            float3 eye = (float3)view.position + system.Anchor, ray = view.forward, at;
            string facts;

            things.Clear();
            if (body.TryGetComponent(out Scatter scatter)) scatter.Collect(eye, things);
            if (body.TryGetComponent(out Creatures creatures)) things.AddRange(creatures.Seen);
            if (system.Reference == SolarSystemDescriptor.Home) things.Add(game.Hermit.Likeness);
            foreach (var foe in game.Battle.Foes)
                things.Add(new Scannable { name = foe.kind == FoeKind.Sentry ? "Presence " + foe.caste + (foe.caste == MotileCaste.Stalker ? ": six-legged close-range fighter" : foe.caste == MotileCaste.Breacher ? ": heavily armoured eight-legged charger" : ": four-legged mortar body that retreats before firing")
                    : foe.kind == FoeKind.Mind ? "Presence mind: rooted controller; its death scatters this brood" : "Presence " + foe.kind,
                    foot = foe.position - math.normalizesafe(foe.position) * foe.size * 0.5f, height = foe.size * 2, girth = foe.size });
            game.Frontier.Collect(things);
            if (game.Player.Has(Gear.Ship) || game.Player.captive)
                things.Add(new Scannable { name = "Foundry pilotable spacecraft", foot = game.Player.ship.position, height = 8, girth = 10 });
            int pick = Core.Scan.Pick(eye, ray, things), world = -1;
            if (pick >= 0)
            {
                Subject = things[pick].name;
                at = things[pick].foot + math.normalize(things[pick].foot) * (things[pick].height * 0.5f);
            }
            else if (body.Descriptor.kind != BodyKind.Sun && Core.Scan.Ground(body.Descriptor, eye, ray, out at))
                Subject = Core.Scan.Terrain(body.Descriptor, math.normalize(at));
            else
            {
                world = Core.Scan.Body(system.Descriptor, system.Reference, system.Clock, eye, ray);
                if (world < 0) return;
                Subject = system.Descriptor.bodies[world].name;
                at = system.ToReference(world);
            }
            Mark = at - system.Anchor;
            Since = Time.unscaledTime;

            // one entry per kind of thing per world, and one per world seen from afar
            string key = $"{game.Seed}/{(world < 0 ? system.Reference : world)}/{(world < 0 ? Subject : "world")}";
            Result = game.Journal.Find(entry => entry.key == key);
            if (Result != null) { Source = "SAVED JOURNAL · NO NEW REQUEST"; return; }
            facts = world < 0
                ? $"Subject: a {Subject}, found {Core.Scan.Place(system.Descriptor, system.Reference, at, system.Clock)}.\nThat world is {Core.Scan.Describe(body.Descriptor)}"
                : $"Subject: a whole world, seen from {math.distance(eye, at) / 1000f:0} km away. It is {Core.Scan.Describe(system.Descriptor.bodies[world])}";

            facts = facts.Substring(0, math.min(facts.Length, 1200));
            string writer = Oracle.Writer.ToUpperInvariant();
            Source = $"ASKING {writer} · OBSERVED FACTS";
            Busy = true;
            try
            {
                string reply = (await Oracle.Ask(Persona, facts)).Trim();
                int line = reply.IndexOf('\n');
                Result = new Entry { key = key, title = (line < 0 ? Subject : reply.Substring(0, line)).Trim(), text = line < 0 ? reply : reply.Substring(line).Trim() };
                Source = $"WRITTEN BY {writer}";
                game.Remember(Result);
            }
            catch (Exception)
            {
                Source = "OBSERVED FACTS · MODEL UNAVAILABLE";
                Result = new Entry { title = Subject, text = facts };
            }
            Busy = false;
            Since = Time.unscaledTime;
        }
    }
}
