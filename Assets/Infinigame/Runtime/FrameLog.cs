using System;
using System.Collections.Generic;
using System.IO;
using Infinigame.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // Keeps a record of how fast the game runs while it is played: a line every few seconds with
    // the frame rate, the slowest frames, where the time went and what was on screen. Detail is
    // tuned from this record, not from guesswork. script/play points it at Logs/frames.log.
    public class FrameLog : MonoBehaviour
    {
        // Where to write, and how often. Tests set these; otherwise the command line or a default decides.
        public static string File;
        public static float Every = 5f;
        public static bool Automatic = true;

        [NonSerialized] public Game game;

        readonly List<float> frames = new List<float>(1024);
        readonly FrameTiming[] timing = new FrameTiming[1];
        double main, work, render, gpu;
        int timed;
        float since, resolution = 1;
        int spare;
        float refresh = 60;
        public float Fps { get; private set; }
        public string Status => $"{Fps:0} FPS · LOD {LodSettings.Adaptive:0.00} · RES {ScalableBufferManager.widthScaleFactor:0.00}";
        StreamWriter log;

        // Says whether jobs really are compiled by Burst: the managed half is left out when they are.
        [BurstCompile]
        struct Probe : IJob
        {
            public NativeArray<int> compiled;

            public void Execute()
            {
                compiled[0] = 1;
                Managed(ref compiled);
            }

            [BurstDiscard]
            static void Managed(ref NativeArray<int> compiled) => compiled[0] = 0;
        }

        void Start()
        {
            Camera.main.allowDynamicResolution = true;
            var displays = new List<UnityEngine.XR.XRDisplaySubsystem>(); SubsystemManager.GetSubsystems(displays);
            if (DeviceControls.VR && displays.Count > 0 && displays[0].TryGetDisplayRefreshRate(out float rate)) refresh = rate;
            LodSettings.Adaptive = 1;
            string[] arguments = Environment.GetCommandLineArgs();
            int named = Array.IndexOf(arguments, "-frameLog");
            string path = File ?? (named >= 0 && named + 1 < arguments.Length ? arguments[named + 1]
                : Application.isEditor ? "Logs/frames.log" : Path.Combine(Application.persistentDataPath, "frames.log"));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            log = new StreamWriter(path, true) { AutoFlush = true };

            var flag = new NativeArray<int>(1, Allocator.TempJob);
            new Probe { compiled = flag }.Schedule().Complete();
            var screen = Screen.currentResolution;
            log.WriteLine($"# {DateTime.Now:yyyy-MM-dd HH:mm:ss}  Infinigame {Application.version}  Unity {Application.unityVersion}  {(Application.isEditor ? "editor" : "player")}");
            log.WriteLine($"# {SystemInfo.deviceModel}  {SystemInfo.processorType} x{SystemInfo.processorCount}  {SystemInfo.graphicsDeviceName}  {SystemInfo.operatingSystem}");
            log.WriteLine($"# screen {screen.width}x{screen.height} at {screen.refreshRateRatio.value:0} Hz  vsync every {QualitySettings.vSyncCount}  target {Application.targetFrameRate}  HDR {HDROutputSettings.main.active}  build {Application.buildGUID}  burst {(flag[0] == 1 ? "yes" : "NO")}");
            log.WriteLine("time\tfps\tmedian_ms\tp95_ms\tp99_ms\tworst_ms\tmain_ms\twork_ms\trender_ms\tgpu_ms\tdetail\tpixels\twhere\tdoing\tspeed\tpatches\tplants\tring_cells\tfoes\tdepth\tstage\tmanaged_mb\tcorpses\thdr\tadaptive_lod\tresolution_scale\tactive_corpses");
            flag.Dispose();
        }

        void Update()
        {
            if (!Automatic && (LodSettings.Adaptive != 1 || resolution != 1))
            {
                LodSettings.Adaptive = resolution = 1; spare = 0;
                if (!DeviceControls.VR && SystemInfo.supportsDynamicResolution) ScalableBufferManager.ResizeBuffers(1, 1);
            }
            Fps = Mathf.Lerp(Fps, 1 / Mathf.Max(Time.unscaledDeltaTime, 0.001f), 0.04f);
            frames.Add(Time.unscaledDeltaTime * 1000f);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                main += timing[0].cpuMainThreadFrameTime;
                // what the main thread spent working, not waiting for the screen
                work += timing[0].cpuMainThreadFrameTime - timing[0].cpuMainThreadPresentWaitTime;
                render += timing[0].cpuRenderThreadFrameTime;
                gpu += timing[0].gpuFrameTime;
                timed++;
            }
            if ((since += Time.unscaledDeltaTime) < Every) return;

            frames.Sort();
            float At(float share) => frames[math.min(frames.Count - 1, (int)(share * frames.Count))];
            var player = game.Player;
            var system = game.System;
            var body = system.Bodies[system.Reference];
            string doing = game.Dead ? "dead" : game.Title ? "title" : game.Paused || game.ShowControls || game.Hermit.Talking ? "paused" : player.SystemView ? "map" : !player.flying ? "foot" : player.ship.landed ? "landed" : player.Autopilot ? "autopilot" : "flying";
            int Count<T>(Func<T, int> of) where T : Component => body.TryGetComponent(out T part) ? of(part) : 0;
            log.WriteLine(string.Join("\t",
                Time.realtimeSinceStartup.ToString("0"), (frames.Count / since).ToString("0.0"), At(0.5f).ToString("0.0"), At(0.95f).ToString("0.0"), At(0.99f).ToString("0.0"),
                frames[frames.Count - 1].ToString("0.0"), (main / math.max(timed, 1)).ToString("0.0"), (work / math.max(timed, 1)).ToString("0.0"), (render / math.max(timed, 1)).ToString("0.0"), (gpu / math.max(timed, 1)).ToString("0.0"),
                QualitySettings.names[QualitySettings.GetQualityLevel()], $"{Screen.width}x{Screen.height}", system.ReferenceBody.name, doing, math.length(player.Velocity).ToString("0"),
                body.PatchCount, Count<Scatter>(scatter => scatter.Drawn), Count<RingField>(rings => rings.Drawn), game.Battle.Foes.Count, game.Depth, game.Stage, (GC.GetTotalMemory(false) / 1048576).ToString(), game.Corpses.Count, HDROutputSettings.main.active, LodSettings.Adaptive.ToString("0.00"), ScalableBufferManager.widthScaleFactor.ToString("0.00"), game.Corpses.ActiveCount));
            if (Automatic && !Application.isBatchMode && (doing == "foot" || doing == "flying" || doing == "autopilot"))
            {
                float budget = 1000 / refresh;
                float median = At(0.5f), cpu = (float)(work / math.max(timed, 1)), graphics = (float)(gpu / math.max(timed, 1));
                // Median windows reject isolated generation stalls; recovery needs fifteen quiet seconds.
                if (median > budget * 1.15f)
                {
                    spare = 0;
                    if (cpu > budget * 0.9f) LodSettings.Adaptive = Mathf.Max(0.65f, LodSettings.Adaptive - 0.05f);
                    if (graphics > budget * 0.9f) resolution = Mathf.Max(0.7f, resolution - 0.05f);
                }
                else if (cpu > 0 && cpu < budget * 0.72f && graphics > 0 && graphics < budget * 0.72f && ++spare >= 3)
                {
                    spare = 0; LodSettings.Adaptive = Mathf.Min(1.15f, LodSettings.Adaptive + 0.025f); resolution = Mathf.Min(1, resolution + 0.05f);
                }
                else if (cpu >= budget * 0.72f || graphics >= budget * 0.72f) spare = 0;
                if (!Application.isBatchMode && !DeviceControls.VR && SystemInfo.supportsDynamicResolution) ScalableBufferManager.ResizeBuffers(resolution, resolution);
            }
            frames.Clear();
            main = work = render = gpu = since = timed = 0;
        }

        void OnDestroy() => log?.Dispose();
    }
}
