# Performance evidence and tuning

Run `script/play`, spend several minutes walking, fighting, expanding a factory and flying between worlds, then quit. Run `script/performance`. The script reads the latest real player session from `Logs/frames.log`, excluding title, pause and map samples. Logs remain local and ignored by Git.

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
