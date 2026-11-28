using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.UI;
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
        public string key, title, text, source;
    }

    // The one object in the scene: builds the solar system and everything in it, owns the save, and
    // decides each frame who the controls belong to: the player, the pause menu or a conversation.
    //
    // It also keeps the game, which is a string of missions (see Campaign). The player wakes
    // buried, with nothing, and wins everything they carry and fly from the raiders, a mission at
    // a time, until their citadel falls and moonfall opens the next system.
    // Dying costs only the mission in hand, which begins again. Each save slot holds a game and a
    // journal of its own, and the game opens on a title page where one is chosen.
    public class Game : MonoBehaviour
    {
        [Serializable]
        class Save
        {
            public int version;
            public uint seed = NewSeed?.Invoke() ?? (unchecked((uint)Guid.NewGuid().GetHashCode()) | 1u);
            public Progression progression = new Progression(), checkpoint = new Progression();
            public int depth = 1, runs, deaths;
            public EscortOrders escorts = new EscortOrders();
            // which mission is in hand, and everything won by the ones before it, as Campaign's bits
            public int stage, kit;
            public Difficulty difficulty = Difficulty.Normal;
            public int fitted = -1;
            public double clock;
            public bool gate;
            public Presence presence = new Presence();
            public Moonfall fall = new Moonfall();
            public Colony cargo = new Colony { world = -1, robots = 0 };
            public List<Colony> colonies = new List<Colony>();
            public List<Ruin> ruins = new List<Ruin>();
            public float look = DesktopControls.Sensitivity;
            public List<Entry> journal = new List<Entry>();
            public List<string> talk = new List<string>();
            public List<Entry> wording = new List<Entry>();
        }

        // How many slots there are, and how many clicks it takes to dig out of the ground.
        int dugHands;
        public const int Slots = 3, Digs = 2;

        public const string Settings = "Settings", Cheats = "Cheats", NewDifficulty = "Choose difficulty";
        // The rows of the settings page before those of the oracle's voices.
        public const string Writers = "Expedition language model";
        string writersParent;
        bool signingIn;
        int connectionCheck;
        int visibleLayers;
        int Dial => Application.isMobilePlatform ? Selected + 4 : Selected;
        int DialCount => Application.isMobilePlatform ? 2 : 7;
        public const string HandAlignment = "VR fist alignment", WristAlignment = "VR wrist display";
        const float DefaultPalm = 11;
        internal static readonly Vector4 DefaultWrist = new Vector4(25, 11, 0, 90);
        static readonly Vector3 IndexAlignment = new Vector3(90, -22, 80);
        public Vector3 FistAngles { get; private set; }
        public float PalmSetback { get; private set; } = DefaultPalm;
        public Vector4 WristFit { get; private set; } = DefaultWrist;
        public bool FittingHands => Page == HandAlignment || Page == WristAlignment;
        public Pose GlovePose(int side, Vector3 grip, Quaternion rotation)
        {
            float mirror = side == 0 ? 1 : -1;
            rotation *= Quaternion.Euler(FistAngles.x, mirror * FistAngles.y, mirror * FistAngles.z);
            return new Pose(grip - rotation * Vector3.forward * (PalmSetback * 0.01f), rotation);
        }
        public Difficulty Challenge => save.difficulty;
        public bool InvertShipPitch { get; private set; }
        public int MapPage { get; private set; }
        public bool MapSummary => MapPage == 1;
        public Vector2 MapPan { get; private set; }
        bool fromMap;
        int previousTab;
        public float MapZoom { get; private set; } = 1;
        public static readonly string[] Menu = { "Resume", "Controls", "Expedition map", Settings, Cheats, "Recentre VR", "New game", "Quit to title", "Quit to desktop" };
        public static readonly string[] Confirm = { "Keep playing", "Quit" }, Over = { "Try again" };

        public SolarSystem System { get; private set; }
        public Player Player { get; private set; }
        public Scanner Scanner { get; private set; }
        public Hermit Hermit { get; private set; }
        public Battle Battle { get; private set; }
        public Corpses Corpses { get; private set; }
        public Outpost Outpost { get; private set; }
        public Sounds Sounds { get; private set; }
        public Speech Speech { get; private set; }
        public Effects Effects { get; private set; }
        public Frontier Frontier { get; private set; }
        public Presence Presence => save.presence;
        public Moonfall Fall => save.fall;
        public List<Colony> Colonies => save.colonies;
        public List<Ruin> Ruins => save.ruins;
        public bool GatePowered => save.gate;
        public Colony Settlement => Colonies.FirstOrDefault(colony => colony.world == System.Reference && math.distance(Player.Position, colony.site) < 120);
        public bool NearFactory => Settlement != null && math.distance(Player.Position, Settlement.site) < 6;

        public uint Seed => save.seed;
        public int Slot { get; private set; }
        // How many systems deep the game has gone, which mission is in hand there (Campaign.Won once
        // the citadel has fallen) and how tough that makes the raiders.
        public Progression Progression => save.progression;
        Upgrade? upgrade;
        public bool Levelling => Paused && Page == "Level up";
        public bool FactoryUnlocked => Stage > Campaign.FirstBurrow || Stage == Campaign.FirstBurrow && Outpost.SiteCleared;
        public int Depth => save.depth;
        public float Pressure => War.Pressure(save.deaths);
        public EscortOrders Escorts => save.escorts;
        public int Stage => save.stage;
        public int Threat => 1 + 4 * (save.depth - 1) + save.stage / 4 + (int)(Presence.played / 1800);
        public Mission Mission => Campaign.Missions[math.min(save.stage, Campaign.Citadel)];
        // Still under the ground, and how many handfuls of it have been torn away.
        bool rebuildingUnderground;
        public bool Buried => save.stage == Campaign.Buried || rebuildingUnderground;
        public int Dug { get; private set; }
        // What the player is about, for the HUD.
        public Colony MoonYard => FindMoonYard(System.Descriptor);
        Colony FindMoonYard(SolarSystemDescriptor system) => Colonies.Where(site => system.bodies[site.world].orbit.parent > 0
            && Presence.fronts.Any(front => front.world == site.world && front.region == site.region && front.shipyard)).OrderByDescending(site => site.world == Fall.moon).FirstOrDefault();
        public float3 EnginePole => math.lengthsq(Fall.site) > 0 ? math.normalize(Fall.site) : math.normalizesafe(MoonYard?.site ?? new float3(0, 1, 0));
        int EngineMoon => !Fall.armed && System.ReferenceBody.kind == BodyKind.Rock && System.ReferenceBody.orbit.parent > 0 ? System.Reference : Fall.moon;
        public string Objective => Stage < Campaign.Won ? $"{Mission.name.ToUpperInvariant()}  ·  {MissionAction}" : MissionAction;
        public string MissionAction => save.stage >= Campaign.Won ? Fall.active ? Frontier.Ending : Fall.armed ? $"TAKE OFF · CLEAR {System.Descriptor.bodies[Fall.moon].name.ToUpperInvariant()} BY {Frontier.LaunchAltitude:0} M"
            : Frontier.CanBuild(2) ? DeviceControls.Key("B", "LT + D-PAD DOWN", "… / BUILD", "WRIST · BUILD") + " · BUILD MOON ENGINES HERE"
            : "LAND ANYWHERE ON ANY MOON · BUILD MOON ENGINES"
            : Stage == Campaign.Recovery && Player.NearShip ? Wording("boardShip")
            : Stage == Campaign.Recovery && Player.InSpace ? Wording("holdForShip")
            : Outpost.Fallen ? Outpost.Errand : Outpost.World == System.Reference ? Outpost.Instruction : Wording(Mission.task);
        // Position of the current mission in the player's reference frame, including a landing site on another world.
        public float3? Goal => Stage == Campaign.Won && !Fall.active && EngineMoon >= 0
            ? Fall.armed ? System.ToReference(EngineMoon) + math.rotate(System.Descriptor.Turn(EngineMoon, System.Reference, System.Clock), EnginePole * (System.Descriptor.bodies[EngineMoon].radius + Frontier.LaunchAltitude))
                : EngineMoon == System.Reference ? math.normalizesafe(Player.Position) * System.ReferenceBody.GroundRadius(math.normalizesafe(Player.Position)) : System.ToReference(EngineMoon)
            : Stage == Campaign.Recovery ? Player.ship.position : System.Reference == SolarSystemDescriptor.Home && save.stage == Campaign.Stranger ? Hermit.Position
            : Outpost.World < 0 || Outpost.ClearOfDemolition && !Outpost.SiteCleared ? (float3?)null : Outpost.World != System.Reference
                ? Stage >= Campaign.Tower && Stage < Campaign.Citadel ? (float3)System.Bodies[Outpost.World].transform.TransformPoint(Outpost.LandingSite) + System.Anchor : System.ToReference(Outpost.World)
                : Outpost.Rooms == null ? (float3?)null : Stage == Campaign.Citadel ? Outpost.Waypoint : Outpost.Recovered && !Outpost.EquipmentReady ? Outpost.SiteCleared ? Outpost.DeliverySite : Outpost.Exit : Outpost.Fallen ? Outpost.Prize : Outpost.Waypoint;
        // Everything scanned so far, and the talk with this system's hermit as alternating lines.
        public List<Entry> Journal => save.journal;
        public List<string> Talk => save.talk;

        // The menu: the world waits while it is open. Quitting asks first. The game opens on its
        // title page, where a save slot is chosen to play; after that it is the pause menu.
        public bool Paused { get; private set; }
        public bool Title { get; private set; }
        public bool Confirming { get; private set; }
        public string Confirmation { get; private set; } = "Quit to desktop?";
        string pending, cheatCode = "";
        int pendingSlot;
        public bool CheatsUnlocked { get; private set; }
        public bool GodMode { get; private set; }
        public bool Subtitles { get; private set; } = true;
        bool mapOpen, mapPressed;
        Vector2 lastPointer;
        public SuitTutorial Tutorial { get; private set; }
        public int MapSelection { get; private set; } = 1;
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
        public string[] Items => Levelling ? Enumerable.Range(0, Progression.ranks.Length)
            .Select(i => (upgrade == (Upgrade)i ? "•  " : "") + Progression.Preview((Upgrade)i, Player.Weapon.damage))
            .Append(upgrade.HasValue ? "Confirm upgrade" : "Select an attribute").ToArray()
            : Page == "Fleet orders" ? new[] { $"ORDERS   ·   {Escorts.order.ToString().ToUpperInvariant()}", "Back" }
            : Dead ? Over : Confirming ? new[] { "Cancel", "Confirm" }
            : Page == NewDifficulty ? Enum.GetNames(typeof(Difficulty)).Append("Back").ToArray()
            : Page == Cheats ? new[] { "Choose mission", "Take every weapon and all gear", "Mend suit and hull", "Next system", $"INVULNERABLE   ·   {(GodMode ? "ON" : "OFF")}", "Back" }
            : Page == "Missions" ? Campaign.Missions.Select(mission => mission.name).Append("Build moon engines").Append("Replay the moon finale").Select((name, i) => $"{i + 1}. {name}").Append("Back").ToArray()
            : Voice >= 0 ? (Oracle.Enabled ? new[] { "Disconnect language model" } :
                (Connected(Voice) ? new[] { "Use this account" } : Array.Empty<string>()).Concat(Oracle.Roads(Voice)).Append("Use the key on the clipboard"))
                .Append("Back").ToArray()
            : Page == Writers ? Enumerable.Range(0, Oracle.Voices.Length).Where(i => !Oracle.Enabled || Oracle.Voice == i)
                .Select(i => $"{Oracle.Voices[i]}   ·   {signedIn[i]}{(Oracle.Enabled && Oracle.Voice == i && Connected(i) ? "   ·   IN USE" : "")}").Concat(Oracle.Enabled ? new[] { "Sign out" } : Array.Empty<string>()).Append("Back").ToArray()
            : Page == HandAlignment ? new[] { $"PITCH −     {FistAngles.x - IndexAlignment.x:0}°", "PITCH +", $"YAW INWARD −     {FistAngles.y - IndexAlignment.y:0}°", "YAW INWARD +", $"ROLL −     {FistAngles.z - IndexAlignment.z:0}°", "ROLL +", $"PALM SETBACK −     {PalmSetback - DefaultPalm:0} cm", "PALM SETBACK +", "Reset alignment", "Back" }
            : Page == WristAlignment ? new[] { $"TOWARDS ELBOW −     {WristFit.x - DefaultWrist.x:0} cm", "TOWARDS ELBOW +", $"FACE HEIGHT −     {WristFit.y - DefaultWrist.y:0} cm", "FACE HEIGHT +",
                $"AROUND FOREARM −     {WristFit.z - DefaultWrist.z:0}°", "AROUND FOREARM +", $"SCREEN ROTATION −     {WristFit.w - DefaultWrist.w:0}°", "SCREEN ROTATION +", "Reset wrist display", "Back" }
            : Page == Settings ? new[]
            {
                $"DETAIL   ·   {QualitySettings.names[QualitySettings.GetQualityLevel()].ToUpperInvariant()}", $"MUSIC   ·   {(Music.Muted ? "OFF" : "ON")}",
                $"LOOK SPEED   ·   {DesktopControls.Sensitivity:0.00}", $"FULL SCREEN   ·   {(Screen.fullScreen ? "ON" : "OFF")}", $"ROBOT VOICE   ·   {(Speech.Muted ? "OFF" : "ON")}", $"ROBOT SUBTITLES   ·   {(Subtitles ? "ON" : "OFF")}", $"SHIP STICK PITCH   ·   {(InvertShipPitch ? "INVERTED" : "NORMAL")}",
            }.Skip(Application.isMobilePlatform ? 4 : 0).Take(DialCount).Append($"DIFFICULTY   ·   {Challenge.ToString().ToUpperInvariant()}").Concat(DeviceControls.VR ? new[] { HandAlignment, WristAlignment } : Array.Empty<string>()).Append(Writers).Append("Back").ToArray()
            : Title ? slots.Append(Writers).Concat(Enumerable.Range(0, Slots).Where(slot => File.Exists(SlotPath(slot))).Select(slot => $"Delete slot {slot + 1}")).Concat(CheatsUnlocked ? new[] { Cheats, Settings, "Quit to desktop" } : new[] { Settings, "Quit to desktop" }).Where(item => item != "Quit to desktop" || Application.platform != RuntimePlatform.IPhonePlayer).ToArray()
            : Menu.Where(item => (item != "Quit to desktop" || Application.platform != RuntimePlatform.IPhonePlayer) && (item != Cheats || CheatsUnlocked) && (item != "Recentre VR" || DeviceControls.VR)).ToArray();
        // On a voice's page: how the player stands with it and what it has been asked for so far,
        // and what the highlighted item would do.
        bool Connected(int i) => signedIn[i] == "YOUR OWN KEY" || signedIn[i]?.StartsWith("SIGNED IN") == true;
        public string Standing => Voice < 0 ? null : $"{signedIn[Voice]}{(Oracle.Enabled && Voice == Oracle.Voice && Connected(Voice) ? "   ·   IN USE" : "")}   ·   {Oracle.Used(Voice)}";
        public string Hint => Page == WristAlignment ? "Raise your left arm to preview. Adjust the distance along your arm, clearance above it, mounting side and screen angle."
            : Page == HandAlignment ? "Hold the controllers naturally. Setback slides both gloves towards your elbows. Yaw and roll mirror between hands."
            : Page == "Fleet orders" ? Frontier.FleetHint

            : Page == Cheats ? Selected == 1 ? "RECOVER ALL SUIT MODULES, SHIP SYSTEMS AND TORPEDOES"
                : "CHOOSE A MISSION OR PREPARE THE FACTORY AND CONQUESTS REQUIRED FOR MOON ENGINES"
            : Page == Writers || Page == null && Title && Selected == Slots ? "Choose one account to personalise the story, mission guidance and scanner journal for this expedition. Ask your robot follow-up questions by text or voice."
            : Page == NewDifficulty || Page == Settings && Selected == DialCount ? "Creative keeps you alive and drops foes in one hit. Realistic makes weapons lethal on both sides."
            : Page == null && Title ? "Optional language model · personalise your robot’s dialogue and mission guidance with your own account." : Page == Settings ? Dial switch
            {
                0 => "CHOOSE THE DETAIL PRESET FOR THIS DEVICE",
                1 => "TURN THE SOUNDTRACK ON OR OFF",
                2 => "ADJUST MOUSE LOOK SPEED",
                3 => "TOGGLE FULL SCREEN ON DESKTOP",
                4 => "TOGGLE THE ROBOT’S LOCAL VOICE · TURNING BOTH OUTPUTS OFF ENABLES SUBTITLES",
                5 => "SHOW ROBOT SUBTITLES · ESSENTIAL SUIT INSTRUCTIONS REMAIN VISIBLE",
                6 when !Application.isMobilePlatform => "REVERSE UP AND DOWN ON THE SHIP STEERING STICK",
                _ when Items[Selected] == WristAlignment => "Fit the screen to the back of your left forearm.",
                _ when Items[Selected] == HandAlignment => "Align the gauntlets with your controllers.",
                _ when Items[Selected] == Writers => "Personalise the expedition and talk to your robot through your own account.",
                _ => "",
            }
            : Voice >= 0 ? Items[math.min(Selected, Items.Length - 1)] switch
            {
                "Disconnect language model" => "Return to the authored story. The game forgets its saved credentials.",
                "Use the key on the clipboard" => "Requests use your provider’s API balance. This device stores the key.",
                "Sign in with Claude Code" => "Use Claude Code’s browser sign-in and your Claude plan.",
                "Sign in with Codex" => "Use Codex’s browser sign-in and your ChatGPT plan.",
                "Continue with ChatGPT" => "Open the provider’s sign-in page in your browser.",
                _ => $"{Oracle.Model(Voice).name} · lowest effort · requests use your own account",
            }
            // on the pause menu itself: what there is to do, and what it wins
            : Page == null && !Title && !Dead && save.stage < Campaign.Won ? MissionAction + (Mission.wins.Length > 0 ? "   ·   REWARD: " + Campaign.Spoils(Stage) : "")
            : "";
        // Detail and music are the device's to remember, not a character's. Tests keep theirs under another name.
        public static string Prefs = "infinigame";
        // Where a key is taken from. Tests supply their own.
        public static Func<string> Clipboard = () => GUIUtility.systemCopyBuffer;
        // A line of news for the HUD, and when it broke.
        public string Notice { get; private set; }
        public float NoticeAt { get; private set; } = float.MinValue;

        public bool CanWarp => false;

        // What the interact key would do right now.
        public string Prompt => !Outpost.Near && Hermit.Near ? $"Talk to {Hermit.Name}" : Outpost.Near ? Outpost.EquipmentReady ? $"Collect {Campaign.Spoils(save.stage).ToLowerInvariant()}" : "Arm burrow demolition" : Player.Prompt ?? ((Frontier.CanBuild(2) || NearFactory && Frontier.CanBuild(0)) ? Frontier.Construction : null);

        [NonSerialized] public SolarSystemDescriptor PreparedSystem;
        Save save;
        readonly ExpeditionText script = new ExpeditionText();
        public string Wording(string key) => script.Read(key);
        public bool Prepared(string key) => script.Prepared(key);
        public bool PreparingOpening => script.WaitingForOpening;
        public bool PreparingText => script.Busy;
        Hud hud;
        Transform burial;
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
        public static Func<uint> NewSeed;

        static Save Read(int slot)
        {
            var read = new Save { stage = Start, kit = Campaign.Kit(Start), difficulty = (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt($"{Prefs}.difficulty", (int)Difficulty.Normal), 0, (int)Difficulty.Realistic) };
            if (File.Exists(SlotPath(slot)))
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(SlotPath(slot)), read);
                if (read.version < 5)
                {
                    // Retired surface missions have no walls in the new citadel.
                    read.ruins.RemoveAll(ruin => Campaign.MigrateStage(ruin.stage, read.version) >= Campaign.Citadel);
                    foreach (var ruin in read.ruins) ruin.stage = Campaign.MigrateStage(ruin.stage, read.version);
                    read.ruins = read.ruins.GroupBy(ruin => ruin.stage).Select(group => group.First()).ToList();
                    read.stage = Campaign.MigrateStage(read.stage, read.version);
                    read.kit |= Campaign.Kit(read.stage);
                    read.wording.Clear();
                }
                if (read.version < 3)
                {
                    for (int region = 0; region < 3; region++)
                    {
                        var front = read.presence.fronts.FirstOrDefault(item => item.world == SolarSystemDescriptor.Home && item.region == region);
                        if (front == null)
                        {
                            front = War.Create(SolarSystemDescriptor.Home, read.depth, 0);
                            front.region = region; front.aliens /= 3; read.presence.fronts.Add(front);
                        }
                        front.mindDead |= read.stage > Campaign.FirstBurrow + region;
                    }
                }
            }
            if (read.version < 4)
            {
                read.progression.level = 1 + read.progression.ranks.Sum();
                read.checkpoint.level = 1 + read.checkpoint.ranks.Sum();
            }
            read.version = 5;
            return read;
        }

        public bool Ready { get; private set; }
        Canvas loadScreen;
        Image loadBackdrop;
        Text loadMessage;
        string loadStage;
        double nextLoadCaption, loadStarted;
        int loadPercent;
        internal static string Activity => new string('.', 1 + (int)(Time.unscaledTime * 2) % 3);
        T Add<T>() where T : MonoBehaviour { var component = gameObject.AddComponent<T>(); component.enabled = false; return component; }
        void Awake() => StartCoroutine(Initialise());
        IEnumerator Initialise()
        {
            if (Application.isMobilePlatform)
            {
                QualitySettings.SetQualityLevel(0);
                GameObject.Find("Global Volume")?.SetActive(false);
            }
            var camera = Camera.main;
            visibleLayers = camera.cullingMask;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var loading = new GameObject("Loading", typeof(Canvas), typeof(CanvasScaler));
            loading.transform.SetParent(transform, false);
            loadScreen = loading.GetComponent<Canvas>();
            loadScreen.renderMode = RenderMode.ScreenSpaceOverlay;
            loadScreen.sortingOrder = 100;
            var scaler = loading.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var panel = new GameObject("Background", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(loading.transform, false);
            loadBackdrop = panel.GetComponent<Image>(); loadBackdrop.color = new Color(0.015f, 0.025f, 0.04f);
            loadBackdrop.rectTransform.anchorMin = Vector2.zero; loadBackdrop.rectTransform.anchorMax = Vector2.one; loadBackdrop.rectTransform.offsetMin = loadBackdrop.rectTransform.offsetMax = Vector2.zero;
            var message = new GameObject("Progress", typeof(RectTransform), typeof(Text)).GetComponent<Text>(); message.transform.SetParent(loading.transform, false);
            message.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); message.fontSize = 32; message.alignment = TextAnchor.UpperCenter; message.color = new Color(0.5f, 0.9f, 1);
            message.rectTransform.sizeDelta = new Vector2(1080, 320);
            loadMessage = message;
            loadStarted = Time.unscaledTimeAsDouble;
            loadStage = "Restoring suit telemetry";
            RefreshLoadCaption();
            foreach (Transform part in loading.GetComponentsInChildren<Transform>()) part.gameObject.layer = LayerMask.NameToLayer("UI");
            yield return null; yield return null;
            Narrative.Load(Resources.Load<TextAsset>("STORY").text, Resources.Load<TextAsset>("MISSIONS").text,
                Resources.Load<TextAsset>("FACTIONS").text, Resources.Load<TextAsset>("COLONIES").text);
            Application.targetFrameRate = 60;
            Cursor.lockState = CursorLockMode.Locked;
            // the slot played last is the one written last
            for (int slot = 1; slot < Slots; slot++)
                if (File.GetLastWriteTimeUtc(SlotPath(slot)) > File.GetLastWriteTimeUtc(SlotPath(Slot)))
                    Slot = slot;
            save = Read(Slot);
            DesktopControls.Sensitivity = save.look;
            Oracle.Load();
            if (!Application.isMobilePlatform)
            {
                int detail = PlayerPrefs.GetInt($"{Prefs}.detail", QualitySettings.GetQualityLevel());
                if (QualitySettings.GetQualityLevel() != detail) QualitySettings.SetQualityLevel(detail);
            }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            yield return DeviceControls.StartWindowsDisplay();
#endif
            QualitySettings.vSyncCount = 0;
            FrameLog.Automatic = PlayerPrefs.GetInt($"{Prefs}.automaticQuality", 1) == 1;
            Music.Muted = PlayerPrefs.GetInt($"{Prefs}.muted") == 1;
            Speech.Muted = PlayerPrefs.GetInt($"{Prefs}.silent") == 1;
            InvertShipPitch = PlayerPrefs.GetInt($"{Prefs}.invertShipPitch") != 0;
            Subtitles = PlayerPrefs.GetInt($"{Prefs}.subtitles", 1) == 1;
            if (Speech.Muted && !Subtitles) { Subtitles = true; PlayerPrefs.SetInt($"{Prefs}.subtitles", 1); }
            FistAngles = new Vector3(PlayerPrefs.GetFloat($"{Prefs}.fistPitch", IndexAlignment.x), PlayerPrefs.GetFloat($"{Prefs}.fistYaw", IndexAlignment.y), PlayerPrefs.GetFloat($"{Prefs}.fistRoll", IndexAlignment.z));
            if (!PlayerPrefs.HasKey($"{Prefs}.wristFitVersion"))
            {
                // Old settings saved a whole page even if just one adjustment was made.
                foreach (var setting in new[] { ("palmSetback", 5f), ("wristAlong", 37f), ("wristHeight", 10f), ("wristRotation", 0f) })
                {
                    string key = $"{Prefs}.{setting.Item1}";
                    if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetFloat(key) == setting.Item2) PlayerPrefs.DeleteKey(key);
                }
                PlayerPrefs.SetInt($"{Prefs}.wristFitVersion", 1);
            }
            PalmSetback = PlayerPrefs.GetFloat($"{Prefs}.palmSetback", DefaultPalm);
            WristFit = new Vector4(PlayerPrefs.GetFloat($"{Prefs}.wristAlong", DefaultWrist.x), PlayerPrefs.GetFloat($"{Prefs}.wristHeight", DefaultWrist.y),
                PlayerPrefs.GetFloat($"{Prefs}.wristAround", DefaultWrist.z), PlayerPrefs.GetFloat($"{Prefs}.wristRotation", DefaultWrist.w));
            System = Add<SolarSystem>();
            Add<Rainfall>();
            Player = new GameObject("Player").AddComponent<Player>();
            loadStage = "Fitting your suit"; yield return null;
            Player.Init(System);
            camera.clearFlags = CameraClearFlags.SolidColor;
            Player.game = this;
            var devices = Add<DeviceControls>(); devices.game = this; Player.controls = devices.Poll;
            loadStage = "Loading equipment"; yield return null;
            Scanner = Add<Scanner>();
            Scanner.game = this;
            Hermit = Add<Hermit>();
            Hermit.game = this;
            Effects = Add<Effects>();
            Effects.game = this;
            Corpses = Add<Corpses>();
            Corpses.game = this;
            Battle = Add<Battle>();
            Battle.game = this;
            System.ReferenceChanged += Battle.Rebase;
            loadStage = "Preparing flight systems"; yield return null;
            Outpost = Add<Outpost>();
            Outpost.game = this;
            Sounds = Add<Sounds>();
            Sounds.game = this;
            Speech = Add<Speech>();
            Add<Music>().game = this;
            Add<Arms>().game = this;
            hud = Add<Hud>();
            hud.game = this;
            Add<SuitHud>().game = this;
            Add<VoiceInput>().game = this;
            Add<FrameLog>().game = this;
            Frontier = Add<Frontier>();
            Frontier.game = this;
            Tutorial = Add<SuitTutorial>(); Tutorial.game = this;
            yield return LoadExpedition(true);
        }

        IEnumerator LoadExpedition(bool title)
        {
            Ready = false;
            loadStage = "Charting the star system";
            if (!title)
            {
                loadStarted = Time.unscaledTimeAsDouble;
                loadPercent = 0;
                RefreshLoadCaption();
            }
            loadBackdrop.enabled = true;
            loadScreen.gameObject.SetActive(true);
            var camera = Camera.main;
            camera.cullingMask = DeviceControls.VR ? 1 << LayerMask.NameToLayer("UI") : 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            foreach (var component in GetComponents<MonoBehaviour>())
                if (component != this) component.enabled = false;
            uint seed = Seed;
            int depth = Depth;
            // Pure world generation and landing-site searches can run while the visor still redraws.
            var chart = Task.Run(() =>
            {
                var system = depth == 1 ? SolarSystemDescriptor.Generate(seed) : SolarSystemDescriptor.Beyond(seed);
                Campaign.Site(system, 0);
                return system;
            });
            yield return null;
            while (!chart.IsCompleted) yield return null;
            var descriptor = chart.GetAwaiter().GetResult();
            int built = 0;
            foreach (var stage in Load(descriptor))
            {
                loadStage = stage;
                loadPercent = 5 + 90 * ++built / (descriptor.bodies.Length + 3);
                yield return null;
            }
            Enter(worldBuilt: true);
            if (title) ShowTitle();
            else { Began = Time.unscaledTime; Dead = false; Resume(); Sounds.Warp(); }
            foreach (var component in GetComponents<MonoBehaviour>()) component.enabled = true;
            loadPercent = 99;
            loadStage = "Opening suit optics";
            yield return null;
            camera.cullingMask = DeviceControls.VR && Title ? 1 << LayerMask.NameToLayer("UI") : visibleLayers;
            camera.clearFlags = DeviceControls.VR && Title ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            loadBackdrop.enabled = false;
            if (DeviceControls.VR) loadMessage.material = Hud.Overlay;
            Ready = true;
        }

        // Builds the saved seed's system, terrain and gravity together, round the world the
        // mission in hand begins on, settles its hermit and the raiders' outpost and clears the air.
        IEnumerable<string> Load(SolarSystemDescriptor system, int? rebuildWorld = null)
        {
            if (save.fall.moon < 0)
            {
                save.fall.moon = Campaign.FinalMoon(system);
                save.fall.world = system.bodies[save.fall.moon].orbit.parent;
            }
            if (Stage == Campaign.Won && !Fall.armed && !Fall.active && FindMoonYard(system) is Colony yard) Fall.moon = yard.world;
            system.moonfall = save.fall;
            Campaign.Site(system, 0);
            System.RestoreClock(save.clock);
            // once the ship can leave the air a mission begins over the world it is on; the citadel's, well away from its guns
            int reference = rebuildWorld ?? (save.stage >= Campaign.Tower && save.stage < Campaign.Citadel ? Campaign.World(system, save.stage) : SolarSystemDescriptor.Home);
            if (!rebuildWorld.HasValue && Stage == Campaign.Won && !Fall.active) reference = Fall.moon;
            foreach (int i in System.Build(system, reference))
                yield return $"World {i + 1}/{system.bodies.Length}  ·  {system.bodies[i].name}";
            yield return "Placing the recovery robot";
            save.kit |= Campaign.Kit(save.stage);
            if (Presence.fronts == null) Presence.fronts = new List<Front>();
            Player.kit = save.kit;
            Player.cloaked = false;
            save.fitted = Suit.Default(save.kit);
            Player.fitted = save.fitted;
            Player.energy = Player.Powered ? Suit.Capacity : 3;
            Hermit.Settle();
            Battle.Reset();
            yield return "Growing burrows";
            Outpost.Settle();
            yield return "Assembling settlements";
            Frontier.Settle();
        }

        // Puts the player where the mission in hand begins, whole: on the ground where they woke
        // or, once the ship can leave the air, in orbit.
        void Enter(bool worldBuilt = false)
        {
            bool lostShip = Dead && Player.flying && Player.Has(Gear.Ship);
            Colony factory = Dead ? Colonies.Where(site => Presence.fronts.Any(f => f.world == site.world && f.region == site.region && f.factory))
                .OrderByDescending(site => lostShip && Presence.fronts.Any(f => f.world == site.world && f.region == site.region && f.shipyard))
                .ThenByDescending(site => site.world == System.Reference).ThenBy(site => math.distancesq(site.site, Player.Position)).FirstOrDefault() : null;
            rebuildingUnderground = Dead && factory == null;
            if (!worldBuilt)
            {
                var system = PreparedSystem ?? (Depth == 1 ? SolarSystemDescriptor.Generate(Seed) : SolarSystemDescriptor.Beyond(Seed));
                PreparedSystem = null;
                foreach (var _ in Load(system, factory?.world)) { }
            }
            if (save.stage >= Campaign.Tower && Player.Has(Gear.Ship) && !Dead) Player.SpawnInOrbit();
            else Player.Spawn();
            Stow();
            if (rebuildingUnderground && Outpost.World == System.Reference && Outpost.Rooms != null)
            {
                float3 entrance = Outpost.Place(Outpost.Rooms.entrance);
                float3 at = Outpost.Place(Outpost.Rooms.entrance - new float3(0, 0, 14));
                Player.walker = Walker.Spawn(System.ReferenceBody, at, entrance - at);
            }
            if (!Dead && Stage == Campaign.Won && !Fall.active)
            {
                if (Fall.armed)
                {
                    float3 pole = EnginePole, forward = Walker.Project(new float3(0.2f, 0.9f, 0.4f), pole);
                    Player.walker = Walker.Spawn(System.ReferenceBody, pole, forward);
                    Player.Park(Player.walker.position + Player.walker.forward * 8);
                    Player.flying = false;
                }
                else factory = MoonYard;
            }
            if (factory != null && factory.world == System.Reference)
            {
                float3 up = math.normalizesafe(factory.site);
                float3 forward = math.normalizesafe(Walker.Project(new float3(0.2f, 0.9f, 0.4f), up));
                bool yard = Frontier.State(factory.world, factory.region).shipyard;
                float3 workshop = yard ? (float3)Models.Kennel(factory.site) : factory.site;
                Player.walker = Walker.Spawn(System.ReferenceBody, math.normalizesafe(workshop), forward);
                if (Player.Has(Gear.Ship))
                {
                    Player.Park(yard ? factory.site : workshop + forward * 8);
                    if (yard) Player.ship.rotation = Quaternion.FromToRotation(Vector3.up, up);
                    Player.flying = lostShip && yard;
                }
            }
            // or the system would look for the world the player is near from wherever they last were
            System.Focus = Player.Position;
            Player.health = Player.MaxHealth;
            Player.hull = Player.MaxHull;
            dugHands = 0; Dug = 0; pulled = false; fromMap = mapOpen = false; MapPage = 0; MapPan = Vector2.zero; Tutorial?.Reset();
            if (burial != null) Destroy(burial.gameObject);
            // the hole they wake in
            if (Buried)
            {
                Player.Calibrate();
                System.Bodies[System.Reference].Dig(Player.walker.position, 8f);
                float3 up = math.normalize(Player.walker.position);
                Player.walker.position = up * System.ReferenceBody.GroundRadius(up);
                burial = new GameObject("Burial rubble").transform;
                burial.SetParent(System.Bodies[System.Reference].transform, false);
                burial.localPosition = Player.walker.position;
                burial.localRotation = Quaternion.LookRotation(Player.walker.forward, up);
                var stone = Shapes.Solid(Surface.Rock, new Color(0.15f, 0.13f, 0.11f), true);
                for (int i = 0; i < Digs; i++)
                    Shapes.Part(burial, "Packed rubble", Shapes.Rock(5), stone, new Vector3(i == 0 ? -0.38f : 0.38f, Walker.EyeHeight + 0.59f, 0.16f), new Vector3(1.45f, 0.66f, 1.8f));
                Shapes.Part(burial, "Sealed overburden", Shapes.Box(), stone, Vector3.up * 5, new Vector3(30, 2, 30));
            }
            if (Stage <= Campaign.Stranger)
                Hermit.Rescue(Buried ? BurialExit(Player.walker.forward) : Player.walker.position);
        }

        float3 BurialExit(float3 facing)
        {
            float3 up = math.normalize(Player.walker.position + facing * 9);
            return up * System.ReferenceBody.GroundRadius(up);
        }

        void RefreshLoadCaption()
        {
            int tick = (int)((Time.unscaledTimeAsDouble - loadStarted) * 2);
            string report = string.Join("\n", Enumerable.Range(math.max(0, tick - 3), math.min(4, tick + 1)).Select(Suit.Diagnostic));
            loadMessage.text = $"SUIT STARTUP  {loadPercent}%\n{loadStage.ToUpperInvariant()}\n\n{report}";
            nextLoadCaption = Time.unscaledTimeAsDouble + 0.5;
        }

        void LateUpdate()
        {
            if (!Ready) return;
            bool show = Buried && !Title && !Paused && !Dead && !ShowControls && !mapOpen && !Hermit.Talking && !GetComponent<DeviceControls>().ReadingWrist;
            loadScreen.gameObject.SetActive(show);
            if (show) loadMessage.text = $"SUIT STARTUP  100%\n{Tutorial.Hint}";
        }

        void Update()
        {
            if (!Ready)
            {
                if (loadMessage != null)
                {
                    if (DeviceControls.VR && loadScreen.renderMode != RenderMode.ScreenSpaceCamera)
                    {
                        loadScreen.worldCamera = Camera.main;
                        loadScreen.planeDistance = 1.5f;
                        loadScreen.renderMode = RenderMode.ScreenSpaceCamera;
                        Camera.main.cullingMask = 1 << LayerMask.NameToLayer("UI");
                    }
                    if (Time.unscaledTimeAsDouble >= nextLoadCaption) RefreshLoadCaption();
                }
                return;
            }
            if (DeviceControls.VR)
            {
                Camera.main.cullingMask = Title ? 1 << LayerMask.NameToLayer("UI") : visibleLayers;
                Camera.main.clearFlags = Title ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            }
            script.Prepare(this, save.wording ??= new List<Entry>());
            if (!Paused && !Dead && !Hermit.Talking && !ShowControls && !mapOpen && !Fall.active && Player.Powered && Progression.Pending)
            { Pause(); Page = "Level up"; }
            if (Paused) Selected = math.clamp(Selected, 0, Items.Length - 1);
            var c = Player.controls();
            if (Paused && !string.IsNullOrEmpty(c.typed))
            {
                cheatCode = (cheatCode + c.typed.ToLowerInvariant());
                if (cheatCode.Contains("idgaf")) ToggleCheats();
                if (cheatCode.Length > 12) cheatCode = cheatCode.Substring(cheatCode.Length - 12);
            }
            if (Buried && DeviceControls.VR && !c.interfaceActive) c.fire = GetComponent<Arms>().MotionPunch;
            GetComponent<VoiceInput>().Hold(c.voice);
            if (c.voice && Hermit.Talking) c = default;
            if (c.systemView && !mapPressed && !Paused && !Dead && !Hermit.Talking && !Fall.active) { mapOpen = !mapOpen; ShowControls = false; if (mapOpen) { if (MapPage == 3) MapPage = 0; MapSelection = System.Reference; MapPan = Vector2.zero; } }
            mapPressed = c.systemView;
            if (c.menu && mapOpen && !ShowControls) { mapOpen = false; c.menu = false; }
            bool map = mapOpen && !Paused && !Dead && !Hermit.Talking;
            c.systemView = map;
            Time.timeScale = Paused || Dead || Hermit.Talking || ShowControls || map ? 0 : 1;
            if (map)
            {
                int action = c.click ? hud.MapControlAt(c.pointer) : -1;
                int page = c.toggleView ? (MapPage + 1) % 4 : action == 7 ? 3 : action >= 0 && action <= 2 ? action : MapPage;
                if (MapPage == 3 && (c.menu || c.click && hud.CloseControlsAt(c.pointer))) { page = previousTab; c.menu = c.click = false; }
                if (page == 3 && MapPage != 3) previousTab = MapPage;
                MapPage = page;
                ShowControls = page == 3;

                if (MapPage == 0)
                {
                    float pinch = c.mapPinch > 0 ? c.mapPinch : 1;
                    MapZoom = Mathf.Clamp(MapZoom * Mathf.Pow(1.35f, -c.cycle) / pinch, 0.02f, 4);
                    MapPan = Vector2.ClampMagnitude(MapPan - c.mapDrag, 1.5f);
                }
                if (action == 3) { mapOpen = map = c.systemView = ShowControls = false; }
                if (action == 5) Frontier.BuildFactory(Frontier.CanBuild(2) ? 2 : 0);
                if (action == 4 || action == 9)
                {
                    Pause(); fromMap = action != 9; Page = action == 4 ? "Fleet orders" : null;
                    Selected = 0; ShowControls = false;
                    mapOpen = map = c.systemView = false; c = default;
                }
                if (action == 6) c.travel = true;
                if (action == 8) Frontier.CallDogs();
                c.toggleView = false; c.cycle = 0;
                if (MapPage <= 1 && (c.up || c.down || !System.Charted(MapSelection)))
                    for (int tries = 0; tries < System.Bodies.Length; tries++)
                    {
                        MapSelection = (MapSelection + (c.up ? System.Bodies.Length - 1 : 1)) % System.Bodies.Length;
                        if (MapSelection > 0 && System.Charted(MapSelection) && (System.Descriptor.bodies[MapSelection].kind != BodyKind.Station || Stage >= Campaign.Citadel)) break;
                    }
                if (c.click && hud.MapItemAt(c.pointer) is int selectedWorld && selectedWorld >= 0) MapSelection = selectedWorld;
                if (MapPage <= 1 && (c.confirm || c.travel)) { Notify(Player.TravelTo(MapSelection)); c.travel = false; }
                c.talk = c.interact = c.confirm = false;
            }
            if (Dead)
            {
                if (c.confirm || (c.click && hud.MenuItemAt(c.pointer) >= 0)) Begin();
                c = default;
            }
            else if (Hermit.Talking)
            {
                if (c.click)
                {
                    int question = hud.QuestionAt(c.pointer);
                    if (question >= 0) { c.typed = (question + 1).ToString(); c.confirm = false; }
                    else if (hud.CloseConversationAt(c.pointer)) c.menu = true;
                }
                Hermit.Converse(c);
                c = default;
            }
            else if (Paused)
            {
                if (!Levelling && c.click && hud.CheatAt(c.pointer)) { ToggleCheats(); if (CheatsUnlocked) Page = Cheats; c.click = false; }
                int hover = hud.MenuItemAt(c.pointer);
                if (hover >= 0 && (c.click || !DeviceControls.VR && c.pointer != lastPointer) && !c.up && !c.down) Selected = hover;
                lastPointer = c.pointer;
                if (c.up || c.down) Selected = (Selected + (c.down ? 1 : Items.Length - 1)) % Items.Length;
                if (Levelling) c.menu = false;
                if (c.menu && (Confirming || Page != null)) Back();
                // escape leaves the pause menu, but the only way off the title page is to choose
                else if (c.menu && !Title) Resume();
                else if (c.confirm || (c.click && hover >= 0)) Choose();
                c = default;
            }
            else
            {
                // escape closes whatever is open before it pauses
                if (ShowControls && (c.menu || c.confirm || c.click && hud.CloseControlsAt(c.pointer)))
                { ShowControls = false; c = new Controls { systemView = map }; }
                else if (c.menu) Pause();
                if (c.help) ShowControls = !ShowControls;
                if (ShowControls) c = new Controls { systemView = map };
                else if (!map) Tutorial.Filter(ref c);
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
                if (keys != null && keys.altKey.isPressed && keys.enterKey.wasPressedThisFrame) ToggleFullScreen();
                // the hermit is talked to at his fire, or from anywhere once he rides in the ship
                if (!Buried && (c.interact && Hermit.Near && !Outpost.Near || c.talk && Player.Has(Gear.Suit)))
                {
                    Hermit.Greet();
                    c = default;
                }
                else if ((c.ride || c.interact && Player.Prompt == null && !Outpost.Near && (Frontier.CanBuild(2) || NearFactory && Frontier.CanBuild(0))) && !Player.flying && FactoryUnlocked)
                {
                    Frontier.BuildFactory(Frontier.CanBuild(2) ? 2 : 0); c = default;
                }
                else if (c.interact && Outpost.Near)
                {
                    if (Outpost.SiteCleared) Advance(); else Outpost.ArmDemolition();
                    c = default;
                }
                if (Buried)
                {
                    // Each punch removes a piece of overhead rubble. The floor stays intact.
                    int hits = DeviceControls.VR ? GetComponent<Arms>().PunchHands & ~dugHands : !pulled ? 1 << Dug : 0;
                    if (c.fire && hits != 0)
                    {
                        Sounds.Dig();
                        for (int hand = 0; hand < Digs; hand++)
                            if ((hits & (1 << hand)) != 0)
                            {
                                Player.PunchRubble(hand);
                                dugHands |= 1 << hand;
                                if (burial != null) burial.GetChild(hand).gameObject.SetActive(false);
                                Dug++;
                            }
                        if (Dug == Digs)
                        {
                            if (burial != null) Destroy(burial.gameObject);
                            Player.ClimbOut(BurialExit(Stage <= Campaign.Stranger ? Player.walker.forward : Hermit.RescueFacing));
                            rebuildingUnderground = false;
                            if (Stage == Campaign.Buried) Advance();
                        }
                    }
                    c.interfaceActive = true;
                    (c.move, c.jump) = (Vector2.zero, false);
                    c.look = Vector2.zero;
                }
                pulled = c.fire;
            }
            if (!Ready) return;
            Time.timeScale = Paused || Dead || Hermit.Talking || ShowControls || map ? 0 : 1;
            Cursor.lockState = Paused || Dead || map || ShowControls || Hermit.Talking || DeviceControls.Pointer ? CursorLockMode.None : CursorLockMode.Locked;

            if (Fall.active && !Paused) c = default;
            int bound = Player.Destination;
            string held = Player.Weapon.name;
            bool landed = Player.ship.landed, hot = Player.Heat > Player.HeatLimit * 0.9f;
            Player.beacon = Goal ?? float3.zero;
            bool followingMoon = Fall.active;
            if (followingMoon && !Paused && !Dead) Frontier.Tick();
            Player.Tick(c);
            GetComponent<Arms>().TrackGloves();
            if (Player.flying && !landed && Player.ship.landed) Hermit.Remark(Wording("landed").Replace("{world}", System.ReferenceBody.name));
            if (!hot && Player.Heat > Player.HeatLimit * 0.9f && !Player.Dead) Hermit.Remark(Wording("heat"));
            // behind the title page the view drifts slowly from side to side
            if (Title && !DeviceControls.VR) Camera.main.transform.Rotate(0f, 20f * Mathf.Sin(0.15f * Time.unscaledTime), 0f);
            // taking up a different weapon, or going from the ship's to your own, names it
            if (Player.Weapon.name != held && Player.Armed) Notify(Player.Weapon.name.ToUpperInvariant());
            // what the ship cannot do yet is said when it is tried
            if (Player.Stalled == Time.time && Time.unscaledTime - NoticeAt > 3f) Notify("THE ENGINES GIVE OUT UP HERE  ·  SHE NEEDS AN ORBITAL DRIVE");
            if (Player.Irradiated && !cooking)
            {
                Notify("RADIATION  ·  TURN BACK  ·  NOTHING LIVES OUT HERE WITHOUT A SHIELD");
                Sounds.Alarm();
                Hermit.Remark(Wording("radiation"));
            }
            cooking = Player.Irradiated;
            bool holed = Player.flying && Player.hull < 0.3f * Player.MaxHull;
            if (holed && !failing && !Player.Dead) Hermit.Remark(Wording("hull"));
            failing = holed;
            // the bike's engine follows its speed, and the counter clicks faster the less hull there is left to lose
            Sounds.Bike(Player.riding ? 0.1f + math.length(Player.walker.velocity) : 0f);
            Sounds.Geiger(Player.Irradiated ? 1f - 0.7f * math.saturate(Player.hull / Player.MaxHull) : 0f);
            // the autopilot lets go by itself only when it has arrived
            if (bound >= 0 && !Player.Autopilot && !c.travel && c.move == Vector2.zero && c.lift == 0f)
            {
                Notify($"ARRIVED  ·  {System.Descriptor.bodies[bound].name.ToUpperInvariant()}");
                Sounds.Chime();
                Hermit.Remark(Wording("arrival").Replace("{world}", System.Descriptor.bodies[bound].name));
            }
            // a fight waits for a conversation, a menu or a look at the map
            if (!Paused && !Dead && !Hermit.Talking && !Player.SystemView && !ShowControls)
            {
                if (!Fall.active && !Buried) { Outpost.Tick(); Battle.Tick(c); }
                if (!followingMoon) Frontier.Tick();
                save.clock = System.Clock;
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
            if (Buried || Tutorial.Locked) return;
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
        public void Award(FoeKind kind, bool credited = true)
        {
            if (kind == FoeKind.Base)
                foreach (Transform part in System.Bodies[System.Station].transform)
                    part.gameObject.SetActive(false);
            Outpost.Killed(kind);
            if (credited && Stage >= Campaign.FirstBurrow)
            {
                GainExperience(kind == FoeKind.Mind ? 35 : kind == FoeKind.Raider ? 20 : 10);
            }
        }

        // A mission done, which is to say what it was for has been taken: that is the player's
        // from now on, they are made whole, and the next begins.
        void GainExperience(int amount)
        {
            Progression.experience += amount;
            if (Stage <= Campaign.FirstBurrow && Progression.level == 1)
                Progression.experience = math.min(Progression.experience, Progression.Required);
        }

        public void FitRewards()
        {
            foreach (var gear in Mission.wins)
            {
                save.kit |= 1 << (int)gear;
                int fitted = Player.fitted | 1 << (int)gear;
                if (Suit.Modules.Contains(gear) && Suit.Fits(save.kit, fitted)) Player.fitted = fitted;
            }
            Player.kit = save.kit;
            Player.energy = Player.Powered ? Suit.Capacity : 3;
            save.fitted = Player.fitted;
            if (Stage == Campaign.FirstBurrow && Progression.level == 1)
                Progression.experience = Progression.Required;
            save.checkpoint = Progression.Copy();
        }

        public void Advance()
        {
            if (save.stage >= Campaign.Won) return;
            var done = Mission;
            string spoils = Campaign.Spoils(save.stage);
            bool crewed = Hermit.Aboard;
            FitRewards();
            if (Outpost.World >= 0) Presence.Liberate(Outpost.World);
            save.stage++;
            if (done.count > 0 && save.stage - 1 != Campaign.FirstBurrow) GainExperience(20);
            save.checkpoint = Progression.Copy();
            if (Stage == Campaign.Won && MoonYard is Colony yard) Fall.moon = yard.world;
            Frontier.Settle();
            Player.health = Player.MaxHealth;
            Player.hull = Player.MaxHull;
            Notify(save.stage == Campaign.Won ? "SHIELD CORE DESTROYED  ·  LAND ANYWHERE ON ANY MOON AND BUILD ENGINES"
                : spoils.Length == 0 ? "DAYLIGHT  ·  A ROBOT IS CALLING NEAR BY"
                : $"{done.name.ToUpperInvariant()}  ·  YOU HAVE {spoils}{(Hermit.Aboard && !crewed ? $"  ·  {Hermit.Name.ToUpperInvariant()} FLIES WITH YOU NOW  ·  {DeviceControls.Key("RETURN", "LT + LEFT", "ROBOT", "WRIST ROBOT")} TO TALK" : "")}");
            Sounds.Chime();
            Outpost.Settle();
            Stow();
            // riding along, the hermit says at once what he makes of the next one
            if (Stage != Campaign.Stranger && Outpost.Demolition <= 0) Hermit.Remark(Hermit.Briefing);
            if (done.wins.Contains(Gear.Capacitor)) Hermit.Remark(Wording("poweredPunch"));
            if (done.wins.Contains(Gear.Stealth)) Hermit.Remark(Wording("cloak"));
            Write();
        }

        // The unmanned ship holds above the original impact site until the suit can reach orbit.
        void Stow()
        {
            Player.captive = save.stage < Campaign.Won && Mission.wins.Contains(Gear.Ship);
            if (Player.ShipOverhead)
            {
                float3 up = Campaign.Site(System.Descriptor, 0);
                float3 orbit = up * (System.ReferenceBody.radius * Shapes.AirTop(System.ReferenceBody) + Combat.ShipRadius + 1);
                Player.Park(orbit);
                Player.ship.position = orbit;
                Player.ship.velocity = float3.zero;
                Player.ship.landed = false;
            }
        }

        // Restore the settlements that delivered the equipment for the selected mission.
        public void Jump(int stage)
        {
            Title = false;
            save.stage = math.clamp(stage, 0, Campaign.Won);
            save.fall = new Moonfall(); save.gate = false;
            var system = System.Descriptor;
            if (Ruins.RemoveAll(ruin => ruin.stage == Stage && (ruin.destroyed || ruin.detonates > 0)) > 0)
            {
                int world = Campaign.World(system, Stage);
                Frontier.State(world, world == SolarSystemDescriptor.Home ? Stage - Campaign.FirstBurrow : 0).mindDead = false;
            }
            for (int completed = Campaign.FirstBurrow; completed < Stage; completed++)
            {
                if (completed == Campaign.Recovery) continue;
                int world = Campaign.World(system, completed);
                int region = world == SolarSystemDescriptor.Home ? completed - Campaign.FirstBurrow : 0;
                var front = Frontier.State(world, region);
                front.mindDead = true;
                if (!Presence.worlds.Contains(world)) Presence.Liberate(world);
                if (completed == Campaign.Citadel) continue;
                var ruin = Ruins.Find(item => item.stage == completed);
                if (ruin == null)
                {
                    ruin = new Ruin { stage = completed };
                    Ruins.Add(ruin);
                }
                ruin.open = ruin.destroyed = true;
                if (ruin.detonates <= 0) ruin.detonates = math.max(1, System.Clock);
                front.established = true;
                War.Build(front, false);
                if (completed >= Campaign.ThirdBurrow || Stage == Campaign.Won) War.Build(front, true);
                if (Stage == Campaign.Won && world == SolarSystemDescriptor.Home) front.aliens = front.alienShips = 0;
                if (Colonies.Exists(colony => colony.world == world && colony.region == region)) continue;
                var body = system.bodies[world];
                float3 up = Campaign.Site(system, completed);
                float3 beside = math.normalize(up * body.GroundRadius(up) + 24 * math.normalizesafe(math.cross(up, new float3(0.2f, 0.9f, 0.4f))));
                if (body.GroundRadius(beside) > body.SeaRadius + 1) up = beside;
                Colonies.Add(new Colony { world = world, region = region, site = up * body.GroundRadius(up) });
            }
            if (Stage == Campaign.Won && MoonYard is Colony yard)
            {
                Fall.world = system.bodies[Campaign.FinalMoon(system)].orbit.parent;
                Fall.moon = yard.world;
                var front = Frontier.State(yard.world, yard.region);
                front.aliens = front.alienShips = 0;
            }
            save.kit = Campaign.Kit(save.stage);
            save.fitted = -1;
            Write();
            Begin();
        }

        static void ToggleFullScreen()
        {
            bool fullscreen = !Screen.fullScreen;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!DeviceControls.VR)
            {
                if (fullscreen) { DeviceControls.SetNativeMonitor(); return; }
            }
#endif
            Screen.fullScreen = fullscreen;
        }

        void ToggleCheats()
        {
            CheatsUnlocked = !CheatsUnlocked;
            cheatCode = ""; Selected = 0;
            if (!CheatsUnlocked) { GodMode = false; if (Page == Cheats || Page == "Missions") Page = null; }
            Notify(CheatsUnlocked ? "CHEATS UNLOCKED" : "CHEATS LOCKED");
        }

        void Pause()
        {
            Paused = true;
            upgrade = null;
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
            if (fromMap && !Confirming)
            {
                fromMap = false; Page = null; mapOpen = true; Resume(); return;
            }
            string from = Confirming ? "Quit to desktop" : Page;
            int voice = Voice;
            if (!Confirming) Page = voice >= 0 ? Writers : from == Writers ? writersParent : (from == HandAlignment || from == WristAlignment) ? Settings : from == "Missions" ? Cheats : null;
            Confirming = false;
            Selected = voice >= 0 ? Oracle.Enabled ? 0 : voice : math.max(0, Array.IndexOf(Items, from));
        }

        void Detail(int tier)
        {
            QualitySettings.SetQualityLevel(tier);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
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
        void Switch(int slot, Difficulty? difficulty = null)
        {
            Slot = slot;
            save = Read(slot);
            if (difficulty.HasValue) save.difficulty = difficulty.Value;
            DesktopControls.Sensitivity = save.look;
            // the slot played last is the one written last
            Write();
            StartCoroutine(LoadExpedition(false));
        }

        // Finds out how the player stands with each voice.
        async void Check()
        {
            int checking = ++connectionCheck;
            Array.Fill(signedIn, "CHECKING");
            for (int voice = 0; voice < signedIn.Length; voice++)
            {
                string status = await Oracle.Status(voice);
                if (checking != connectionCheck || this == null) return;
                signedIn[voice] = status;
            }
        }

        // Opens the player's browser to sign in to their own account with one of the voices, by
        // the first or second of its roads. That voice then writes from now on.
        async void SignIn(int voice, string road)
        {
            connectionCheck++;
            signingIn = true;
            signedIn[voice] = "APPROVE IN YOUR BROWSER";
            signedIn[voice] = await Oracle.SignIn(voice, road == "Continue with ChatGPT" ? 1 : 0);
            signingIn = false;
            if (Connected(voice)) Oracle.Voice = voice;
            Selected = 0;
        }

        // Acts on the highlighted menu item.
        void Choose()
        {
            if (Page == HandAlignment && Selected < 9)
            {
                var angles = FistAngles;
                if (Selected == 8) { angles = IndexAlignment; PalmSetback = DefaultPalm; }
                else if (Selected >= 6) PalmSetback = Mathf.Clamp(PalmSetback + (Selected == 6 ? -1 : 1), -10, 20);
                else angles[Selected / 2] = Mathf.DeltaAngle(0, angles[Selected / 2] + (Selected % 2 == 0 ? -5 : 5));
                FistAngles = angles;
                PlayerPrefs.SetFloat($"{Prefs}.palmSetback", PalmSetback);
                PlayerPrefs.SetFloat($"{Prefs}.fistPitch", angles.x);
                PlayerPrefs.SetFloat($"{Prefs}.fistYaw", angles.y);
                PlayerPrefs.SetFloat($"{Prefs}.fistRoll", angles.z);
                return;
            }
            if (Page == WristAlignment && Selected < 9)
            {
                var fit = WristFit;
                int axis = Selected / 2, step = Selected % 2 == 0 ? -1 : 1;
                if (Selected == 8) fit = DefaultWrist;
                else if (axis == 0) fit.x = Mathf.Clamp(fit.x + step, 18, 50);
                else if (axis == 1) fit.y = Mathf.Clamp(fit.y + step, 6, 20);
                else fit[axis] = Mathf.DeltaAngle(0, fit[axis] + step * 5);
                WristFit = fit;
                PlayerPrefs.SetFloat($"{Prefs}.wristAlong", fit.x);
                PlayerPrefs.SetFloat($"{Prefs}.wristHeight", fit.y);
                PlayerPrefs.SetFloat($"{Prefs}.wristAround", fit.z);
                PlayerPrefs.SetFloat($"{Prefs}.wristRotation", fit.w);
                return;
            }
            if (Levelling)
            {
                if (Selected < Progression.ranks.Length) { upgrade = (Upgrade)Selected; return; }
                if (!upgrade.HasValue || !Progression.Improve(upgrade.Value)) return;
                Player.health = Player.MaxHealth;
                Player.hull = Player.MaxHull;
                Player.energy = Player.MaxEnergy;
                Battle.Rearm();
                upgrade = null;
                Selected = 0;
                if (Outpost.SiteCleared) save.checkpoint = Progression.Copy();
                Write();
                if (!Progression.Pending) Resume();
                return;
            }
            if (Page == "Fleet orders" && Selected == 0)
            {
                Escorts.order = (EscortOrder)(((int)Escorts.order + 1) % 3);
                Write(); return;
            }
            if (Page == NewDifficulty && Selected < (int)Difficulty.Realistic + 1)
            {
                var difficulty = (Difficulty)Selected;
                Page = null; Title = false; Began = Time.unscaledTime;
                if (pending == "new")
                {
                    save.difficulty = difficulty; Forfeit(); StartCoroutine(LoadExpedition(false));
                }
                else Switch(pendingSlot, difficulty);
                pending = null; return;
            }
            if (Page == "Missions" && Selected == Campaign.Won + 1) { PreviewFinale(); return; }
            if (Page == "Missions" && Selected <= Campaign.Won) { Jump(Selected); return; }
            if (Confirming)
            {
                if (Selected == 0) { Back(); return; }
                Confirming = false;
                if (pending == "delete")
                {
                    File.Delete(SlotPath(pendingSlot));
                    if (pendingSlot == Slot) { save = new Save { version = 5, kit = Campaign.Kit(0) }; Enter(); }
                    ShowTitle(); return;
                }
                if (pending == "new") { Page = NewDifficulty; Selected = (int)Challenge; return; }
                if (pending == "slot") { Title = false; Switch(pendingSlot); return; }
                Time.timeScale = 1;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.ExitPlaymode();
#else
                Application.Quit();
#endif
                return;
            }
            if (Items[Selected].StartsWith("Delete slot "))
            {
                pendingSlot = int.Parse(Items[Selected].Substring(12)) - 1;
                pending = "delete"; Confirmation = $"Delete slot {pendingSlot + 1}? This cannot be undone."; Confirming = true; Selected = 0; return;
            }
            if (Page == Cheats && Selected == 4) { GodMode = !GodMode; return; }
            if (Page == Cheats && Selected < 4)
            {
                if (Selected == 0) { Page = "Missions"; Selected = math.min(Stage, Campaign.Citadel); return; }
                else if (Selected == 1)
                {
                    Player.kit = save.kit |= Campaign.Kit(Campaign.Citadel);
                    Player.fitted = save.fitted = Suit.Default(save.kit);
                    Write();
                }
                else if (Selected == 3) { NextSystem(); return; }
                else (Player.health, Player.hull) = (Player.MaxHealth, Player.MaxHull);
                Title = false; Resume(); return;
            }
            if (Page == Settings && Selected == DialCount)
            {
                save.difficulty = (Difficulty)(((int)Challenge + 1) % ((int)Difficulty.Realistic + 1));
                if (!Title || File.Exists(SlotPath(Slot))) Write();
                return;
            }
            if (Page == Settings && Selected < DialCount)
            {
                switch (Dial)
                {
                    case 0: Detail((QualitySettings.GetQualityLevel() + 1) % QualitySettings.names.Length); break;
                    case 1: Mute(); break;
                    case 2: Look(DesktopControls.Sensitivity > 0.7f ? 0.08f : DesktopControls.Sensitivity * 1.25f); break;
                    case 3: ToggleFullScreen(); break;
                    case 4:
                    case 5:
                        if (Dial == 4) { Speech.Muted = !Speech.Muted; if (Speech.Muted) Subtitles = true; }
                        else { Subtitles = !Subtitles; if (!Subtitles) Speech.Muted = false; }
                        PlayerPrefs.SetInt($"{Prefs}.silent", Speech.Muted ? 1 : 0);
                        PlayerPrefs.SetInt($"{Prefs}.subtitles", Subtitles ? 1 : 0);
                        break;
                    case 6: InvertShipPitch = !InvertShipPitch; PlayerPrefs.SetInt($"{Prefs}.invertShipPitch", InvertShipPitch ? 1 : 0); break;
                }
                return;
            }
            if (Page == Writers && Items[Selected] != "Back")
            {
                if (Items[Selected] == "Sign out") { Oracle.Forget(Oracle.Voice); Selected = 0; Check(); return; }
                Page = Oracle.Enabled ? Oracle.Voices[Oracle.Voice] : Oracle.Voices[Selected];
                Selected = 0; return;
            }
            if (Voice >= 0 && Items[Selected] != "Back")
            {
                if (signingIn) return;
                int voice = Voice;
                switch (Items[Selected])
                {
                    case "Disconnect language model":
                        Oracle.Forget(voice); Check(); break;
                    case "Use this account":
                        Oracle.Voice = voice; break;
                    case "Use the key on the clipboard":
                        connectionCheck++;
                        signedIn[voice] = Oracle.UseKey(voice, Clipboard()) ? "YOUR OWN KEY" : "NO KEY ON THE CLIPBOARD"; break;
                    default: SignIn(voice, Items[Selected]); break;
                }
                Selected = 0;
                return;
            }
            if (Title && Page == null && !Confirming && Selected < Slots)
            {
                if (!File.Exists(SlotPath(Selected)))
                {
                    pending = "slot"; pendingSlot = Selected; Page = NewDifficulty; Selected = (int)Difficulty.Normal; return;
                }
                // A saved slot resumes without confirmation.
                Title = false;
                Began = Time.unscaledTime;
                if (Selected == Slot) Resume();
                else Switch(Selected);
                return;
            }
            switch (Items[Selected])
            {
                case "Expedition map":
                    MapPage = 0; MapSelection = System.Reference; MapPan = Vector2.zero; mapOpen = true; Resume(); break;
                case Writers:
                    writersParent = Page;
                    Page = Writers; Selected = 0; Check(); break;
                case Cheats:
                case Settings:
                case HandAlignment:
                case WristAlignment:
                    Page = Items[Selected];
                    Selected = 0;
                    break;
                case "Back":
                case "Keep playing":
                case "Cancel":
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
                    pending = "quit"; Confirmation = "Quit to desktop?";
                    Confirming = true;
                    Selected = 0;
                    break;
                case "Recentre VR":
                    Player.Calibrate(); Resume(); break;
                case "Controls":
                    ShowControls = true;
                    Resume();
                    break;
                case "New game":
                    pending = "new"; Confirmation = $"Replace slot {Slot + 1} with a new game?"; Confirming = true; Selected = 0;
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
            if (CanWarp) NextSystem();
            else Notify("THE MOONFALL WILL OPEN THE WAY TO ANOTHER SYSTEM");
        }

        public void PreviewFinale()
        {
            Jump(Campaign.Won);
            Frontier.Launch(); Write();
        }

        public void NextSystem()
        {
            Title = false;
            save.seed++;
            save.depth++;
            save.stage = Campaign.Tower;
            save.kit |= Campaign.Kit(save.stage);
            save.kit &= ~(1 << (int)Gear.GateParts);
            save.gate = false;
            save.clock = 0;
            save.presence = new Presence();
            save.fall = new Moonfall();
            save.colonies.Clear();
            save.escorts.pack = false;
            save.ruins.Clear();
            save.talk.Clear();
            save.wording.Clear();
            Enter();
            Resume();
            Sounds.Warp();
            script.Prepare(this, save.wording);
            StartCoroutine(IntroduceSystem(save));
            Notify($"DEPTH {Depth}  ·  AN UNCHARTED SYSTEM");
            Write();
        }

        IEnumerator IntroduceSystem(Save entered)
        {
            uint seed = entered.seed;
            while (entered == save && seed == Seed && !Title && PreparingOpening) yield return null;
            if (entered == save && seed == Seed && !Title)
                Hermit.Remark(Wording("newSystem").Replace("{star}", System.Descriptor.bodies[0].name).Replace("{world}", System.Descriptor.bodies[Campaign.World(System.Descriptor, Campaign.Tower)].name));
        }

        // Killed. Nothing is lost but the mission in hand, which begins again.
        void Die()
        {
            Dead = true;
            save.deaths++;
            save.progression = save.checkpoint.Copy();
            if (!Outpost.SiteCleared) save.ruins.RemoveAll(ruin => ruin.stage == Stage);
            if (!Outpost.SiteCleared && Outpost.World >= 0) Frontier.State(Outpost.World, Outpost.World == SolarSystemDescriptor.Home ? Stage - Campaign.FirstBurrow : 0).mindDead = false;
            Write();
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
            save.deaths = 0; save.escorts = new EscortOrders();
            save.stage = save.kit = 0;
            save.version = 5;
            save.progression = new Progression(); save.checkpoint = new Progression();
            save.journal.Clear();
            save.fitted = -1;
            save.clock = 0;
            save.gate = false;
            save.fall = new Moonfall();
            save.presence = new Presence();
            save.colonies.Clear();
            save.cargo = new Colony { world = -1, robots = 0 };
            save.ruins.Clear();
            save.talk.Clear();
            save.wording.Clear();
            Write();
        }

        // Starts the mission the save now holds, from where it begins.
        void Begin()
        {
            Began = Time.unscaledTime;
            Enter();
            Dead = false;
            Resume();
            Sounds.Warp();
        }

        public void Remember(Entry entry)
        {
            save.journal.Add(entry);
            GainExperience(15);
            Sounds.Chime();
            Write();
        }

        public void Write() => File.WriteAllText(SlotPath(Slot), JsonUtility.ToJson(save));
    }
}
