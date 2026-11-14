using System;
using System.Collections.Generic;
using System.IO;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Infinigame
{
    // The one object in the scene: builds the solar system and the player, and runs the loop of
    // finding every relic in a system to charge the warp drive, then warping to the next seed.
    public class Game : MonoBehaviour
    {
        [Serializable]
        class Save
        {
            public uint seed = 1;
            public int systemsVisited;
            public List<int> collected = new List<int>();
        }

        const float WarpAltitude = 300f;

        public SolarSystem System { get; private set; }
        public Player Player { get; private set; }
        public uint Seed => save.seed;
        public int SystemsVisited => save.systemsVisited;
        public int RelicTotal { get; private set; }

        // Relics still to find, with the glowing marker that shows each one.
        public readonly List<(RelicSite site, Transform view)> Relics = new List<(RelicSite, Transform)>();

        public bool CanWarp => Relics.Count == 0 && Player.flying && !Player.ship.landed
            && math.length(Player.Position) - System.ReferenceBody.radius > WarpAltitude;

        Save save = new Save();
        Sounds sounds;
        // Tests point this at a scratch file so they never touch a real save.
        public static string SaveFile = "save.json";
        static string SavePath => Path.Combine(Application.persistentDataPath, SaveFile);

        void Awake()
        {
            QualitySettings.vSyncCount = 1;
            Cursor.lockState = CursorLockMode.Locked;
            if (File.Exists(SavePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(SavePath), save);
            System = gameObject.AddComponent<SolarSystem>();
            Player = new GameObject("Player").AddComponent<Player>();
            Player.Init(System);
            sounds = gameObject.AddComponent<Sounds>();
            sounds.player = Player;
            gameObject.AddComponent<Hud>().game = this;
            Load();
            Player.Spawn();
        }

        // Builds the saved seed's system, terrain and gravity together, and hides its relics.
        void Load()
        {
            var descriptor = SolarSystemDescriptor.Generate(save.seed);
            // the first world is earth-like; every later seed picks its own colours
            if (save.systemsVisited == 0) descriptor.bodies[1].hue = 0.5f;
            System.Build(descriptor, 1);

            foreach (var relic in Relics) Destroy(relic.view.gameObject);
            Relics.Clear();
            var sites = Core.Relics.Place(descriptor);
            RelicTotal = sites.Count;
            Mesh orb = Shapes.Sphere(0.7f, 4), beam = Shapes.Box();
            Material glow = Shapes.Material("Glow", new Color(3f, 6f, 5f)), light = Shapes.Material("Beam", new Color(0.4f, 1f, 0.8f, 0.35f));
            foreach (var site in sites)
            {
                if (save.collected.Contains(site.id)) continue;
                var view = Shapes.Part(System.Bodies[site.body].transform, "Relic", orb, glow, site.position, Vector3.one, false).transform;
                view.localRotation = Quaternion.FromToRotation(Vector3.up, site.position);
                // a beam tall enough to pick out from orbit
                Shapes.Part(view, "Beam", beam, light, new Vector3(0f, 200f, 0f), new Vector3(1.2f, 400f, 1.2f), false);
                Relics.Add((site, view));
            }
        }

        void Update()
        {
            var c = Player.controls();
            if (c.regenerate) Travel(false);
            if (c.warp && CanWarp) Travel(true);
            if (c.tierUp || c.tierDown)
                QualitySettings.SetQualityLevel(Mathf.Clamp(QualitySettings.GetQualityLevel() + (c.tierUp ? 1 : -1), 0, QualitySettings.names.Length - 1));
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;

            for (int i = Relics.Count - 1; i >= 0; i--)
            {
                var (site, view) = Relics[i];
                if (site.body != System.Reference || math.distance(Player.Position, site.position) > (Player.flying ? Core.Relics.ShipRange : Core.Relics.WalkRange))
                    continue;
                Destroy(view.gameObject);
                Relics.RemoveAt(i);
                save.collected.Add(site.id);
                sounds.Chime(Relics.Count == 0);
                Write();
            }
        }

        // Moves on to the next seed: by warp drive, arriving in orbit, or instantly on foot for testing.
        void Travel(bool warp)
        {
            save.seed++;
            save.systemsVisited++;
            save.collected.Clear();
            Load();
            if (warp) Player.SpawnInOrbit();
            else Player.Spawn();
            sounds.Warp();
            Write();
        }

        void Write() => File.WriteAllText(SavePath, JsonUtility.ToJson(save));
    }
}
