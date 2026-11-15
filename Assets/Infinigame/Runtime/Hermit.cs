using System;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Infinigame
{
    // The one person on the home world: a hermit camped a short walk from where the player wakes.
    // Walk up and talk. He gives the first weapon, and what he has to say about each mission needs
    // no model to say it. Anything the player types is answered by the oracle, in character, from
    // what the game knows: the mission, the worlds of the system, the hour and the weather at the
    // camp and what has been scanned. With no oracle to ask he falls back on a few set answers.
    // Once the ship is won back he leaves his fire to fly in it as copilot: he can be talked to
    // from anywhere, knows how the flight is going and speaks up of his own accord. Whatever he
    // says is spoken aloud as well as written.
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
        public Scannable Likeness => new Scannable { name = "human hermit in a patched robe and a straw hat, leaning on a staff beside a campfire", foot = Position, height = 1.9f, girth = 0.6f };

        Transform camp, figure;
        bool seated, unasked;

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
            float3 landing = body.FindLanding(system.seed + 1u), spot = body.FindCamp(landing);
            Position = spot * body.GroundRadius(spot);
            camp = Models.Camp();
            camp.SetParent(game.System.Bodies[SolarSystemDescriptor.Home].transform, false);
            camp.localPosition = Position - spot * 0.05f;
            // facing the landing site, to watch the traveller arrive
            camp.localRotation = Quaternion.LookRotation(Walker.Project(landing - spot, spot), spot);
            figure = camp.Find("Hermit");
            Effects.Kindle(camp.Find("Fire"));
            Talking = Thinking = false;
            Line = Draft = "";
            Spoke = float.MinValue;
        }

        // He sits beside the pilot once he flies with them, and is back by his fire in a game where he does not yet.
        void Update()
        {
            if (figure == null || seated == Aboard || !(game.Player.Seat is Transform seat)) return;
            seated = Aboard;
            figure.SetParent(seated ? seat : camp, false);
            // sat down he is half the height he stands, and his staff and lantern stay behind: the cabin has no room for them
            figure.localScale = Vector3.one * (seated ? 0.5f : 1f);
            foreach (string carried in new[] { "Staff", "Lantern", "Lamp" }) figure.Find(carried).gameObject.SetActive(!seated);
        }

        public void Greet()
        {
            Talking = true;
            unasked = false;
            Draft = "";
            // what he makes of the mission in hand comes first, aloud if he has not said it already
            if (Line != Briefing) game.Speech.Say(Briefing);
            Line = Briefing;
            Spoke = Time.unscaledTime;
            // meeting him is a mission of its own: he hands over a pistol
            if (game.Stage == Campaign.Stranger) game.Advance();
        }

        // Something he says without being asked, from the seat beside the pilot.
        public void Remark(string words)
        {
            if (!Aboard || Talking) return;
            unasked = true;
            Line = words;
            Spoke = Time.unscaledTime;
            game.Speech.Say(words);
        }

        public string Briefing => game.Stage >= Campaign.Won ? "They are gone, and the sky is quiet for the first time I can remember. Go and find out who you are." : game.Mission.brief;

        // What he can say with no oracle to ask.
        string Canned(string asked) => asked.IndexOf("who", StringComparison.OrdinalIgnoreCase) >= 0 ? $"{Name}. I watch the sky and keep this fire in. That is most of what there is to me."
            : asked.IndexOf("happen", StringComparison.OrdinalIgnoreCase) >= 0 || asked.IndexOf("remember", StringComparison.OrdinalIgnoreCase) >= 0
                ? "A ship came down burning a few nights back, with the raiders after it. Then you came up out of the soil. I can add, traveller."
            : Briefing;

        // One frame of conversation: typing, sending with confirm, leaving with the menu key.
        public void Converse(Controls c)
        {
            if (c.menu) Talking = false;
            if (!Talking || Thinking) return;
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

        // Sends the traveller's words and waits for the reply. The talk so far is kept as
        // alternating lines: the traveller's, then the hermit's.
        async void Say(string words)
        {
            var system = game.System;
            var home = system.Descriptor.bodies[SolarSystemDescriptor.Home];
            string persona =
                $"You are {Name}, a hermit and self-taught naturalist who has lived alone for decades by a campfire on {home.name}, " +
                "a living world barely a kilometre across in a toy-sized solar system. Nobody here finds its size odd. You are warm, " +
                "dry-witted and curious about travellers. This one clawed up out of the ground beside your fire with no memory of anything, after a ship came down " +
                "burning with the raiders after it: iron machines, fond of spikes and furnaces, that hold every world of this system. " +
                $"What you want the traveller to do next, in your own words: {Briefing} You know your sky: " +
                // the star, the planets and the home world's own moon: the far moons are beyond a hermit's eyesight
                string.Join(" ", system.Descriptor.bodies.Where((body, index) => body.orbit.parent <= 0 || index == SolarSystemDescriptor.Moon).Select(Scan.Describe)) +
                $" Speak only as {Name}: one to three short sentences of spoken words, with no stage directions, quotation marks or formatting. " +
                "Asked about anything beyond these worlds, answer as someone who has never left them.";
            string discoveries = string.Join(", ", game.Journal.Select(entry => entry.title));
            var player = game.Player;
            string where = !Aboard ? $"You are {Scan.Place(system.Descriptor, SolarSystemDescriptor.Home, Position, system.Clock)}."
                // as copilot he knows what the instruments say
                : "You have left your fire to fly beside the traveller as copilot of their small ship. " + (!player.flying
                    ? $"They are out on foot on {system.ReferenceBody.name} and you are minding the ship, talking over the radio."
                    : player.ship.landed ? $"The ship is landed on {system.ReferenceBody.name}."
                    : $"You are in flight at {math.length(player.Velocity):0} metres a second, {player.Range:0} metres from the surface of {system.Descriptor.bodies[player.Ahead].name}"
                        + (player.Autopilot ? $", with the autopilot taking you to {system.Descriptor.bodies[player.Destination].name}" : "") + ".")
                    + $" The hull is at {100f * player.hull / player.MaxHull:0} per cent{(player.Irradiated ? " and the sun's radiation is eating it" : "")}. What is in hand: {game.Objective.ToLowerInvariant()}.";
            string prompt =
                $"{where} The traveller's scanner journal holds: {(discoveries.Length == 0 ? "nothing yet" : discoveries)}.\n\n" +
                string.Concat(game.Talk.Skip(math.max(0, game.Talk.Count - 12)).Select((line, i) => $"{(i % 2 == 0 ? "Traveller" : Name)}: {line}\n")) +
                $"Traveller: {words}\n\nReply as {Name}.";

            Thinking = true;
            try
            {
                Line = (await Oracle.Ask(persona, prompt)).Trim();
                game.Talk.Add(words);
                game.Talk.Add(Line);
                game.Write();
            }
            catch (Exception)
            {
                Line = Canned(words);
            }
            Thinking = false;
            Spoke = Time.unscaledTime;
            game.Speech.Say(Line);
        }
    }
}
