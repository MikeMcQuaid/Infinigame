# Playing on iPhone, iPad and SteamVR

The repository targets Unity **6000.6.9f1**. Install additional modules for that exact editor in Unity Hub, under Installs > its menu > Add modules. This Mac currently has Mac and WebGL support; iOS and Windows support still need installing. Close the editor before running build scripts.

## iPhone and iPad

1. Install **iOS Build Support** in Unity Hub. Install Xcode and its iOS platform support, then launch Xcode once to finish setup.
2. Run `script/build-ios` from this repository. It exports an Xcode project to `Build/iOS`; the log is `Logs/build-ios.log`.
3. Open `Build/iOS/Unity-iPhone.xcodeproj`. In the Unity-iPhone target's Signing & Capabilities, select your Apple team and automatic signing. Change the bundle identifier if your team needs a unique one. The export starts with `games.infinigame.player`.
4. Connect and trust the iPhone or iPad, enable Settings > Privacy & Security > Developer Mode on it, then select it as the destination in Xcode. Choose the Unity-iPhone scheme and Run.
5. Start with the Mobile quality tier. Movement is on the left, looking on the right and the action buttons below. The Robot button opens dialogue and the on-screen keyboard. Companion replies are text on iOS. The Mic button requests microphone and speech permission and uses local British English dictation when installed; typed dialogue remains available.

A personal Apple Account can sign builds for testing on your own devices, with provisioning limits. App Store and TestFlight distribution require Apple Developer Program membership. Unity exports the project; Xcode signs, installs and launches it. See [Unity's iOS build process](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/iphone/ios-building-and-delivering/build-process), [Apple's device setup](https://developer.apple.com/documentation/xcode/building-and-running-an-app) and [Developer Mode](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device).

## SteamVR and Valve Index

1. Add **Windows Build Support (Mono)** to this Unity editor. If building on Windows with IL2CPP instead, install the matching Unity module and Microsoft's C++ build tools. Select Mono as the Windows scripting backend when exporting from this Mac.
2. Run `script/build-windows`. It writes `Build/Windows/Infinigame.exe` and its companion files. The log is `Logs/build-windows.log`. The build configures Unity's official OpenXR loader and Valve Index controller profile.
3. Copy the **whole `Build/Windows` folder** to the Windows PC, including the data folder and DLLs.
4. Install SteamVR on the PC, connect the headset and controllers and complete its room setup. In SteamVR settings, enable advanced settings if necessary, open OpenXR and make SteamVR the current OpenXR runtime.
5. Start SteamVR, then run `Infinigame.exe`. Use a seated position for the cockpit. The left stick moves, the right stick snap-turns, right trigger fires and right grip interacts. Left grip plus left A opens robot dialogue; left grip plus a right-stick click starts or finishes dictation. The README lists the remaining controls.

See [Unity's OpenXR configuration](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.17/manual/project-configuration.html). SteamVR is the PC runtime; installing its Unity Asset Store plugin is unnecessary for this OpenXR build.

## First test devices

The owner will test an iPhone 18 Pro and a 13-inch M4 iPad Air, Wi-Fi, 256 GB. Use Mobile quality initially on both. Record frame rate, memory pressure and heat after sustained combat before increasing the preset. Screen size alone does not determine a safe rendering budget.

## Validation still needed on hardware

The macOS scene tests check the shared game logic and compile the platform code. They cannot establish iPad performance, signing success, headset rendering, controller bindings or VR comfort. Before treating either port as ready, play the opening, punch out, descend into the first burrow, restore power, walk underwater, jetpack to the orbital ship, seed both factories, speak to the companion and test save/reload on the actual device. Run the same checks after changing graphics budgets.

## Checks from the repository

Run `script/test` for the complete core suite. Run `script/test --unity -testFilter Rework` for the new opening, burrow, factories, orbital boarding, bounded companion state and singularity transition. Screenshots are written to `Logs/Screenshots/110-*.png` through `118-*.png`. Run `script/play` for a normal Mac player build with frame logging. Close the editor before these Unity commands.

For hardware testing, use a fresh save slot. Confirm the first frame is black, emergency fists are the only active weapon and the robot is visible after digging out. Test each powered weapon, voice permission denial as well as approval, factory saves and the whiteout transition. Check touch controls at the actual device resolution and headset comfort during lunges and the collapse. Automated Mac tests do not establish device frame rate or VR comfort.
