# Development guide

## Toolchain (no Visual Studio needed)
`build.bat` uses the C# compiler that ships with Windows, `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. It compiles against the **game's own** assemblies in `<game>\mywintercar_Data\Managed\`:

- `mscorlib.dll`, `System.dll`, `System.Core.dll` (the Mono 2.0 / .NET 3.5 profile: `/nostdlib+ /noconfig`)
- `UnityEngine.dll`, `PlayMaker.dll`, `Assembly-CSharp.dll`, `cInput.dll`
- `0Harmony.dll` (Harmony 1.2.0.1), `MSCLoader.dll`

### Language limits: C# 5 (`/langversion:5`) on .NET 3.5
- **Syntax:** no `=>` members, `?.`, `$""`, `nameof`, auto-property initializers or `static using`.
- **APIs:** no `string.IsNullOrWhiteSpace`, `Tuple`, `Task`, `HashSet` (that one is in System.Core 3.5, which is fine), or `WaitHandle.WaitOne(int)`. Use `WaitOne(int, bool)`.
- **Unity 5.0 API:** `Cursor.visible` / `Cursor.lockState` exist; `Application.isFocused` doesn't, so use `OnApplicationFocus`.

## Build
1. Put the repo folder **inside the game folder**: `<game>\DualSensePad_src\`. Or set the environment variable `MWC_DIR` to the game folder.
2. Double-click `build.bat`. The output goes to `bin\DualSensePad.dll` and is copied to `<game>\Mods\`.
3. The compiler messages are in `build_log.txt`. They use the console's OEM code page, which is not UTF-8 on non-English Windows (e.g. cp866 for Cyrillic).

The copy into `Mods\` fails while the game is running, because the DLL is locked. Quit the game first.

## Test loop
1. Build, start the game, and open *Mods → DualSense Pad*. The first line shows the controller status.
2. Turn on **Show debug readout**. It's an OnGUI panel showing:
   - context, rpm vs learned redline (and the car's `maxPowerRPM` / `maxRPM`), gear, ratio, clutch engagement;
   - driven tyre speed vs ground speed and the SPIN / SHIFT UP / STALL flags;
   - `car gets:` the inputs the CarController actually received;
   - `pad sends:` the actions the mod injects this frame, plus raw L2/R2;
   - `game bindings:` the cInput bindings for clutch, throttle, brake and handbrake;
   - motor levels and the current trigger modes.
3. Check the logs:
   - `<game>\output_log.txt`: Unity and MSCLoader log. Mod messages are prefixed `[DualSensePad]`, e.g. `linked 62/62 game actions` or `connected: DualSense (Bluetooth)`.
   - MSCLoader console (in game): the same messages.
4. Before blaming the mod for vehicle behaviour, **reproduce it with `Mods\DualSensePad.dll` removed and the keyboard only**.

## Reverse-engineering the game
Use AssetRipper to export the game into `<game>\assetRipper\`. You get decompiled scripts under `ExportedProject\Assets\Scripts\<Assembly>\*.cs` and scenes as YAML under `Assets\_Scenes\` (`GAME.unity` is about 240 MB).

Useful searches:
- **Script usage:** `grep -rl "cInput\." Scripts/Assembly-CSharp`.
- **PlayMaker FSM strings:** each FSM state has `actionNames`, and `fsmStringParams` hold the arguments in order. That is how the cInput action list in `GAME_INTERNALS.md` was extracted from the "Setup" FSM.
- **Serialized component values:** grep the scene for field names, e.g. `^  maxRPM:`.

Field-level facts are collected in [GAME_INTERNALS.md](GAME_INTERNALS.md).

## Adding things

### A new assignable action
1. Add the cInput action name to `CInputBridge.Actions`, if it's a game action.
2. **Append** it to the end of both `Layout.Ids` and `Layout.Names`. The settings dropdowns persist the **index**, so inserting in the middle reshuffles users' saved layouts.
3. Handle it in `PadBehaviour.ApplyButton` if it's special (ids starting with `#`).

### A new feedback effect
1. Compute it in `PadBehaviour.UpdateFeedback` from telemetry; add to `big` / `small` (0..1) or choose an `r2` / `l2` effect.
2. Give it a slider in `DualSensePadMod.Mod_Settings` and read it live with `DualSensePadMod.F(slider, default)`.
3. Add its value or flag to the debug readout.

### Trigger effects
Use `OutputBuilder.TriggerOff / TriggerFeedback(start, strength) / TriggerFeedback(int[10]) / TriggerVibration(start, amplitude, hz)`. Changes of trigger mode are sent immediately; motor changes are rate-limited to about 30 Hz.

## Release checklist
- Bump `Version` in `DualSensePadMod.cs` and `AssemblyVersion`.
- Update `CHANGELOG.md`.
- Build, then test on USB **and** Bluetooth, on foot, in a car and in menus, and with Steam Input disabled.
- Attach `bin\DualSensePad.dll` to the GitHub release.
- Keep the AI disclosure in `Description` and in `[assembly: AssemblyTrademark]`. MSCLoader asks for it.

## Repo layout
```
build.bat            Windows build script (csc.exe + game DLLs)
src/                 mod source (see docs/ARCHITECTURE.md)
docs/                architecture, game internals, DualSense protocol, this guide
AGENTS.md            instructions for AI coding agents (CLAUDE.md points here)
CHANGELOG.md
bin/, build_log.txt  build output (git-ignored)
```
