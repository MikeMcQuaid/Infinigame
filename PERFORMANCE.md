# Performance evidence and tuning

Run `script/play`, spend several minutes walking, fighting, expanding a factory and flying between worlds, then quit. Run `script/performance`. The script reads the latest real player session from `Logs/frames.log`, excluding title, pause and map samples. Logs remain local and ignored by Git.

## Latest Mac session, 17 November 2026 at 15:01:23

Apple M5 Max, 2992 × 1878 on a 120 Hz display, PC detail, target 60, VSync disabled and **Burst active**. This session confirms the previous half-refresh cap is gone.

| Activity | Samples | FPS | Window p95, ms | CPU work, ms | GPU, ms | Worst frame, ms |
|---|---:|---:|---:|---:|---:|---:|
| On foot | 43 | 50.2 | 33.3 | 18.6 | 7.1 | 1799.2 |
| Flying | 77 | 59.4 | 17.3 | 15.3 | 7.1 | 20556.7 |
| Autopilot | 2 | 59.2 | 16.9 | 15.2 | 5.5 | 49.6 |

Walking remains over the 16.7 ms CPU budget. A 20.56-second Earth flight stall at 589 seconds is the largest concern; a generated-world flight stall took 2.46 seconds and an Earth walking stall took 1.80 seconds. These samples cannot identify the responsible method. They do not justify raising global polygon or terrain budgets.

This revision avoids rebuilding unchanged factories and patrols on mission completion and clones prepared robot models when a factory deploys another unit. These are code-based reductions in repeated work, not a measured explanation or fix for the 20-second stall. Keep Automatic quality enabled and collect another real, Burst-enabled session. If long stalls persist, capture a Unity Profiler recording around mission transitions, factory deployment and world arrival before changing detail budgets. Device log retrieval is in [PLATFORMS.md](PLATFORMS.md#retrieving-iphone-and-ipad-performance-logs).

## Earlier Mac session, 17 November 2026 at 11:14:08

Apple M5 Max, 5120 × 2880 at 60 Hz, PC detail and **Burst active**. The log records `vsync every 2`, despite the 60 FPS application target. The quality preset was applied after the frame limiter and restored half-refresh VSync.

| Activity | Windows | FPS | Window p95, ms | CPU work, ms | GPU, ms |
|---|---:|---:|---:|---:|---:|
| On foot | 32 | 28.5 | 49.5 | 34.0 | 10.9 |
| Flying | 61 | 29.0 | 34.3 | 33.9 | 8.1 |

The largest Earth flight stall was 9.566 seconds; a generated-world stall was 3.466 seconds and an Earth walking stall was 1.933 seconds. These still need profiling if they recur. The roughly 29 FPS result is consistent with the unintended 30 Hz cap. CPU frame timing can include pacing waits, so it does not by itself prove 34 ms of useful CPU work.

The application now sets a fixed 60 FPS target and clears the quality preset's VSync override after every preset change. **Settings > Automatic quality** enables or disables the measured controller below. Turning it off restores the selected preset's normal terrain distance and full rendering resolution. There is no frame-rate picker. VR presentation remains governed by the headset compositor. No global detail budget was raised on the strength of this log; a fresh, matching Burst-enabled play session is needed to measure the improvement.

## Earlier Mac session, 16 November 2026 at 20:03:23

Apple M5 Max, 2992 × 1878, 120 Hz display with every second refresh targeted, PC detail, **Burst active**, HDR inactive. Medians of five-second windows:

| Activity | Windows | FPS | Window p95, ms | CPU work, ms | GPU, ms | Worst frame, ms |
|---|---:|---:|---:|---:|---:|---:|
| On foot | 35 | 59.2 | 17.5 | 14.5 | 6.6 | 36.8 |
| Flying | 60 | 59.4 | 17.3 | 14.4 | 5.8 | 1633.3 |
| Autopilot | 2 | 56.9 | 25.2 | 14.3 | 5.9 | 35.6 |

The two largest stalls were near Venus at 133 s and 254 s, about 1.6 s each. A later Luna frame took 533 ms. These logs locate the stalls but do not identify the responsible method. The CPU is much closer to the 16.7 ms frame budget than the GPU, so increasing all mesh budgets would be premature. This session uses a lower resolution than the earlier baseline below; it is not evidence that this new revision is faster.

The live controller uses five-second median frame windows. Sustained CPU pressure reduces terrain subdivision distance and per-frame patch work in small steps; sustained GPU pressure lowers dynamic resolution towards 70%. Fifteen seconds of headroom slowly restores resolution and can increase subdivision distance up to 115% of the selected preset. A single loading spike cannot trigger a reduction. Foliage density and mesh sizes do not change during this adjustment, avoiding mass regeneration. Hardware that does not expose GPU timings keeps full resolution. The map and controls screen show live FPS; logs record adaptive LOD and actual resolution scale. VR keeps compositor-controlled resolution and uses the headset refresh rate for its terrain budget.

Initial suit, machine and world generation now have a visible loading screen between stages. This makes initial work visible; it does not claim to eliminate every synchronous generation stall. Capture a matching real play session after rebuilding with Burst to assess the remaining Venus stalls.

**Burst** is Unity's compiler for the game's terrain and texture jobs, not a play mode, weapon setting or visual effect. [Unity's Burst documentation](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/index.html) explains the native-code compilation. Leave it enabled for performance measurements.

## Mac baseline, 16 November 2026

Session began at 15:43:38, Apple M5 Max, 5120 × 2880 at 60 Hz, PC detail, Burst enabled. These are medians of five-second reporting windows, not a combined per-frame distribution:

| Activity | Windows | FPS | Window p95, ms | CPU work, ms | GPU, ms | Worst frame, ms |
|---|---:|---:|---:|---:|---:|---:|
| On foot | 172 | 57.6 | 17.6 | 15.9 | 9.4 | 1800.0 |
| Flying | 88 | 58.8 | 17.5 | 16.0 | 7.2 | 6849.9 |
| Autopilot | 21 | 59.0 | 17.5 | 15.9 | 7.1 | 133.1 |

Average GPU work leaves some room, but CPU work is already near the 16.7 ms target and large stalls spoil motion. The log does not identify individual method costs. Code inspection found two avoidable sources of work: every robot population change destroyed and recreated all nearby models, and each ship generated a fresh detailed mesh. Units now update incrementally, and ship instances share generated meshes and materials. Enemy wreck sections are prepared during scene loading instead of first death.

The changed build logs its build identifier, mission, managed heap size, corpse count and actual HDR output state alongside existing CPU/GPU timing, terrain, foliage and enemy counts. Paused controls and conversations are excluded. Ragdoll counts are bounded to 16 on Mobile and 40 on PC/VR.

Terrain patch density and global LOD distances have **not** been increased: those CPU measurements do not justify a larger global budget. Improvements concentrate on readable HUDs, persistent detailed wrecks, equipment silhouettes and factory geometry. The before/after performance claim requires another real play session on the changed build; automated scene tests and their non-Burst batch renderer are not a substitute.

Keep resolution, detail, HDR state and activity similar for a comparison. Check the largest stalls as well as FPS. If they persist, capture Unity Profiler markers around model loading, terrain rebuilds and save writes before changing budgets. Device-specific Mobile and VR budgets still require hardware measurements. Builds produced by this sandbox have Burst disabled; use your normal Mac account for `script/play` and device builds when measuring performance. The build scripts clear stale missing-Burst caches so a normal rebuild can restore optimised jobs.
