using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Infinigame.Tests
{
    // Drives the real scene with scripted controls and a scripted oracle. Any error logged while a test runs fails it.
    [Timeout(180000)]
    public partial class PlayTests
    {
        const int Home = SolarSystemDescriptor.Home, Moon = SolarSystemDescriptor.Moon;

        Game game;
        Controls input;
        int quality, asked;
        string lastPersona, lastPrompt;

        Player Player => game.Player;
        SolarSystem Solar => game.System;
        BodyDescriptor Body => Solar.ReferenceBody;
        float Altitude => Player.Altitude;
        // The direction of the sun from the centre of the body the player is on.
        float3 Sunward => math.normalize(Solar.ToReference(0));

        [UnitySetUp]
        public IEnumerator LoadGame()
        {
            Debug.Log("PLAY TEST: " + TestContext.CurrentContext.Test.Name);
            quality = QualitySettings.GetQualityLevel();
            Game.SaveFile = "test-save.json";
            Oracle.Store = "test-accounts.json";
            Game.Prefs = "infinigame-test";
            // most tests want the whole game open to them: every mission but the last done, and all that those win
            Game.Start = Campaign.Citadel;
            foreach (string kept in new[] { "detail", "muted", "silent", "voice", "robot", "subtitles", "robotStrength", "automaticQuality" }) PlayerPrefs.DeleteKey($"{Game.Prefs}.{kept}");
            foreach (string kept in new[] { "test-save.json", "test-save2.json", "test-save3.json", Oracle.Store }) File.Delete(Path.Combine(Application.persistentDataPath, kept));
            // no test may spend a real player's plan: the oracle answers from a script, and until a
            // test supplies stand-ins there are no tools for it to run
            Oracle.Tools[0] = Oracle.Tools[1] = "not-installed";
            // nor is anything said aloud unless a test listens for it
            Speech.Render = (words, directory) => null;
            VoiceInput.Capture = capture => capture.Submit("What should I do now?");
            asked = 0;
            Oracle.Ask = (persona, prompt) =>
            {
                asked++;
                lastPersona = persona;
                lastPrompt = prompt;
                return Task.FromResult(persona.Contains("recovery robot")
                    ? "Well now, a visitor. Sit by the fire and tell me which of the lights up there you came down from."
                    : "Lantern Fir\nA resinous conifer that folds its lower boughs at dusk. Herds shelter beneath it when the storms come in off the sea.");
            };
            FrameLog.File = Path.Combine(Application.temporaryCachePath, "test-frames.log");
            FrameLog.Every = 0.5f;
            File.Delete(FrameLog.File);
            yield return SceneManager.LoadSceneAsync("Main");
            game = Object.FindAnyObjectByType<Game>();
            while (!game.Ready) yield return null;
            input = default;
            Player.controls = () => input;
            // foes turn up only where a test puts them
            game.Battle.quiet = true;
            yield return null;
            // the game opens on its title page: play the slot it offers, from the ground beside the ship
            Assert.IsTrue(game.Title);
            yield return Press(new Controls { confirm = true });
            if (game.Confirming) { yield return Press(new Controls { down = true }); yield return Press(new Controls { confirm = true }); }
            Player.Spawn();
            yield return null;
        }

        [TearDown]
        public void Restore()
        {
            Time.timeScale = 1f;
            QualitySettings.SetQualityLevel(quality);
            Game.SaveFile = "save.json";
            Oracle.Ask = Oracle.Run;
            Oracle.Tools[1] = "codex";
            Oracle.Tools[0] = "claude";
            Speech.Render = Speech.Festival;
            VoiceInput.Capture = null;
            Speech.Muted = false;
            (Oracle.Hosts[0], Oracle.Hosts[1], Oracle.Hosts[2]) = ("https://api.anthropic.com", "https://api.openai.com", "https://auth.openai.com");
            Oracle.Browse = Application.OpenURL;
            Oracle.Store = "accounts.json";
            Game.Prefs = "infinigame";
            Game.Start = 0;
            Music.Muted = false;
            Game.Clipboard = () => GUIUtility.systemCopyBuffer;
            FrameLog.File = null;
            FrameLog.Every = 5f;
        }

        static IEnumerator Seconds(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }

        // Holds the given controls for one frame, as a key press would.
        IEnumerator Press(Controls pressed)
        {
            var held = input;
            input = pressed;
            yield return null;
            input = held;
            yield return null;
        }

        // Puts the player in a hovering ship at a point given relative to the reference body.
        void HoverAt(float3 position)
        {
            Player.flying = true;
            Player.ship.landed = false;
            Player.ship.position = position;
            Player.ship.velocity = float3.zero;
        }

        // Hovers on the side of a body that faces where the player came from, which then becomes the reference.
        IEnumerator HoverOver(int body, float altitude)
        {
            float3 to = Solar.ToReference(body);
            HoverAt(to + math.normalize(-to) * (Solar.Descriptor.bodies[body].radius + altitude));
            yield return null;
            yield return null;
            // Standing still in the frame of the turning world just left is a great speed in the new
            // one: stop dead there, as HoverAt means to.
            HoverAt(math.normalize(Player.ship.position) * (Solar.Descriptor.bodies[body].radius + altitude));
            yield return null;
        }

        void Stand(float3 direction, float3 facing)
        {
            Player.flying = false;
            Player.walker = Walker.Spawn(Body, direction, facing);
        }

        [UnityTest]
        public IEnumerator StandsOnTheHomeWorldWhileTerrainStreamsIn()
        {
            yield return Seconds(3f);
            Assert.AreEqual(Home, Solar.Reference);
            Assert.IsFalse(Player.flying);
            Assert.IsTrue(Player.walker.grounded);
            Assert.AreEqual(0f, Altitude, 0.05f);
            Assert.Greater(Solar.Bodies[Home].PatchCount, 20);
            Assert.Less(Vector3.Distance(Camera.main.transform.position, Player.walker.position), 3f);
            Assert.AreEqual(31, Solar.Bodies.Length, "a star, eight planets, twenty-one moons and the citadel");
        }

        [UnityTest]
        public IEnumerator StaysSilentWhenNobodyIsWatching()
        {
            yield return null;
            Assert.AreEqual(Application.isBatchMode ? 0f : 1f, AudioListener.volume);
        }

        [UnityTest]
        public IEnumerator EscapeOpensAMenuThatPausesTheWorldAndAsksBeforeQuitting()
        {
            yield return Press(new Controls { menu = true });
            Assert.IsTrue(game.Paused);
            double paused = Solar.Clock;
            yield return null;
            yield return null;
            Assert.AreEqual(paused, Solar.Clock, 1e-3);

            // up from the first item wraps round to the last, which is quitting
            yield return Press(new Controls { up = true });
            Assert.AreEqual("Quit to desktop", game.Items[game.Selected]);
            yield return Press(new Controls { confirm = true });
            Assert.IsTrue(game.Confirming, "quitting asks first");
            Assert.AreEqual("Cancel", game.Items[game.Selected]);
            yield return Press(new Controls { menu = true });
            Assert.IsFalse(game.Confirming);
            Assert.IsTrue(game.Paused);

            yield return Press(new Controls { menu = true });
            Assert.IsFalse(game.Paused);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator ControlsStayOffScreenUntilAskedFor()
        {
            yield return null;
            Assert.IsFalse(game.ShowControls);
            yield return Press(new Controls { help = true });
            Assert.IsTrue(game.ShowControls);
            // escape closes them, and that is all it does
            yield return Press(new Controls { menu = true });
            Assert.IsFalse(game.ShowControls);
            Assert.IsFalse(game.Paused);
            // the pause menu can show them too
            yield return Choose("Controls");
            Assert.IsTrue(game.ShowControls);
            Assert.IsFalse(game.Paused);
        }

        [UnityTest]
        public IEnumerator WalksForward()
        {
            yield return Seconds(0.5f);
            float3 start = Player.walker.position;
            input.move = new Vector2(0f, 1f);
            yield return Seconds(3f);
            Assert.That(math.distance(start, Player.walker.position), Is.InRange(2.5f * Tuning.WalkSpeed, 3.2f * Tuning.WalkSpeed));
        }

        [UnityTest]
        public IEnumerator BoardsFliesLandsAndLeaves()
        {
            Player.walker = Walker.Spawn(Body, Player.ship.position + math.rotate(Player.ship.rotation, new float3(3f, 0, 0)), Player.walker.forward);
            yield return null;
            Assert.AreEqual("Board ship", game.Prompt);
            yield return Press(new Controls { interact = true });
            Assert.IsTrue(Player.flying, "F next to the ship boards it");
            var cockpit = GameObject.Find("Ship").transform.Find("Cockpit").gameObject;
            Assert.IsTrue(cockpit.activeSelf, "the pilot sees the cockpit");

            input.lift = 1f;
            yield return Seconds(2f);
            input = default;
            Assert.IsFalse(Player.ship.landed);
            Assert.Greater(Altitude, 10f);
            yield return Press(new Controls { toggleView = true });
            Assert.IsFalse(cockpit.activeSelf, "the chase view shows the ship from outside");

            yield return Press(new Controls { interact = true });
            Assert.IsTrue(Player.landing, "F in flight starts the landing assist");
            for (float t = 0; t < 30f && !Player.ship.landed; t += Time.unscaledDeltaTime) yield return null;
            Assert.IsTrue(Player.ship.landed);
            StringAssert.StartsWith("Landing confirmed", game.Hermit.Line);

            yield return Press(new Controls { interact = true });
            Assert.IsFalse(Player.flying, "F on the ground leaves the ship");
            yield return Seconds(1f);
            Assert.IsTrue(Player.walker.grounded);
            Assert.Less(math.distance(Player.walker.position, Player.ship.position), 8f);
        }

        [UnityTest]
        public IEnumerator ReferenceSwitchesToTheMoonWithoutAJump()
        {
            yield return HoverOver(Moon, 60f);
            Assert.AreEqual(Moon, Solar.Reference);
            Assert.AreEqual(Solar.Descriptor.bodies[Moon].radius + 60f, math.length(Player.ship.position), 5f);
            Assert.Less(Solar.Bodies[Moon].transform.position.magnitude, 1e-3f, "the reference body sits at the origin");
            yield return Seconds(2f);
            Assert.AreEqual(Moon, Solar.Reference);
            Assert.Greater(Solar.Bodies[Moon].PatchCount, 6);
        }

        [UnityTest]
        public IEnumerator TheSceneRidesWithTheShipBetweenTheWorlds()
        {
            // straight up out of the home world's influence, into the sun's
            HoverAt(math.normalize(Player.ship.position) * (Body.soiRadius * 2f));
            yield return null;
            yield return null;
            Assert.AreEqual(0, Solar.Reference);
            Assert.Greater(math.length(Player.ship.position), 10000f, "the ship is far from the sun");
            Assert.Less(Camera.main.transform.position.magnitude, 50f, "yet it is drawn at the origin, where floats are fine");
            Assert.Greater(Solar.Bodies[0].transform.position.magnitude, 10000f);
        }

        [UnityTest]
        public IEnumerator GiantsOfferNowhereToLand()
        {
            int giant = System.Array.FindIndex(Solar.Descriptor.bodies, body => body.kind == BodyKind.Gas);
            yield return HoverOver(giant, 80f);
            Assert.AreEqual(giant, Solar.Reference);
            Assert.IsNull(game.Prompt);
            yield return Press(new Controls { interact = true });
            Assert.IsFalse(Player.landing);
            yield return Seconds(1.5f);
            Assert.IsFalse(Player.ship.landed);
            Assert.Greater(Solar.Bodies[giant].PatchCount, 5);
        }

        // Opens the pause menu and chooses an item by name.
        IEnumerator Choose(string item)
        {
            yield return Press(new Controls { menu = true });
            for (int i = 0; i < System.Array.IndexOf(game.Items, item); i++) yield return Press(new Controls { down = true });
            Assert.AreEqual(item, game.Items[game.Selected]);
            yield return Press(new Controls { confirm = true });
        }

        [UnityTest]
        public IEnumerator ANewGameBeginsBuriedInANewSystemInEmergencyArmour()
        {
            uint seed = game.Seed;
            var before = Solar.Bodies[Home];
            Player.health = 40f;
            Assert.IsTrue(Player.Has(Gear.Ship));
            yield return Choose("New game");
            Assert.AreEqual(seed, game.Seed);
            Assert.IsTrue(game.Confirming);
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreNotEqual(seed, game.Seed);
            Assert.IsTrue(before == null, "the old planet is destroyed");
            Assert.AreEqual(SolarSystemDescriptor.Generate(game.Seed).bodies[Home].surfaceGravity, Body.surfaceGravity, "terrain and gravity are rebuilt together");
            Assert.AreEqual(1, game.Depth);
            Assert.AreEqual(Campaign.Buried, game.Stage);
            Assert.AreEqual(1 << (int)Gear.Suit, Player.kit, "only emergency armour comes along");
            Assert.AreEqual(Player.Full, Player.health);
            Assert.IsFalse(game.Paused);
        }

        [UnityTest]
        public IEnumerator YouWakeBuriedAndDigYourselfOutIntoDaylight()
        {
            game.Jump(Campaign.Buried);
            // long enough to have dropped to the bottom of the hole
            yield return Seconds(Game.BootDuration + 0.5f);
            Assert.IsTrue(game.Buried);
            StringAssert.Contains("PUNCH", game.Objective);
            var ground = Solar.Bodies[Home].Descriptor;
            int holes = ground.craters.Length;
            Assert.Greater(holes, 0, "there is a hole in the ground, and the player is at the bottom of it");
            float3 woke = Player.walker.position;
            Assert.Less(math.length(woke), ground.radius + ground.terrain.MaxHeight);
            // there is nothing to do down there but dig: no walking off, and nothing in hand to fire
            input.move = Vector2.up;
            yield return Seconds(1f);
            input.move = Vector2.zero;
            Assert.Less(math.distance(woke, Player.walker.position), 0.5f);
            Assert.IsTrue(Player.Armed);

            // each click tears more of it away, and holding the button down is still only one click
            for (int click = 0; click < Game.Digs; click++)
            {
                Assert.IsTrue(game.Buried);
                input.fire = true;
                yield return null;
                yield return null;
                input.fire = false;
                yield return null;
                Assert.AreEqual(click + 1 == Game.Digs ? 0 : click + 1, game.Dug % Game.Digs);
            }
            Assert.AreEqual(Campaign.Stranger, game.Stage, "daylight");
            Assert.AreEqual(holes + Game.Digs, ground.craters.Length, "the earth really was dug away");
            StringAssert.Contains("FOLLOW THE ROBOT", game.Objective);
            // and the way out is walkable
            input.move = Vector2.up;
            yield return Seconds(2f);
            Assert.Greater(math.distance(woke, Player.walker.position), 4f);
        }

        [UnityTest]
        public IEnumerator TheRobotWakesTheSuitAndOutpostsNeedTheirMindsSilenced()
        {
            game.Jump(Campaign.Stranger);
            yield return null;
            game.Hermit.Greet();
            Assert.IsTrue(Player.Has(Gear.Suit));
            Assert.AreEqual(2, game.Stage);
            yield return Press(new Controls { menu = true });
            var outpost = game.Outpost;
            Player.walker.position = outpost.Place(new float3(0, 0, -8));
            Player.flying = false;
            yield return Seconds(0.2f);
            Assert.IsTrue(game.Battle.Foes.Any(foe => foe.kind == FoeKind.Mind));
            game.Award(FoeKind.Drone);
            Assert.IsFalse(outpost.Fallen, "patrol kills do not count as silencing the mind");
            Assert.IsFalse(outpost.AtConsole);
            Assert.IsFalse(outpost.Use());
            game.Award(FoeKind.Mind);
            Assert.IsTrue(outpost.Fallen);
            Assert.IsFalse(outpost.Shield.active);
            Assert.IsFalse(Player.Has(Gear.Thrusters), "the prize still has to be collected");
            Player.walker.position = outpost.Prize;
            yield return Press(new Controls { interact = true });
            Assert.AreEqual(3, game.Stage);
            Assert.IsTrue(Player.Has(Gear.Thrusters));
            Assert.IsEmpty(game.Colonies, "the player must build the first factory");
        }

        [UnityTest]
        public IEnumerator FullSongsAdvanceThroughStreamingDecksWithoutLooping()
        {
            var music = game.GetComponent<Music>();
            var clips = Enumerable.Range(0, 3).Select(i => AudioClip.Create($"test-song-{i}", 44100, 1, 22050, false)).ToArray();
            music.SetTracks(clips, new AudioClip[0]);
            var heard = new HashSet<AudioClip>();
            for (float until = Time.realtimeSinceStartup + 8; Time.realtimeSinceStartup < until;)
            {
                foreach (var source in game.GetComponents<AudioSource>().Where(source => clips.Contains(source.clip)))
                {
                    Assert.IsFalse(source.loop);
                    if (source.isPlaying) heard.Add(source.clip);
                    if (Application.isBatchMode) Assert.AreEqual(0, source.volume);
                }
                yield return null;
            }
            Assert.AreEqual(3, heard.Count);
            music.enabled = false;
            foreach (var source in game.GetComponents<AudioSource>().Where(source => clips.Contains(source.clip))) { source.Stop(); source.clip = null; }
            foreach (var clip in clips) Object.Destroy(clip);
        }





        [UnityTest]
        public IEnumerator ReturningToAnOutpostKeepsItsMindAndDamage()
        {
            game.Jump(2);
            Player.walker.position = game.Outpost.Place(new float3(0, 0, -8));
            yield return null;
            game.Outpost.Tick();
            int index = game.Battle.Foes.FindIndex(foe => foe.kind == FoeKind.Mind);
            Assert.GreaterOrEqual(index, 0);
            var mind = game.Battle.Foes[index];
            mind.health = 17;
            game.Battle.Foes[index] = mind;
            game.Battle.Rebase(Home, Moon);
            game.Battle.Rebase(Moon, Home);
            game.Outpost.Tick();
            Assert.AreEqual(17, game.Battle.Foes.Single(foe => foe.kind == FoeKind.Mind).health);
        }

        [UnityTest]
        public IEnumerator EmergencyFistsSilenceAMindInsideItsBurrow()
        {
            game.Jump(2);
            Player.health = 10000;
            Player.walker.position = game.Outpost.Place(game.Outpost.Rooms.core + new float3(0, -0.6f, -3));
            Player.walker.forward = math.normalize(Walker.Project(game.Outpost.Core - Player.Position, math.normalize(Player.Position)));
            yield return null;
            input.fire = true;
            for (float until = Time.unscaledTime + 10; !game.Outpost.Fallen && Time.unscaledTime < until;) yield return null;
            input.fire = false;
            Assert.IsTrue(game.Outpost.Fallen);
            Assert.IsTrue(game.Battle.Foes.All(foe => foe.scattered));
            Assert.Greater(game.Frontier.Local.kills, 0);
        }

        [UnityTest]
        public IEnumerator TheWayToTheRaidersIsAlwaysMarked()
        {
            game.Jump(2);
            yield return Seconds(0.5f);
            var outpost = game.Outpost;
            var hud = GameObject.Find("HUD").GetComponent<RectTransform>();
            UnityEngine.UI.Text Shown(string words) => hud.GetComponentsInChildren<UnityEngine.UI.Text>().FirstOrDefault(text => text.enabled && text.text == words);
            float3 up = math.normalize(Player.walker.position), towards = math.normalize(Walker.Project(outpost.Position - Player.walker.position, up));
            // the outpost is over the horizon of so small a world from where the player wakes
            Assert.Greater(math.distance(Player.walker.position, outpost.Position), 100f);
            Assert.Less(math.dot(math.normalize(outpost.Position), up) * Body.GroundRadius(math.normalize(outpost.Position)), math.length(Player.walker.position) - 5f);

            // facing it, its mark stands in the sky over the way there, with how far it is
            Player.walker.forward = towards;
            yield return null;
            yield return null;
            var mark = Shown("THE FIRST BURROW");
            Assert.IsNotNull(mark, "marked, though the ground is in the way");
            Vector2 middle = hud.rect.size / 2f, at = ((RectTransform)mark.transform.parent).anchoredPosition;
            Assert.Less(Mathf.Abs(at.x - middle.x), 60f, "dead ahead");
            Assert.Less(Mathf.Abs(at.y - middle.y), 120f, "on the horizon");
            // facing away, it waits at the edge of the view on the side to turn to
            Player.walker.forward = math.cross(up, towards);
            yield return null;
            yield return null;
            at = ((RectTransform)Shown("THE FIRST BURROW").transform.parent).anchoredPosition;
            Assert.Less(at.x, 80f, "it is to the left, and so is its mark");
            Player.walker.forward = -towards;
            yield return null;
            yield return null;
            at = ((RectTransform)Shown("THE FIRST BURROW").transform.parent).anchoredPosition;
            Assert.IsTrue(Mathf.Abs(at.x - middle.x) > middle.x - 80f || Mathf.Abs(at.y - middle.y) > middle.y - 80f, "behind: still at an edge");
            // and the radar carries a mark of its own for it
            Assert.AreEqual(outpost.Position, game.Goal);

            // the mission is set out in full as it begins
            Assert.IsNotNull(Shown("The first burrow"));
            Assert.IsTrue(hud.GetComponentsInChildren<UnityEngine.UI.Text>().Any(text => text.text.Contains("Suit thrusters") && text.text.Contains("The first factory")), "what it wins, and what comes next");
            // and the map, until there is a ship to cross the system in, is of the world underfoot with both ends of the way on it
            input.systemView = true;
            yield return Seconds(0.5f);
            Assert.IsTrue(Player.PlanetView);
            Assert.That(Camera.main.transform.position.magnitude - Body.radius, Is.InRange(500f, 1500f), "near enough to make out a short walk");
            Assert.IsFalse(Solar.Bodies[Home].transform.Find("Clouds").gameObject.activeSelf, "and with the cloud out of the way");
            Assert.IsNotNull(Shown("YOU"));
            Assert.IsNotNull(Shown("THE FIRST BURROW"));
            Assert.IsNotNull(Shown("The first burrow"));
            input.systemView = false; yield return null;
            yield return Press(new Controls { systemView = true });
            yield return null;

            // before that, the way to the stranger's fire is marked the same
            game.Jump(Campaign.Stranger);
            yield return Seconds(0.3f);
            Assert.AreEqual(game.Hermit.Position, game.Goal);
            Assert.IsNotNull(Shown(game.Hermit.Name.ToUpperInvariant()));
        }



        [UnityTest]
        public IEnumerator TheCitadelCoreRequiresEntryAndEscapeBeforeMoonfall()
        {
            yield return HoverOver(Solar.Station, 500);
            game.Battle.Foes.Clear();
            game.Outpost.Tick();
            Assert.IsTrue(game.Outpost.Shield.active);
            Assert.IsFalse(game.CanWarp);
            float3 core = game.Outpost.Core;
            game.Battle.Foes.Add(Combat.Spawn(FoeKind.Mind, core, new float3(0, 0, -1), 1, 1));
            var target = game.Battle.Foes.Last();
            game.Battle.Bolts.Add(Combat.Fire(new float3(160, 0, 600), new float3(0, 0, -1), float3.zero, 1000, 800, false, 60));
            yield return Seconds(0.5f);
            Assert.AreEqual(target.health, game.Battle.Foes.Last(foe => foe.kind == FoeKind.Mind).health);
            Assert.AreEqual(3, game.Outpost.Left);
            game.Award(FoeKind.Mind);
            Assert.IsFalse(game.Outpost.Fallen);
            game.Award(FoeKind.Mind);
            game.Award(FoeKind.Mind);
            Assert.IsTrue(game.Outpost.Fallen);
            Assert.Greater(game.Outpost.Escape, 0);
            HoverAt(new float3(0, 0, -400));
            yield return null;
            yield return null;
            Assert.AreEqual(Campaign.Won, game.Stage);
            Assert.IsTrue(Player.Has(Gear.GateParts));
            Assert.IsFalse(game.CanWarp, "crossing systems now requires moonfall");
            int depth = game.Depth;
            bool invalidFacing = false;
            void Log(string message, string trace, LogType kind) => invalidFacing |= message.Contains("Look rotation viewing vector is zero");
            Application.logMessageReceived += Log;
            try { game.NextSystem(); }
            finally { Application.logMessageReceived -= Log; }
            Assert.IsFalse(invalidFacing, "the robot needs a valid facing even if no separate camp site can be found");
            yield return null;
            Assert.AreEqual(depth + 1, game.Depth);
            Assert.IsTrue(Solar.Descriptor.generated);
            Assert.IsFalse(game.GatePowered);
            Assert.AreEqual(-1, Solar.Gate);
        }

        [UnityTest]
        public IEnumerator DetailTiersCanBeSwitchedWhilePlaying()
        {
            // terrain streams in over a few seconds, so compare the counts once each has stopped changing
            IEnumerator Settle()
            {
                for (int last = -1; last != Solar.Bodies[Home].PatchCount;)
                {
                    last = Solar.Bodies[Home].PatchCount;
                    yield return Seconds(1f);
                }
            }
            yield return Settle();
            int high = Solar.Bodies[Home].PatchCount;
            yield return Press(new Controls { tierDown = true });
            yield return Press(new Controls { tierDown = true });
            Assert.AreEqual(0, QualitySettings.GetQualityLevel());
            yield return Settle();
            Assert.That(Solar.Bodies[Home].PatchCount, Is.InRange(6, high - 1));
        }

        [UnityTest]
        public IEnumerator ItRainsUnderAStorm()
        {
            float3 storm = Weather.Storm(Body, 0, Solar.Clock).xyz;
            Stand(storm, World(storm));
            yield return null;
            yield return null;
            Assert.Greater(Solar.Rain, 0.9f);
            // above the clouds it is dry
            HoverAt(storm * (Body.radius * 1.6f));
            yield return null;
            yield return null;
            Assert.AreEqual(0f, Solar.Rain);
        }

        [UnityTest]
        public IEnumerator AnimalsLiveWhereTheCoreSaysTheyDo()
        {
            int depth = QuadtreeLod.DepthFor(Body.radius, Wildlife.CellSize(Creature.Grazer));
            Troop herd = default;
            for (int cell = 0; herd.count == 0; cell++)
                Wildlife.Find(Body, Creature.Grazer, new NodeId(cell % 6, depth, cell / 6 % (1 << depth), cell / 6 / (1 << depth)), out herd);
            Stand(math.normalize(herd.centre + World(herd.centre) * (25f / Body.radius)), -World(herd.centre));
            // herds are looked for off the main thread, so give them a moment to be found
            yield return Seconds(1f);
            var seen = Solar.Bodies[Home].GetComponent<Creatures>().Seen;
            Assert.GreaterOrEqual(seen.Count(animal => animal.name.StartsWith("grazing")), herd.count);
            Assert.IsTrue(seen.Any(animal => math.distance(animal.foot, herd.centre * herd.level) < 20f));
        }

        [UnityTest]
        public IEnumerator ScanningAsksTheOracleOnceAndTheJournalRemembers()
        {
            // from high above, aim the ship at the moon
            HoverAt(math.normalize(Solar.ToReference(Moon)) * (Body.radius * 2.5f));
            Player.ship.rotation = quaternion.LookRotationSafe(Solar.ToReference(Moon) - Player.ship.position, new float3(0, 1, 0));
            yield return null;
            yield return Press(new Controls { scan = true });
            Assert.IsFalse(game.Scanner.Busy);
            Assert.AreEqual(Solar.Descriptor.bodies[Moon].name, game.Scanner.Subject);
            Assert.AreEqual(1, asked);
            StringAssert.Contains(Solar.Descriptor.bodies[Moon].summary, lastPrompt, "the oracle is told the facts");
            Assert.AreEqual(1, game.Journal.Count);
            Assert.AreEqual("Lantern Fir", game.Journal[0].title);
            StringAssert.StartsWith("A resinous conifer", game.Scanner.Result.text);

            yield return Press(new Controls { scan = true });
            Assert.AreEqual(1, asked, "a second look comes from the journal");
            Assert.AreEqual(1, game.Journal.Count);
            StringAssert.Contains("Lantern Fir", File.ReadAllText(Path.Combine(Application.persistentDataPath, Game.SaveFile)));
        }

        [UnityTest]
        public IEnumerator ScanningTheGroundDescribesWhereYouStand()
        {
            yield return Seconds(1f);
            // look down at your feet
            yield return Press(new Controls { look = new Vector2(0f, -80f) });
            yield return Press(new Controls { scan = true });
            Assert.AreEqual(1, game.Journal.Count);
            StringAssert.Contains($"on {Body.name} at", lastPrompt);
            StringAssert.Contains("above the sea", lastPrompt);
        }

        [UnityTest]
        public IEnumerator TheHermitTalksAndRemembers()
        {
            // early on, while he still keeps to his fire
            game.Jump(2);
            yield return null;
            var hermit = game.Hermit;
            Assert.Less(math.distance(hermit.Position, Player.walker.position), 80f, "the camp is a short walk from the landing site");
            Assert.Greater(math.distance(hermit.Position, Player.ship.position), 8f, "and clear of the ship");
            Stand(math.normalize(hermit.Position + World(math.normalize(hermit.Position)) * 12f), World(math.normalize(hermit.Position)));
            yield return null;
            Assert.IsFalse(hermit.Near);
            Stand(math.normalize(hermit.Position + World(math.normalize(hermit.Position)) * 2.5f), World(math.normalize(hermit.Position)));
            yield return null;
            Assert.AreEqual($"Talk to {hermit.Name}", game.Prompt);

            yield return Press(new Controls { interact = true });
            Assert.IsTrue(hermit.Talking);
            Assert.AreEqual(game.Mission.brief, hermit.Line, "what he makes of the mission in hand comes first, and needs no model");
            Assert.AreEqual(0, asked);

            // what is pressed goes to the hermit, not to the game: nobody shoots or walks off in mid sentence
            float3 stood = Player.walker.position;
            yield return Press(new Controls { typed = "where are the rings?", fire = true, move = Vector2.up });
            Assert.AreEqual("where are the rings?", hermit.Draft);
            Assert.AreEqual(0, game.Battle.Bolts.Count);
            yield return Press(new Controls { erase = true });
            Assert.AreEqual("where are the rings", hermit.Draft);
            yield return Press(new Controls { confirm = true });
            StringAssert.Contains("Traveller: where are the rings", lastPrompt);
            StringAssert.Contains(hermit.Name, lastPersona);
            StringAssert.Contains(game.Mission.brief, lastPersona, "the model is told what he wants of the traveller");
            StringAssert.StartsWith("Well now, a visitor", hermit.Line);
            Assert.AreEqual(2, game.Talk.Count);
            Assert.AreEqual("", hermit.Draft);
            // a number asks one of the questions he always has an answer to, and with no model to ask he answers it himself
            yield return Press(new Controls { typed = "1" });
            StringAssert.Contains("Traveller: Who are you?", lastPrompt);
            Oracle.Ask = (persona, prompt) => Task.FromException<string>(new System.InvalidOperationException("Not signed in to Claude."));
            yield return Press(new Controls { typed = "2" });
            yield return null;
            StringAssert.Contains("recovered you after the crash", hermit.Line);
            yield return Press(new Controls { typed = "what now?" });
            yield return Press(new Controls { confirm = true });
            yield return null;
            Assert.AreEqual(game.Mission.brief, hermit.Line);
            Assert.Less(math.distance(stood, Player.walker.position), 0.5f, "the player stands still to talk");

            yield return Press(new Controls { menu = true });
            Assert.IsFalse(hermit.Talking);
            Assert.IsFalse(game.Paused, "escape left the conversation and nothing more");
        }

        [UnityTest]
        public IEnumerator OnceThereIsAShipTheHermitFliesInItAsCopilot()
        {
            var hermit = game.Hermit;
            Assert.IsTrue(hermit.Aboard);
            // there is nobody at the fire to walk up to
            Stand(math.normalize(hermit.Position + World(math.normalize(hermit.Position)) * 2.5f), World(math.normalize(hermit.Position)));
            yield return null;
            Assert.IsFalse(hermit.Near);
            Assert.AreNotEqual($"Talk to {hermit.Name}", game.Prompt);

            // he is talked to from the pilot's seat instead, and knows what the instruments say
            HoverAt(math.normalize(Player.ship.position) * (Body.radius + 400f));
            yield return null;
            yield return Press(new Controls { talk = true });
            Assert.IsTrue(hermit.Talking);
            Assert.AreEqual(game.Mission.brief, hermit.Line);
            yield return Press(new Controls { typed = "how are we doing?" });
            yield return Press(new Controls { confirm = true });
            StringAssert.Contains("copilot", lastPersona);
            StringAssert.Contains("\"location\":\"" + Body.name + "\"", lastPrompt);
            StringAssert.Contains("\"hull\":100", lastPrompt);
            StringAssert.Contains("DESTROY THE SHIELD CORE", lastPrompt);
            Assert.IsTrue(Player.flying && !Player.ship.landed, "and the ship flies on while they talk");
            yield return Press(new Controls { menu = true });
            Assert.IsFalse(hermit.Talking);

            // what he said when asked does not linger, and he speaks up unasked when the hull is failing
            yield return null;
            Assert.IsFalse(hermit.Remarking);
            Player.hull = 0.2f * Player.MaxHull;
            yield return null;
            yield return null;
            Assert.IsTrue(hermit.Remarking);
            StringAssert.Contains("coming apart", hermit.Line);
            var caption = Object.FindObjectsByType<UnityEngine.UI.Text>().Single(text => text.text.Contains("coming apart"));
            Assert.Greater(caption.GetComponentInParent<CanvasGroup>().alpha, 0f, "written under the view as he says it");
        }

        [UnityTest]
        public IEnumerator ALandingOnGroundTooRoughToSitOnLevelsAPadFirst()
        {
            int Pads() => Body.blasts.Count(blast => blast.scar == Scar.Pad);
            IEnumerator Land(float3 over)
            {
                HoverAt(over * (Body.GroundRadius(over) + 40f));
                yield return null;
                yield return Press(new Controls { interact = true });
                Assert.IsTrue(Player.landing);
                for (float t = 0; t < 25f && !Player.ship.landed; t += Time.unscaledDeltaTime) yield return null;
                Assert.IsTrue(Player.ship.landed, "down");
            }
            int existing = Pads();
            // on level ground there is no need
            yield return Land(Body.FindLanding(game.Seed + 1u));
            Assert.AreEqual(existing, Pads());

            // the steepest dry ground of a few thousand places, well away from the hermit's camp
            var rnd = new Unity.Mathematics.Random(5);
            float3 steep = default;
            float worst = 1f;
            for (int i = 0; i < 4000; i++)
            {
                float3 d = rnd.NextFloat3Direction();
                float level = math.dot(Body.GroundNormal(d), d);
                if (Body.GroundRadius(d) > Body.SeaRadius + 5f && math.distance(d * Body.radius, game.Hermit.Position) > 200f && level < worst) (worst, steep) = (level, d);
            }
            Assert.IsTrue(Ship.Rough(Body, steep));
            yield return Land(steep);
            Assert.AreEqual(existing + 1, Pads(), "a charge went down first");
            var pad = Body.blasts.Last(blast => blast.scar == Scar.Pad);
            Assert.Less(math.distance(pad.crater.xyz, math.normalize(Player.ship.position)) * Body.radius, 3f, "and the ship is on the pad it made");
            Assert.Greater(math.dot(math.rotate(Player.ship.rotation, new float3(0, 1, 0)), math.normalize(Player.ship.position)), 0.98f, "sitting level");
            Assert.AreEqual(Player.MaxHull, Player.hull, 0.5f, "without a scratch");
        }

        [UnityTest]
        public IEnumerator EveryInstrumentInTheCockpitShowsWhatTheShipIsDoing()
        {
            HoverAt(math.normalize(Player.ship.position) * (Body.radius + 300f));
            yield return Seconds(1f);
            var cockpit = Object.FindAnyObjectByType<Cockpit>();
            Assert.IsNotNull(cockpit, "in view from the pilot's seat");
            Quaternion At(string part) => cockpit.transform.Find(part).localRotation;
            string Reads(string screen) => cockpit.transform.Find(screen).GetComponentInChildren<TextMesh>().text;
            // each is set down as it stands, something is done to the ship, and it has moved
            IEnumerator Moves(string part, System.Action act, float wait = 0.6f)
            {
                var before = At(part);
                act();
                yield return Seconds(wait);
                Assert.Greater(Quaternion.Angle(before, At(part)), 3f, part);
            }
            Assert.AreEqual("0", Reads("SpeedScreen"));
            StringAssert.Contains(Body.name.ToUpperInvariant(), Reads("RangeScreen/Legend"));
            yield return Moves("HULL/Needle", () => Player.hull = 0.5f * Player.MaxHull);
            Player.hull = Player.MaxHull;
            yield return Moves("Throttle", () => input.move = Vector2.up);
            yield return Moves("THRUST/Needle", () => input.sprint = true);
            Assert.AreNotEqual("0", Reads("SpeedScreen"));
            (input.move, input.sprint) = (Vector2.zero, false);
            yield return Moves("CLIMB/Needle", () => input.lift = 1f);
            yield return Moves("ALTITUDE/Needle", () => { }, 3f);
            input.lift = 0f;
            yield return Moves("Stick", () => input.look = new Vector2(1.5f, 0f), 0.3f);
            yield return Moves("Attitude/Horizon", () => input.look = new Vector2(0f, 1.5f), 0.5f);
            input.look = Vector2.zero;
            yield return Moves("Selector", () => input.select = 2);
            input.select = 0;
            yield return Moves("ASSIST/Switch", () => Player.assist = false);
            Player.assist = true;
            yield return Moves("LAND/Switch", () => Player.landing = true, 0.3f);
            // and the hermit is in the seat beside the pilot's, without his staff
            var copilot = cockpit.transform.Find("Copilot/Hermit");
            Assert.IsNotNull(copilot);
            Assert.IsNotNull(copilot.Find("Torso/Neck/Head/Three optics"));
        }

        [UnityTest]
        public IEnumerator TheHermitsWordsAreSpokenAloud()
        {
            string spoken = "";
            Speech.Render = (words, directory) => { spoken = words; return Enumerable.Repeat(0.5f, 3200).ToArray(); };
            AudioSource Voice() => game.GetComponents<AudioSource>().FirstOrDefault(source => source.clip != null && source.clip.name == "Speech");

            game.Jump(2);
            yield return null;
            var hermit = game.Hermit;
            Stand(math.normalize(hermit.Position + World(math.normalize(hermit.Position)) * 2.5f), World(math.normalize(hermit.Position)));
            yield return null;
            yield return Press(new Controls { interact = true });
            yield return Until(() => Voice() != null);
            Assert.AreEqual(game.Mission.brief, spoken, "what he says of the mission is read out");
            Assert.AreEqual(3200, Voice().clip.samples);
            Assert.AreEqual(32000, Voice().clip.frequency);
            Assert.AreEqual("cmu_us_slt_arctic_hts", Speech.Choice);
            Assert.AreEqual(1f, Voice().pitch);
            Assert.IsTrue(Voice().ignoreListenerPause);
            // and so is whatever the model answers for him
            yield return Press(new Controls { typed = "1" });
            yield return Until(() => spoken.StartsWith("Well now"));

            // unless the player would rather read
            Speech.Muted = true;
            spoken = "";
            yield return Press(new Controls { typed = "2" });
            yield return Seconds(0.5f);
            Assert.IsEmpty(spoken);
        }

        // A stand-in for Claude Code or Codex: it records how it was called and answers as told.
        static string StandIn(string name, string script)
        {
            string folder = Path.Combine(Application.temporaryCachePath, "stand-in"), tool = Path.Combine(folder, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(tool, "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$0.args\"\n" + script + "\n");
            System.Diagnostics.Process.Start("/bin/chmod", $"+x \"{tool}\"").WaitForExit();
            return tool;
        }

        // A stand-in for the makers' servers: it records what it was asked and answers as told.
        class Server : System.IDisposable
        {
            public readonly string Url;
            public readonly List<(string path, string body, NameValueCollection headers)> Asked = new List<(string, string, NameValueCollection)>();
            public System.Func<string, string, NameValueCollection, (int code, string type, string body)> Answer;
            readonly HttpListener listener = new HttpListener();

            public Server()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                Url = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}";
                probe.Stop();
                listener.Prefixes.Add(Url + "/");
                listener.Start();
                new Thread(() =>
                {
                    while (true)
                    {
                        HttpListenerContext context;
                        try
                        {
                            context = listener.GetContext();
                        }
                        catch (System.Exception)
                        {
                            return;
                        }
                        string body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        lock (Asked) Asked.Add((context.Request.Url.AbsolutePath, body, context.Request.Headers));
                        var (code, type, text) = Answer(context.Request.Url.AbsolutePath, body, context.Request.Headers);
                        byte[] bytes = Encoding.UTF8.GetBytes(text);
                        context.Response.StatusCode = code;
                        context.Response.ContentType = type;
                        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                        context.Response.Close();
                    }
                }) { IsBackground = true }.Start();
            }

            public void Dispose() => listener.Close();

            // The makers' servers as the game needs them: Anthropic's messages for one key, OpenAI's
            // responses for one key and for whatever pass the sign-in last gave, and the sign-in
            // itself, whose passes last as long as a test says: none at all, to begin with.
            public const string ClaudeKey = "sk-ant-api03-a-key-for-the-tests", OpenAiKey = "sk-proj-a-key-for-the-tests";
            public int Grants, Lasts;
            public bool Spent, Revoked;

            public (int, string, string) Makers(string path, string body, NameValueCollection headers)
            {
                const string json = "application/json";
                if (path == "/v1/messages")
                    return headers["x-api-key"] == ClaudeKey
                        ? (200, json, "{\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"},{\"type\":\"text\",\"text\":\"A fir, and it\u2019s by key.\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":120000,\"output_tokens\":30000}}")
                        : (401, json, "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}");
                if (path == "/api/accounts/oauth/token")
                    return Revoked ? (400, json, "{\"error\":\"invalid_grant\"}")
                        : (200, json, $"{{\"access_token\":\"pass-{++Grants}\",\"refresh_token\":\"renew-{Grants}\",\"expires_in\":{Lasts},\"id_token\":\"unread\"}}");
                if (path != "/v1/responses") return (404, "text/plain", "");
                if (headers["Authorization"] != $"Bearer {OpenAiKey}" && headers["Authorization"] != $"Bearer pass-{Grants}")
                    return (401, json, "{\"error\":{\"message\":\"Missing bearer authentication\",\"code\":\"subscription_sharing_invalid_user\"}}");
                if (Spent) return (429, json, "{\"error\":{\"message\":\"Usage limit reached.\",\"code\":\"subscription_sharing_usage_limit_exceeded\"}}");
                return (200, "text/event-stream",
                    "event: response.created\ndata: {\"type\":\"response.created\",\"response\":{\"status\":\"in_progress\",\"error\":null}}\n\n" +
                    "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"A fir, \"}\n\n" +
                    "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"by \\\"plan\\\".\"}\n\n" +
                    "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"error\":null,\"usage\":{\"input_tokens\":900,\"output_tokens\":40}}}\n\n");
            }
        }

        // Points the game at a stand-in for the makers' servers, and at a browser that approves
        // every sign-in at once: it goes straight back to the game with a code, as OpenAI's page would.
        Server StandInServers(List<Dictionary<string, string>> opened)
        {
            var server = new Server();
            server.Answer = server.Makers;
            Oracle.Hosts[0] = Oracle.Hosts[1] = Oracle.Hosts[2] = server.Url;
            Oracle.Browse = url =>
            {
                var asked = Fields(url.Substring(url.IndexOf('?') + 1));
                asked["page"] = url.Substring(0, url.IndexOf('?'));
                opened.Add(asked);
                new Thread(() =>
                {
                    using var web = new WebClient();
                    web.DownloadString($"{asked["redirect_uri"]}?code=the-code&scope=granted&state={asked["state"]}&client_id=oaiapp_made_for_the_test");
                }) { IsBackground = true }.Start();
            };
            return server;
        }

        // The fields of a query or a form.
        static Dictionary<string, string> Fields(string encoded) =>
            encoded.Split('&').Select(pair => pair.Split('=')).ToDictionary(pair => pair[0], pair => System.Uri.UnescapeDataString(pair[1].Replace('+', ' ')));

        // Waits, without the game's clock, until something is so or ten seconds have gone.
        static IEnumerator Until(System.Func<bool> done)
        {
            for (float t = 0; t < 10f && !done(); t += Time.unscaledDeltaTime) yield return null;
        }

        static IEnumerator Finish(Task task)
        {
            while (!task.IsCompleted) yield return null;
        }

        [UnityTest]
        public IEnumerator TheOracleAsksClaudeThroughClaudeCode()
        {
            Oracle.Tools[0] = StandIn("claude-signed-in", "if [ \"$1\" = auth ]; then echo '{ \"loggedIn\": true, \"authMethod\": \"claude.ai\" }'; else " +
                "printf '%s\\n' '{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"A fir, by plan.\",\"usage\":{\"input_tokens\":2,\"cache_creation_input_tokens\":650,\"output_tokens\":7}}'; fi");
            var status = Oracle.Status(0);
            yield return Finish(status);
            Assert.AreEqual("SIGNED IN WITH CLAUDE CODE", status.Result);
            var reply = Oracle.Run("Be a field guide.", "Describe a fir.");
            yield return Finish(reply);
            Assert.AreEqual("A fir, by plan.", reply.Result);
            // it is a coding agent told to be nothing of the kind: the persona for its instructions, no tools, nothing of the player's and nothing kept
            string[] asked = File.ReadAllLines(Oracle.Tools[0] + ".args");
            CollectionAssert.AreEqual(new[] { "-p", "Describe a fir.", "--model", "claude-haiku-5-5", "--effort", "low", "--system-prompt", "Be a field guide.", "--tools", "" }, asked.Take(10));
            foreach (string flag in new[] { "--restricted", "--strict-mcp-config", "--disable-slash-commands", "--no-session-persistence" }) CollectionAssert.Contains(asked, flag);
            Assert.AreEqual("ASKED 1 TIME   ·   652 TOKENS IN   ·   7 OUT", Oracle.Used(0), "counted, and not priced: a plan pays for it");

            Oracle.Tools[0] = StandIn("claude-signed-out", "echo '{\"type\":\"result\",\"is_error\":true,\"result\":\"Not logged in · Please run /login\"}'\nexit 1");
            var refused = Oracle.Run("", "Anything.");
            yield return Finish(refused);
            StringAssert.Contains("Not signed in to Claude. Open Claude or ChatGPT on the menu", refused.Exception.InnerException.Message);
            Oracle.Tools[0] = StandIn("claude-spent", "echo '{\"type\":\"result\",\"is_error\":true,\"result\":\"You have reached your usage limit.\"}'\nexit 1");
            var spent = Oracle.Run("", "Anything.");
            yield return Finish(spent);
            StringAssert.Contains("usage limit", spent.Exception.InnerException.Message);
        }

        [UnityTest]
        public IEnumerator TheOracleAsksChatGPTThroughCodexAndThePlayersOwnSignIn()
        {
            Oracle.Voice = 1;
            // codex reports each thing it does as a line of JSON: its answer, and what the turn took
            Oracle.Tools[1] = StandIn("codex-signed-in", "printf '%s\\n' '{\"type\":\"thread.started\",\"thread_id\":\"t\"}' '{\"type\":\"turn.started\"}' " +
                "'{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"agent_message\",\"text\":\"A fir, as you\u2019d expect.\"}}' " +
                "'{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":11052,\"cached_input_tokens\":7296,\"output_tokens\":5}}'");
            var reply = Oracle.Run("Be a \"field\" guide.", "Describe a fir.\nBriefly.");
            yield return Finish(reply);
            Assert.AreEqual("A fir, as you\u2019d expect.", reply.Result, "no key is needed: codex finds the sign-in itself, and its words arrive as it wrote them");
            string[] asked = File.ReadAllLines(Oracle.Tools[1] + ".args");
            CollectionAssert.AreEqual(new[] { "exec", "--json" }, asked.Take(2));
            Assert.AreEqual("gpt-6-luna", asked[System.Array.IndexOf(asked, "-m") + 1]);
            // a coding agent with no shell, standing in an empty folder that is nothing to do with the game or the player
            Assert.AreEqual("shell_tool", asked[System.Array.IndexOf(asked, "--disable") + 1]);
            StringAssert.StartsWith(Path.GetTempPath(), asked[System.Array.IndexOf(asked, "-C") + 1]);
            // the persona takes the place of its own instructions, and the question is the last thing it is given
            Assert.AreEqual("Be a \"field\" guide.", File.ReadAllText(asked.Single(argument => argument.StartsWith("model_instructions_file=")).Split('"')[1]));
            CollectionAssert.AreEqual(new[] { "Describe a fir.", "Briefly." }, asked.Skip(asked.Length - 2));
            Assert.AreEqual("ASKED 1 TIME   ·   11,052 TOKENS IN   ·   5 OUT", Oracle.Used(1));

            Oracle.NextModel(1);
            reply = Oracle.Run("Be a field guide.", "Describe a fir.");
            yield return Finish(reply);
            asked = File.ReadAllLines(Oracle.Tools[1] + ".args");
            Assert.AreEqual("gpt-6.1-terra", asked[System.Array.IndexOf(asked, "-m") + 1]);

            // most of what a coding agent brings along is left at home, and a codex that has since forgotten one of those names is asked again without them
            CollectionAssert.Contains(asked, "apps");
            Oracle.Tools[1] = StandIn("codex-newer", "case \"$*\" in *apps*) echo 'Error: Unknown feature flag: apps' >&2; exit 1;; esac\n" +
                "printf '%s\\n' '{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"A fir again.\"}}' '{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":7,\"output_tokens\":3}}'");
            reply = Oracle.Run("Be a field guide.", "Describe a fir.");
            yield return Finish(reply);
            Assert.AreEqual("A fir again.", reply.Result);
            asked = File.ReadAllLines(Oracle.Tools[1] + ".args");
            CollectionAssert.DoesNotContain(asked, "apps");
            Assert.AreEqual("shell_tool", asked[System.Array.IndexOf(asked, "--disable") + 1]);
            // how hard it thinks is the player's to choose too
            CollectionAssert.Contains(asked, "model_reasoning_effort=\"low\"");
            Oracle.NextEffort(1);
            reply = Oracle.Run("Be a field guide.", "Describe a fir.");
            yield return Finish(reply);
            CollectionAssert.Contains(File.ReadAllLines(Oracle.Tools[1] + ".args"), "model_reasoning_effort=\"medium\"");

            Oracle.Tools[1] = StandIn("codex-signed-out", "printf '%s\\n' '{\"type\":\"error\",\"message\":\"Reconnecting... 5/5 (unexpected status 401 Unauthorized)\"}' " +
                "'{\"type\":\"turn.failed\",\"error\":{\"message\":\"unexpected status 401 Unauthorized: Missing bearer or basic authentication in header\"}}'\nexit 1");
            var refused = Oracle.Run("", "Room 401 is on the left.");
            yield return Finish(refused);
            StringAssert.Contains("Not signed in to ChatGPT. Open Claude or ChatGPT on the menu", refused.Exception.InnerException.Message);

            Oracle.Tools[1] = StandIn("codex-spent", "printf '%s\\n' '{\"type\":\"turn.failed\",\"error\":{\"message\":\"You have hit your usage limit. Try again at 3:11 PM.\"}}'\nexit 1");
            var spent = Oracle.Run("", "Room 401 is on the left.");
            yield return Finish(spent);
            Assert.AreEqual("ChatGPT could not answer. You have hit your usage limit. Try again at 3:11 PM.", spent.Exception.InnerException.Message);

            Oracle.Tools[1] = "codex-that-is-not-installed";
            var none = Oracle.Run("", "");
            yield return Finish(none);
            StringAssert.Contains("install OpenAI's codex", none.Exception.InnerException.Message);
        }

        [UnityTest]
        public IEnumerator TheOracleAsksTheMakersItselfWithAKeyOrAChatGPTSignIn()
        {
            var opened = new List<Dictionary<string, string>>();
            using var server = StandInServers(opened);
            Dictionary<string, string> Last(string path) => Fields(server.Asked.Last(asked => asked.path == path).body);

            // Claude, with a key of the player's own: the only road to it that needs no tool
            Assert.IsFalse(Oracle.UseKey(0, "not a key"));
            Assert.IsFalse(Oracle.UseKey(0, Server.OpenAiKey), "nor will another maker's do");
            Assert.IsTrue(Oracle.UseKey(0, $"  {Server.ClaudeKey}\n"));
            var status = Oracle.Status(0);
            var reply = Oracle.Run("Be a \"field\" guide.", "Describe a fir.\nBriefly.");
            yield return Finish(reply);
            Assert.AreEqual("A fir, and it\u2019s by key.", reply.Result);
            Assert.AreEqual("YOUR OWN KEY", status.Result);
            var asked = server.Asked.Single();
            Assert.AreEqual("/v1/messages", asked.path);
            Assert.AreEqual("2026-10-21", asked.headers["anthropic-version"]);
            StringAssert.Contains("\"model\":\"claude-haiku-5-5\"", asked.body);
            StringAssert.Contains("\"system\":\"Be a \\\"field\\\" guide.\"", asked.body);
            StringAssert.Contains("{\"role\":\"user\",\"content\":\"Describe a fir.\\nBriefly.\"}", asked.body);
            Assert.AreEqual("ASKED 1 TIME   ·   120,000 TOKENS IN   ·   30,000 OUT   ·   ABOUT $0.03", Oracle.Used(0), "tokens are counted, and priced where the price is known");
            // it is kept on this device, and nowhere in the save that travels with a character
            StringAssert.Contains(Server.ClaudeKey, File.ReadAllText(Path.Combine(Application.persistentDataPath, Oracle.Store)));
            Oracle.Forget(0);
            StringAssert.DoesNotContain(Server.ClaudeKey, File.ReadAllText(Path.Combine(Application.persistentDataPath, Oracle.Store)));
            status = Oracle.Status(0);
            yield return Finish(status);
            Assert.AreEqual("NOT SIGNED IN", status.Result);

            // ChatGPT, by OpenAI's own sign-in: the game opens their page and waits on this device for the browser to come back
            Oracle.Voice = 1;
            status = Oracle.SignIn(1, 1);
            yield return Finish(status);
            Assert.AreEqual("SIGNED IN WITH CHATGPT", status.Result);
            var page = opened.Single();
            Assert.AreEqual($"{server.Url}/api/accounts/authorize", page["page"]);
            Assert.AreEqual("dynamic_agent_client", page["client_id"], "the first time, OpenAI is asked to make a client of this device");
            Assert.AreEqual("Infinigame", page["agent_name_hint"]);
            StringAssert.StartsWith("urn:uuid:", page["ext_agent_host_id"]);
            Assert.AreEqual("openid profile email offline_access resource.invoke chatgpt.tokens.use.direct", page["scope"]);
            Assert.AreEqual("https://api.openai.com/v1", page["resource"]);
            StringAssert.IsMatch("^http://127\\.0\\.0\\.1:\\d+/auth/callback$", page["redirect_uri"]);
            var traded = Last("/api/accounts/oauth/token");
            Assert.AreEqual("authorization_code", traded["grant_type"]);
            Assert.AreEqual("oaiapp_made_for_the_test", traded["client_id"], "the client it was made is the one that trades the code");
            Assert.AreEqual("the-code", traded["code"]);
            Assert.AreEqual(page["redirect_uri"], traded["redirect_uri"]);
            // the code is only good to whoever holds the secret whose hash went with the request
            using (var sha = System.Security.Cryptography.SHA256.Create())
                Assert.AreEqual(page["code_challenge"], System.Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(traded["code_verifier"]))).TrimEnd('=').Replace('+', '-').Replace('/', '_'));

            // its first pass has run out already, so it is renewed before it is used
            server.Lasts = 3600;
            reply = Oracle.Run("Be a field guide.", "Describe a fir.");
            yield return Finish(reply);
            Assert.AreEqual("A fir, by \"plan\".", reply.Result);
            var renewed = Last("/api/accounts/oauth/token");
            Assert.AreEqual("refresh_token", renewed["grant_type"]);
            Assert.AreEqual("renew-1", renewed["refresh_token"]);
            asked = server.Asked.Last();
            Assert.AreEqual("/v1/responses", asked.path);
            Assert.AreEqual("Bearer pass-2", asked.headers["Authorization"]);
            StringAssert.Contains("\"model\":\"gpt-6-luna\"", asked.body);
            StringAssert.Contains("\"instructions\":\"Be a field guide.\"", asked.body);
            StringAssert.Contains("\"store\":false,\"stream\":true", asked.body);
            Assert.AreEqual("ASKED 1 TIME   ·   900 TOKENS IN   ·   40 OUT", Oracle.Used(1));
            // and the next question goes with the same pass
            reply = Oracle.Run("Be a field guide.", "Describe a fir.");
            yield return Finish(reply);
            Assert.AreEqual(2, server.Grants);

            // signing in again is as the client this device already is
            status = Oracle.SignIn(1, 1);
            yield return Finish(status);
            Assert.AreEqual("oaiapp_made_for_the_test", opened.Last()["client_id"]);
            Assert.IsFalse(opened.Last().ContainsKey("agent_name_hint"));
            Assert.AreEqual(page["ext_agent_host_id"], opened.Last()["ext_agent_host_id"]);

            // a plan with nothing left to give says so
            server.Spent = true;
            var spent = Oracle.Run("", "Anything.");
            yield return Finish(spent);
            StringAssert.Contains("chatgpt.com/settings/usage", spent.Exception.InnerException.Message);
            server.Spent = false;

            // a key of the player's own comes before the sign-in
            Assert.IsFalse(Oracle.UseKey(1, Server.ClaudeKey));
            Assert.IsTrue(Oracle.UseKey(1, Server.OpenAiKey));
            reply = Oracle.Run("Be a field guide.", "Describe a fir.");
            yield return Finish(reply);
            Assert.AreEqual($"Bearer {Server.OpenAiKey}", server.Asked.Last().headers["Authorization"]);

            // and a sign-in that OpenAI has withdrawn by the time its pass runs out leaves the player signed out
            Oracle.Forget(1);
            server.Lasts = 0;
            status = Oracle.SignIn(1, 1);
            yield return Finish(status);
            server.Revoked = true;
            var refused = Oracle.Run("", "Anything.");
            yield return Finish(refused);
            StringAssert.Contains("Not signed in to ChatGPT", refused.Exception.InnerException.Message);
            status = Oracle.Status(1);
            yield return Finish(status);
            Assert.AreEqual("NOT SIGNED IN", status.Result);
        }

        [UnityTest]
        public IEnumerator EachVoiceHasAPageOfTheMenuToSignInGiveAKeyAndChooseAModel()
        {
            var opened = new List<Dictionary<string, string>>();
            using var server = StandInServers(opened);
            // none of the makers' tools holds a sign-in until it has been told to sign in
            Oracle.Tools[1] = StandIn("codex-login", "if [ \"$2\" = status ]; then [ -f \"$0.in\" ] || exit 1; echo 'Logged in using ChatGPT'; else touch \"$0.in\"; fi; exit 0");
            Oracle.Tools[0] = StandIn("claude-login", "if [ \"$2\" = status ]; then [ -f \"$0.in\" ] && echo '{ \"loggedIn\": true }' || echo '{ \"loggedIn\": false }'; else touch \"$0.in\"; fi; exit 0");
            foreach (string tool in Oracle.Tools) File.Delete(tool + ".in");

            // the settings page says how the player stands with each, and which model it would use
            yield return Choose(Game.Settings);
            yield return Until(() => !game.Items.Any(item => item.Contains("CHECKING")));
            CollectionAssert.AreEqual(new[] { "CLAUDE   ·   NOT SIGNED IN   ·   HAIKU 5.5   ·   IN USE", "CHATGPT   ·   NOT SIGNED IN   ·   GPT-6 LUNA", "Back" }, game.Items.Skip(7));

            // ChatGPT's page: a model and how hard it thinks, then two ways to sign in, both in the player's own browser
            for (int i = 0; i < 7; i++) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual("ChatGPT", game.Page);
            CollectionAssert.AreEqual(new[]
            {
                "Have ChatGPT write", "MODEL   ·   GPT-6 LUNA", "THINKING   ·   LOW", "Sign in with Codex", "Continue with ChatGPT", "Use the key on the clipboard", "Forget key and sign-in", "Back",
            }, game.Items);
            Assert.AreEqual("NOT SIGNED IN   ·   NOT ASKED ANYTHING YET", game.Standing);
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual("MODEL   ·   GPT-6.1 TERRA", game.Items[1]);
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual("THINKING   ·   HIGH", game.Items[2]);
            // OpenAI's own page, opened by the game: whoever they sign in to writes from then on
            yield return Press(new Controls { down = true });
            StringAssert.Contains("CODEX USE", game.Hint);
            yield return Press(new Controls { down = true });
            StringAssert.Contains("CHATGPT PLUS OR PRO", game.Hint);
            yield return Press(new Controls { confirm = true });
            yield return Until(() => game.Standing.StartsWith("SIGNED IN"));
            Assert.AreEqual("SIGNED IN WITH CHATGPT   ·   IN USE   ·   NOT ASKED ANYTHING YET", game.Standing);
            Assert.AreEqual(1, opened.Count, "the page was opened");
            Assert.AreEqual(1, Oracle.Voice);
            Assert.IsTrue(game.Paused, "and the menu stays open to show it");

            // a key from the clipboard, when what is there looks like one
            yield return Press(new Controls { down = true });
            Game.Clipboard = () => "a shopping list";
            yield return Press(new Controls { confirm = true });
            StringAssert.StartsWith("NO KEY ON THE CLIPBOARD", game.Standing);
            Game.Clipboard = () => Server.OpenAiKey;
            yield return Press(new Controls { confirm = true });
            StringAssert.StartsWith("YOUR OWN KEY", game.Standing);
            // forgetting drops the key and the sign-in alike
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            yield return Until(() => game.Standing.StartsWith("NOT SIGNED IN"));
            // and codex's own sign-in is the other way in
            for (int i = 0; i < 3; i++) yield return Press(new Controls { up = true });
            Assert.AreEqual("Sign in with Codex", game.Items[game.Selected]);
            yield return Press(new Controls { confirm = true });
            yield return Until(() => game.Standing.StartsWith("SIGNED IN"));
            StringAssert.StartsWith("SIGNED IN WITH CODEX", game.Standing);
            Assert.IsTrue(File.Exists(Oracle.Tools[1] + ".in"), "codex was told to sign in");
            CollectionAssert.AreEqual(new[] { "login" }, File.ReadAllLines(Oracle.Tools[1] + ".args").Take(1));

            // back to the settings, and on to Claude's page, where Claude Code does the signing in
            yield return Press(new Controls { menu = true });
            Assert.AreEqual(Game.Settings, game.Page);
            Assert.AreEqual(7, game.Selected);
            yield return Press(new Controls { up = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual("Claude", game.Page);
            CollectionAssert.AreEqual(new[] { "Sign in with Claude Code" }, game.Items.Skip(3).Take(1));
            for (int i = 0; i < 3; i++) yield return Press(new Controls { down = true });
            StringAssert.Contains("CLAUDE PLAN", game.Hint);
            yield return Press(new Controls { confirm = true });
            yield return Until(() => game.Standing.StartsWith("SIGNED IN"));
            StringAssert.StartsWith("SIGNED IN WITH CLAUDE CODE   ·   IN USE", game.Standing);
            CollectionAssert.AreEqual(new[] { "auth", "status" }, File.ReadAllLines(Oracle.Tools[0] + ".args"), "it was told to sign in, then asked how that went");
            Assert.IsTrue(File.Exists(Oracle.Tools[0] + ".in"));
            Assert.AreEqual(0, Oracle.Voice);
            yield return Press(new Controls { down = true });
            Assert.AreEqual("Use the key on the clipboard", game.Items[game.Selected]);
            Assert.IsFalse(game.Items.Any(item => item.Contains("with ant")));

            // either can be had to write, signed in or not, and the choice outlasts the game
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(1, Oracle.Voice);
            Oracle.Load();
            Assert.AreEqual(1, Oracle.Voice);
            Assert.AreEqual("GPT-6.1 Terra", Oracle.Model(1).name);
            Assert.AreEqual("high", Oracle.Effort(1));
            // and each escape goes back a page, to the item that led away from it
            yield return Press(new Controls { menu = true });
            Assert.AreEqual(Game.Settings, game.Page);
            StringAssert.StartsWith("CHATGPT", game.Items[game.Selected]);
            yield return Press(new Controls { menu = true });
            Assert.IsNull(game.Page);
            Assert.AreEqual(Game.Settings, game.Items[game.Selected]);
        }

        [UnityTest] public IEnumerator FestivalSettingsPreviewAndRememberEffects()
        {
            string spoken = "";
            Speech.Render = (words, directory) => { spoken = words; return new float[3200]; };
            IEnumerator Pick(string prefix)
            {
                int at = System.Array.FindIndex(game.Items, item => item.StartsWith(prefix));
                Assert.GreaterOrEqual(at, 0, prefix + " is offered");
                while (game.Selected != at) yield return Press(new Controls { down = true });
                yield return Press(new Controls { confirm = true });
            }
            yield return Choose(Game.Settings);
            yield return Pick("ROBOT VOICE");
            Assert.AreEqual(5, game.Items.Length);
            yield return Pick("FESTIVAL SLT");
            yield return Until(() => spoken.Length > 0);
            yield return Pick("ROBOT EFFECT");
            Assert.AreEqual(1, PlayerPrefs.GetInt($"{Game.Prefs}.robot"));
            yield return Pick("EFFECT STRENGTH");
            Assert.AreEqual(0, Speech.RobotStrength);
            yield return Pick("Voice off");
            Assert.IsTrue(Speech.Muted);
            yield return SceneManager.LoadSceneAsync("Main");
            game = Object.FindAnyObjectByType<Game>();
            while (!game.Ready) yield return null;
            Player.controls = () => input;
            Assert.IsTrue(Speech.Muted);
            Assert.IsTrue(Speech.Robot);
            Assert.AreEqual(0, Speech.RobotStrength);
            Assert.AreEqual("cmu_us_slt_arctic_hts", Speech.Choice);
        }

        [UnityTest] public IEnumerator FestivalDiscardsInterruptedSpeechAndPausesWithMenus()
        {
            using var started = new System.Threading.ManualResetEventSlim();
            using var finish = new System.Threading.ManualResetEventSlim();
            Speech.Render = (words, directory) =>
            {
                if (words == "Old line") { started.Set(); finish.Wait(10000); }
                return Enumerable.Repeat(words == "Old line" ? 0.1f : 0.5f, 320000).ToArray();
            };
            try
            {
                game.Speech.Say("Old line");
                yield return Until(() => started.IsSet);
                game.Speech.Say("New line");
                finish.Set();
                AudioSource Voice() => game.GetComponents<AudioSource>().FirstOrDefault(source => source.clip != null && source.clip.name == "Speech");
                yield return Until(() => Voice() != null);
                var samples = new float[1]; Voice().clip.GetData(samples, 0);
                Assert.AreEqual(0.5f, samples[0], "Only the replacement line reaches the audio source");
                yield return Press(new Controls { menu = true });
                Assert.IsTrue(game.Paused);
                Assert.IsFalse(Voice().isPlaying);
                game.Speech.Silence();
                Assert.IsFalse(game.Speech.Speaking);
                Assert.IsNull(Voice());
            }
            finally { finish.Set(); }
        }

        [UnityTest]
        public IEnumerator SettingsChangeHowTheGameLooksAndSoundsAndAreRemembered()
        {
            int tiers = QualitySettings.names.Length, tier = QualitySettings.GetQualityLevel();
            float look = DesktopControls.Sensitivity;
            yield return Choose(Game.Settings);
            Assert.AreEqual(10, game.Items.Length, "graphics, audio, controls, automatic quality, model providers and back");
            Assert.AreEqual($"DETAIL   ·   {QualitySettings.names[tier].ToUpperInvariant()}", game.Items[0]);
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual((tier + 1) % tiers, QualitySettings.GetQualityLevel());
            yield return Press(new Controls { down = true });
            Assert.AreEqual("MUSIC   ·   ON", game.Items[1]);
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual("MUSIC   ·   OFF", game.Items[1]);
            Assert.IsTrue(Music.Muted);
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(look * 1.25f, DesktopControls.Sensitivity, 1e-4f);
            StringAssert.Contains("MOUSE LOOK SPEED", game.Hint);
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { down = true });
            Assert.AreEqual("ROBOT VOICE   ·   ON", game.Items[4]);
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual("Voice", game.Page);
            while (game.Items[game.Selected] != "Voice off") yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.IsTrue(Speech.Muted);
            yield return Press(new Controls { menu = true });

            // the device remembers detail, music and speech for whoever plays next
            (Music.Muted, Speech.Muted) = (false, false);
            QualitySettings.SetQualityLevel(tier);
            yield return SceneManager.LoadSceneAsync("Main");
            game = Object.FindAnyObjectByType<Game>();
            while (!game.Ready) yield return null;
            Assert.AreEqual((tier + 1) % tiers, QualitySettings.GetQualityLevel());
            Assert.IsTrue(Music.Muted && Speech.Muted);
        }

        [UnityTest]
        public IEnumerator GauntletsReplaceCarriedGunsAndLowerInTheShip()
        {
            yield return Seconds(0.5f);
            var hands = Camera.main.transform.Find("Gauntlets");
            Assert.IsNotNull(hands);
            Assert.IsTrue(hands.gameObject.activeSelf);
            Assert.IsNotNull(hands.Find("Right gauntlet/Armour"));
            yield return Press(new Controls { fire = true });
            Assert.Greater(hands.localPosition.z, 0);
            Player.flying = true;
            yield return null;
            Assert.IsFalse(hands.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator TheInterfaceHoldsStillOverTheSystemMapAndItsNamesKeepOutOfEachOthersWay()
        {
            input.systemView = true;
            yield return Seconds(0.5f);
            var canvas = GameObject.Find("HUD").GetComponent<RectTransform>();
            var view = Camera.main;
            // The camera is a very long way from the middle of the scene here, where positions come
            // only in coarse steps. One step, as a share of the canvas's height in the world, is how
            // far its text could shiver from frame to frame as the system turns under it.
            for (int frame = 0; frame < 5; frame++)
            {
                yield return null;
                float far = view.transform.position.magnitude, step = Mathf.Pow(2f, Mathf.Ceil(Mathf.Log(far, 2f)) - 24f);
                float tall = 2f * GameObject.Find("HUD").GetComponent<Canvas>().planeDistance * Mathf.Tan(view.fieldOfView * Mathf.Deg2Rad / 2f);
                Assert.Greater(far, 100000f, "the map is seen from far off");
                Assert.Less(step / tall * 2880f, 0.05f, "of a pixel, on a 5K screen");
            }

            // every name that is shown has room to itself
            var names = canvas.GetComponentsInChildren<UnityEngine.UI.Text>().Where(text => text.transform.parent.name == "Marker" && text.enabled && text.text.Length > 0)
                .Select(text => (text.text, at: (Vector2)canvas.InverseTransformPoint(text.transform.position), size: new Vector2(text.preferredWidth, text.preferredHeight))).ToArray();
            // a star, eight planets, the raiders' base and the moon of the world the player is on
            Assert.GreaterOrEqual(names.Length, 9, $"nearly every world still has its name: {string.Join(" ", names.Select(name => name.text))}");
            foreach (var one in names)
            foreach (var other in names)
                Assert.IsTrue(one == other || Mathf.Abs(one.at.x - other.at.x) >= (one.size.x + other.size.x) / 2f - 1f || Mathf.Abs(one.at.y - other.at.y) >= (one.size.y + other.size.y) / 2f - 1f,
                    $"{one.text} lies over {other.text}");
        }

        [UnityTest]
        public IEnumerator AShortFallHurtsAndALongOneKillsButOnlyTheMissionIsLost()
        {
            Player.fitted &= ~(1 << (int)Gear.Armour);
            yield return Seconds(0.5f);
            float3 up = math.normalize(Player.walker.position);
            Player.walker.position += up * 14f;
            Player.walker.grounded = false;
            yield return Seconds(3f);
            Assert.That(Player.health, Is.InRange(20f, Player.MaxHealth - 5f));
            Assert.IsFalse(game.Dead);

            uint seed = game.Seed;
            int kit = Player.kit;
            Player.walker.position += up * 150f;
            Player.walker.grounded = false;
            for (float t = 0; t < 12f && !game.Dead; t += Time.unscaledDeltaTime) yield return null;
            Assert.IsTrue(game.Dead);
            StringAssert.Contains($"Fell to the ground on {Body.name}", game.Epitaph);
            StringAssert.Contains("YOU KEEP ALL YOU HAVE WON", game.Tally);
            Assert.AreEqual(0f, Time.timeScale, "the world stops");
            CollectionAssert.AreEqual(Game.Over, game.Items);

            yield return Press(new Controls { confirm = true });
            Assert.IsFalse(game.Dead);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(Player.MaxHealth, Player.health);
            Assert.AreEqual(seed, game.Seed, "the same system");
            Assert.AreEqual(Campaign.Citadel, game.Stage, "the same mission");
            Assert.AreEqual(kit, Player.kit, "and everything that had been won");
        }

        [UnityTest]
        public IEnumerator FlyingIntoTheGroundCostsHullAndLandingMendsIt()
        {
            float3 up = math.normalize(Player.ship.position);
            HoverAt(Player.ship.position + up * 40f);
            Player.assist = false;
            Player.ship.velocity = -up * 40f;
            yield return Seconds(3f);
            Assert.That(Player.hull, Is.InRange(5f, Player.MaxHull - 20f));
            StringAssert.Contains("Crashed on", Player.Cause);
            Assert.IsFalse(game.Dead);

            float hurt = Player.hull;
            Player.assist = true;
            yield return Press(new Controls { interact = true });
            for (float t = 0; t < 30f && !Player.ship.landed; t += Time.unscaledDeltaTime) yield return null;
            yield return Seconds(2f);
            Assert.Greater(Player.hull, hurt, "a ship on the ground is patched up");
        }

        [UnityTest]
        public IEnumerator AContinuousBeamUsesTheSameEnergyAsThrustersAndShields()
        {
            Player.fitted = (1 << (int)Gear.Beams) | (1 << (int)Gear.Thrusters);
            yield return Press(new Controls { select = 2 });
            Assert.IsTrue(Player.Weapon.beam);
            float energy = Player.energy;
            input.fire = true;
            yield return Seconds(1);
            input.fire = false;
            Assert.Less(Player.energy, energy - 5);
            Assert.AreEqual(0, game.Battle.Bolts.Count, "a continuous beam is not a stream of bolts");
            var ray = Object.FindObjectsByType<LineRenderer>().Single(line => line.name == "Weapon beam");
            Assert.Greater(Vector3.Distance(ray.GetPosition(0), ray.GetPosition(1)), 0);
            Player.fitted = 1 << (int)Gear.Armour;
            Player.energy = 50;
            float health = Player.health;
            Player.Hurt(20, "test shield");
            Assert.Less(Player.energy, 50);
            Assert.Greater(Player.health, health - 20);
        }

        [UnityTest]
        public IEnumerator DronesShootBackUntilYouAreDead()
        {
            yield return Seconds(0.5f);
            float3 ahead = math.normalize(Player.walker.position + Player.walker.forward * 12f);
            for (int i = 0; i < 3; i++) game.Battle.Add(FoeKind.Drone, ahead * (Body.GroundRadius(ahead) + 1.8f + i));
            Time.timeScale = 6f;
            for (float t = 0; t < 120f && !game.Dead; t += Time.unscaledDeltaTime) yield return null;
            Assert.IsTrue(game.Dead);
            StringAssert.Contains("drones", game.Epitaph);
        }

        [UnityTest]
        public IEnumerator RaidersAttackAShipAndCanBeShotDown()
        {
            float3 up = math.normalize(Player.ship.position);
            HoverAt(up * (Body.radius * 1.6f));
            yield return null;
            float3 nose = math.rotate(Player.ship.rotation, new float3(0, 0, 1));
            game.Battle.Add(FoeKind.Raider, Player.ship.position + nose * 600f);
            for (float t = 0; t < 30f && !game.Battle.Bolts.Any(bolt => bolt.hostile); t += Time.unscaledDeltaTime) yield return null;
            Assert.IsTrue(game.Battle.Bolts.Any(bolt => bolt.hostile), "it opens fire");

            input.fire = true;
            for (float t = 0; t < 40f && game.Battle.Foes.Count > 0; t += Time.unscaledDeltaTime)
            {
                // lead it, as the HUD's marker shows how
                var foe = game.Battle.Foes[0];
                float3 to = foe.position - Player.ship.position;
                Player.ship.rotation = quaternion.LookRotationSafe(to + (foe.velocity - Player.ship.velocity) * (math.length(to) / Player.Weapon.speed), up);
                Player.hull = Player.MaxHull;
                yield return null;
            }
            Assert.AreEqual(0, game.Battle.Foes.Count, "the raider is destroyed");
        }

        [UnityTest]
        public IEnumerator FoesComeForAnyoneWhoLingers()
        {
            game.Battle.quiet = false;
            // well away from the hermit's fire, where nothing hunts
            float3 far = -math.normalize(game.Hermit.Position);
            HoverAt(far * (Body.radius * 1.5f));
            Time.timeScale = 8f;
            for (float t = 0; t < 60f && !game.Battle.Foes.Any(foe => foe.kind == FoeKind.Raider); t += Time.unscaledDeltaTime) yield return null;
            Assert.IsTrue(game.Battle.Foes.Any(foe => foe.kind == FoeKind.Raider), "raiders jump a ship in flight");
            Assert.AreEqual("RAIDERS INBOUND", game.Notice);
        }

        [UnityTest]
        public IEnumerator TheAutopilotFliesToTheWorldYouFace()
        {
            float3 moon = Solar.ToReference(Moon);
            HoverAt(math.normalize(moon) * (Body.radius * 1.6f));
            Player.ship.rotation = quaternion.LookRotationSafe(moon - Player.ship.position, new float3(0, 1, 0));
            yield return null;
            yield return Press(new Controls { travel = true });
            Assert.AreEqual(Moon, Player.Destination);
            StringAssert.Contains(Solar.Descriptor.bodies[Moon].name.ToUpperInvariant(), game.Notice);
            Assert.AreEqual(Moon, Player.Ahead, "the instruments measure to where it is going");

            Time.timeScale = 4f;
            float fastest = 0f;
            for (float t = 0; t < 120f && Player.Autopilot; t += Time.unscaledDeltaTime)
            {
                fastest = math.max(fastest, math.length(Player.ship.velocity));
                yield return null;
            }
            Assert.IsFalse(Player.Autopilot, "it lets go on arrival");
            Assert.AreEqual(Moon, Solar.Reference);
            Assert.AreEqual(Autopilot.Standoff(Body), math.length(Player.ship.position), 30f);
            Assert.Greater(fastest, Tuning.ShipSpaceSpeed, "faster than a pilot may fly");
            StringAssert.StartsWith("ARRIVED", game.Notice);
            Assert.AreEqual(Player.MaxHull, Player.hull, "and unharmed");

            // touching the controls takes the ship back
            Time.timeScale = 1f;
            Player.ship.rotation = quaternion.LookRotationSafe(Solar.ToReference(Home) - Player.ship.position, new float3(0, 1, 0));
            yield return null;
            yield return Press(new Controls { travel = true });
            Assert.AreEqual(Home, Player.Destination);
            yield return Press(new Controls { move = Vector2.up });
            Assert.IsFalse(Player.Autopilot);
        }

        [UnityTest]
        public IEnumerator RingsAreRocksYouCanFlyInto()
        {
            int giant = System.Array.FindIndex(Solar.Descriptor.bodies, body => body.ringDensity > 0.5f);
            yield return HoverOver(giant, Solar.Descriptor.bodies[giant].radius);
            float middle = (Body.ringInner + Body.ringOuter) / 2f;
            HoverAt(new float3(middle, 60f, 0f));
            Player.ship.rotation = quaternion.LookRotationSafe(new float3(0, -0.3f, 1), new float3(0, 1, 0));
            yield return Seconds(3f);
            var field = Solar.Bodies[giant].GetComponent<RingField>();
            Assert.Greater(field.Drawn, 8, "cells of rock are drawn round the ship");
            Assert.Greater(Object.FindObjectsByType<MeshFilter>().Count(part => part.name == "Rocks" && part.sharedMesh.vertexCount > 100), 5);

            // drive at a big one
            var rocks = new System.Collections.Generic.List<Rock>();
            Rings.Rocks(Body, 0, 1, 0, rocks);
            float3 Now(double ahead) => Rings.Now(Body, 0, 1, Solar.Clock + ahead, rocks[0].position);
            float3 moving = (Now(0.01) - Now(-0.01)) / 0.02f, from = math.normalize(new float3(Now(0).x, 0, Now(0).z));
            Player.assist = false;
            HoverAt(Now(0) + from * (rocks[0].radius + 14f));
            Player.ship.velocity = moving - from * 50f;
            yield return Seconds(1f);
            Assert.That(Player.hull, Is.InRange(Player.MaxHull - 10f, Player.MaxHull - 1f), "a dent, hard as it was struck");
            StringAssert.Contains("rock in the rings", Player.Cause);
            // and a hull with next to nothing left is not finished off by one
            Player.hull = 3f;
            HoverAt(Now(0) + from * (rocks[0].radius + 14f));
            Player.ship.velocity = moving - from * 50f;
            yield return Seconds(1f);
            Assert.IsFalse(game.Dead);
        }

        [UnityTest]
        public IEnumerator TheInstrumentsMeasureToTheNearestSurface()
        {
            HoverAt(math.normalize(Player.ship.position) * (Body.radius + 300f));
            yield return null;
            Assert.AreEqual(Home, Player.Ahead);
            Assert.AreEqual(Altitude, Player.Range, 1f);
            // far out between the worlds it is still a distance to somewhere, not a height above nothing
            HoverAt(math.normalize(Player.ship.position) * (Body.soiRadius * 2f));
            yield return null;
            yield return null;
            Assert.AreEqual(0, Solar.Reference);
            Assert.AreNotEqual(0, Player.Ahead, "the nearest world, not the far-off sun");
            Assert.Greater(Player.Range, 1000f);
        }

        [UnityTest]
        public IEnumerator FrameRateIsWrittenToALog()
        {
            // however slowly the machine is running: until there are a few lines of it to read
            yield return Until(() => File.Exists(FrameLog.File) && File.ReadAllLines(FrameLog.File).Length > 6);
            string[] lines = File.ReadAllLines(FrameLog.File);
            StringAssert.Contains("burst", lines[2]);
            StringAssert.StartsWith("time\tfps", lines[3]);
            Assert.Greater(lines.Length, 4, "a line every half second");
            // the first of them may have been taken on the title page
            StringAssert.IsMatch("\t(title|foot)\t", lines[4]);
            string[] fields = lines[lines.Length - 1].Split('\t');
            Assert.AreEqual(lines[3].Split('\t').Length, fields.Length);
            Assert.Greater(float.Parse(fields[1]), 1f);
            Assert.AreEqual(Body.name, fields[12]);
            Assert.AreEqual("foot", fields[13]);
        }

        [UnityTest]
        public IEnumerator TheTitlePageOffersSaveSlotsThatEachKeepAGameOfTheirOwn()
        {
            Game.Start = 0;
            game.Jump(3);
            uint seed = game.Seed;
            Assert.AreEqual(0, game.Slot);
            yield return Choose("Quit to title");
            Assert.IsTrue(game.Title);
            Assert.IsTrue(game.Paused);
            Assert.AreEqual(Game.Slots + 3, game.Items.Length, "three slots, one delete action, settings and the way out");
            Assert.AreEqual("SLOT 1   ·   THE FIRST FACTORY", game.Items[0]);
            Assert.AreEqual("SLOT 2   ·   NEW GAME", game.Items[1]);
            Assert.AreEqual(0, game.Selected, "the slot played last is the one offered");
            // escape is no way off the title page
            yield return Press(new Controls { menu = true });
            Assert.IsTrue(game.Title);
            Assert.IsTrue(game.Paused);
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.IsTrue(game.Confirming);
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(1, game.Slot);
            Assert.IsFalse(game.Title);
            Assert.IsFalse(game.Paused, "choosing a slot starts playing it");
            Assert.IsTrue(game.Buried, "a new slot is a new game, from under the ground");
            Assert.AreEqual(1 << (int)Gear.Suit, Player.kit);
            Assert.IsTrue(File.Exists(Path.Combine(Application.persistentDataPath, "test-save2.json")));

            // the first is as it was left
            yield return Choose("Quit to title");
            Assert.AreEqual("SLOT 1   ·   THE FIRST FACTORY", game.Items[0]);
            Assert.AreEqual("SLOT 2   ·   BURIED", game.Items[1]);
            Assert.AreEqual(1, game.Selected);
            yield return Press(new Controls { up = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(0, game.Slot);
            Assert.AreEqual(3, game.Stage);
            Assert.IsTrue(Player.Has(Gear.Thrusters) && !Player.Has(Gear.Stealth));
            Assert.AreEqual(seed, game.Seed);

            // and the game opens on its title page, offering whichever was played last
            yield return Choose("Quit to title");
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(2, game.Slot);
            yield return SceneManager.LoadSceneAsync("Main");
            game = Object.FindAnyObjectByType<Game>();
            while (!game.Ready) yield return null;
            Assert.IsTrue(game.Title);
            Assert.AreEqual(0f, Time.timeScale, "the world waits behind it");
            Assert.AreEqual(2, game.Slot);
            Assert.AreEqual(2, game.Selected);
            Assert.IsTrue(game.Buried);
        }

        [UnityTest]
        public IEnumerator TheCheatsPageJumpsFromMissionToMission()
        {
            game.Jump(2);
            yield return Choose("Quit to title");
            Assert.IsFalse(game.Items.Contains(Game.Cheats));
            yield return Press(new Controls { typed = "idgaf" });
            Assert.IsTrue(game.Items.Contains(Game.Cheats));
            yield return Press(new Controls { confirm = true });
            yield return Choose(Game.Cheats);
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual("Missions", game.Page);
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(3, game.Stage);
            Assert.IsTrue(Player.Powered);
            yield return Choose(Game.Cheats);
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(Campaign.Won, game.Stage);
            Assert.IsTrue(game.Frontier.Local.shipyard);
            Assert.AreEqual(0, game.Frontier.Local.alienShips);
        }

        [UnityTest]
        public IEnumerator OnlyFittedSuitWeaponsCanBeSelected()
        {
            Player.fitted = 1 << (int)Gear.Rail;
            yield return Press(new Controls { select = 2 });
            Assert.AreEqual("Powered fists", Player.Weapon.name);
            yield return Press(new Controls { select = 4 });
            Assert.AreEqual("Rail gauntlet", Player.Weapon.name);
            Player.fitted = 1 << (int)Gear.BackMissiles;
            yield return Press(new Controls { select = 3 });
            Assert.AreEqual("Back missiles", Player.Weapon.name);
            yield return Press(new Controls { fire = true });
            Assert.IsTrue(game.Battle.Bolts.Any(bolt => bolt.homing > 0));
            HoverAt(math.normalize(Player.ship.position) * (Body.radius * 1.6f));
            yield return Press(new Controls { select = 2 });
            Assert.AreEqual("Seeker missiles", Player.Weapon.name);
            game.Jump(2);
            yield return Press(new Controls { select = 4 });
            Assert.AreEqual("Emergency fists", Player.Weapon.name);
        }

        [UnityTest]
        public IEnumerator ShotsBlowCratersInTheGround()
        {
            // dry ground well away from the hermit's camp, where nothing is ever dug
            float3 up = default;
            for (uint spot = 5; math.lengthsq(up) == 0f || math.distance(up * Body.radius, game.Hermit.Position) < 200f; spot++) up = Body.FindLanding(spot);
            HoverAt(up * (Body.GroundRadius(up) + 50f));
            Player.ship.rotation = quaternion.LookRotationSafe(-up, World(up));
            yield return Seconds(1f);
            float before = Body.GroundRadius(up);
            yield return Press(new Controls { select = 1 });
            yield return Press(new Controls { fire = true });
            yield return Seconds(0.5f);
            Assert.Greater(Body.craters.Length, 0);
            Assert.Less(Body.GroundRadius(up), before - 1f, "the ground under the ship has been dug out");
            Assert.AreEqual(2.5f * Combat.Guns[0].blast, Body.blasts.Last(blast => blast.scar != Scar.Pad).crater.w, 1e-3f, "a ship's guns tear far bigger holes than their blast alone");

            // someone standing in the crater stands on its floor
            Stand(up, World(up));
            yield return Seconds(0.5f);
            Assert.AreEqual(Body.GroundRadius(up), math.length(Player.walker.position), 0.05f);
        }

        [UnityTest]
        public IEnumerator HeatIsReadEverywhereAndTooMuchOfItKills()
        {
            yield return Seconds(0.3f);
            Assert.That(Climate.Celsius(Player.Heat), Is.InRange(-40f, 40f), "mild at home");

            // the powered suit survives a furnace world
            int furnace = System.Array.FindIndex(Solar.Descriptor.bodies, body => body.summary.Contains("furnace"));
            yield return HoverOver(furnace, 100f);
            float3 down = math.normalize(Player.ship.position);
            Assert.AreEqual(Player.MaxHull, Player.hull, 0.5f, "a hull can take it");
            Stand(down, World(down));
            yield return Seconds(1.5f);
            Assert.Greater(Player.Heat, 700f);
            Assert.AreEqual(Player.MaxHealth, Player.health, 0.1f, "the powered suit survives furnace heat");
            Player.health = Player.MaxHealth;

            // and a ship that sinks into a giant is cooked
            int giant = System.Array.FindIndex(Solar.Descriptor.bodies, body => body.kind == BodyKind.Gas);
            yield return HoverOver(giant, 200f);
            Assert.Less(Climate.Celsius(Player.Heat), -80f, "cold above the cloud tops");
            HoverAt(math.normalize(Player.ship.position) * (Body.radius - 400f));
            for (float t = 0; t < 10f && !game.Dead; t += Time.unscaledDeltaTime) yield return null;
            Assert.IsTrue(game.Dead);
            StringAssert.Contains("Cooked in the depths", game.Epitaph);
        }

        [UnityTest]
        public IEnumerator TheAsteroidBeltIsRocksTooAndMoonsAreChartedWhereTheyCanBeSeen()
        {
            Assert.IsTrue(Solar.Charted(Moon), "the home world's moon, from the home world");
            int far = System.Array.FindIndex(Solar.Descriptor.bodies, body => body.summary.Contains("sulphur"));
            Assert.IsFalse(Solar.Charted(far), "not a far giant's moon");
            Assert.IsTrue(Solar.Charted(Solar.Descriptor.bodies[far].orbit.parent) && Solar.Charted(Solar.Station), "planets and the base, always");
            // out of the home world's own space, its moon still shows as more than a speck, and so the autopilot will fly there
            float3 past = Solar.ToReference(Moon);
            HoverAt(past + math.normalize(past) * (2f * Body.soiRadius));
            yield return null;
            yield return null;
            Assert.AreEqual(0, Solar.Reference);
            Assert.IsTrue(Solar.Charted(Moon), $"the moon from {math.distance(Player.ship.position, Solar.ToReference(Moon)):0} m");
            Player.assist = false;
            Player.ship.rotation = quaternion.LookRotationSafe(Solar.ToReference(Moon) - Player.ship.position, new float3(0, 1, 0));
            yield return null;
            StringAssert.Contains(Solar.Descriptor.bodies[Moon].name.ToUpperInvariant(), Player.Travel());
            Player.Travel();
            yield return HoverOver(Home, 300f);

            var star = Solar.Descriptor.bodies[0];
            // a point in the plane the planets circle in, which is not the plane of the home world's own equator
            HoverAt(Solar.Descriptor.ToLocal(Solar.Reference, Solar.Clock, new double3(star.ringInner + 6000.0, 0, 0)));
            yield return null;
            yield return null;
            Assert.AreEqual(0, Solar.Reference, "the belt is in the star's own space");
            Assert.IsFalse(Solar.Charted(Moon), "from here the moon is a speck, and has no mark");
            yield return Seconds(3f);
            Assert.Greater(Solar.Bodies[0].GetComponent<RingField>().Drawn, 5, "asteroids are drawn round the ship");
            Assert.Greater(Object.FindObjectsByType<MeshFilter>().Count(part => part.name == "Rocks" && part.sharedMesh.vertexCount > 100), 3);
        }

        [UnityTest]
        public IEnumerator TheRadarGlobeShowsContactsInFrontBehindAndAbove()
        {
            yield return null;
            var camera = Camera.main.transform;
            foreach (Vector3 direction in new[] { camera.forward, -camera.forward, camera.up })
                game.Battle.Add(FoeKind.Mind, Player.Position + (float3)direction * 100);
            yield return null;
            yield return null;
            var radar = Object.FindAnyObjectByType<Radar>();
            var contacts = radar.GetComponentsInChildren<Transform>().Where(part => part.name == "Contact" && part.gameObject.activeSelf).ToArray();
            Assert.AreEqual(3, contacts.Length);
            Assert.IsTrue(contacts.Any(contact => contact.localPosition.z > 0));
            Assert.IsTrue(contacts.Any(contact => contact.localPosition.z < 0));
            Assert.AreEqual(3, contacts.Select(contact => contact.localPosition).Distinct().Count());
            Assert.IsNotNull(radar.transform.Find("Globe").GetComponent<MeshRenderer>());
        }



        // Not an assertion: saves pictures to Logs/Screenshots for a look at what the game draws.
        // The tour of every world and every screen takes a few minutes.
        [UnityTest, Timeout(900000)]
        public IEnumerator CapturesScreenshots()
        {
            Directory.CreateDirectory("Logs/Screenshots");
            foreach (string old in Directory.GetFiles("Logs/Screenshots", "*.png").Where(path => Path.GetFileNameWithoutExtension(path).Split('-')[0].Length == 2 && Path.GetFileName(path)[0] < '8')) File.Delete(old);
            var planet = Solar.Descriptor.bodies[Home];
            float3 sun = Sunward;

            // The first of many random directions on the home world that passes the test.
            float3 Find(System.Func<float3, float, bool> wanted)
            {
                var rnd = new Unity.Mathematics.Random(11);
                for (int i = 0; i < 40000; i++)
                {
                    float3 direction = rnd.NextFloat3Direction();
                    if (wanted(direction, planet.GroundRadius(direction) - planet.radius)) return direction;
                }
                Assert.Fail("no such place on this planet");
                return default;
            }

            // Waits for terrain to stream in, then renders the main camera, HUD and all, to a PNG.
            // A batch-mode editor has no game window to grab, so the camera draws to a texture all
            // through the tour, which also keeps the HUD laid out for the picture's shape.
            var target = new RenderTexture(1280, 720, 24);
            Camera.main.targetTexture = target;
            IEnumerator Shot(string name, float pitch = 0f, float wait = 3f)
            {
                for (float t = 0; t < wait; t += Time.unscaledDeltaTime) yield return null;
                var camera = Camera.main;
                camera.transform.rotation *= Quaternion.Euler(pitch, 0f, 0f);
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                File.WriteAllBytes($"Logs/Screenshots/{name}.png", image.EncodeToPNG());
                RenderTexture.active = null;
                Object.Destroy(image);
            }

            bool Level(float3 d) => math.dot(planet.GroundNormal(d), d) > 0.96f;

            // The heading from a spot that looks out over the lowest ground in reach, not into a hillside.
            float3 Outlook(float3 from)
            {
                float3 view = World(from);
                for (float angle = 0f; angle < 6.28f; angle += 0.4f)
                {
                    float3 heading = math.rotate(quaternion.AxisAngle(from, angle), World(from));
                    if (planet.GroundRadius(math.normalize(from + heading * 0.15f)) < planet.GroundRadius(math.normalize(from + view * 0.15f))) view = heading;
                }
                return view;
            }

            // Hangs in space off the sunlit side of the reference body, a little above its equator, looking at it.
            void Behold(float distance, float above = 0.3f)
            {
                float3 from = math.normalize(Sunward + new float3(0f, above, 0f));
                Player.ship.rotation = quaternion.LookRotationSafe(-from, new float3(0, 1, 0));
                HoverAt(from * (Body.radius * distance));
            }

            // the title page, as the game opens on it
            yield return Choose("Quit to title");
            yield return Shot("00-title", 0f, 4f);
            yield return Press(new Controls { confirm = true });
            // the mission's card shows as play begins: let it fade before the pictures of the world
            yield return Seconds(11f);

            float3 meadow = Find((d, height) => height > 5f && height < 30f && math.dot(d, sun) > 0.7f && Level(d));
            Stand(meadow, World(meadow));
            yield return Shot("01-meadow-at-noon");
            yield return Shot("02-ground-underfoot", 40f, 0.5f);

            float3 woods = Find((d, height) => height > 6f && height < 0.3f * planet.terrain.mountainAmp && math.dot(d, sun) > 0.5f
                && math.dot(planet.GroundNormal(d), d) > 0.95f && noise.snoise(d * 9f + planet.terrain.offset) > 0.3f);
            Stand(woods, World(woods));
            yield return Shot("03-in-the-woods");
            yield return Press(new Controls { scan = true });
            yield return Shot("04-scanner", 0f, 2.5f);

            int depth = QuadtreeLod.DepthFor(planet.radius, Wildlife.CellSize(Creature.Grazer));
            Troop herd = default;
            for (int cell = 0; herd.count < 6 || math.dot(herd.centre, sun) < 0.5f; cell++)
                Wildlife.Find(planet, Creature.Grazer, new NodeId(cell % 6, depth, cell / 6 % (1 << depth), cell / 6 / (1 << depth)), out herd);
            Stand(math.normalize(herd.centre + World(herd.centre) * (16f / planet.radius)), -World(herd.centre));
            yield return Shot("05-herd", 4f);

            float3 beach = Find((d, height) => height > 0.5f && height < 2.5f && math.dot(d, sun) > 0.4f);
            Stand(beach, World(beach));
            yield return Shot("06-beach");

            float3 peak = Find((d, height) => height > 0.6f * planet.terrain.mountainAmp && math.dot(d, sun) > 0.3f);
            Stand(peak, Outlook(peak));
            yield return Shot("07-mountain-top", 15f);

            // dry land under whichever storm has the most daylight
            float4 storm = Enumerable.Range(0, Weather.Storms).Select(i => Weather.Storm(planet, i, Solar.Clock)).OrderByDescending(s => math.dot(s.xyz, sun)).First();
            float3 wet = Find((d, height) => height > 3f && height < 60f && math.dot(d, storm.xyz) > math.cos(0.4f * storm.w) && Level(d));
            Stand(wet, Outlook(wet));
            yield return Shot("08-rain", -6f);

            float3 dusk = Find((d, height) => height > 5f && height < 60f && math.dot(d, sun) > 0.02f && math.dot(d, sun) < 0.1f && Level(d));
            Stand(dusk, sun);
            yield return Shot("09-sunset");

            float3 night = Find((d, height) => height > 3f && math.abs(d.y) > 0.7f && math.abs(d.y) < 0.78f && math.dot(d, sun) < -0.35f && Level(d));
            Stand(night, math.normalize(Walker.Project(new float3(0f, math.sign(night.y), 0f), night)));
            yield return Shot("12-aurora", -28f);

            Player.ship.rotation = quaternion.LookRotationSafe(World(meadow), meadow);
            HoverAt(meadow * (planet.GroundRadius(meadow) + 60f));
            yield return Shot("13-cockpit");
            yield return Press(new Controls { toggleView = true });
            yield return Shot("14-ship-over-land", 0f, 1f);

            Behold(3.2f);
            yield return Shot("15-home-from-space");

            // The app's icon: the home world from space with its moon beyond it, cut to the rounded
            // square that every icon on a Mac sits in. Kept in Assets/Infinigame/Icon.png.
            float3 moonward = math.normalize(Solar.ToReference(Moon)), sunlit = math.normalize(Walker.Project(Sunward, moonward));
            float3 vantage = math.normalize(-moonward * math.cos(math.radians(40f)) + sunlit * math.sin(math.radians(40f)));
            HoverAt(vantage * (planet.radius * 3.2f));
            yield return Seconds(4f);
            var lens = Camera.main;
            var square = new RenderTexture(1024, 1024, 24);
            var hud = GameObject.Find("HUD").GetComponent<Canvas>();
            var hull = GameObject.Find("Ship");
            (hud.enabled, lens.targetTexture, lens.fieldOfView) = (false, square, 56f);
            hull.SetActive(false);
            lens.transform.position = (Vector3)(vantage * (planet.radius * 3.2f));
            Vector3 inward = -(Vector3)vantage, moonSide = Vector3.ProjectOnPlane((Vector3)Solar.ToReference(Moon) - lens.transform.position, inward).normalized;
            // the world a little down and to the left of the middle, its moon up and to the right
            lens.transform.rotation = Quaternion.LookRotation(inward + 0.1f * moonSide, moonSide) * Quaternion.Euler(0f, 0f, 45f);
            lens.Render();
            RenderTexture.active = square;
            var icon = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            icon.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
            RenderTexture.active = null;
            var cut = icon.GetPixels();
            for (int i = 0; i < cut.Length; i++)
            {
                // a square with corners rounded the way a superellipse rounds them, an eighth in from each edge
                float2 from = math.abs(new float2(i % 1024, i / 1024) + 0.5f - 512f) / 412f;
                cut[i].a = math.saturate((1f - math.pow(math.pow(from.x, 5f) + math.pow(from.y, 5f), 0.2f)) * 412f + 0.5f);
            }
            icon.SetPixels(cut);
            File.WriteAllBytes("Logs/Screenshots/icon.png", icon.EncodeToPNG());
            Object.Destroy(icon);
            (hud.enabled, lens.targetTexture) = (true, target);
            hull.SetActive(true);
            square.Release();
            float3 pole = math.normalize(new float3(0f, 1f, 0f) - sun * 0.9f);
            Player.ship.rotation = quaternion.LookRotationSafe(-pole, World(pole));
            HoverAt(pole * (planet.radius * 2.6f));
            yield return Shot("16-aurora-from-space");

            // the sunlit side of the moon, which becomes the body at the origin
            yield return HoverOver(Moon, 40f);
            float3 noon = Sunward;
            Player.ship.rotation = quaternion.LookRotationSafe(World(noon), noon);
            HoverAt(noon * (Body.GroundRadius(noon) + 25f));
            yield return Shot("17-over-the-moon", 10f);

            // other worlds: from space, and from the ground where they have any
            int shot = 18;
            foreach (var (body, name) in new[] { (1, "scorched-rock"), (2, "shrouded-furnace"), (5, "rust-desert"), (6, "rubble-moon"), (9, "banded-giant"), (10, "volcano-moon"),
                (14, "ringed-giant"), (20, "hazy-moon"), (22, "tilted-ice-giant"), (28, "last-world"), (30, "backwards-moon") })
            {
                yield return HoverOver(body, Solar.Descriptor.bodies[body].radius);
                // near enough to stay inside its sphere of influence
                Behold(body == 14 ? 3.6f : body == 6 ? 2.6f : 3.2f, body == 14 ? 0.5f : 0.3f);
                yield return Shot($"{shot++}-{name}");
                float3 day = math.normalize(Sunward + World(Sunward) * 0.4f);
                if (Body.kind == BodyKind.Gas)
                {
                    Player.ship.rotation = quaternion.LookRotationSafe(World(day), day);
                    HoverAt(day * (Body.radius + (body == 14 ? Body.radius * 0.5f : 120f)));
                }
                else Stand(day, World(day));
                yield return Shot($"{shot++}-{name}-close", body == 14 ? -25f : 0f);
            }

            // the sky-life of a giant: hang beside a herd of drifters
            yield return HoverOver(9, 600f);
            int cells = QuadtreeLod.DepthFor(Body.radius, Wildlife.CellSize(Creature.Drifter));
            Troop drifters = default;
            for (int cell = 0; drifters.count == 0 || math.dot(drifters.centre, Sunward) < 0.5f; cell++)
                Wildlife.Find(Body, Creature.Drifter, new NodeId(cell % 6, cells, cell / 6 % (1 << cells), cell / 6 / (1 << cells)), out drifters);
            float3 beside = math.normalize(drifters.centre + World(drifters.centre) * (500f / Body.radius));
            Player.ship.rotation = quaternion.LookRotationSafe(drifters.centre * drifters.level - beside * (drifters.level + 60f), beside);
            HoverAt(beside * (drifters.level + 60f));
            yield return Shot("40-drifters", 0f, 2f);

            // striders on the cold desert
            yield return HoverOver(5, 200f);
            cells = QuadtreeLod.DepthFor(Body.radius, Wildlife.CellSize(Creature.Strider));
            Troop striders = default;
            for (int cell = 0; striders.count == 0 || math.dot(striders.centre, Sunward) < 0.4f; cell++)
                Wildlife.Find(Body, Creature.Strider, new NodeId(cell % 6, cells, cell / 6 % (1 << cells), cell / 6 / (1 << cells)), out striders);
            Stand(math.normalize(striders.centre + World(striders.centre) * (30f / Body.radius)), -World(striders.centre));
            yield return Shot("41-striders", 6f, 2.5f);

            // the raiders' base in the asteroid belt, and the belt itself
            yield return HoverOver(Solar.Station, 700f);
            Player.ship.rotation = quaternion.LookRotationSafe(-Player.ship.position, new float3(0, 1, 0));
            yield return Shot("42-raiders-base", 0f, 2f);
            game.Battle.Foes.Clear();
            game.Battle.Bolts.Clear();
            Player.hull = Player.MaxHull;
            var star = Solar.Descriptor.bodies[0];
            var asteroids = new System.Collections.Generic.List<Rock>();
            HoverAt(Player.ship.position + math.normalize(Player.ship.position) * 6000f);
            yield return null;
            yield return null;
            for (int sector = 0; asteroids.Count == 0; sector++) Rings.Rocks(star, 0, Rings.Bands(star, 0) / 2, sector, asteroids);
            float3 asteroid = Rings.Now(star, 0, Rings.Bands(star, 0) / 2, Solar.Clock, asteroids[0].position), outward = math.normalize(new float3(asteroid.x, 0f, asteroid.z));
            // from the sunward side, so that its lit face is the one in view
            Player.ship.rotation = quaternion.LookRotationSafe(outward, new float3(0, 1, 0));
            HoverAt(asteroid - outward * (asteroids[0].radius * 4f));
            yield return Shot("43-asteroid-belt", 0f, 4f);

            // the broad rings from above their sunlit side, then from down among the rocks they are made of
            yield return HoverOver(14, Solar.Descriptor.bodies[14].radius);
            float3 lit = math.normalize(Sunward * new float3(1, 0, 1)), round = math.cross(new float3(0, 1, 0), lit);
            float ring = 0.42f * Body.ringInner + 0.58f * Body.ringOuter;
            Player.ship.rotation = quaternion.LookRotationSafe(round - new float3(0, 0.45f, 0), new float3(0, 1, 0));
            HoverAt(lit * ring + new float3(0, 260f, 0));
            yield return Shot("44-over-the-rings", 0f, 4f);
            Player.ship.rotation = quaternion.LookRotationSafe(round - lit * 0.2f, new float3(0, 1, 0));
            HoverAt(lit * ring + new float3(0, 5f, 0));
            // a ship holding still here is soon struck, and the picture wants one that is not yet wreckage
            Player.hull = 1e6f;
            yield return Shot("45-among-the-ring-rocks", 0f, 4f);
            Player.hull = Player.MaxHull;

            // back on the home world: a fight on foot, a fight in the air and the autopilot
            yield return HoverOver(Home, 300f);
            float3 noonNow = Sunward;
            // open ground: woods would hide them
            float3 field = Find((d, height) => height > 5f && height < 30f && math.dot(d, noonNow) > 0.7f && Level(d) && noise.snoise(d * 9f + planet.terrain.offset) < -0.45f);
            Stand(field, World(field));
            yield return Seconds(2f);
            foreach (float side in new[] { -5f, 4f })
            {
                float3 spot = math.normalize(Player.walker.position + Player.walker.forward * 13f + math.cross(field, Player.walker.forward) * side);
                game.Battle.Add(FoeKind.Drone, spot * (planet.GroundRadius(spot) + 1.8f));
            }
            input.fire = true;
            yield return Shot("46-drones", 0f, 1.8f);
            game.Battle.Foes.Clear();
            Player.health = Player.MaxHealth;

            Player.ship.rotation = quaternion.LookRotationSafe(World(field), field);
            HoverAt(field * (planet.radius * 1.5f));
            yield return null;
            float3 prow = math.rotate(Player.ship.rotation, new float3(0, 0, 1));
            game.Battle.Add(FoeKind.Raider, Player.ship.position + prow * 120f + field * 16f - math.cross(field, prow) * 30f);
            game.Battle.Add(FoeKind.Raider, Player.ship.position + prow * 320f - field * 30f + math.cross(field, prow) * 80f);
            yield return Shot("47-raiders", 0f, 0.8f);
            // the nearer of them shot half to pieces, then the other going up
            var holed = game.Battle.Foes[0];
            holed.health *= 0.35f;
            game.Battle.Foes[0] = holed;
            yield return Shot("68-raider-holed", 0f, 0.2f);
            game.Effects.Explode(game.Battle.Foes[1].position, game.Battle.Foes[1].velocity, 10f);
            game.Battle.Foes.RemoveAt(1);
            yield return Shot("69-raider-explodes", 0f, 0.6f);
            // then missiles, and a shot into the ground to leave a crater
            yield return Press(new Controls { select = 2, fire = true });
            yield return Shot("48-missiles", 0f, 0.6f);
            input.fire = false;
            game.Battle.Foes.Clear();
            game.Battle.Bolts.Clear();
            Player.hull = Player.MaxHull;

            Player.ship.rotation = quaternion.LookRotationSafe(-field, World(field));
            HoverAt(field * (planet.GroundRadius(field) + 45f));
            input.fire = true;
            yield return Seconds(4f);
            input.fire = false;
            Player.ship.rotation = quaternion.LookRotationSafe(World(field) - field * 0.5f, field);
            HoverAt(field * (planet.GroundRadius(field) + 40f) - World(field) * 50f);
            yield return Shot("49-craters", 0f, 2.5f);

            // a torpedo's cloud over the hills, seen from the ground a few hundred metres off: away from the
            // field, which the lance has dug into a flooded pit by now
            float3 hill = Find((d, height) => height > 5f && height < 30f && math.dot(d, Sunward) > 0.6f && Level(d) && math.dot(d, field) < 0.9f);
            Stand(hill, World(hill));
            Player.health = 1e6f;
            float3 ground0 = math.rotate(quaternion.AxisAngle(math.cross(hill, World(hill)), 350f / planet.radius), hill);
            game.Effects.Mushroom(ground0 * planet.GroundRadius(ground0), ground0, 96f);
            yield return Shot("70-mushroom-cloud", -14f, 9f);
            game.Effects.Clear();
            Player.health = Player.MaxHealth;

            Player.ship.rotation = quaternion.LookRotationSafe(Solar.ToReference(14) - Player.ship.position, new float3(0, 1, 0));
            HoverAt(field * (planet.radius * 1.6f));
            yield return null;
            yield return Press(new Controls { travel = true });
            yield return Shot("50-autopilot", 0f, 1.5f);
            yield return Press(new Controls { travel = true });

            input.systemView = true;
            yield return Shot("51-system-map", 0f, 1.5f);
            input.systemView = false; yield return null;
            yield return Press(new Controls { systemView = true });
            yield return Press(new Controls { help = true });
            yield return Shot("52-controls", 0f, 1f);
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { down = true });
            yield return Shot("53-pause-menu", 0f, 1f);
            yield return Press(new Controls { down = true });
            Oracle.Tools[0] = StandIn("claude-somebody", "echo '{\"loggedIn\":true}'");
            Oracle.Tools[1] = StandIn("codex-somebody", "echo 'Logged in using ChatGPT'");
            yield return Press(new Controls { confirm = true });
            yield return Shot("54-settings", 0f, 1.5f);
            for (int i = 0; i < 5; i++) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            for (int i = 0; i < 3; i++) yield return Press(new Controls { down = true });
            yield return Shot("55-claude", 0f, 1f);
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            yield return Press(new Controls { down = true });
            yield return Shot("56-chatgpt", 0f, 1f);
            for (int i = 0; i < 3; i++) yield return Press(new Controls { menu = true });
            Assert.IsFalse(game.Paused);
            Player.Hurt(1000f, $"Shot down over {Body.name}");
            yield return null;
            yield return Shot("57-died", 0f, 1.5f);

            // the game as it begins: under the ground, then out to the stranger's fire
            yield return Press(new Controls { confirm = true });
            game.Jump(Campaign.Buried);
            yield return Seconds(Game.BootDuration + 0.5f);
            for (int click = 0; click < 3; click++)
            {
                input.fire = true;
                yield return null;
                yield return null;
                input.fire = false;
                yield return null;
            }
            yield return Shot("58-buried", 0f, 1f);
            game.Jump(Campaign.Stranger);
            yield return Shot("62-a-mission-begins", 0f, 3f);
            float3 camp = math.normalize(game.Hermit.Position), back = math.normalize(Walker.Project(planet.FindLanding(game.Seed + 1u) - camp, camp));
            Stand(math.normalize(camp + back * (4f / planet.radius)), -back);
            yield return Shot("10-the-hermit", 4f, 9f);
            yield return Press(new Controls { interact = true });
            yield return Press(new Controls { typed = "Which of those lights has the rings?" });
            yield return Shot("11-talking", 4f, 2.5f);
            yield return Press(new Controls { menu = true });

            // the way to the raiders' first outpost: marked from where the player wakes and on the map of the world, then the place itself
            game.Jump(2);
            float3 fort = math.normalize(game.Outpost.Position), woke = math.normalize(Player.walker.position);
            Player.walker.forward = math.normalize(Walker.Project(game.Outpost.Position - Player.walker.position, woke) + 0.5f * World(woke));
            yield return Shot("63-the-way-there", 0f, 11f);
            input.systemView = true;
            yield return Shot("64-map-of-the-world", 0f, 2.5f);
            input.systemView = false; yield return null;
            yield return Press(new Controls { systemView = true });
            Stand(math.normalize(game.Outpost.Position + World(fort) * 42f), -World(fort));
            Player.health = 1e6f;
            yield return Shot("59-the-outpost", -6f, 6f);
            // its guard dead, what they kept is there to be taken
            game.Battle.Foes.Clear();
            game.Award(FoeKind.Mind);
            float3 crate = math.normalize(game.Outpost.Prize);
            Stand(math.normalize(game.Outpost.Prize + World(crate) * 4.5f), -World(crate));
            yield return Shot("65-the-prize", -4f, 5f);

            game.Jump(4);
            yield return null;
            float3 held = math.normalize(Player.ship.position);
            Player.walker.position = Player.ship.position - held * 5;
            Player.walker.forward = World(held);
            yield return Shot("66-orbital-ship", -4f, 0.1f);

            // with the ship comes a launcher, for raiders that come over while it is on the ground
            game.Jump(Campaign.Tower);
            yield return Seconds(0.5f);
            float3 here = math.normalize(Player.walker.position);
            yield return Press(new Controls { select = 5 });
            yield return Seconds(11f);
            game.Battle.Add(FoeKind.Raider, Player.walker.position + here * 90f + Player.walker.forward * 260f);
            Player.health = 1e6f;
            yield return Press(new Controls { fire = true });
            yield return Shot("67-launcher", 14f, 0.9f);
            game.Battle.Foes.Clear();
            game.Battle.Bolts.Clear();

            yield return Press(new Controls { menu = true });
            for (int i = 0; i < 3; i++) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            yield return Shot("61-cheats", 0f, 1f);
            Camera.main.targetTexture = null;
            target.Release();
        }

        // Some direction along the ground at a point on a sphere.
        static float3 World(float3 direction) => math.normalize(math.cross(direction, new float3(0.2f, 0.9f, 0.4f)));
    }
}
