using System;
using System.Collections.Generic;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // Scans combine observed facts with the wording prepared for this expedition.
    public class Scanner : MonoBehaviour
    {
        [NonSerialized] public Game game;

        // What was last scanned, in a few words, and where it is in the scene.
        public string Subject { get; private set; }
        public Vector3 Mark { get; private set; }
        // The recorded entry appears as soon as the scanner identifies its subject.
        public Entry Result { get; private set; }
        public string Source { get; private set; } = "";
        // When Result last changed, for the HUD to reveal and later put away.
        public float Since { get; private set; } = float.MinValue;

        readonly List<Scannable> things = new List<Scannable>();

        public void Scan()
        {
            var system = game.System;
            var view = Camera.main.transform;
            var body = system.Bodies[system.Reference];
            float3 eye = (float3)view.position + system.Anchor, ray = view.forward, at;


            things.Clear();
            if (body.TryGetComponent(out Scatter scatter)) scatter.Collect(eye, things);
            if (body.TryGetComponent(out Creatures creatures)) things.AddRange(creatures.Seen);
            if (system.Reference == SolarSystemDescriptor.Home) things.Add(game.Hermit.Likeness);
            foreach (var foe in game.Battle.Foes)
                things.Add(new Scannable { name = foe.stationary ? "Presence Spine turret: rooted defence; destroy it to lower the local shield" : foe.kind == FoeKind.Sentry ? "Presence " + foe.caste + (foe.caste == MotileCaste.Stalker ? ": six-legged close-range fighter" : foe.caste == MotileCaste.Breacher ? ": heavily armoured eight-legged charger" : ": four-legged mortar body that retreats before firing")
                    : foe.kind == FoeKind.Mind ? "Presence mind: rooted controller; its death scatters this brood" : "Presence " + Combat.Name(foe),
                    foot = foe.position - math.normalizesafe(foe.position) * foe.size * 0.5f, height = foe.size * 2, girth = foe.size });
            game.Frontier.Collect(things);
            if (game.Player.Has(Gear.Ship) || game.Player.ShipOverhead)
                things.Add(new Scannable { name = game.Player.ShipOverhead ? "Foundry spacecraft holding above the crash site" : "Foundry pilotable spacecraft", foot = game.Player.ship.position, height = 8, girth = 10 });
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
            string description = Core.Scan.Describe(world < 0 ? body.Descriptor : system.Descriptor.bodies[world]);
            string text = game.Wording(description);
            string keyForPlace = pick < 0 ? "scanLocal" : Subject.StartsWith("Presence") ? "scanPresence" : Subject.StartsWith("Foundry") ? "scanFoundry" : "scanSpecimen";
            Source = game.Prepared(description) || world < 0 && game.Prepared(keyForPlace) ? "EXPEDITION FIELD JOURNAL" : "OBSERVED FACTS";
            if (world < 0) text = game.Wording(keyForPlace).Replace("{subject}", Subject).Replace("{place}", Core.Scan.Place(system.Descriptor, system.Reference, at, system.Clock)) + " " + text;
            Result = new Entry { key = key, title = Subject, text = text };
            game.Remember(Result);
            Since = Time.unscaledTime;
        }
    }
}
