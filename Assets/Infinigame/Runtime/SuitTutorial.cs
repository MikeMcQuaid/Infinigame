using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    public class SuitTutorial : MonoBehaviour
    {
        public Game game;
        int phase;
        bool wakeSpoken;
        float elapsed, looked;
        float3 start;
        public string Hint => game.Buried ? $"{(DeviceControls.VR ? "PUNCH UP ONCE WITH EACH HAND" : DeviceControls.Fire)} · {game.Wording("digOverhead")}  {game.Dug}/{Game.Digs}"
            : game.Outpost.Demolition > 0 ? game.Wording(game.Outpost.ClearOfDemolition ? "waitForBlast" : "blastRadius")
            : game.Outpost.EquipmentReady ? DeviceControls.Use + " · " + game.Wording("collect")
            : game.Outpost.SiteCleared ? DeviceControls.Key("B", "LT + D-PAD DOWN", "… / BUILD", "WRIST BUILD") + " · " + game.Outpost.FactoryOrder.ToUpperInvariant()
            : game.Outpost.Fallen && game.Stage != Campaign.Citadel ? $"{DeviceControls.Use} · {game.Wording("armCoreHint")}"
            : game.Stage == Campaign.FirstBurrow ? !game.Outpost.Rooms.walls.Last().broken ? $"{DeviceControls.Fire} · {game.Wording("punchDoor")}" : $"{DeviceControls.Fire} · {game.Wording("punchMind")}"
            : game.Stage == Campaign.SecondBurrow ? game.Wording("branchHint")
            : game.Stage == Campaign.ThirdBurrow ? game.Wording("orbitHint")
            : game.Stage == Campaign.Recovery ? game.Player.NearShip ? DeviceControls.Use + " · " + game.Wording("boardShip")
                : game.Player.InSpace ? game.Wording("holdForShip") : DeviceControls.Key("SPACE", "RB", "LIFT", "L TRIGGER") + " · " + game.Wording("climbShip")
            : game.Stage > Campaign.Recovery ? game.Stage < Campaign.Citadel && game.Outpost.World == game.System.Reference && !game.Outpost.Fallen ? game.Outpost.Instruction : ""
            : phase < 3 ? game.Wording("repairHint")
            : phase == 3 ? $"{DeviceControls.Look} · {game.Wording("lookShip")}"
            : phase == 4 ? $"{DeviceControls.Move} · {game.Wording("walk")}" : $"{DeviceControls.Fire} · {game.Wording("punchFighter")}";
        public bool PointingAtShip => game.Stage == Campaign.Stranger && phase == 3;
        public bool Repairing => game.Stage == Campaign.Stranger && phase == 2;
        public float RepairProgress => math.saturate(elapsed / 4.5f);
        public bool Locked => game.Stage == Campaign.Stranger && phase < 5;
        public void Reset() { phase = 0; elapsed = looked = 0; wakeSpoken = false; }
        public void Filter(ref Controls c)
        {
            if (game.Buried)
            {
                if (!wakeSpoken && !game.PreparingOpening)
                {
                    wakeSpoken = true;
                    game.Hermit.Remark(game.Wording("wake"));
                }
                c.look = c.move = Vector2.zero; c.jump = false;
                return;
            }
            if (game.Stage != Campaign.Stranger) return;
            if (phase == 0)
            {
                if (game.PreparingOpening) { c = default; return; }
                game.Hermit.Remark(game.Wording("rescue"));
                phase = 1; elapsed = 0;
            }
            elapsed += Time.deltaTime;
            if (phase == 1 && elapsed > 12 && !game.Speech.Speaking)
            {
                phase = 2; elapsed = 0;
            }
            if (phase == 2 && elapsed > 4.5f)
            {
                phase = 3; elapsed = 0;
                game.Hermit.Remark(game.Wording("optics"));
            }
            if (phase == 3)
            {
                looked += c.look.magnitude;
                if (elapsed > 5 && !game.Speech.Speaking && (looked > 45 || DeviceControls.VR))
                {
                    phase = 4; start = game.Player.Position;
                    game.Hermit.Remark(game.Wording("legs"));
                }
            }
            if (phase == 4 && math.distance(start, game.Player.Position) > 3 && !game.Speech.Speaking)
            {
                phase = 5;
                float3 point = game.Player.Position + game.Player.walker.forward * 18 + math.normalizesafe(math.cross(math.normalizesafe(game.Player.Position), game.Player.walker.forward)) * 12;
                float3 up = math.normalize(point);
                game.Battle.Add(FoeKind.Sentry, up * (game.System.ReferenceBody.GroundRadius(up) + 0.9f));
                game.Hermit.Remark(game.Wording("fighter"));
            }
            if (phase < 4) { c.move = Vector2.zero; c.jump = false; }
            if (phase < 3) c.look = Vector2.zero;
            if (phase < 5) { c.interfaceActive = true; c.fire = c.talk = c.interact = c.travel = c.ride = false; }
            if (phase == 5 && !game.Battle.Foes.Exists(foe => foe.kind == FoeKind.Sentry && foe.health > 0)) game.Advance();
        }
    }
}
