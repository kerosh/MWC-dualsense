# Architecture

```
            ┌───────────── reader thread ─────────────┐
DualSense ──HID──► DualSenseHid.ReadLoop ─► PadState   │
   ▲              (USB 0x01 / BT 0x31 / BT 0x01 simple)│
   │              └─────────────────────────────────────┘
   │                               │ GetState() (locked copy)
   │                               ▼
   │      ┌──────────────── PadBehaviour.Update (main thread) ──────────────┐
   │      │ context: Menu / Foot / Car                                      │
   │      │  ├─ buttons → Layout targets ──► CInputBridge.Set/Press (actions)│
   │      │  │                          └──► NativeInput (mouse/keys/wheel)  │
   │      │  ├─ sticks/triggers → walk / steer / throttle / brake (analog)   │
   │      │  └─ UpdateFeedback: car telemetry → rumble, trigger effects, LED │
   │      └─────────────────────────────────────────────────────────────────┘
   │                               │ SetOutput(47-byte block)
   │      ┌──────── writer thread ─▼───────┐
   └──HID─┤ DualSenseHid.WriteLoop          │  (USB 0x02 / BT 0x31 + CRC32)
          └─────────────────────────────────┘

cInput.Update() → _CheckInputs()  ──Harmony postfix──►  CInputBridge.Postfix()
   (keyboard state computed)                         merges pad values into cInput's arrays
cInput._GetCalibratedAxisInput("Joy…") ──Harmony prefix──► 0 while the mod drives the pad
```

## Files (`src/`)

| File | Responsibility |
|---|---|
| `DualSensePadMod.cs` | The MSCLoader `Mod`: metadata, all settings (static fields read live every frame), lifecycle (`OnMenuLoad`, `OnLoad`, `OnModEnabled`/`Disabled`). Creates one persistent `PadBehaviour` GameObject (`DontDestroyOnLoad`). |
| `PadBehaviour.cs` | Per-frame logic: context detection, button mapping, analog driving and walking, mouse emulation, auto-clutch, run toggle, the feedback model (rumble, triggers, lightbar) and the debug OnGUI panel. |
| `CInputBridge.cs` | Harmony patches on the game's input library **cInput**. The pad becomes "just another key" for every game script and PlayMaker FSM. |
| `DualSenseHid.cs` | Raw HID device: discovery, input parsing, output report building, CRC32, adaptive-trigger effect encoders (`OutputBuilder`). |
| `NativeInput.cs` | `SendInput` wrappers: relative mouse move, buttons, wheel, keys. |
| `Layout.cs` | Catalogue of assignable targets (dropdown order is persisted by index, so **append only**), and the default on-foot and vehicle layouts. |

## Key design decisions

### 1. Inject at the cInput layer, not the car
Every game control goes through `cInput` named actions (`ThrottleOn`, `Use`, `ShiftUp`, …), including PlayMaker FSMs. Each frame, `cInput._CheckInputs()` fills static arrays:
- `_getKeyArray`, `_getKeyDownArray`, `_getKeyUpArray` (per input)
- `_getAxis` (smoothed) and `_getAxisRaw` (per input)
- `_getAxisArray` / `_getAxisArrayRaw` (per axis, built from input pairs through `_makeAxis`)

The **postfix** works like this:
- Analog values: `max(keyboard, pad)`.
- Held state: ORed with the keyboard.
- Edges: generated from the pad's own previous state.
- Axes: the per-axis arrays are recomputed afterwards.

Results:
- The keyboard keeps working, because nothing is rebound.
- Every vehicle and FSM sees the pad exactly as it would see a key press. Analog values are allowed.
- The mod injects into the **same input the keyboard key drives**: W drives both `ThrottleOn` and `Up`, so the pad sets both. That keeps axis maths, inversion and normalization identical to the keyboard.

**Timing:** values set in `PadBehaviour.Update` are kept until the next `Update`, so they are applied whichever of the two scripts runs first. That costs at most one frame of latency.

### 2. Mouse look and clicks through SendInput
The camera scripts use `Input.GetAxis("Mouse X")`, and interaction uses `Input.GetMouseButton`. These are Unity internal calls and can't be patched with Harmony on this Mono. So the right stick and the click/wheel/Esc targets produce **real OS input events**, with sub-pixel remainders kept between frames.

Safety rules:
- Events are only sent while the game is focused.
- Everything held is released on focus loss, mod disable, quit, or a context change.

### 3. Own HID access instead of Unity, Steam or XInput
Unity 5's legacy input has no rumble, triggers or lightbar. XInput through Steam would give rumble only. Direct HID gives everything over both USB and BT.
- **Two handles** to the same device path: a synchronous `ReadFile` blocks, and Windows serializes synchronous I/O per handle, so reading and writing need separate handles.
- **Reader thread:** blocking reads, then parse into a `PadState` under a lock. It reconnects automatically every 1.5 s.
- **Writer thread:** sends the latest pending output when it changes, plus a keep-alive at least once a second.
- **Main thread:** rate-limits output to about 30 reports/s. Trigger-mode changes and "motors off" are always sent immediately.

### 4. Context detection
```
Menu  = PlayMaker global bool PlayerInMenu  OR  (Cursor.visible && lockState == None)
Car   = PlayMaker global string PlayerCurrentVehicle != ""
Foot  = otherwise
```
**Vehicle lookup:** the enabled `AxisCarController` nearest the camera, within 8 m, re-checked every second. It caches the `Drivetrain`, `CarDynamics`, `Rigidbody` and `Axles.allWheels`.

### 5. Feedback model (`PadBehaviour.UpdateFeedback`)
Everything here is computed per frame from car telemetry. The motors are 0..1: the big one is low-frequency (left), the small one high-frequency (right).

| Effect | Source | Rule |
|---|---|---|
| Learned redline | `Drivetrain.rpm` | Starts at `maxPowerRPM` (the cars' `maxRPM` of 8000–9000 is never reached). Raised to the highest rpm held with throttle > 0.6, in gear, clutch engaged > 0.9. Stored per car name for the session. |
| Shift-up marker | rpm / redline | Above 0.92, in a forward gear that isn't the top gear: pulses at 4 → 8 Hz, duty 40 %, small motor. × clutch engagement. |
| Lugging | rpm < 0.97 × `minRPM`, in gear, clutch > 0.3 | Big motor kicks at `clamp(rpm/90, 3, 9)` Hz, duty 30 %, stronger the deeper the rpm drops. |
| Stall | rpm crosses 300 downward while loaded (or within 0.6 s of lugging) | 0.18 s thump (big 0.9, small 0.35). |
| Road feel | `CarDynamics.forceFeedback` (mean Mz) | High-pass magnitude → big motor; smoothed magnitude × speed → small motor. |
| Bumps / crashes | `Rigidbody.velocity` per FixedUpdate | Peak acceleration minus a slow baseline minus 12 m/s² → decaying jolt. |
| Sliding | `Wheel.longitudinalSlip/lateralSlip` (normalized, > 1 = beyond peak grip) | Small motor. |
| Surface | `Wheel.surfaceType != track` | Small motor × speed. |
| Wheelspin → soft R2 | driven wheels: `wheelTireVelo` vs `wheelRoadVelo` (m/s) | Spin if the tyre is > max(1.4 m/s, 15 % of ground speed) faster; ends below half of that, 0.15 s hold; in gear, clutch > 0.3, throttle > 0.05. |
| Rev limiter → R2 vibration | rpm / redline > 0.97 with throttle | 45 Hz, start zone 3. |
| Brake pedal | — | Zone 0 free, zones 1–9 strength `1.5 + 0.7·i` (× strength), clamped 1..8. |
| Wheel lock / ABS → L2 vibration | `CarController.ABSTriggered` or a wheel with `longitudinalSlip < −1.3`, brake > 0.15, > 6 km/h | 20 Hz. |
| Lightbar | rpm / redline | Green → red over 0.25..0.9; flashes above 0.97 in gear; amber when the engine is off. |

### 6. The game's own gamepad bindings
Unity also reads the DualSense (DirectInput) and Steam's virtual Xbox pad. While the pad is connected and the setting is on:
- **Axes:** `cInput._GetCalibratedAxisInput` returns 0 for any `Joy…` axis.
- **Buttons:** inputs bound to joystick buttons are recomputed from their keyboard key only.

## Threading rules
- Unity APIs only on the main thread. HID threads touch nothing but byte buffers, `PadState` and handles.
- Locks:
  - `stateLock` protects `PadState`.
  - `ioLock` protects the handles and the pending output buffer.
- Shutdown order: neutral report → stop flag → `CancelIoEx` on the read handle → `Join` → close handles.
