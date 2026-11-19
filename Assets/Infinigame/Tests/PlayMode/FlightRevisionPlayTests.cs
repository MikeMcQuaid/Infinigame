using System.Collections;
using System.IO;
using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infinigame.Tests
{
    public partial class PlayTests
    {
        [UnityTest] public IEnumerator FlightRevisionTitleCheatEntersTheGame()
        {
            yield return Choose("Quit to title");
            yield return Press(new Controls { typed = "idgaf" });
            int row = System.Array.IndexOf(game.Items, Game.Cheats);
            for (int i = 0; i < row; i++) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.IsFalse(game.Title);
            Assert.IsFalse(game.Paused);
            Assert.IsTrue(game.Frontier.Local.shipyard);
            Assert.Less(math.distance(Player.Position, game.Colonies.First(colony => colony.world == Solar.Reference).site), 30);
        }
        [UnityTest] public IEnumerator FlightRevisionMenusFreezeSimulationAndDeletionAsksFirst()
        {
            input.systemView = true;
            yield return null; yield return null;
            double clock = Solar.Clock; float3 at = Player.Position;
            yield return Seconds(0.2f);
            Assert.AreEqual(clock, Solar.Clock);
            Assert.AreEqual(at, Player.Position);
            input = default; yield return null;
            yield return Press(new Controls { systemView = true });
            yield return Press(new Controls { help = true });
            clock = Solar.Clock;
            yield return Seconds(0.2f); Assert.AreEqual(clock, Solar.Clock);
            yield return Press(new Controls { menu = true });
            yield return Choose("Quit to title");
            int row = System.Array.IndexOf(game.Items, "Delete slot 1");
            for (int i = 0; i < row; i++) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.IsTrue(game.Confirming);
            Assert.IsTrue(File.Exists(Path.Combine(Application.persistentDataPath, "test-save.json")));
            yield return Press(new Controls { down = true }); yield return Press(new Controls { confirm = true });
            Assert.IsFalse(File.Exists(Path.Combine(Application.persistentDataPath, "test-save.json")));
            Assert.IsTrue(game.Title);
        }
        [UnityTest] public IEnumerator FlightRevisionRailChargesAndDeadBodiesRemain()
        {
            game.Jump(Campaign.Citadel);
            float3 up = Campaign.Site(Solar.Descriptor, Campaign.Stranger);
            Stand(up, World(up));
            yield return null;
            Assert.IsFalse(GameObject.Find("Traveller").transform.Find("Torso/Head").gameObject.activeSelf);
            Assert.IsTrue(GameObject.Find("Traveller").transform.Find("Left leg").gameObject.activeSelf);
            yield return Press(new Controls { select = 4 });
            Assert.IsTrue(Player.Weapon.rail);
            float fired = game.Battle.Fired;
            input.fire = true; yield return Seconds(0.6f);
            Assert.AreEqual(fired, game.Battle.Fired);
            Assert.Greater(game.Battle.RailCharge, 0.3f);
            Assert.Less(Camera.main.fieldOfView, 58);
            input.fire = false; yield return null; yield return null;
            Assert.Greater(game.Battle.Fired, fired);
            game.Battle.Add(FoeKind.Sentry, Player.Position + Player.walker.forward * 5);
            var foe = game.Battle.Foes.Last(); foe.health = 0; game.Battle.Foes[game.Battle.Foes.Count - 1] = foe;
            yield return Seconds(0.3f);
            Assert.Greater(game.Corpses.Count, 0);
            yield return ReworkShot("120-remains-and-visor");
        }
        [UnityTest] public IEnumerator FlightRevisionMeshesAreSharedAndMapShowsWar()
        {
            var a = Models.Ship(); var b = Models.Ship();
            Assert.AreSame(a.GetComponentInChildren<MeshFilter>().sharedMesh, b.GetComponentInChildren<MeshFilter>().sharedMesh);
            Object.Destroy(a.gameObject); Object.Destroy(b.gameObject);
            input.systemView = true; yield return Seconds(0.3f);
            yield return ReworkShot("121-war-map");
            var labels = GameObject.Find("HUD").GetComponentsInChildren<UnityEngine.UI.Text>();
            Assert.IsTrue(labels.Any(label => label.text.StartsWith("ALIENS")));
            Assert.IsFalse(labels.Any(label => label.text.StartsWith("GRAVITY")));
        }
    }
}
