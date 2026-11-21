using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Infinigame.Core;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Infinigame.Editor
{
    // Source art and editable geometry live outside the player. Each platform imports the finished assets.
    public static class AssetWorkshop
    {
        const string Root = "Assets/Infinigame/Resources/";

        [MenuItem("Infinigame/Bake fixed assets")]
        public static void Bake()
        {
            BakeSurfaces();
            AudioWorkshop.Export(Root + "Effects");
            AssetDatabase.Refresh();
            BakeModels();
        }

        [MenuItem("Infinigame/Bake models")]
        public static void BakeModels()
        {
            int previous = QualitySettings.GetQualityLevel();
            try
            {
                foreach (int level in new[] { 0, 2 })
                {
                    QualitySettings.SetQualityLevel(level, false);
                    ModelsForTier(level == 0 ? "Mobile" : "Desktop");
                }
                AssetDatabase.SaveAssets();
            }
            finally { QualitySettings.SetQualityLevel(previous, false); }
        }

        static void PNG(string path, Texture2D texture)
        {
            File.WriteAllBytes(Root + path + ".png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        [MenuItem("Infinigame/Bake surface artwork")]
        public static void BakeSurfaces()
        {
            Directory.CreateDirectory(Root + "Surfaces");
            Directory.CreateDirectory(Root + "Visor");
            void Paint(string file, Surface first, int columns)
            {
                var source = new Texture2D(2, 2);
                if (!source.LoadImage(File.ReadAllBytes("ArtSource/" + file + ".png"))) throw new IOException("Unreadable " + file);
                using (var pixels = new NativeArray<Color32>(source.GetPixels32(), Allocator.Persistent))
                    for (int i = 0; i < columns * 2; i++)
                    {
                        var surface = (Surface)((int)first + i);
                        var tile = SurfaceWorkshop.PaintTile(surface, pixels, source.width, new int2(source.width / columns, source.height / 2),
                            new int2(i % columns, 1 - i / columns));
                        PNG("Surfaces/" + surface, tile.albedo);
                        PNG("Surfaces/" + surface + " relief", tile.normal);
                    }
                Object.DestroyImmediate(source);
            }
            Paint("Landscape-surfaces", Surface.Sand, 4);
            Paint("Faction-surfaces", Surface.Metal, 2);
            PNG("Surfaces/Blades", ShapeWorkshop.Blades());
            PNG("Surfaces/Needles", ShapeWorkshop.Needles());
            PNG("Surfaces/Billows", BillowWorkshop.Billows());
            PNG("Visor/Panel", VisorWorkshop.Rounded(0, 48, 12));
            PNG("Visor/Outline", VisorWorkshop.Rounded(2));
            PNG("Visor/Dot", VisorWorkshop.Rounded(0, 16, 8));
            PNG("Visor/Pointer", VisorWorkshop.Pointer());
            PNG("Visor/Touch", VisorWorkshop.Touch());
            AssetDatabase.Refresh();
        }

        public static void BakeWeaponMarks()
        {
            ModelsForTier("Desktop", true);
            ModelsForTier("Mobile", true);
        }

        static void ModelsForTier(string tier, bool marksOnly = false)
        {
            string directory = Root + "Models/" + tier + "/";
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();

            void Parts(string name, Mesh[] meshes, Vector3[] centres = null)
            {
                var asset = ScriptableObject.CreateInstance<ModelParts>();
                asset.meshes = meshes;
                asset.centres = centres;
                AssetDatabase.CreateAsset(asset, directory + name + ".asset");
                for (int i = 0; i < meshes.Length; i++)
                {
                    if (AssetDatabase.Contains(meshes[i])) continue;
                    if (string.IsNullOrEmpty(meshes[i].name)) meshes[i].name = name + " " + i;
                    AssetDatabase.AddObjectToAsset(meshes[i], asset);
                }
                EditorUtility.SetDirty(asset);
            }
            Parts("Visor weapon marks", Enumerable.Range(0, Combat.Arms.Length + Combat.Guns.Length).Select(VisorWorkshop.WeaponMark).ToArray());
            if (marksOnly) { AssetDatabase.SaveAssets(); return; }

            void Wreck(string name, Mesh mesh)
            {
                var pieces = ShapeWorkshop.Split(mesh);
                Parts(name + " wreck", pieces.Select(p => p.mesh).ToArray(), pieces.Select(p => p.centre).ToArray());
            }
            Mesh Combine(CombineInstance[] sources)
            {
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(sources);
                return mesh;
            }
            void Prefab(string name, Transform root)
            {
                root.name = name;
                var filters = root.GetComponentsInChildren<MeshFilter>(true);
                Parts(name + " geometry", filters.Select(f => f.sharedMesh).Where(m => m != null).Distinct().ToArray());
                string path = directory + name + " geometry.asset";
                var materials = new Dictionary<Material, Material>();
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.TryGetComponent<TextMesh>(out var readout))
                    {
                        readout.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                        renderer.sharedMaterial = readout.font.material;
                        continue;
                    }
                    var source = renderer.sharedMaterial;
                    if (source == null || AssetDatabase.Contains(source)) continue;
                    if (!materials.TryGetValue(source, out var copy))
                    {
                        copy = new Material(source) { name = renderer.name + " finish " + materials.Count };
                        AssetDatabase.AddObjectToAsset(copy, path);
                        materials.Add(source, copy);
                    }
                    renderer.sharedMaterial = copy;
                }
                if (name == "Ship" || name == "Robot")
                {
                    var hull = Combine(root.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).Select(f =>
                        new CombineInstance { mesh = f.sharedMesh, transform = root.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray());
                    Wreck(name, hull); Object.DestroyImmediate(hull);
                }
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, directory + name + ".prefab", out bool saved);
                Object.DestroyImmediate(root.gameObject);
                if (!saved) throw new IOException("Could not save " + name);
            }

            var launcher = ModelWorkshop.SpineLauncher();
            Parts("Turret emplacement", launcher);
            var wreck = Combine(launcher.Select((mesh, i) => new CombineInstance { mesh = mesh, transform = Matrix4x4.Translate(i == 0 ? Vector3.down : Vector3.zero) }).ToArray());
            Wreck("Spine turret", wreck); Object.DestroyImmediate(wreck);
            Prefab("Gauntlet", ModelWorkshop.Gauntlet()); Prefab("Traveller", ModelWorkshop.Traveller());
            Prefab("Robot", ModelWorkshop.Robot()); Prefab("Ship", ModelWorkshop.Ship());
            foreach (Building kind in Enum.GetValues(typeof(Building)))
                if (kind != Building.Robot) Prefab(kind.ToString(), ModelWorkshop.Building(kind));
            Prefab("Equipment case", ModelWorkshop.Equipment(false)); Prefab("Demolition core", ModelWorkshop.Equipment(true));
            Prefab("Gate", ModelWorkshop.Gate()); Prefab("Presence nest", ModelWorkshop.Nest());
            Prefab("Citadel carapace", ModelWorkshop.CitadelCarapace());
            Prefab("Citadel sensory chamber", ModelWorkshop.CitadelRoof(true)); Prefab("Citadel vault", ModelWorkshop.CitadelRoof(false));
            void Pair(string name, (Mesh body, Mesh light) pair)
            {
                Parts(name, new[] { pair.body, pair.light });
                Wreck(name, pair.body);
            }
            Parts("Burrow root", new[] { ModelWorkshop.BurrowRoot() });
            Pair("Harrower", ModelWorkshop.Raider()); Pair("Skimmer", ModelWorkshop.Drone()); Pair("Mind", ModelWorkshop.Mind());
            foreach (MotileCaste caste in Enum.GetValues(typeof(MotileCaste)))
            for (int level = 0; level < 3; level++)
            {
                var model = ModelWorkshop.Sentry(caste, level);
                Parts(caste + " " + level, new[] { model.body, model.light, model.leg });
                if (level != 0) continue;
                var parts = new List<CombineInstance> { new CombineInstance { mesh = model.body, transform = Matrix4x4.identity } };
                for (int i = 0; i < Models.LimbCount(caste); i++)
                    parts.Add(new CombineInstance { mesh = model.leg, transform = Matrix4x4.TRS(Models.LimbRoot(caste, i),
                        Quaternion.Euler(0, i < Models.LimbCount(caste) / 2 ? 180 : 0, 0), Vector3.one) });
                var whole = Combine(parts.ToArray());
                Wreck(caste.ToString(), whole); Object.DestroyImmediate(whole);
            }
            Parts("Missile", new[] { ModelWorkshop.Missile() }); Parts("Drifter", new[] { ModelWorkshop.Drifter() });
            var strider = ModelWorkshop.Strider(); Parts("Strider", new[] { strider.body, strider.lights });
            Parts("Crystals", ModelWorkshop.Crystals()); Parts("Tube grove", new[] { ModelWorkshop.TubeGrove() });
            Parts("Box", new[] { ShapeWorkshop.Box() }); Parts("Wrist housing", ShapeWorkshop.Bracer());
            Parts("Moon engine", new[] { ShapeWorkshop.MoonEngine() });
            Parts("Effect card", new[] { new Mesh { vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) }, triangles = new[] { 0, 2, 1, 1, 2, 3 } } });
            Parts("Accretion", new[] { Shapes.Ring(1.045f, 3.4f, 192) }); Parts("Light ring", new[] { Shapes.Ring(1.045f, 1.3f, 128) });
            Parts("Shock front", new[] { Shapes.Ring(0.97f, 1, 96) });
            foreach (var sphere in new[] { (1f, 12), (1f, 16), (1f, 24), (1f, 32), (1f, 48), (0.5f, 8), (0.045f, 10), (1.7f, 32), (22f, 24), (45f, 24) })
                Parts("Orb " + sphere.Item1.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + sphere.Item2, new[] { Shapes.Sphere(sphere.Item1, sphere.Item2) });
            foreach (int quads in new[] { 2, 3, 5, 6, 7, 12 }) Parts("Rock " + quads, new[] { ShapeWorkshop.Rock(quads) });
            var tree = ShapeWorkshop.Conifer();
            var wood = new[] { tree.trunk, tree.needles };
            Parts("Conifer", wood);
            for (int stage = 0; stage < 3; stage++)
            {
                var cut = wood.Select(source =>
                {
                    var vertices = source.vertices; var triangles = source.triangles; var indices = new List<int>();
                    for (int t = 0; t < triangles.Length; t += 3)
                        if (vertices[triangles[t]].y < 7.5f - stage * 3 && vertices[triangles[t + 1]].y < 7.5f - stage * 3 && vertices[triangles[t + 2]].y < 7.5f - stage * 3)
                            indices.AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
                    var mesh = Object.Instantiate(source); mesh.triangles = indices.ToArray(); mesh.RecalculateBounds();
                    return mesh;
                }).ToArray();
                Parts("Conifer broken " + stage, cut);
            }
            Parts("Spire", new[] { ShapeWorkshop.Spire() }); Parts("Tuft", new[] { ShapeWorkshop.Tuft() });
            Parts("Seaweed", new[] { ShapeWorkshop.Seaweed() }); Parts("Grazer", new[] { ShapeWorkshop.Grazer() });
            var bird = ShapeWorkshop.Bird(); Parts("Bird", new[] { bird.body, bird.wing });
            Parts("Fish", new[] { ShapeWorkshop.Fish() }); Parts("Ray", new[] { ShapeWorkshop.Ray() });
            var butterfly = ShapeWorkshop.Butterfly(); Parts("Butterfly", new[] { butterfly.body, butterfly.wing });

            AssetDatabase.SaveAssets();
        }
    }
}
