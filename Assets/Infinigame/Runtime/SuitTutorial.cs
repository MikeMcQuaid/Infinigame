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
            : game.Outpost.Demolition > 0 ? "DEMOLITION ARMED · LEAVE THE BURROW · GET 65 m FROM THE MIND"
            : game.Stage == 2 ? game.Outpost.Fallen ? $"{DeviceControls.Use} · TAKE THE SHIELDED BATTERY" : !game.Outpost.Rooms.walls.Last().broken ? $"{DeviceControls.Fire} · PUNCH THE MARKED ENTRANCE" : $"{DeviceControls.Fire} · PUNCH THE MIND AND ITS GUARDS"
            : game.Stage == 3 ? $"{DeviceControls.Key("B", "LT + DOWN", "BUILD", "WRIST BUILD")} · SEED A FACTORY · YOUR ROBOTS WILL FINISH THE WAR"
            : game.Stage == 4 ? $"{DeviceControls.Key("SPACE", "RB", "LIFT", "LEFT A")} · CLIMB TO THE SHIP · {DeviceControls.Key("T", "D-PAD RIGHT", "RECALL", "RIGHT STICK")} · CALL IT TO YOU"
            : game.Stage > 4 ? game.Outpost.OuterLeft > 0 ? $"DESTROY THE OUTER DEFENCES · {game.Outpost.OuterLeft} TURRETS / AIRCRAFT LEFT" : ""
            : phase < 2 ? "COMPANION LINK · LISTEN · OPTICS AND LEGS REPAIRING"
            : phase == 2 ? $"{DeviceControls.Look} · LOOK AROUND · LEGS STILL REPAIRING"
            : phase == 3 ? $"{DeviceControls.Move} · WALK A FEW STEPS" : $"{DeviceControls.Fire} · PUNCH THE GROUND FIGHTER";
        public bool Locked => game.Stage == Campaign.Stranger && phase < 4;
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
            if (phase == 1 && elapsed > 10 && !game.Speech.Speaking)
            {
                phase = 2; elapsed = 0;
                game.Hermit.Remark("Optics restored. Look around. Your legs are still locked while I repair their power supply.");
            }
            if (phase == 2)
            {
                looked += c.look.magnitude;
                if (elapsed > 5 && (looked > 45 || DeviceControls.VR))
                {
                    phase = 3; start = game.Player.Position;
                    game.Hermit.Remark("Leg power restored. Walk a few steps. Only your emergency fists work until we recover a power cell.");
                }
            }
            if (phase == 3 && math.distance(start, game.Player.Position) > 3 && !game.Speech.Speaking)
            {
                phase = 4;
                float3 point = game.Player.Position + game.Player.walker.forward * 18;
                float3 up = math.normalize(point);
                game.Battle.Add(FoeKind.Sentry, up * (game.System.ReferenceBody.GroundRadius(up) + 0.9f));
                game.Hermit.Remark("One ground fighter. Get close and punch it. I will guide you to the burrow when it is down.");
            }
            if (phase < 3) { c.move = Vector2.zero; c.jump = false; }
            if (phase < 2) c.look = Vector2.zero;
            if (phase < 4) { c.interfaceActive = true; c.fire = c.talk = c.interact = c.travel = c.ride = false; }
            if (phase == 4 && !game.Battle.Foes.Exists(foe => foe.kind == FoeKind.Sentry && foe.health > 0)) game.Advance();
        }
    }
}
