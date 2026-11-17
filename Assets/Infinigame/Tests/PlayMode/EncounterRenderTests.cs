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
                    previous = triangles;
                    Object.Destroy(shell); Object.Destroy(eyes); Object.Destroy(limb);
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
            var shell = Shapes.SentryShell();
            var senses = Shapes.Hull(Color.black, 1.2f, new Color(0.9f, 0.08f, 0.2f));
            senses.SetFloat("_Organic", 1);
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
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                foreach (var item in new Object[] { stage.gameObject, picture, target, shell, senses, ground, body, eyes, limb }) Object.Destroy(item);
            }
        }
    }
}
