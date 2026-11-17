using System;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Infinigame
{
    // Authored mission guidance and optional free conversation with the recovery robot.
    public class Hermit : MonoBehaviour
    {
        const float Reach = 5f;

        [NonSerialized] public Game game;
        public string Name { get; private set; }
        // Where the hermit stands, relative to the home world's centre.
        public float3 Position { get; private set; }
        public bool Talking { get; private set; }
        // Waiting for the hermit to answer.
        public bool Thinking { get; private set; }
        // What the player has typed so far, the hermit's latest words and when they came.
        public string Source { get; private set; } = "MISSION BRIEFING";
        public string Draft { get; private set; } = "";
        public string Line { get; private set; } = "";
        public float Spoke { get; private set; }
        // His latest words were said unasked, and are shown as a caption while they last.
        public bool Remarking => unasked && !Talking && Time.unscaledTime - Spoke < 2.5f + Line.Length / 14f;

        // He flies with the player once there is a ship to fly in.
        public bool Aboard => game.Player.Has(Gear.Ship);
        public bool Near => !Aboard && game.System.Reference == SolarSystemDescriptor.Home && !game.Player.flying && math.distance(game.Player.walker.position, Position) < Reach;

        // What can always be asked, with a number key, when nothing has been typed.
        public static readonly string[] Questions = { "Who are you?", "What happened to me?", "What should I do now?" };

        // The hermit as the scanner sees them.
        public Scannable Likeness => new Scannable { name = "autonomous robot with plated limbs, exposed conduits and three cyan optical lenses with a dorsal laser", foot = Position, height = 1.9f, girth = 0.6f };

        Transform camp, figure;
        bool seated, unasked;
        LineRenderer repair;
        readonly LineRenderer[] repairRays = new LineRenderer[6];
        int punches;
        public bool Provoked => punches >= 5;
        public void Punched()
        {
            punches++;
            Remark(punches >= 5 ? "Repeated attack confirmed. Defensive weapons engaged." : punches >= 3 ? "Stop attacking friendly robots. The next attacks will trigger lethal defence." : "Do not punch the robodogs. We are on your side.");
        }

        // Pitches the camp on the freshly built home world, a short walk from the landing site.
        public void Settle()
        {
            if (camp != null) Destroy(camp.gameObject);
            // he may be in the ship, and not go with his camp
            if (figure != null) Destroy(figure.gameObject);
            seated = false;
            var system = game.System.Descriptor;
            var body = system.bodies[SolarSystemDescriptor.Home];
            var rnd = new Random(math.hash(new uint2(system.seed, 77u)) | 1u);
            Name = SolarSystemDescriptor.Name(ref rnd);
            float3 landing = Campaign.Site(system, 0), spot = Campaign.Site(system, 1);
            Position = spot * body.GroundRadius(spot);
            game.System.Bodies[SolarSystemDescriptor.Home].Level(Position, 14);
            camp = new GameObject("Recovery companion").transform;
            var robot = Models.Robot(); robot.name = "Hermit"; robot.SetParent(camp, false);
            camp.SetParent(game.System.Bodies[SolarSystemDescriptor.Home].transform, false);
            camp.localPosition = Position - spot * 0.05f;
            // facing the landing site, to watch the traveller arrive
            float3 facing = Walker.Project(landing - spot, spot);
            camp.localRotation = math.lengthsq(facing) > 1e-8f ? Quaternion.LookRotation(facing, spot) : Quaternion.FromToRotation(Vector3.up, spot);
            figure = camp.Find("Hermit");
            var movement = figure.gameObject.AddComponent<MachineMotion>();
            movement.quadruped = true;
            movement.look = Camera.main.transform;

            Talking = Thinking = false;
            Line = Draft = ""; punches = 0;
            Spoke = float.MinValue;
        }

        // The recovered companion rides behind the pilot with only its head above the socket.
        void Update()
        {
            if (camp != null)
            {
                var armour = camp.Find("Workshop/Powered armour");
                if (armour != null) armour.gameObject.SetActive(!game.Player.Has(Gear.Suit));
                var helm = camp.Find("Workshop/Fluted great helm");
                if (helm != null) helm.gameObject.SetActive(!game.Player.Has(Gear.Suit));
            }
            if (repair == null)
            {
                repair = new GameObject("Suit repair beam").AddComponent<LineRenderer>();
                repair.transform.SetParent(transform, false); repair.positionCount = 2;
                repair.sharedMaterial = Shapes.Material("Glow", new Color(0.08f, 0.35f, 0.95f));
                repair.startWidth = 0.018f; repair.endWidth = 0.035f;
                for (int i = 0; i < repairRays.Length; i++)
                {
                    repairRays[i] = Instantiate(repair, transform);
                    repairRays[i].name = "Repair scanning laser";
                    repairRays[i].sharedMaterial = Shapes.Material("Glow", Color.HSVToRGB(0.45f + i * 0.055f, 0.7f, 0.9f));
                }
            }
            repair.enabled = game.Tutorial != null && game.Tutorial.Repairing && !game.Paused;
            if (repair.enabled)
            {
                repair.SetPosition(0, camp.position + camp.up * 1.1f);
                repair.SetPosition(1, (Vector3)(game.Player.Position - game.System.Anchor) + (Vector3)math.normalizesafe(game.Player.Position) * Mathf.Lerp(1.85f, 0.12f, game.Tutorial.RepairProgress));
            }
            for (int i = 0; i < repairRays.Length; i++)
            {
                var laser = repairRays[i]; laser.enabled = repair.enabled;
                if (!laser.enabled) continue;
                laser.SetPosition(0, repair.GetPosition(0));
                laser.SetPosition(1, repair.GetPosition(1) + Camera.main.transform.right * ((i - 2.5f) * 0.13f * math.saturate(game.Tutorial.RepairProgress * 5)));
            }
            if (Provoked && !Aboard && !game.Paused && !game.Dead && math.distance(Position, game.Player.Position) < 40 && game.Outpost.Visible(Position + math.normalizesafe(Position), game.Player.Position))
            {
                repair.enabled = true; repair.SetPosition(0, camp.position + camp.up);
                repair.SetPosition(1, game.Player.Position - game.System.Anchor + math.normalizesafe(game.Player.Position));
                game.Player.Hurt(180 * Time.deltaTime, "Killed by friendly robot defensive fire after repeated attacks");
            }
            if (game.Paused || game.ShowControls || game.Player.SystemView) Spoke += Time.unscaledDeltaTime;
            if (figure == null || seated == Aboard || !(game.Player.Seat is Transform seat)) return;
            seated = Aboard;
            figure.SetParent(seated ? seat : camp, false);
            // The seated figure fits beneath the canopy.
            figure.localScale = Vector3.one * (seated ? 0.25f : 1f);
            figure.localPosition = seated ? new Vector3(0, -0.24f, 0) : Vector3.zero;
            figure.localRotation = Quaternion.identity;
            figure.GetComponent<MachineMotion>().seated = seated;
        }

        public void Rescue()
        {
            float3 up = math.normalize(game.Player.Position + game.Player.walker.forward * 5);
            Position = up * game.System.ReferenceBody.GroundRadius(up);
            camp.localPosition = Position;
            camp.localRotation = Quaternion.LookRotation(-game.Player.walker.forward, up);
        }

        public void Greet()
        {
            Talking = true;
            Source = "MISSION BRIEFING · NO MODEL";
            unasked = false;
            Draft = "";
            // what he makes of the mission in hand comes first, aloud if he has not said it already
            if (Line != Briefing) game.Speech.Say(Briefing);
            Line = Briefing;
            Spoke = Time.unscaledTime;

        }

        // Something he says without being asked, from the seat beside the pilot.
        public void Remark(string words)
        {
            if (!game.Player.Has(Gear.Suit) || Talking) return;
            unasked = true;
            Source = "COMPANION GUIDANCE · NO MODEL";
            Line = words;
            Spoke = Time.unscaledTime;
            game.Speech.Say(words);
        }

        public string Briefing => game.Stage >= Campaign.Won ? game.Fall.destroyed ? "The mind has collapsed. Stay with me while the system falls into it." : "The core is gone, but the final mind is buried too deep for torpedoes. Our colonies must build engines on a moon." : game.Stage == Campaign.Tower || game.Stage == Campaign.Tower + 1 ? "Break this world’s mind, then recover its equipment. " + game.Frontier.FactoryHint(0) : game.Mission.brief;

        // Authored replies keep the early story playable without an account.
        string Canned(string asked) => asked.IndexOf("factory", StringComparison.OrdinalIgnoreCase) >= 0 || asked.IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0
            ? game.Frontier.FactoryHint(game.Frontier.Local.shipyard ? 2 : game.Frontier.Local.factory && game.Frontier.Local.mindDead ? 1 : 0)
            : asked.IndexOf("world", StringComparison.OrdinalIgnoreCase) >= 0 || asked.IndexOf("presence", StringComparison.OrdinalIgnoreCase) >= 0
            ? "We are the Foundry, independent machines protecting what still lives. The presence grows bodies from a shared mind and consumes everything beyond itself. We must sever those minds and rebuild safe settlements."
            : asked.IndexOf("who", StringComparison.OrdinalIgnoreCase) >= 0 ? $"{Name}. An autonomous recovery machine. I can keep this channel open wherever you go."
            : asked.IndexOf("happen", StringComparison.OrdinalIgnoreCase) >= 0 || asked.IndexOf("remember", StringComparison.OrdinalIgnoreCase) >= 0
                ? game.Stage < 8 ? "I recovered you after the crash. Some records are damaged. Let us restore your equipment first."
                : game.Stage < Campaign.Citadel ? "I recognise the work beneath that armour. I helped make it, and you. I am still afraid of what that means."
                : "I helped create you to silence the presence. The last mind lies beneath the crust. A moon impact will collapse it, and the system with it."
            : Briefing;

        // One frame of conversation: typing, sending with confirm, leaving with the menu key.
        public void Converse(Controls c)
        {
            if (c.menu) Talking = false;
            if (!Talking || Thinking) return;
            if (c.replaceText != null) Draft = c.replaceText;
            // with nothing typed yet, a number asks one of the questions he always has an answer to
            if (Draft.Length == 0 && c.typed?.Length == 1 && c.typed[0] >= '1' && c.typed[0] < '1' + Questions.Length)
            {
                Say(Questions[c.typed[0] - '1']);
                return;
            }
            foreach (char letter in c.typed ?? "")
                if (!char.IsControl(letter) && letter < 0xF700 && Draft.Length < 160)
                    Draft += letter;
            if (c.erase && Draft.Length > 0) Draft = Draft.Substring(0, Draft.Length - 1);
            if (!c.confirm || Draft.Trim().Length == 0) return;
            Say(Draft.Trim());
            Draft = "";
        }

        [Serializable] public class WorldState { public string world; public int region, aliens, dogs, hostileShips, ships; public bool factory, shipyard; }
        [Serializable] public class State { public int stage, depth, power, health, hull, altitude, level, experience, salvage; public string location, objective; public WorldState[] worlds; }

        // Sends the traveller's words and waits for the reply. The talk so far is kept as
        // alternating lines: the traveller's, then the hermit's.
        public async void Say(string words)
        {
            unasked = !Talking;
            string command = words.Trim().ToLowerInvariant();
            if (command.Contains("recall ship") || command.Contains("pick me up") || command.Contains("come get me") || command.Contains("land near me"))
            {
                Source = "LOCAL SHIP COMMAND"; Line = game.Player.Travel();
                Spoke = Time.unscaledTime; game.Speech.Say(Line); return;
            }
            var system = game.System;
            string persona = $"You are {Name}, the traveller's Foundry recovery robot and copilot. " +
                "Speak in one to three short sentences. Use only the known story; do not invent later revelations. Explain XP level choices, salvage modifications and regional population control when asked. Powered fist kills restore energy; rail kills repair armour. Three damage ranks make beams chain. " +
                $"Current mission: {Briefing} The following story addresses the traveller as 'you', not the companion. " + Narrative.Context(game.Stage, game.Fall.destroyed);
            words = words.Substring(0, math.min(words.Length, 400));
            var states = game.Presence.fronts.OrderBy(front => front.world == system.Reference ? 0 : 1).Take(24)
                .Select(front => new WorldState { world = system.Descriptor.bodies[front.world].name, region = front.region + 1, aliens = (int)front.aliens, dogs = (int)front.dogs,
                    hostileShips = (int)front.alienShips, ships = (int)front.ships, factory = front.factory, shipyard = front.shipyard }).ToArray();
            string snapshot = JsonUtility.ToJson(new State { stage = game.Stage, depth = game.Depth, location = system.ReferenceBody.name, level = game.Progression.level, experience = game.Progression.experience, salvage = game.Progression.salvage,
                objective = game.Objective, power = (int)game.Player.energy, health = (int)game.Player.health, hull = (int)(100 * game.Player.hull / game.Player.MaxHull), altitude = (int)game.Player.Altitude, worlds = states });
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "companion-state.json"), snapshot);
            persona = persona.Substring(0, math.min(persona.Length, 4000));
            string prompt = "System state (JSON, facts only): " + snapshot + "\n"
                + string.Join("\n", game.Talk.Skip(math.max(0, game.Talk.Count - 6)).Select((line, i) => (i % 2 == 0 ? "Traveller: " : "Robot: ") + line.Substring(0, math.min(line.Length, 240))))
                + "\nTraveller: " + words;

            string writer = Oracle.Writer.ToUpperInvariant();
            Source = $"ASKING {writer} · STORY AND RECENT CONVERSATION";
            Thinking = true;
            try
            {
                Line = (await Oracle.Ask(persona, prompt)).Trim();
                Source = $"REPLY BY {writer}";
                game.Talk.Add(words);
                game.Talk.Add(Line);
                game.Write();
            }
            catch (Exception)
            {
                Line = Canned(words);
                Source = "AUTHORED REPLY · MODEL UNAVAILABLE";
            }
            Thinking = false;
            Spoke = Time.unscaledTime;
            game.Speech.Say(Line);
        }
    }
}
