using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // A text overlay: where you are, how fast, what there is to find and what the interact key does.
    public class Hud : MonoBehaviour
    {
        [System.NonSerialized] public Game game;
        GUIStyle text, centred, right;
        float fps, scale;

        void Update() => fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);

        void OnGUI()
        {
            text ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, normal = { textColor = new Color(1f, 1f, 1f, 0.9f) } };
            centred ??= new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
            right ??= new GUIStyle(text) { alignment = TextAnchor.UpperRight, fontSize = 22 };
            // lay out for a 1080-line screen and scale to whatever we have
            scale = Screen.height / 1080f;
            float width = Screen.width / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var player = game.Player;
            var system = game.System;
            var body = system.ReferenceBody;
            float3 up = math.normalize(player.Position);
            float altitude = math.length(player.Position) - math.max(body.GroundRadius(up), body.SeaRadius);
            string craft = player.flying
                ? $"ship  assist {(player.assist ? "on" : "off")}{(player.ship.landed ? "  landed" : "")}"
                : $"on foot  jetpack {Mathf.RoundToInt(100f * player.walker.fuel / Walker.JetpackFuel)}%{(player.walker.swimming ? "  swimming" : "")}";
            GUI.Label(new Rect(20, 14, 900, 120),
                $"{body.name}  system {game.SystemsVisited + 1}  seed {game.Seed}    altitude {altitude:0} m    {math.length(player.Velocity):0} m/s\n{craft}\n" +
                $"<color=#ffffff80>{fps:0} fps  {QualitySettings.names[QualitySettings.GetQualityLevel()]} detail  {system.Bodies[system.Reference].PatchCount} patches</color>", text);
            GUI.Label(new Rect(width - 420, 14, 400, 30), $"relics {game.RelicTotal - game.Relics.Count} / {game.RelicTotal}", right);
            GUI.Label(new Rect(20, 1080 - 64, width - 40, 50),
                "<color=#ffffff80>WASD move  mouse look  Space jump/jetpack/lift  Ctrl descend  Shift sprint/boost  Q/E roll  F interact  Z assist  V view  Tab system  G warp  R new system  [ ] detail</color>", text);

            foreach (var (site, view) in game.Relics)
                Marker(view.position, site.body == system.Reference ? Distance(math.distance(player.Position, site.position)) : "", new Color(0.5f, 1f, 0.85f), "◆");
            for (int i = 0; i < system.Bodies.Length; i++)
                if (i != system.Reference || player.SystemView)
                    Marker(system.Bodies[i].transform.position, Distance(math.distance(player.Position, system.Bodies[i].transform.position)), Color.white, system.Descriptor.bodies[i].name);
            if (!player.flying && math.distance(player.walker.position, player.ship.position) > 20f)
                Marker(player.ship.position, Distance(math.distance(player.walker.position, player.ship.position)), new Color(1f, 0.8f, 0.5f), "▲ ship");

            string prompt = game.CanWarp ? "G  warp to a new system" : game.Relics.Count == 0 ? "warp drive charged: fly up to open space" : player.Prompt;
            if (prompt != null && !player.SystemView) GUI.Label(new Rect(0, 1080 * 0.62f, width, 40), $"<size=24>{prompt}</size>", centred);
            if (!player.flying && !player.SystemView) GUI.Label(new Rect(0, 1080 / 2f - 20, width, 40), "<size=24>·</size>", centred);
        }

        void Marker(Vector3 world, string detail, Color colour, string label)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(world);
            if (screen.z <= 0f) return;
            GUI.color = colour;
            GUI.Label(new Rect(screen.x / scale - 150, (Screen.height - screen.y) / scale - 24, 300, 48), $"{label}\n<size=14>{detail}</size>", centred);
            GUI.color = Color.white;
        }

        static string Distance(float metres) => metres < 1000f ? $"{metres:0} m" : $"{metres / 1000f:0.0} km";
    }
}
