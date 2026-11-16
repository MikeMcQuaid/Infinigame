# Installing on iPhone, iPad, Windows and Steam Deck

Use Unity **6000.6.9f1**. Close the editor before building. This Mac has the iOS and Windows support modules installed. Build output stays in the ignored `Build/` directory. Each script prints progress and its final installation instructions. In a sandbox without Burst support, the scripts say so; rebuild from your normal Mac account before performance testing or choosing graphics settings. Missing-Burst caches are cleared for every target.

## iPhone and iPad

1. Connect the unlocked device by USB. Accept **Trust this computer** on the device. Enable **Settings > Privacy & Security > Developer Mode**, restart if asked and confirm it after restarting.
2. Open Xcode > Settings > Accounts and add your Apple Account. A personal team works for development on your own devices, subject to Apple's provisioning limits.
3. In a terminal at this repository, run `./script/build-ios`. It exports Unity, then runs **xcodebuild** to compile and link the actual device app. Logs are `Logs/build-ios.log` and `Logs/build-ios-xcode.log`.
4. The script reuses the signing team from your existing `Build/iOS/Unity-iPhone.xcodeproj`. For a clean checkout, supply `INFINIGAME_APPLE_TEAM=YOUR_TEAM_ID ./script/build-ios`, or use Xcode to choose your team first. Without a team it performs an unsigned compile, which cannot yet be installed. Use `./script/build-ios --unsigned` explicitly to check compilation on an account without Apple signing credentials.
5. Open `Build/iOS/Unity-iPhone.xcodeproj`. Select **Unity-iPhone** in the scheme selector, then your connected iPhone or iPad as the destination, rather than a simulator. Under the Unity-iPhone target's **Signing & Capabilities**, select your team and leave **Automatically manage signing** enabled. The bundle identifier is **com.mikemcquaid.infinigame**.
6. Choose **Product > Run**, or press **Cmd-R**. Xcode installs and launches the game. For a device-signed build already produced by the script, use **Window > Devices and Simulators > your device > Installed Apps > +**, then select the `.app` in `Build/iOSDerived/Build/Products/Release-iphoneos/`.
7. If the device asks to trust your developer profile, use **Settings > General > VPN & Device Management**. Start with Mobile detail and test a sustained battle before increasing it.

Automatically manage signing is correct. Accepting Xcode's recommended settings is normally fine. Unity can overwrite changes made only in its exported Xcode project, so persistent export fixes belong in the repository. The exporter now links **SafariServices.framework**, which supplies the previously missing `SFSafariViewController`, as well as the frameworks needed for voice input.

See [Apple's build and run guide](https://developer.apple.com/documentation/xcode/building-and-running-an-app) and [Developer Mode](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device). TestFlight or App Store distribution is separate from these local installation steps.

## Windows and SteamVR

1. Run `./script/build-windows` on this Mac. It builds `Build/Windows/Infinigame.exe`, its data and DLLs, then packages **Build/Infinigame-Windows.zip**. Read `Logs/build-windows.log` if it fails.
2. Copy that zip to the Windows PC by USB, AirDrop alternative or file sharing. Right-click it > **Extract All**. Keep the whole extracted **Windows** folder together. Double-click **Infinigame.exe** to play. Copying only the exe will not work.
3. For VR, install Steam and SteamVR, connect the headset and controllers and complete SteamVR room setup. In **SteamVR Settings > OpenXR**, choose **Set SteamVR as OpenXR Runtime**. Start SteamVR, then the game.
4. Left stick moves, right stick snap-turns and right grip interacts. Fists use the speed of your tracked hand movements, with a short cooldown; the trigger fires other weapons. Left grip plus left A opens robot dialogue. Left grip plus right-stick click starts or ends dictation.

The build uses Unity's official OpenXR loader and Valve Index profile. Other headset bindings require device testing. See [Unity's OpenXR setup](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.17/manual/project-configuration.html).

## Steam Deck

1. Run **`./script/build-steam-deck`**. This makes a Windows desktop build with automatic XR startup disabled and packages **Build/Infinigame-SteamDeck.zip**. No Linux Unity module is required.
2. On Deck, hold Power > **Switch to Desktop**. Copy the zip to **Documents**, then extract it. Keep the entire **SteamDeck** folder.
3. Open Steam > **Games > Add a Non-Steam Game to My Library > Browse**. Change the file filter to all files if needed and choose **Documents/SteamDeck/Infinigame.exe**.
4. In that entry's **Properties > Compatibility**, enable **Force the use of a specific Steam Play compatibility tool**, then select **Proton Experimental**. Return to Gaming Mode and launch it.
5. Choose a keyboard-and-mouse Steam Input template: left stick WASD, right trackpad mouse, right trigger left click, left trigger right click, A Space, B F, X R, Y V, a rear button Tab, another T, another B and Menu Escape. Add Return for robot chat. Start at **1280×800, Mobile detail**.

This route uses Proton to run the Windows player. It is a local sideload, not a Steam store release or a Deck Verified build. Native Deck dictation is not implemented; use the on-screen keyboard (Steam + X) for robot dialogue. Valve documents [adding a non-Steam game](https://help.steampowered.com/en/faqs/view/671A-4453-E8D2-323C) and [forcing Proton compatibility](https://help.steampowered.com/en/faqs/view/7DD4-C618-182E-0E49).

## HDR and hardware checks

HDR output starts automatically when Unity and the active display support it, with SDR fallback. Windows must have HDR enabled in its display settings. The neutral tone mapper uses detected display brightness and paper white. Mac Metal and iOS 16+ are supported by Unity; a particular panel still needs HDR capability. Steam Deck HDR through Proton and the OLED panel needs an actual device check. See [Unity's HDR compatibility](https://docs.unity.com/en-us/engine/6000.5/manual/materials-and-shaders/graphics-color/hdr/hdr-in-urp/hdr-output).

The first intended mobile devices are the owner's iPhone 18 Pro and 13-inch M4 iPad Air. Automated Mac tests cannot establish their frame rate, heat, signing success or headset comfort. On each device, test the buried opening, motion/touch punches, the first burrow, ship rendezvous, factory upgrades, voice permission denial, save deletion and the ending. HDR brightness needs inspection on a real HDR display.

For Mac performance testing, run `./script/play`, play normally for several minutes, quit and run `./script/performance`. It summarises the latest player session in `Logs/frames.log` and excludes menus. See [PERFORMANCE.md](PERFORMANCE.md).

## Build verification, 16 November 2026

The macOS, Windows/OpenXR, Steam Deck and unsigned iOS scripts all completed successfully. Xcode linked the iOS app with SafariServices, Speech and AVFoundation; the previous missing-symbol failure is resolved. The Mac app signature and both zip archives verified. Signing could not run under the sandbox account because it has no Apple Account or provisioning profile; the exported project retains the owner's local team. The normal account must sign for installation.

All 580 core tests pass, alongside targeted Unity EditMode and gameplay checks for the opening, burrow, shields, charged rail, retained remains, menus, save deletion, factories, ship recovery, station navigation and ending. These are build and automated scene checks, not hardware certification. HDR, VR motion punches, mobile dictation and Deck frame rate remain device checks.
