# Offline Festival SLT speech

The owner approved bundling this speech engine and voice. These are the original, unmodified archives:

| File | Upstream source |
|---|---|
| `cmu_us_slt_arctic_hts.tar.gz` | https://www.cstr.ed.ac.uk/downloads/festival/2.1/festvox_cmu_us_slt_arctic_hts.tar.gz |
| `flite-hts-1.02.tar.gz` | https://downloads.sourceforge.net/project/hts-engine/Flite%2Bhts_engine/Flite%2Bhts_engine-1.02/flite%2Bhts_engine-1.02.tar.gz |
| `hts-engine-1.05.tar.gz` | https://downloads.sourceforge.net/project/hts-engine/hts_engine%20API/hts_engine_API-1.05/hts_engine_API-1.05.tar.gz |

`script/build-speech` verifies the archives' SHA-256 hashes before extracting into ignored `Build/Speech`. These older compatible engine versions read the requested Festival voice's separate model and tree files directly. Substituting a newer SLT voice would change its sound. The front end is Flite, not Festival's Scheme interpreter: acoustic model and parameters match, but phrasing can differ from desktop Festival. The whole game uses this same front end on every target.

`bridge.c` is our adapter. It loads the original 32 kHz, 160-sample frame period and 0.45 alpha parameters and returns PCM to Unity. Playback and robot modulation are handled in Unity, so there is no native audio device dependency. Calls are serialised by `Speech.cs` because Flite has shared voice state. The model and licence notices are bundled alongside the native library; nothing is downloaded during play.

One clearly marked patch is applied to the extracted `flite_hts_engine.c`: delay `HTS_Engine_refresh` until the adapter has copied the generated samples. Source archive contents remain unchanged. Compiler options disable native audio and select the reduced Flite front end. macOS builds are universal arm64/x86_64, Windows is x86_64 including Proton and SteamVR, and iOS builds target arm64 devices. iOS simulator builds are not supplied.

`LICENSES.txt` contains upstream notices for Flite, HTS and the voice. Original notices also remain in the archives. The build copies the combined notices into every app's Festival data folder. The optional Windows cross compiler is Zig 0.15.2 from ziglang.org, verified against pinned upstream checksums; it remains under ignored `tmp/speech-tools` and is not shipped in the game.
