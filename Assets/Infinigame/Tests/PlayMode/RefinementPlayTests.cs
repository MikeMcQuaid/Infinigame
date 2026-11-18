using System.Collections;
using System.Linq;
using System.Reflection;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infinigame.Tests
{
    public partial class PlayTests
    {
        [UnityTest] public IEnumerator RefinementRepairBeamAndWeaponOutline()
        {
            game.Jump(1);
            yield return new WaitForSeconds(13);
            Assert.IsTrue(game.Tutorial.Repairing);
            var beam = GameObject.Find("Suit repair beam").GetComponent<LineRenderer>();
            Assert.IsTrue(beam.enabled);
            Assert.Greater(beam.sharedMaterial.color.b, beam.sharedMaterial.color.r);
            var glyph = Object.FindAnyObjectByType<WeaponGlyph>();
            Canvas.ForceUpdateCanvases();
            var mesh = glyph.canvasRenderer.GetMesh();
            Assert.Greater(mesh.vertexCount, 20, "The equipped weapon has a visible outline mesh");
            Assert.Greater(glyph.canvasRenderer.GetInheritedAlpha(), 0.5f);
            Assert.IsFalse(glyph.canvasRenderer.cull);
            Object.Destroy(mesh);
            yield return ReworkShot("revision3-repair");
        }
        [UnityTest] public IEnumerator RefinementFactoryUplinkExplainsLockedUpgrades()
        {
            game.Jump(Campaign.ThirdBurrow); yield return null;
            yield return Press(new Controls { ride = true });
            Assert.AreEqual(Game.ColonyPage, game.Page);
            Assert.IsFalse(game.Frontier.CanBuild(1));
            yield return Press(new Controls { down = true });
            StringAssert.Contains("BUILD A ROBODOG FACTORY FIRST", game.Hint);
            yield return Press(new Controls { confirm = true });
            Assert.IsFalse(game.Frontier.Local.shipyard);
            game.Frontier.Local.factory = true;
            Assert.IsTrue(game.Items[0].Contains("built"));
            StringAssert.Contains("FACTORY BUILT", game.Frontier.FactoryHint(0));
            for (int region = 0; region < 3; region++) { var front = game.Frontier.State(Home, region); front.aliens = 0; front.factory = front.mindDead = true; }
            Assert.IsTrue(game.Frontier.CanBuild(1));
            Assert.IsFalse(game.Frontier.CanBuild(2));
        }
        [UnityTest] public IEnumerator RefinementQualitySettingsKeepSixtyFrames()
        {
            yield return Choose(Game.Settings);
            Assert.AreEqual(60, Application.targetFrameRate);
            Assert.AreEqual(0, QualitySettings.vSyncCount, "A quality preset must not halve a 60 Hz display");
            Assert.IsTrue(game.Items.Any(item => item.StartsWith("AUTOMATIC QUALITY")));
            int at = System.Array.FindIndex(game.Items, item => item.StartsWith("AUTOMATIC QUALITY"));
            while (game.Selected != at) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            StringAssert.EndsWith("OFF", game.Items[at]);
            Assert.AreEqual(0, PlayerPrefs.GetInt($"{Game.Prefs}.automaticQuality"));
        }
        [UnityTest] public IEnumerator RefinementConversationCanBeDismissedWithoutOpeningMenu()
        {
            game.Hermit.Greet();
            yield return null;
            yield return Press(new Controls { click = true, pointer = Vector2.zero });
            Assert.IsFalse(game.Hermit.Talking, "Clicking outside the dialogue closes it");
            Assert.IsFalse(game.Paused);
            game.Hermit.Greet();
            yield return Press(new Controls { menu = true });
            Assert.IsFalse(game.Hermit.Talking);
            Assert.IsFalse(game.Paused);
        }
        [UnityTest] public IEnumerator RefinementTouchActionsHideUnavailableEquipment()
        {
            game.Jump(2);
            yield return null;
            var device = game.GetComponent<DeviceControls>();
            var visible = typeof(DeviceControls).GetMethod("Visible", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsFalse((bool)visible.Invoke(device, new object[] { 8 }), "Recall is unavailable before the ship recovery mission");
            Assert.IsFalse((bool)visible.Invoke(device, new object[] { 13 }), "Cloak needs its equipment");
            Assert.IsNotNull(GameObject.Find("Move joystick"));
            Assert.IsNotNull(GameObject.Find("Look joystick"));
        }
        [UnityTest] public IEnumerator RefinementBurrowPickupRestsBehindTheMind()
        {
            game.Jump(2);
            yield return null;
            Assert.Greater(game.Outpost.Local(game.Outpost.Prize).z, game.Outpost.Rooms.core.z);
            float3 core = math.normalizesafe(game.Outpost.Core);
            float floor = Body.GroundRadius(core);
            Body.Dig(core, 2);
            Assert.AreEqual(floor, Body.GroundRadius(core), "The living burrow must keep its floor until demolition");
            float3 up = math.normalizesafe(game.Outpost.Prize);
            Assert.AreEqual(Body.GroundRadius(up), math.length(game.Outpost.Prize), 0.15f);
        }
        [UnityTest] public IEnumerator RefinementMapPagesZoomAndGodMode()
        {
            yield return Press(new Controls { systemView = true });
            Assert.IsTrue(Player.SystemView); Assert.IsFalse(game.MapSummary);
            double clock = Solar.Clock;
            yield return Press(new Controls { cycle = 1 });
            Assert.Less(game.MapZoom, 1);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return ReworkShot("revision3-map");
            yield return Press(new Controls { toggleView = true });
            Assert.IsTrue(game.MapSummary); Assert.AreEqual(clock, Solar.Clock);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return ReworkShot("revision3-summary");
            yield return Press(new Controls { systemView = true });
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { typed = "idgaf" });
            yield return Press(new Controls { menu = true });
            yield return Choose(Game.Cheats);
            while (!game.Items[game.Selected].StartsWith("INVULNERABLE")) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            float health = Player.health, hull = Player.hull;
            Player.Hurt(10000, "test");
            Assert.IsTrue(game.GodMode); Assert.AreEqual(health, Player.health); Assert.AreEqual(hull, Player.hull);
        }
        [UnityTest] public IEnumerator RefinementTwinBeamsAndShipRailCharge()
        {
            HoverAt(math.normalizesafe(Player.Position) * (Body.radius + 1200));
            yield return null; yield return null;
            yield return Press(new Controls { select = 3 });
            input.fire = true; yield return null; yield return null;
            var rays = game.Battle.GetComponentsInChildren<LineRenderer>().Where(ray => ray.enabled && (ray.name == "Weapon beam" || ray.name == "Twin rail trail")).ToArray();
            Assert.AreEqual(2, rays.Length);
            Assert.Greater(Vector3.Distance(rays[0].GetPosition(0), rays[1].GetPosition(0)), 3);
            input = default; yield return Seconds(0.2f);
            yield return Press(new Controls { select = 4 });
            float fired = game.Battle.Fired;
            input.fire = true; yield return Seconds(0.5f);
            Assert.AreEqual(fired, game.Battle.Fired); Assert.Greater(game.Battle.RailCharge, 0.35f);
            input.fire = false; yield return null; yield return null;
            Assert.Greater(game.Battle.Fired, fired);
        }
        [UnityTest] public IEnumerator RefinementLivingCitadelAndLiquidRemains()
        {
            yield return HoverOver(Solar.Station, 500);
            game.Outpost.Tick();
            Assert.AreEqual(3, game.Battle.Foes.Count(foe => foe.kind == FoeKind.Mind));
            Assert.IsTrue(game.Battle.Foes.Where(foe => foe.kind == FoeKind.Mind).All(foe => foe.size >= 6));
            Assert.GreaterOrEqual(game.Battle.Foes.Count(foe => foe.stationary), 6);
            Assert.IsTrue(game.Outpost.Shield.active);
            foreach (var mind in game.Battle.Foes.Where(foe => foe.kind == FoeKind.Mind).Take(2).ToArray())
            { int i = game.Battle.Foes.IndexOf(mind); var killed = mind; killed.health = 0; game.Battle.Foes[i] = killed; }
            yield return null; yield return null;
            Assert.AreEqual(1, game.Outpost.Left); Assert.IsFalse(game.Outpost.Fallen);
            yield return Until(() => Object.FindAnyObjectByType<IchorPool>() != null);
            var pool = Object.FindAnyObjectByType<IchorPool>();
            Vector3 centre = pool.transform.localPosition;
            yield return Seconds(0.3f);
            Assert.Greater(pool.GetComponent<MeshFilter>().sharedMesh.vertexCount, 100);
            Assert.AreEqual(centre, pool.transform.localPosition);
            Assert.AreEqual("Infinigame/Ichor", pool.GetComponent<Renderer>().sharedMaterial.shader.name);
            Assert.GreaterOrEqual(game.Corpses.Count, 2);
            HoverAt(game.Outpost.Place(game.Outpost.Rooms.route[5] + new float3(0, 0, -20)));
            yield return ReworkShot("revision3-citadel-remains");
        }
    }
}
