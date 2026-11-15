using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Infinigame
{
    // One page of the scanner's journal.
    [Serializable]
    public class Entry
    {
        public string key, title, text;
    }

    // The one object in the scene: builds the solar system and everything in it, owns the save, and
    // decides each frame who the controls belong to: the player, the pause menu or a conversation.
    //
    // It also keeps the game, which is a string of missions (see Campaign). The player wakes
    // buried, with nothing, and wins everything they carry and fly from the raiders, a mission at
    // a time, until their citadel falls and its warp drive opens the way to a harder system.
    // Dying costs only the mission in hand, which begins again. Each save slot holds a game and a
    // journal of its own, and the game opens on a title page where one is chosen.
    public class Game : MonoBehaviour
    {
        [Serializable]
        class Save
        {
            public uint seed = 1;
            public int depth = 1, runs;
            // which mission is in hand, and everything won by the ones before it, as Campaign's bits
            public int stage, kit;
            public float look = DesktopControls.Sensitivity;
            public List<Entry> journal = new List<Entry>();
            public List<string> talk = new List<string>();
        }

        // How many slots there are, and how many clicks it takes to dig out of the ground.
        public const int Slots = 3, Digs = 6;
        public const string Settings = "Settings", Cheats = "Cheats";
        // The rows of the settings page before those of the oracle's voices.
        const int Dials = 5;
        public static readonly string[] Menu = { "Resume", "Controls", Settings, Cheats, "New game", "Quit to title", "Quit to desktop" };
        public static readonly string[] Confirm = { "Keep playing", "Quit" }, Over = { "Try again" };

        public SolarSystem System { get; private set; }
        public Player Player { get; private set; }
        public Scanner Scanner { get; private set; }
        public Hermit Hermit { get; private set; }
        public Battle Battle { get; private set; }
        public Outpost Outpost { get; private set; }
        public Sounds Sounds { get; private set; }
        public Speech Speech { get; private set; }
        public Effects Effects { get; private set; }
        public uint Seed => save.seed;
        public int Slot { get; private set; }
        // How many systems deep the game has gone, which mission is in hand there (Campaign.Won once
        // the citadel has fallen) and how tough that makes the raiders.
        public int Depth => save.depth;
        public int Stage => save.stage;
        public int Threat => 1 + 4 * (save.depth - 1) + save.stage / 4;
        public Mission Mission => Campaign.Missions[math.min(save.stage, Campaign.Citadel)];
        // Still under the ground, and how many handfuls of it have been torn away.
        public bool Buried => save.stage == Campaign.Buried;
        public int Dug { get; private set; }
        // What the player is about, for the HUD.
        public string Objective => save.stage >= Campaign.Won ? "THE SYSTEM IS FREE  ·  G IN OPEN SPACE WARPS TO THE NEXT"
            : $"{Mission.name.ToUpperInvariant()}  ·  {(Outpost.Fallen ? Outpost.Errand : Mission.task)}{(Outpost.World >= 0 && Outpost.Left > 0 && Outpost.Left < Mission.count ? $"  ·  {Outpost.Left} LEFT" : "")}";
        // Where the mission in hand leads on the world the player is at, relative to its centre:
        // the stranger's fire, the raiders' place or what they guarded. Nowhere, if it is on another world.
        public float3? Goal => System.Reference == SolarSystemDescriptor.Home && save.stage == Campaign.Stranger ? Hermit.Position
            : Outpost.World != System.Reference ? (float3?)null : Outpost.Fallen ? Outpost.Prize : save.stage == Campaign.Citadel ? (float3?)null : Outpost.Position;
        // Everything scanned so far, and the talk with this system's hermit as alternating lines.
        public List<Entry> Journal => save.journal;
        public List<string> Talk => save.talk;

        // The menu: the world waits while it is open. Quitting asks first. The game opens on its
        // title page, where a save slot is chosen to play; after that it is the pause menu.
        public bool Paused { get; private set; }
        public bool Title { get; private set; }
        public bool Confirming { get; private set; }
        // The page of the menu that is open, when it is not the first: Cheats, Settings and, from
        // there, the name of one of the oracle's voices.
        public string Page { get; private set; }
        public int Selected { get; private set; }
        // When play last began from the title page.
        public float Began { get; private set; }
        // The list of controls, shown only when asked for.
        public bool ShowControls { get; private set; }
        // The player is dead: what killed them and what they were about, shown until they try again.
        public bool Dead { get; private set; }
        public string Epitaph { get; private set; }
        public string Tally { get; private set; }
        // The menu on screen, if any.
        public string[] Items => Dead ? Over : Confirming ? Confirm
            : Page == Cheats ? new[] { "Skip this mission", "Back a mission", "Take every weapon and all gear", "Mend suit and hull", "Back" }
            : Voice >= 0 ? new[]
            {
                $"Have {Page} write", $"MODEL   ·   {Oracle.Model(Voice).name.ToUpperInvariant()}", $"THINKING   ·   {Oracle.Effort(Voice).ToUpperInvariant()}",
                Oracle.Roads(Voice)[0], Oracle.Roads(Voice)[1], "Use the key on the clipboard", "Forget key and sign-in", "Back",
            }
            : Page == Settings ? new[]
            {
                $"DETAIL   ·   {QualitySettings.names[QualitySettings.GetQualityLevel()].ToUpperInvariant()}", $"MUSIC   ·   {(Music.Muted ? "OFF" : "ON")}",
                $"LOOK SPEED   ·   {DesktopControls.Sensitivity:0.00}", $"FULL SCREEN   ·   {(Screen.fullScreen ? "ON" : "OFF")}", $"SPOKEN VOICES   ·   {(Speech.Muted ? "OFF" : "ON")}",
            }
            // who can write for the scanner and the hermit: how the player stands with each, and the model it would use
            .Concat(Oracle.Voices.Select((voice, i) => $"{voice.ToUpperInvariant()}   ·   {signedIn[i]}   ·   {Oracle.Model(i).name.ToUpperInvariant()}{(i == Oracle.Voice ? "   ·   IN USE" : "")}"))
            .Append("Back").ToArray()
            : Title ? slots.Append(Settings).Append("Quit to desktop").ToArray() : Menu;
        // On a voice's page: how the player stands with it and what it has been asked for so far,
        // and what the highlighted item would do.
        public string Standing => Voice < 0 ? null : $"{signedIn[Voice]}{(Voice == Oracle.Voice ? "   ·   IN USE" : "")}   ·   {Oracle.Used(Voice)}";
        public string Hint => Page == Cheats ? Selected == 2 ? "EVERYTHING THE RAIDERS HOLD BUT THE CITADEL'S WARP DRIVE: EVERY GUN, THE SHIP AND ALL THAT IS FITTED TO IT, TORPEDOES AND ALL"
                : "FOR NOW, WHILE THE GAME IS BEING MADE: JUMP FROM MISSION TO MISSION WITH ALL THAT THE EARLIER ONES WIN"
            : Page == Settings ? Selected switch
            {
                0 => "HOW MUCH IS DRAWN, AND HOW FAR OFF   ·   [ AND ] DO THE SAME WHILE PLAYING",
                1 => "M DOES THE SAME WHILE PLAYING",
                2 => "HOW FAST THE VIEW TURNS   ·   - AND = DO THE SAME WHILE PLAYING",
                3 => "OPTION-RETURN DOES THE SAME WHILE PLAYING",
                4 => "WHETHER THE HERMIT'S WORDS ARE SPOKEN ALOUD AS WELL AS WRITTEN",
                _ when Selected < Dials + Oracle.Voices.Length => "SIGN IN, GIVE IT A KEY, PICK ITS MODEL AND HOW HARD IT THINKS   ·   THE ONE IN USE WRITES THE SCANNER'S JOURNAL AND THE HERMIT'S TALK",
                _ => "",
            }
            : Voice >= 0 ? Selected switch
            {
                0 => $"THE SCANNER'S JOURNAL AND THE HERMIT'S TALK WILL BE {Page.ToUpperInvariant()}'S",
                1 => "A QUICKER MODEL ANSWERS SOONER AND COSTS LESS",
                2 => "THE LESS IT THINKS BEFORE IT ANSWERS, THE SOONER IT DOES",
                3 when Voice == 0 => "CLAUDE CODE'S OWN SIGN-IN, IN YOUR BROWSER   ·   WHAT THE GAME ASKS COUNTS AGAINST YOUR CLAUDE PLAN",
                4 when Voice == 0 => "ANTHROPIC'S ANT, IN YOUR BROWSER   ·   PAID FROM THE API CREDITS A MAX, ULTRA OR TEAM PLAN INCLUDES EACH MONTH",
                3 => "CODEX'S OWN SIGN-IN, IN YOUR BROWSER   ·   WHAT THE GAME ASKS COUNTS AGAINST YOUR CHATGPT PLAN'S CODEX USE",
                4 => "OPENAI'S OWN PAGE, FOR A DEVICE WITH NO CODEX   ·   CHATGPT PLUS OR PRO   ·   WHAT IS LEFT IS AT CHATGPT.COM/SETTINGS/USAGE",
                5 => $"COPY A KEY AT {(Voice == 0 ? "PLATFORM.CLAUDE.COM/SETTINGS/KEYS" : "PLATFORM.OPENAI.COM/API-KEYS")}, THEN CHOOSE THIS   ·   IT STAYS ON THIS DEVICE   ·   YOUR BALANCE IS UNDER BILLING THERE",
                6 => $"DROPS WHAT THIS GAME HOLDS   ·   {(Voice == 0 ? "CLAUDE CODE'S AND ANT'S OWN SIGN-INS" : "CODEX'S OWN SIGN-IN")}, IF ANY, ARE LEFT ALONE",
                _ => "",
            }
            // on the pause menu itself: what there is to do, and what it wins
            : Page == null && !Title && !Dead && save.stage < Campaign.Won ? $"{(Outpost.Fallen ? Outpost.Errand : Mission.task)}   ·   WINS {Campaign.Spoils(save.stage)}"
            : "";
        // Detail and music are the device's to remember, not a character's. Tests keep theirs under another name.
        public static string Prefs = "infinigame";
        // Where a key is taken from. Tests supply their own.
        public static Func<string> Clipboard = () => GUIUtility.systemCopyBuffer;
        // A line of news for the HUD, and when it broke.
        public string Notice { get; private set; }
        public float NoticeAt { get; private set; } = float.MinValue;

        // The warp drive, once it has been taken from the citadel, needs open space round it.
        bool InOpenSpace => Player.flying && !Player.ship.landed && math.length(Player.Position) > System.ReferenceBody.radius * 1.6f;
        public bool CanWarp => InOpenSpace && Player.Has(Gear.Warp);

        // What the interact key would do right now.
        public string Prompt => Hermit.Near ? $"Talk to {Hermit.Name}" : Outpost.Near ? $"Take {Campaign.Spoils(save.stage).ToLowerInvariant()}" : Player.Prompt;

        Save save;
        Hud hud;
        string[] slots;
        // whether the player is signed in to each of the oracle's voices, as far as is known
        readonly string[] signedIn = new string[Oracle.Voices.Length];
        // whether the trigger was down last frame, to tell one dig from the next, and whether the radiation was already biting or the hull already failing
        bool pulled, cooking, failing;
        // Tests point this at a scratch file so they never touch a real save. It is the first slot's; the others' are numbered.
        public static string SaveFile = "save.json";
        // The voice whose page of the menu is open, or -1.
        int Voice => Array.IndexOf(Oracle.Voices, Page);
        static string SlotPath(int slot) => Path.Combine(Application.persistentDataPath, slot == 0 ? SaveFile : $"{Path.GetFileNameWithoutExtension(SaveFile)}{slot + 1}.json");

        // The mission a new game begins at, with all that the ones before it win. Tests begin part-way through.
        public static int Start;

        static Save Read(int slot)
        {
            var read = new Save { stage = Start, kit = Campaign.Kit(Start) };
            if (File.Exists(SlotPath(slot))) JsonUtility.FromJsonOverwrite(File.ReadAllText(SlotPath(slot)), read);
            return read;
        }

        void Awake()
        {
            // in step with the screen, at 60 frames a second: every other refresh of a fast display
            QualitySettings.vSyncCount = Screen.currentResolution.refreshRateRatio.value > 90 ? 2 : 1;
            Application.targetFrameRate = 60;
            Cursor.lockState = CursorLockMode.Locked;
            // the slot played last is the one written last
            for (int slot = 1; slot < Slots; slot++)
                if (File.GetLastWriteTimeUtc(SlotPath(slot)) > File.GetLastWriteTimeUtc(SlotPath(Slot)))
                    Slot = slot;
            save = Read(Slot);
            DesktopControls.Sensitivity = save.look;
            Oracle.Load();
            QualitySettings.SetQualityLevel(PlayerPrefs.GetInt($"{Prefs}.detail", QualitySettings.GetQualityLevel()));
            Music.Muted = PlayerPrefs.GetInt($"{Prefs}.muted") == 1;
            Speech.Muted = PlayerPrefs.GetInt($"{Prefs}.silent") == 1;
            System = gameObject.AddComponent<SolarSystem>();
            gameObject.AddComponent<Rainfall>();
            Player = new GameObject("Player").AddComponent<Player>();
            Player.Init(System);
            Scanner = gameObject.AddComponent<Scanner>();
            Scanner.game = this;
            Hermit = gameObject.AddComponent<Hermit>();
            Hermit.game = this;
            Effects = gameObject.AddComponent<Effects>();
            Effects.game = this;
            Battle = gameObject.AddComponent<Battle>();
            Battle.game = this;
            System.ReferenceChanged += Battle.Rebase;
            Outpost = gameObject.AddComponent<Outpost>();
            Outpost.game = this;
            Sounds = gameObject.AddComponent<Sounds>();
            Sounds.game = this;
            Speech = gameObject.AddComponent<Speech>();
            gameObject.AddComponent<Music>().game = this;
            gameObject.AddComponent<Arms>().game = this;
            hud = gameObject.AddComponent<Hud>();
            hud.game = this;
            gameObject.AddComponent<FrameLog>().game = this;
            Enter();
            ShowTitle();
        }

        // Builds the saved seed's system, terrain and gravity together, round the world the
        // mission in hand begins on, settles its hermit and the raiders' outpost and clears the air.
        void Load()
        {
            var system = SolarSystemDescriptor.Generate(save.seed);
            // once the ship can leave the air a mission begins over the world it is on; the citadel's, well away from its guns
            System.Build(system, save.stage > Campaign.Tower && save.stage < Campaign.Citadel ? Campaign.World(system, save.stage) : SolarSystemDescriptor.Home);
            Player.kit = save.kit;
            Hermit.Settle();
            Battle.Reset();
            Outpost.Settle();
        }

        // Puts the player where the mission in hand begins, whole: on the ground where they woke
        // or, once the ship can leave the air, in orbit.
        void Enter()
        {
            Load();
            if (save.stage > Campaign.Tower) Player.SpawnInOrbit();
            else Player.Spawn();
            Stow();
            // or the system would look for the world the player is near from wherever they last were
            System.Focus = Player.Position;
            Player.health = Player.MaxHealth;
            Player.hull = Player.MaxHull;
            Dug = 0;
            // the hole they wake in
            if (Buried) System.Bodies[System.Reference].Dig(Player.walker.position, 3f);
        }

        void Update()
        {
            var c = Player.controls();
            if (Dead)
            {
                if (c.confirm || (c.click && hud.MenuItemAt(c.pointer) >= 0)) Begin();
                c = default;
            }
            else if (Hermit.Talking)
            {
                Hermit.Converse(c);
                c = default;
            }
            else if (Paused)
            {
                int hover = hud.MenuItemAt(c.pointer);
                if (hover >= 0) Selected = hover;
                else if (c.up || c.down) Selected = (Selected + (c.down ? 1 : Items.Length - 1)) % Items.Length;
                if (c.menu && (Confirming || Page != null)) Back();
                // escape leaves the pause menu, but the only way off the title page is to choose
                else if (c.menu && !Title) Resume();
                else if (c.confirm || (c.click && hover >= 0)) Choose();
                c = default;
            }
            else
            {
                // escape closes whatever is open before it pauses
                if (c.menu && ShowControls) ShowControls = false;
                else if (c.menu) Pause();
                if (c.help) ShowControls = true;
                if (c.warp) Warp();
                if (c.scan) Scanner.Scan();
                if (c.travel)
                {
                    Notify(Player.Travel());
                    if (Player.Autopilot) Sounds.Engage();
                }
                if (c.mute)
                {
                    Mute();
                    Notify(Music.Muted ? "MUSIC OFF" : "MUSIC ON");
                }
                if (c.tierUp || c.tierDown) Detail(Mathf.Clamp(QualitySettings.GetQualityLevel() + (c.tierUp ? 1 : -1), 0, QualitySettings.names.Length - 1));
                var keys = Keyboard.current;
                if (keys != null && keys.altKey.isPressed && keys.enterKey.wasPressedThisFrame) Screen.fullScreen = !Screen.fullScreen;
                if (keys != null && (keys.minusKey.wasPressedThisFrame || keys.equalsKey.wasPressedThisFrame)) Look(DesktopControls.Sensitivity * (keys.minusKey.wasPressedThisFrame ? 0.8f : 1.25f));
                // the hermit is talked to at his fire, or from anywhere once he rides in the ship
                if (c.interact && Hermit.Near || c.talk && Hermit.Aboard)
                {
                    Hermit.Greet();
                    c = default;
                }
                else if (c.interact && Outpost.Near)
                {
                    Advance();
                    c = default;
                }
                if (Buried)
                {
                    // Under the ground there is nothing to do but dig. Each click tears another
                    // hollow out of the earth a little further round, until there is daylight.
                    if (c.fire && !pulled)
                    {
                        float3 up = math.normalize(Player.walker.position), reach = math.rotate(quaternion.AxisAngle(up, 2.4f * Dug), Player.walker.forward);
                        System.Bodies[System.Reference].Dig(Player.walker.position + reach * (1f + 0.5f * Dug), 2.6f);
                        Sounds.Dig();
                        if (++Dug == Digs) Advance();
                    }
                    (c.move, c.jump) = (Vector2.zero, false);
                }
                pulled = c.fire;
            }
            Cursor.lockState = Paused || Dead ? CursorLockMode.None : CursorLockMode.Locked;

            int bound = Player.Destination;
            string held = Player.Weapon.name;
            Player.beacon = Goal ?? float3.zero;
            Player.Tick(c);
            // behind the title page the view drifts slowly from side to side
            if (Title) Camera.main.transform.Rotate(0f, 20f * Mathf.Sin(0.15f * Time.unscaledTime), 0f);
            // taking up a different weapon, or going from the ship's to your own, names it
            if (Player.Weapon.name != held && Player.Armed) Notify(Player.Weapon.name.ToUpperInvariant());
            // what the ship cannot do yet is said when it is tried
            if (Player.Stalled == Time.time && Time.unscaledTime - NoticeAt > 3f) Notify("THE ENGINES GIVE OUT UP HERE  ·  SHE NEEDS AN ORBITAL DRIVE");
            if (Player.Irradiated && !cooking)
            {
                Notify("RADIATION  ·  TURN BACK  ·  NOTHING LIVES OUT HERE WITHOUT A SHIELD");
                Sounds.Alarm();
                Hermit.Remark("Radiation! Turn us round. Nothing lives out here without a shield.");
            }
            cooking = Player.Irradiated;
            bool holed = Player.flying && Player.hull < 0.3f * Player.MaxHull;
            if (holed && !failing && !Player.Dead) Hermit.Remark("She is coming apart. Set her down somewhere and let her mend.");
            failing = holed;
            // the bike's engine follows its speed, and the counter clicks faster the less hull there is left to lose
            Sounds.Bike(Player.riding ? 0.1f + math.length(Player.walker.velocity) : 0f);
            Sounds.Geiger(Player.Irradiated ? 1f - 0.7f * math.saturate(Player.hull / Player.MaxHull) : 0f);
            // the autopilot lets go by itself only when it has arrived
            if (bound >= 0 && !Player.Autopilot && !c.travel && c.move == Vector2.zero && c.lift == 0f)
            {
                Notify($"ARRIVED  ·  {System.Descriptor.bodies[bound].name.ToUpperInvariant()}");
                Sounds.Chime();
            }
            // a fight waits for a conversation, a menu or a look at the map
            if (!Paused && !Dead && !Hermit.Talking && !Player.SystemView)
            {
                Outpost.Tick();
                Battle.Tick(c);
            }
            if (Player.Dead && !Dead) Die();
        }

        public void Notify(string words)
        {
            Notice = words;
            NoticeAt = Time.unscaledTime;
        }

        // A blast at a point in the frame of the body the player is at: a crater in its ground.
        public void Dig(float3 point, float size)
        {
            System.Bodies[System.Reference].Dig(point, size);
            Player.Shaken(point, size);
        }

        // A landing charge gone off there: a pad of level ground big enough to set a ship down on.
        public void Level(float3 point)
        {
            System.Bodies[System.Reference].Level(point, Ship.Pad);
            Player.Shaken(point, Ship.Pad);
        }

        // A foe destroyed. One of an outpost's guards counts towards its mission; of the citadel,
        // the last of them all, nothing is left but wreckage.
        public void Award(FoeKind kind)
        {
            if (kind == FoeKind.Base)
                foreach (Transform part in System.Bodies[System.Station].transform)
                    part.gameObject.SetActive(false);
            Outpost.Killed(kind);
        }

        // A mission done, which is to say what it was for has been taken: that is the player's
        // from now on, they are made whole, and the next begins.
        public void Advance()
        {
            var done = Mission;
            string spoils = Campaign.Spoils(save.stage);
            bool crewed = Hermit.Aboard;
            foreach (var gear in done.wins)
            {
                save.kit |= 1 << (int)gear;
                Player.Wield(gear);
            }
            Player.kit = save.kit;
            save.stage++;
            Player.health = Player.MaxHealth;
            Player.hull = Player.MaxHull;
            Notify(save.stage == Campaign.Won ? "THE WARP DRIVE IS YOURS  ·  G IN OPEN SPACE"
                : spoils.Length == 0 ? "DAYLIGHT  ·  SOMEONE HAS LIT A FIRE NEAR BY"
                : $"{done.name.ToUpperInvariant()}  ·  YOU HAVE {spoils}{(Hermit.Aboard && !crewed ? $"  ·  {Hermit.Name.ToUpperInvariant()} FLIES WITH YOU NOW  ·  RETURN TO TALK" : "")}");
            Sounds.Chime();
            Outpost.Settle();
            Stow();
            // riding along, the hermit says at once what he makes of the next one
            if (Hermit.Aboard) Hermit.Remark(Hermit.Briefing);
            Write();
        }

        // Until it is won back the ship stands where the raiders keep it, to be seen and not flown.
        void Stow()
        {
            Player.captive = save.stage < Campaign.Won && Mission.wins.Contains(Gear.Ship);
            if (Player.captive) Player.Park(Outpost.Prize);
        }

        // Cheating: straight to a mission, with all that the ones before it win and nothing more.
        public void Jump(int stage)
        {
            save.stage = math.clamp(stage, 0, Campaign.Won);
            save.kit = Campaign.Kit(save.stage);
            Write();
            Begin();
        }

        void Pause()
        {
            Paused = true;
            Confirming = false;
            Page = null;
            Selected = 0;
            Time.timeScale = 0f;
        }

        void Resume()
        {
            Paused = false;
            Time.timeScale = 1f;
        }

        // The title page: which slot to play, over the world the last one played was left in.
        void ShowTitle()
        {
            Pause();
            Title = true;
            Selected = Slot;
            // what each slot holds: this one's as it stands, the others' as they were left
            slots = Enumerable.Range(0, Slots).Select(slot =>
            {
                var held = !File.Exists(SlotPath(slot)) ? null : slot == Slot ? save : Read(slot);
                return $"SLOT {slot + 1}   ·   {(held == null ? "NEW GAME" : held.stage >= Campaign.Won ? "THE SYSTEM IS FREE" : Campaign.Missions[held.stage].name.ToUpperInvariant())}"
                    + (held != null && held.depth > 1 ? $"   ·   DEPTH {held.depth}" : "");
            }).ToArray();
        }

        // Back a page of the menu, to the item that led away from it.
        void Back()
        {
            string from = Confirming ? "Quit to desktop" : Page;
            int voice = Voice;
            if (!Confirming) Page = voice >= 0 ? Settings : null;
            Confirming = false;
            Selected = voice >= 0 ? Dials + voice : Array.IndexOf(Items, from);
        }

        void Detail(int tier)
        {
            QualitySettings.SetQualityLevel(tier);
            PlayerPrefs.SetInt($"{Prefs}.detail", tier);
        }

        void Mute()
        {
            Music.Muted = !Music.Muted;
            PlayerPrefs.SetInt($"{Prefs}.muted", Music.Muted ? 1 : 0);
        }

        void Look(float speed)
        {
            save.look = DesktopControls.Sensitivity = speed;
            Write();
        }

        // Takes up another slot's game, from the start of the mission it had reached. Nothing of the
        // last one needs putting away: it is written whenever it changes.
        void Switch(int slot)
        {
            Slot = slot;
            save = Read(slot);
            DesktopControls.Sensitivity = save.look;
            // the slot played last is the one written last
            Write();
            Begin();
        }

        // Finds out how the player stands with each voice.
        async void Check()
        {
            Array.Fill(signedIn, "CHECKING");
            for (int voice = 0; voice < signedIn.Length; voice++) signedIn[voice] = await Oracle.Status(voice);
        }

        // Opens the player's browser to sign in to their own account with one of the voices, by
        // the first or second of its roads. That voice then writes from now on.
        async void SignIn(int voice, int road)
        {
            signedIn[voice] = "APPROVE IN YOUR BROWSER";
            signedIn[voice] = await Oracle.SignIn(voice, road);
            if (signedIn[voice].StartsWith("SIGNED IN")) Oracle.Voice = voice;
        }

        // Acts on the highlighted menu item.
        void Choose()
        {
            if (Page == Cheats && Selected < 4)
            {
                if (Selected < 2) Jump(save.stage + (Selected == 0 ? 1 : -1));
                else if (Selected == 2)
                {
                    // all that every mission before the citadel wins, wherever the game has got to
                    Player.kit = save.kit |= Campaign.Kit(Campaign.Citadel);
                    Write();
                }
                else (Player.health, Player.hull) = (Player.MaxHealth, Player.MaxHull);
                Resume();
                return;
            }
            if (Page == Settings && Selected < Dials)
            {
                if (Selected == 0) Detail((QualitySettings.GetQualityLevel() + 1) % QualitySettings.names.Length);
                else if (Selected == 1) Mute();
                // quicker each time, then back round to the slowest
                else if (Selected == 2) Look(DesktopControls.Sensitivity > 0.7f ? 0.08f : DesktopControls.Sensitivity * 1.25f);
                else if (Selected == 3) Screen.fullScreen = !Screen.fullScreen;
                else PlayerPrefs.SetInt($"{Prefs}.silent", (Speech.Muted = !Speech.Muted) ? 1 : 0);
                return;
            }
            if (Page == Settings && Selected < Dials + Oracle.Voices.Length)
            {
                Page = Oracle.Voices[Selected - Dials];
                Selected = 0;
                return;
            }
            if (Voice >= 0 && Selected < 7)
            {
                int voice = Voice;
                if (Selected == 0) Oracle.Voice = voice;
                else if (Selected == 1) Oracle.NextModel(voice);
                else if (Selected == 2) Oracle.NextEffort(voice);
                else if (Selected < 5) SignIn(voice, Selected - 3);
                else if (Selected == 6)
                {
                    Oracle.Forget(voice);
                    Check();
                }
                // a key that looks like one is taken, and that voice writes from now on
                else if (Oracle.UseKey(voice, Clipboard())) (signedIn[voice], Oracle.Voice) = ("YOUR OWN KEY", voice);
                else signedIn[voice] = "NO KEY ON THE CLIPBOARD";
                return;
            }
            if (Title && Page == null && !Confirming && Selected < Slots)
            {
                // a slot to play: the one already loaded carries on from where it stands
                Title = false;
                Began = Time.unscaledTime;
                if (Selected == Slot) Resume();
                else Switch(Selected);
                return;
            }
            switch (Items[Selected])
            {
                case Cheats:
                case Settings:
                    Page = Items[Selected];
                    Selected = 0;
                    // how the player stands with each voice is looked up as the page opens
                    if (Page == Settings) Check();
                    break;
                case "Back":
                case "Keep playing":
                    Back();
                    break;
                case "Quit":
                    Time.timeScale = 1f;
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.ExitPlaymode();
#else
                    Application.Quit();
#endif
                    break;
                case "Quit to desktop":
                    Confirming = true;
                    Selected = 0;
                    break;
                case "Controls":
                    ShowControls = true;
                    Resume();
                    break;
                case "New game":
                    Forfeit();
                    Begin();
                    break;
                case "Quit to title":
                    Write();
                    ShowTitle();
                    break;
                default:
                    Resume();
                    break;
            }
        }

        void Warp()
        {
            if (!Player.Has(Gear.Warp)) Notify("THERE IS NO WARP DRIVE ABOARD  ·  THE RAIDERS' CITADEL HAS ONE");
            else if (!InOpenSpace) Notify("THE WARP DRIVE NEEDS OPEN SPACE");
            else
            {
                // On to the next seed, a system deeper, arriving over its moon with everything won
                // so far. The drive burns out in the jump: the next citadel has another.
                save.seed++;
                save.depth++;
                save.stage = Campaign.Tower + 1;
                save.kit &= ~(1 << (int)Gear.Warp);
                save.talk.Clear();
                Enter();
                Sounds.Warp();
                Notify($"DEPTH {Depth}  ·  THE RAIDERS HERE ARE HARDER");
                Write();
            }
        }

        // Killed. Nothing is lost but the mission in hand, which begins again.
        void Die()
        {
            Dead = true;
            Selected = 0;
            Epitaph = Player.Cause;
            Tally = save.stage >= Campaign.Won ? "YOU KEEP ALL YOU HAVE WON" : $"{Mission.name.ToUpperInvariant()} BEGINS AGAIN   ·   YOU KEEP ALL YOU HAVE WON";
            Time.timeScale = 0f;
            Sounds.Boom(1f, true);
        }

        // Gives up the whole game for a new one, in a new system, with nothing.
        void Forfeit()
        {
            save.seed = math.hash(new uint2(save.seed, (uint)++save.runs)) % 1000000u + 1u;
            save.depth = 1;
            save.stage = save.kit = 0;
            save.talk.Clear();
            Write();
        }

        // Starts the mission the save now holds, from where it begins.
        void Begin()
        {
            Enter();
            Dead = false;
            Resume();
            Sounds.Warp();
        }

        public void Remember(Entry entry)
        {
            save.journal.Add(entry);
            Sounds.Chime();
            Write();
        }

        public void Write() => File.WriteAllText(SlotPath(Slot), JsonUtility.ToJson(save));
    }
}
