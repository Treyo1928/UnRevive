# UnRevive

Play the PCVR build of **Attack on Quest** without the Oculus PC runtime or Revive. UnRevive
runs the game on **OpenXR** (SteamVR, Quest Link / Air Link, Virtual Desktop, ...) and fills
in everything the game expects from the Oculus runtime, so it plays like it does on Oculus.
It works on Windows and on Linux through Proton. It's a BepInEx 5 mod.

Tested with a Quest 2 through Steam Link and SteamVR on Linux (Proton).

## What it does

- **Starts VR on OpenXR.** The game only lists the Oculus runtime. UnRevive enables Unity's
  built-in OpenVR support and gives it [OpenComposite](https://gitlab.com/znixian/OpenOVR/-/tree/openxr),
  which runs OpenVR on whichever OpenXR runtime is active.
- **Controls like on Oculus.** Every Touch button, trigger, grip, stick, touch and near-touch
  state is mapped the way the Oculus runtime reports it, through SteamVR Input (rebindable in
  SteamVR's Controller Bindings). Other controllers (Index, Pico, ...) use the Touch layout.
- **Menu button.** SteamVR keeps the left menu button for its dashboard, so the press that
  opens the dashboard also opens the game menu (on your left hand), like Beat Saber pausing.
  On runtimes that pass the button through, hold it to open the menu.
- **Height and hand pose like on Oculus.** Your head height when the game starts becomes your
  in-game eye height (1.675 m, the Oculus default), and the hands/blades get the Oculus
  controller angle instead of the runtime's raw one.
- **Recenter** from the mod menu (or by holding the menu button, where it gets through).
- **Haptics.** Blade vibration plays on any runtime.
- **Freeze fixes.** Fixes two hangs that only happen without the Oculus runtime: the black
  screen after the Unity logo (haptics setup) and the menu avatar's microphone wait.

## Install

You need the PCVR build of Attack on Quest and
[BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases) (`win_x64`).

1. Extract BepInEx into the game folder, next to `AttackOnQuest.exe`.
2. Extract the `UnRevive-<version>.zip` `BepInEx` folder over the game's `BepInEx` folder.
3. Optional: [AoQ Mod Manager](https://github.com/Treyo1928/AoQPCVRModManager/releases) to change
   the settings in-game. UnRevive's recenter is a button in its menu.

### Steam (Windows)

1. Steam → *Games* → *Add a Non-Steam Game to My Library* → browse to `AttackOnQuest.exe`.
2. Pick the OpenXR runtime for your headset:
   - **SteamVR / Steam Link:** SteamVR → Settings → OpenXR → *Set SteamVR as OpenXR Runtime*.
     Start SteamVR first.
   - **Quest Link / Air Link:** Meta Quest Link app → Settings → General → OpenXR Runtime →
     *Set Meta Quest Link as active*. SteamVR isn't needed.
   - **Virtual Desktop:** use VDXR (the default OpenXR runtime in Virtual Desktop).
3. Launch the game from Steam.

OpenComposite needs the [Microsoft Visual C++ x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe)
(most PCs already have it).

### Steam (Linux / Proton)

1. Steam → *Add a Non-Steam Game* → `AttackOnQuest.exe`.
2. Properties → Compatibility → force a Proton version (tested: GE-Proton 10).
3. Properties → Launch Options: `WINEDLLOVERRIDES="winhttp=n,b" %command%` (loads BepInEx).
4. **Start SteamVR first**, then launch the game from Steam. Proton only connects the game to
   VR when SteamVR is already running and the game is launched by Steam.

## Settings

`BepInEx/config/aoq.unrevive.cfg`, or in-game through AoQ Mod Manager.

| Setting | Default | |
|---|---|---|
| `Tracking > RecenterNow` | off | A button: press it while standing normally. The salute ring fills for a second, then your current height and position become the starting point. |
| `Tracking > RealHeight` | off | Off: your head height at start/recenter becomes `EyeHeight`, like on Oculus. On: your real height above the runtime's floor (only right if that floor is set right). |
| `Tracking > EyeHeight` | 1.675 | In-game eye height in metres when `RealHeight` is off. |
| `Input > ControllerPitch` | 40 | Degrees the hands/blades are tilted down from the raw controller pose. Raise if blades point up, lower if down. |
| `Input > MenuOnDashboard` | on | Open the game menu when the SteamVR dashboard opens (a menu button press). |
| `Input > DashboardClosesMenu` | off | Also close the game menu on that press if it's open. Off: close it with the laser. |
| `Runtime > UseOpenXR` | on | Run through OpenXR (OpenComposite). Off: Valve's OpenVR, which needs SteamVR. Restart to apply. |

## Uninstall

Delete `BepInEx/patchers/UnRevive` and `BepInEx/plugins/UnRevive`, delete
`AttackOnQuest_Data/Plugins/x86_64/openvr_api.dll`, and rename
`AttackOnQuest_Data/globalgamemanagers.unrevive-backup` back to `globalgamemanagers`.

## Building

Needs `msbuild` (Mono on Linux, Visual Studio on Windows) and a PCVR install with BepInEx, to
copy the reference DLLs into `Libraries/`:

```bash
AOQ_GAME_DIR=/path/to/AoQ bash build.sh            # -> dist/UnRevive-<version>.zip
AOQ_GAME_DIR=/path/to/AoQ bash build.sh --deploy   # ...and install it there
```

## Licenses

`Native/openvr_api.dll` is Valve's OpenVR SDK v2.5.1 (BSD, `Native/LICENSE-openvr.txt`).
`Native/OpenComposite/openvr_api.dll` is an unmodified official OpenComposite build (GPLv3,
`Native/OpenComposite/LICENSE-OpenComposite.txt`; source and build details in
`Native/OpenComposite/README-OpenComposite.txt`).
