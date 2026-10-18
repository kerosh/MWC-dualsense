# AGENTS.md — instructions for AI coding agents

This is **DualSense Pad**, an MSCLoader mod for *My Winter Car* that adds native PS5 DualSense support: controls, rumble, adaptive triggers and the lightbar. Read `README.md` for what it does and `docs/ARCHITECTURE.md` for how. `docs/GAME_INTERNALS.md` and `docs/DUALSENSE_PROTOCOL.md` hold the reverse-engineered facts; trust them over guesses.

## Hard constraints
- **C# 5 on .NET 3.5 / Mono 2.0, Unity 5.0.0f4.**
  - No `=>` bodies, `?.`, `$""`, `nameof` or auto-property initializers.
  - No .NET 4 APIs.
  - Use `WaitOne(ms, false)`.
  - Watch for duplicate local names in nested scopes (CS0136). `UpdateFeedback` is long.
- **The only build path is `build.bat`** (Windows `csc.exe` against the game DLLs). There's no compiler on Linux or macOS sandboxes, so review code carefully and ask the user to build. Paste-ready fixes come from `build_log.txt` errors; the messages may be localized.
- **Name clash:** `Assembly-CSharp` defines a global `Setup` class. Write `Mod.Setup.X` in `SetupFunction(...)`. Use `MSCLoader.Game` for `SupportedGames`.
- **Threads:** Unity API on the main thread only. HID threads handle bytes, `PadState` and handles under `stateLock` / `ioLock`.
- **`Layout.Ids` / `Layout.Names` are persisted by index.** Append only, and keep both arrays the same length.
- **Keep the AI disclosure** in the mod `Description` and in `[assembly: AssemblyTrademark("AI generated code (Claude)")]`.

## How the pieces fit (short)
- **`CInputBridge`:** a Harmony postfix on `cInput._CheckInputs` merges pad values into cInput's static arrays by action name, so the keyboard keeps working. A prefix on `_GetCalibratedAxisInput` zeroes the game's own `Joy…` bindings while the mod is active.
- **`NativeInput`:** `SendInput` for mouse look, clicks, wheel and Esc. The game reads the raw mouse through unpatchable Unity icalls. Only sent while focused; everything held is released on focus loss or a context switch.
- **`DualSenseHid`:** raw HID on two handles (read and write), with a reader thread and a writer thread. Handles USB `0x01`/`0x02`, BT `0x31` with CRC32 (seed `0xA2`), and the BT feature `0x05` switch to full mode.
- **`PadBehaviour`:** context (Menu / Foot / Car), mapping, analog driving, auto-clutch, the feedback model and the debug OnGUI.

## Working conventions
- **Settings:** stored as static fields in `DualSensePadMod` and read live every frame with `DualSensePadMod.F(...)` / `B(...)`. Every new effect gets a slider or toggle plus a debug readout line.
- **Injection:** inject into the *same cInput input the keyboard key drives*. Throttle sets both `ThrottleOn` and `Up`, because W is bound to both. Don't patch car classes for input.
- **Telemetry:** prefer real physical signals over normalized game values. Example: wheelspin is `wheelTireVelo` vs `wheelRoadVelo`, not `longitudinalSlip`. Redline is learned from rpm, because the cars' `maxRPM` is never reached.
- **Feedback design** (owner preferences):
  - Effects should *mean* something: a shift-up marker, not constant engine buzz.
  - No vibration in neutral; fade smoothly with clutch engagement.
  - The gas pedal goes soft on wheelspin.
  - Stall gives low kicks, then one thump.
- **Debugging vehicle problems:** first ask for a repro **with the mod removed and the keyboard only**. Some cars, like the taxi job car, misbehave on their own.

## Owner preferences
- Hotkeys for mods by this author default to **Ctrl + Numpad** (in-game letters are taken). This mod has none.
- The owner tests in game and reports back, and builds by double-clicking `build.bat`. Give short, concrete explanations of what changed and what to look at in the debug readout.

## Useful paths
- Game: `<game folder>`, typically `<Steam library>\steamapps\common\My Winter Car`. The repo is usually cloned inside it, e.g. `<game folder>\DualSensePad_src\`; otherwise set `MWC_DIR`.
- Decompiled game: `<game>\assetRipper\mywintercar\ExportedProject\Assets\Scripts\…`, scene YAML in `Assets\_Scenes\GAME.unity`.
- Logs: `<game>\output_log.txt`.
