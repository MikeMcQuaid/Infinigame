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
        float elapsed, looked;
        float3 start;
        public string Hint => game.Buried ? game.Boot < Game.BootDuration ? "SUIT RECOVERY · MOTOR LOCK" : $"{DeviceControls.Fire} · PUNCH THROUGH THE RUBBLE  {game.Dug}/{Game.Digs}"
            : game.Outpost.Demolition > 0 ? "DEMOLITION ARMED · LEAVE THE BURROW · GET 35 m FROM THE MIND"
            : game.Outpost.SiteCleared ? DeviceControls.Key("B", "LT + D-PAD DOWN", "… / BUILD", "WRIST BUILD") + " · BUILD A ROBODOG FACTORY HERE"
            : game.Stage == 2 ? game.Outpost.Fallen ? $"{DeviceControls.Use} · TAKE THE SHIELDED BATTERY" : !game.Outpost.Rooms.walls.Last().broken ? $"{DeviceControls.Fire} · PUNCH THE MARKED ENTRANCE" : $"{DeviceControls.Fire} · PUNCH THE MIND AND ITS GUARDS"
            : game.Stage == Campaign.SecondBurrow ? "BEAM CUTS TERRAIN · DIG A FLANKING ROUTE · RECOVER SURFACE THRUSTERS"
            : game.Stage == Campaign.ThirdBurrow ? "SURFACE FLIGHT LIMIT 65 m · RECOVER THE ORBITAL BOOSTER"
            : game.Stage == Campaign.Recovery ? $"{DeviceControls.Key("SPACE", "RB", "LIFT", "LEFT A")} · CLIMB TO THE SHIP{(game.Player.InSpace ? " · " + DeviceControls.Key("T", "D-PAD RIGHT", "RECALL", "WRIST RECALL") + " · CALL IT TO YOU" : "")}"
            : game.Stage > Campaign.Recovery ? game.Stage < Campaign.Citadel && game.Outpost.World == game.System.Reference && !game.Outpost.Fallen ? game.Outpost.Instruction : ""
            : phase < 3 ? "COMPANION LINK · LISTEN · OPTICS AND LEGS REPAIRING"
            : phase == 3 ? $"{DeviceControls.Look} · LOOK AROUND · LEGS STILL REPAIRING"
            : phase == 4 ? $"{DeviceControls.Move} · WALK A FEW STEPS" : $"{DeviceControls.Fire} · PUNCH THE GROUND FIGHTER";
        public bool Repairing => game.Stage == Campaign.Stranger && phase == 2;
        public float RepairProgress => math.saturate(elapsed / 4.5f);
        public bool Locked => game.Stage == Campaign.Stranger && phase < 5;
        public void Reset() { phase = 0; elapsed = looked = 0; }
        public void Filter(ref Controls c)
        {
            if (game.Buried) { c.look = c.move = Vector2.zero; c.jump = false; return; }
            if (game.Stage != Campaign.Stranger) return;
            if (phase == 0)
            {
                game.Hermit.Rescue();
                game.Hermit.Remark("You are alive. I am your recovery robot. The living minds are consuming Earth. We build robots to stop them. Your suit is damaged. Stay still while I restore it.");
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
                game.Hermit.Remark("Optics restored. Look around. Your legs are still locked while I repair their power supply.");
            }
            if (phase == 3)
            {
                looked += c.look.magnitude;
                if (elapsed > 5 && (looked > 45 || DeviceControls.VR))
                {
                    phase = 4; start = game.Player.Position;
                    game.Hermit.Remark("Leg power restored. Walk a few steps. Only your emergency fists work until we recover a power cell.");
                }
            }
            if (phase == 4 && math.distance(start, game.Player.Position) > 3 && !game.Speech.Speaking)
            {
                phase = 5;
                float3 point = game.Player.Position + game.Player.walker.forward * 18 + math.normalizesafe(math.cross(math.normalizesafe(game.Player.Position), game.Player.walker.forward)) * 12;
                float3 up = math.normalize(point);
                game.Battle.Add(FoeKind.Sentry, up * (game.System.ReferenceBody.GroundRadius(up) + 0.9f));
                game.Hermit.Remark("One ground fighter approaches from the side. Get close and punch it. I will guide you to the burrow when it is down.");
            }
            if (phase < 4) { c.move = Vector2.zero; c.jump = false; }
            if (phase < 3) c.look = Vector2.zero;
            if (phase < 5) { c.interfaceActive = true; c.fire = c.talk = c.interact = c.travel = c.ride = false; }
            if (phase == 5 && !game.Battle.Foes.Exists(foe => foe.kind == FoeKind.Sentry && foe.health > 0)) game.Advance();
        }
    }
}
