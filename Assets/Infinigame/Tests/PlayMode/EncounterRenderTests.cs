using System.Collections;
using System.IO;
using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infinigame.Tests
{
    public class EncounterRenderTests
    {
        [Test]
        public void FixedEquipmentSurfacesAndEffectsShipAsAssets()
        {
            foreach (string tier in new[] { "Desktop", "Mobile" })
            {
                var hand = Resources.Load<GameObject>("Models/" + tier + "/Gauntlet");
                Assert.IsNotNull(hand, tier + " gauntlet");
                for (int finger = 0; finger < 5; finger++)
                    Assert.IsNotNull(hand.transform.Find("Digit " + finger + "/Middle/Tip"), "Articulated finger " + finger);
                foreach (string form in new[] { "Ray", "Butterfly", "Visor weapon marks" })
                {
                    var geometry = Resources.Load<ModelParts>("Models/" + tier + "/" + form);
                    Assert.IsNotNull(geometry, tier + " " + form);
                    Assert.IsTrue(geometry.meshes.All(mesh => mesh != null && mesh.vertexCount > 8));
                }
                foreach (string name in new[] { "Robot", "Ship", "Traveller", "Factory", "ShipFactory" })
                    Assert.IsNotNull(Resources.Load<GameObject>("Models/" + tier + "/" + name), name);
            }
            foreach (Surface surface in System.Enum.GetValues(typeof(Surface)))
            {
                Assert.IsNotNull(Textures.Albedo(surface), surface.ToString());
                Assert.IsNotNull(Textures.Normal(surface), surface + " relief");
                Assert.AreSame(Resources.Load<Texture2D>("Surfaces/" + surface), Textures.Albedo(surface));
                Assert.AreSame(Resources.Load<Texture2D>("Surfaces/" + surface + " relief"), Textures.Normal(surface));
            }
            Assert.IsNotNull(Resources.Load<AudioClip>("Effects/Engine"));
            Assert.IsNotNull(Resources.Load<AudioClip>("Effects/Encounter Threat 2"));
            var soundtrack = Resources.LoadAll<AudioClip>("Music");
            Assert.IsTrue(soundtrack.Any(track => track.name.StartsWith("explore")));
            Assert.IsTrue(soundtrack.Any(track => track.name.StartsWith("battle")));
        }

        [Test]
        public void GauntletTracksEveryFingerIndependentlyInBothHands()
        {
            foreach (float mirror in new[] { -1f, 1f })
            {
                var hand = Models.Gauntlet();
                try
                {
                    hand.localScale = new Vector3(mirror, 1, 1);
                    var pose = hand.gameObject.AddComponent<GauntletFingers>();
                    for (int i = 0; i < 5; i++) pose.SetCurl(i, 0);
                    var tips = Enumerable.Range(0, 5).Select(i => hand.Find("Digit " + i + "/Middle/Tip")).ToArray();
                    for (int i = 0; i < tips.Length; i++)
                    {
                        var flat = tips.Select(tip => tip.position).ToArray();
                        pose.SetCurl(i, 1);
                        for (int other = 0; other < tips.Length; other++)
                            if (i == other) Assert.Greater(Vector3.Distance(flat[other], tips[other].position), 0.04f);
                            else Assert.AreEqual(flat[other], tips[other].position);
                    }
                }
                finally { Object.DestroyImmediate(hand.gameObject); }
            }
        }

        [Test]
        public void RenderFlightEquipment()
        {
            foreach (var name in new[] { "Ichor", "EnergyRibbon", "Spark", "Flash", "Puff" })
            {
                var shader = Resources.Load<Shader>(name);
                Assert.IsTrue(shader.isSupported, name);
            }
            var ship = Models.Ship();
            var glove = Models.Gauntlet();
            var camera = new GameObject("Equipment review").AddComponent<Camera>();
            var key = new GameObject("Equipment key").AddComponent<Light>();
            var fill = new GameObject("Equipment fill").AddComponent<Light>();
            var target = new RenderTexture(960, 640, 24);
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.13f, 0.17f, 0.21f);
                camera.targetTexture = target; camera.nearClipPlane = 0.015f; camera.cullingMask = 1 << 29;
                key.type = fill.type = LightType.Directional;
                key.cullingMask = fill.cullingMask = camera.cullingMask;
                key.transform.rotation = Quaternion.Euler(43, 151, 0); key.intensity = 2.1f;
                fill.transform.rotation = Quaternion.Euler(26, -36, 0); fill.intensity = 0.85f;
                foreach (var actor in new[] { ship, glove })
                    foreach (var part in actor.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 29;
                void Capture(string name, Vector3 eye, Vector3 aim)
                {
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                    camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    Directory.CreateDirectory("Logs/Screenshots");
                    File.WriteAllBytes($"Logs/Screenshots/{name}.png", image.EncodeToPNG());
                }
                glove.gameObject.SetActive(false);
                ship.Find("Cockpit").gameObject.SetActive(false);
                Capture("flight-chassis", new Vector3(4.6f, 3.8f, 5.48f), Vector3.zero);
                ship.Find("Exterior").gameObject.SetActive(false);
                ship.Find("Cockpit").gameObject.SetActive(true);
                Capture("flight-cabin", Models.PilotEye, new Vector3(0, 0.70f, 2.2f));
                Assert.IsNotNull(ship.Find("Cockpit/Cabin rails"));
                Capture("pilot-seat", Models.PilotEye, new Vector3(0.43f, 0.40f, 0.57f));
                ship.gameObject.SetActive(false); glove.gameObject.SetActive(true);
                Capture("gauntlet-rails", new Vector3(0.31f, 0.34f, 0.38f), Vector3.zero);
                var fingers = glove.gameObject.AddComponent<GauntletFingers>();
                for (int i = 0; i < 5; i++) fingers.SetCurl(i, 0);
                Capture("gauntlet-open", new Vector3(0.30f, -0.37f, 0.48f), new Vector3(0, 0, 0.03f));
                for (int i = 0; i < 5; i++) fingers.SetCurl(i, i == 1 ? 0 : 1);
                Capture("gauntlet-point", new Vector3(0.30f, -0.37f, 0.48f), new Vector3(0, 0, 0.03f));
                glove.gameObject.SetActive(false);
                void Portrait(string name, Transform subject, Vector3 eye, Vector3 centre)
                {
                    try
                    {
                        foreach (var part in subject.GetComponentsInChildren<Transform>()) part.gameObject.layer = 29;
                        Capture(name, eye, centre);
                    }
                    finally { Object.DestroyImmediate(subject.gameObject); }
                }
                Portrait("field-robodog", Models.Robot(), new Vector3(2.23f, 1.44f, 1.40f), new Vector3(0, 0.87f, 0.07f));
                Portrait("field-suit", Models.Traveller(), new Vector3(0.93f, 1.22f, 2.15f), Vector3.up * 1.04f);
                Portrait("field-mind", Models.Nest(), new Vector3(3.8f, 3.45f, 4.55f), Vector3.up * 1.3f);
                var fortress = Models.CitadelCarapace();
                fortress.localScale = Vector3.one * 0.01f;
                Portrait("field-citadel", fortress, new Vector3(10.4f, 4.6f, -5.8f), new Vector3(1.1f, 0, 2.3f));
                var carapace = Shapes.PresenceShell(1.2f);
                var lenses = Shapes.PresenceOptics(1.2f);
                try
                {
                    var launcher = new GameObject("Spine turret").transform;
                    var parts = Models.SpineLauncher();
                    Shapes.Part(launcher, "Root", parts[0], carapace, Vector3.zero, Vector3.one);
                    for (int i = 1; i < parts.Length; i++)
                        Shapes.Part(launcher, "Rack", parts[i], i == 1 ? carapace : lenses, Vector3.up, Vector3.one).transform.localRotation = Quaternion.Euler(-21, 16, 0);
                    Portrait("field-spine-turret", launcher, new Vector3(3.9f, 2.0f, 2.25f), Vector3.up * 0.76f);
                    foreach (MotileCaste caste in System.Enum.GetValues(typeof(MotileCaste)))
                    {
                        var (shell, eyes, leg) = Models.Sentry(caste);
                        var subject = new GameObject(caste.ToString()).transform;
                        Shapes.Part(subject, "Body", shell, carapace, Vector3.zero, Vector3.one);
                        Shapes.Part(subject, "Eyes", eyes, lenses, Vector3.zero, Vector3.one);
                        int legs = Models.LimbCount(caste);
                        for (int i = 0; i < legs; i++)
                            Shapes.Part(subject, "Leg", leg, carapace, Models.LimbRoot(caste, i), Vector3.one).transform.localRotation = Quaternion.Euler(0, i < legs / 2 ? 180 : 0, 0);
                        Portrait("field-" + caste.ToString().ToLowerInvariant(), subject, new Vector3(-1.45f, 0.94f, 3.58f), new Vector3(0, 0.13f, 0.03f));
                    }
                    foreach (bool raider in new[] { false, true })
                    {
                        var (shell, eyes) = raider ? Models.Raider() : Models.Drone();
                        var subject = new GameObject("Living craft").transform;
                        Shapes.Part(subject, "Body", shell, carapace, Vector3.zero, Vector3.one);
                        Shapes.Part(subject, "Eyes", eyes, lenses, Vector3.zero, Vector3.one);
                        Portrait(raider ? "field-harrower" : "field-skimmer", subject,
                            raider ? new Vector3(5.4f, 7.8f, 6.4f) : new Vector3(1.5f, 1.65f, 1.8f), Vector3.zero);
                    }
                }
                finally { Object.DestroyImmediate(carapace); Object.DestroyImmediate(lenses); }
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = active;
                foreach (var item in new Object[] { ship.gameObject, glove.gameObject, camera.gameObject, key.gameObject, fill.gameObject, image, target }) Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void SwarmMeshesKeepTheirSilhouetteAcrossDetailBoundaries()
        {
            foreach (MotileCaste caste in System.Enum.GetValues(typeof(MotileCaste)))
            {
                long previous = long.MaxValue;
                for (int detail = 0; detail < 3; detail++)
                {
                    var (shell, eyes, limb) = Models.Sentry(caste, detail);
                    long triangles = (shell.GetIndexCount(0) + eyes.GetIndexCount(0) + limb.GetIndexCount(0) * Models.LimbCount(caste)) / 3;
                    Assert.Less(triangles, previous);
                    Assert.Greater(shell.bounds.size.z, 1.5f);
                    Assert.Greater(limb.bounds.size.y, 0.85f);
                    Assert.Greater(eyes.bounds.max.y, caste == MotileCaste.Spitter ? 1.05f : 0.8f,
                        $"{caste} must keep its raised sensory crown at detail {detail}");
                    previous = triangles;
                }
            }
            Assert.AreEqual(0, LodSettings.SentryDetail(145, 0));
            Assert.AreEqual(1, LodSettings.SentryDetail(145, 1));
            Assert.AreEqual(1, LodSettings.SentryDetail(40, 1));
            Assert.AreEqual(2, LodSettings.SentryDetail(40, 2));
        }

        [UnityTest]
        public IEnumerator RenderGroundEncounter()
        {
            const int layer = 29;
            var stage = new GameObject("Encounter studio").transform;
            var robot = Models.Robot();
            robot.SetParent(stage, false);
            robot.localPosition = new Vector3(1.28f, 0, 0);
            robot.localRotation = Quaternion.Euler(0, -36, 0);
            var alien = new GameObject("Stalker").transform;
            alien.SetParent(stage, false);
            alien.localPosition = new Vector3(-1.25f, 0.9f, 0);
            alien.localRotation = Quaternion.Euler(0, 36, 0);
            var (body, eyes, limb) = Models.Sentry();
            var shell = Shapes.PresenceShell(1.2f);
            var senses = Shapes.PresenceOptics(1.2f);
            Shapes.Part(alien, "Shell", body, shell, Vector3.zero, Vector3.one);
            Shapes.Part(alien, "Eyes", eyes, senses, Vector3.zero, Vector3.one);
            for (int i = 0; i < Models.LimbCount(MotileCaste.Stalker); i++)
            {
                var leg = Shapes.Part(alien, "Leg", limb, shell, Models.LimbRoot(MotileCaste.Stalker, i), Vector3.one);
                leg.transform.localRotation = Quaternion.Euler(0, i < 3 ? 180 : 0, 0);
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(stage, false);
            floor.transform.localScale = new Vector3(40, 0.1f, 40);
            floor.transform.localPosition = Vector3.down * 0.075f;
            var ground = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.28f, 0.31f, 0.34f) };
            floor.GetComponent<Renderer>().sharedMaterial = ground;
            var key = new GameObject("Softbox").AddComponent<Light>();
            key.transform.SetParent(stage, false);
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(52, 150, 0);
            key.intensity = 2;
            key.shadows = LightShadows.Soft;
            key.cullingMask = 1 << layer;
            var camera = new GameObject("Studio camera").AddComponent<Camera>();
            camera.transform.SetParent(stage, false);
            camera.transform.localPosition = new Vector3(0, 2.2f, 6);
            camera.transform.LookAt(stage.TransformPoint(new Vector3(0, 0.9f, 0)));
            camera.orthographic = true;
            camera.orthographicSize = 1.52f;
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = ground.color;
            camera.cullingMask = 1 << layer;
            foreach (var item in stage.GetComponentsInChildren<Transform>()) item.gameObject.layer = layer;
            var target = new RenderTexture(1280, 640, 24);
            var picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                yield return null;
                camera.targetTexture = target;
                var times = new double[21];
                for (int i = 0; i < times.Length; i++)
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    camera.Render();
                    RenderTexture.active = target;
                    picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                    times[i] = timer.Elapsed.TotalMilliseconds;
                }
                Directory.CreateDirectory("Logs/Screenshots");
                File.WriteAllBytes("Logs/Screenshots/ground-encounter.png", picture.EncodeToPNG());
                long Triangles(Transform actor) => actor.GetComponentsInChildren<MeshFilter>().Sum(part => (long)part.sharedMesh.GetIndexCount(0) / 3);
                File.WriteAllText("Logs/Screenshots/ground-encounter.txt", $"Robot {Triangles(robot)} triangles, stalker {Triangles(alien)} triangles. Median render and readback {times.Skip(1).OrderBy(t => t).ElementAt(10):F2} ms on {SystemInfo.graphicsDeviceName}, detail {LodSettings.Tier}.\n");
                Assert.Greater(Triangles(alien), 30000);
#if UNITY_EDITOR
                var headset = DeviceControls.SimulateHeadset;
                DeviceControls.SimulateHeadset = true;
                try
                {
                    Object.Destroy(shell); Object.Destroy(senses);
                    shell = Shapes.PresenceShell(1.2f);
                    senses = Shapes.PresenceOptics(1.2f);
                    foreach (var part in alien.GetComponentsInChildren<Renderer>())
                        part.sharedMaterial = part.name == "Eyes" ? senses : shell;
                    robot.gameObject.SetActive(false);
                    camera.allowHDR = false;
                    camera.orthographicSize = 1.03f;
                    camera.transform.position = alien.position + new Vector3(0.6f, 0.75f, 3.3f);
                    camera.transform.LookAt(alien.position);
                    camera.Render();
                    RenderTexture.active = target;
                    picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                    File.WriteAllBytes("Logs/Screenshots/index-close-encounter.png", picture.EncodeToPNG());
                    var pixels = picture.GetPixels32();
                    Assert.Less(pixels.Count(p => p.r > 245 && p.b > 245 && (p.g > 245 || p.g < 30)), pixels.Length / 100,
                        "White or shader-error pixels cover the close enemy");
                }
                finally { DeviceControls.SimulateHeadset = headset; }
#endif
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                foreach (var item in new Object[] { stage.gameObject, picture, target, shell, senses, ground }) Object.Destroy(item);
            }
        }
    }
}
