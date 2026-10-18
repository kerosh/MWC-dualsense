# My Winter Car internals used by this mod

These are reverse-engineered from an AssetRipper export of the game (decompiled `Assembly-CSharp`, `cInput`, `GAME.unity` YAML). Engine: **Unity 5.0.0f4**, Mono 2.0 profile, 64-bit. Loader: MSCLoader 1.3.x / 1.4.x.

## Input: cInput (`Managed\cInput.dll`, global class `cInput`)
Keys and axes are defined at runtime by the PlayMaker FSM **"Setup"** in `GAME.unity`. The state *Setup Driving Keys* / *Setup Player Keys* uses `CInputSetKey`; *Setup axis* uses `CInputSetAxis`.

**Inputs** (action → default key):
- **On foot:** `PlayerLeft/Right/Up/Down` (A/D/W/S), `Jump` (Space), `Run` (LeftShift), `Zoom` (LeftControl), `Use` (F), `Crouch` (C), `ReachLeft/Right` (Q/E), `Hitchhike` (O), `Swear` (N), `Hit` (H), `Push` (J), `Finger` (M), `Urinate` (P), `Drunk` (K), `DrivingMode` (Return), `Smoking` (I), `Watch` (U).
- **Driving:**
  - Movement: `Left/Right/Up/Down` (A/D/W/S), `ThrottleOn` (W), `ThrottleOff`, `BrakeOn` (S), `BrakeOff`, `ClutchOn` (X), `ClutchOff`, `Handbrake` (Space), `Handbrake1`.
  - Engine and gears: `StartEngine`, `ShiftUp` (G), `ShiftDown` (B), `reverse`, `neutral`, `first`…`sixth`, `Range` (R), `Boost` (T), `ACC`, `Ignition`.
  - Lifts: `Lift1Up/Down`, `Lift2Up/Down`.
  - Lights and wipers: `LightsOff`, `LightsPark`, `LightsDrive` (L), `HiBeamToggle` (Comma), `HiBeamFlash`, `Wipers` (Slash), `Washer`.
  - Indicators: `IndicatorLeft/Right` (arrows), `IndicatorReturn`.

**Axes** (`SetAxis(description, negativeInput, positiveInput)`, value = positive − negative):
- `PlayerVertical` (PlayerDown, PlayerUp)
- `PlayerHorizontal` (PlayerLeft, PlayerRight)
- `Horizontal` (Left, Right)
- `Vertical` (Up, Down)
- `Throttle` (ThrottleOff, ThrottleOn)
- `Brake` (BrakeOff, BrakeOn)
- `Clutch` (ClutchOff, ClutchOn)
- `Handbrake` (Handbrake, Handbrake1)

**Per-frame pipeline:**
1. `cInput.Update()` → private `_CheckInputs()`.
2. It writes these static arrays, indexed by input:
   - `bool[] _getKeyArray, _getKeyDownArray, _getKeyUpArray`
   - `float[] _getAxis` (smoothed by sensitivity/gravity), `_getAxisRaw`
3. Then, per axis `k`: `_getAxisArray[k] = _getAxis[_makeAxis[k,1]] - _getAxis[_makeAxis[k,0]]`; the raw array works the same way. `_makeAxis[k,1] == -1` means a single-input axis.
4. Getters (`GetKey`, `GetAxisRaw`, …) only read these arrays. Inversion is applied in the getter.

Other details:
- **Name lookup:** `Dictionary<int,int> _inputNameHash` maps `name.GetHashCode()` to the input index. Axes use `_axisNameHash`.
- **Bindings:**
  - `KeyCode[] _inputPrimary/_inputSecondary`: keys.
  - `string[] _axisPrimary/_axisSecondary`: axis strings such as `"Joy Axis 3+"` and `"Joy1 Axis 4-"`.
  - `_GetCalibratedAxisInput(string)` reads them.
- **Flags:** `_cKeysLoaded` (defaults loaded), `_scanning` (the player is rebinding a key: don't inject then).
- **Storage:** bindings are saved in PlayerPrefs (Windows registry).

## Vehicles: UnityCar 2.x scripts
- **`CarController`** (abstract), **`AxisCarController : CarController`**:
  - `GetInput()` reads `cInput.GetAxisRaw("Throttle"/"Brake"/"Horizontal"/"Handbrake"/"Clutch")` and `GetKeyDown("ShiftUp"/"ShiftDown"/"StartEngine")`.
  - With `drivetrain.shifter` it reads held gear buttons.
  - Flags `normalizeThrottleInput` and friends turn pedal axes from −1..1 into 0..1.
  - Public fields: `throttleInput`, `brakeInput`, `clutchInput`, `handbrakeInput`, `steerInput`, `ABSTriggered`, `TCSTriggered`.
- **`Drivetrain`:**
  - Engine: `rpm`, `minRPM` (≈ 650–680), `maxRPM` (8000–9000, **never reached**), `maxPowerRPM` (5300–7800), `maxTorqueRPM`.
  - Gears: `gear`, `neutral` (= 1), `first`, `firstReverse`, `gearRatios[]` (index 0 = R, 1 = N, 2… = forward), `ratio` (0 in neutral).
  - Other: `clutch : Clutch`, `poweredWheels : Wheel[]`, `revLimiter`, `canStall`, `autoClutch`, `automatic`, `shifter`.
- **`Clutch.GetClutchPosition()`:** 1 = engaged, 0 = pedal fully pressed. The game moves it smoothly.
- **`CarDynamics`:** `forceFeedback` (mean aligning torque Mz of the front wheels; what the game's wheel FFB uses), `velo`, and `enum SurfaceType { track, grass, sand, offroad, oil }`.
- **`Axles.allWheels : Wheel[]`.**
- **`Wheel`:**
  - Contact: `onGroundDown`, `surfaceType`.
  - Slip: `longitudinalSlip` (= slipRatio / idealSlipRatio), `lateralSlip` (= slipAngle / idealSlipAngle). Normalized: above 1 means beyond peak grip.
  - Speeds: `wheelTireVelo` (= angularVelocity × loaded radius, m/s), `wheelRoadVelo` (ground speed along the wheel's forward, m/s), `angularVelocity`, `radius`, `Mz`.
- **`ForceFeedback`** (the game's own, for wheels) sends `cardynamics.forceFeedback × multiplier × factor` to `UnityForceFeedback.dll` (DirectInput).

## PlayMaker globals
- `PlayerInMenu` (bool): the pause/options menu is open.
- `PlayerCurrentVehicle` (string): non-empty while seated in a vehicle.
- Read them with `HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool/FindFsmString`.

## Camera and mouse
`MouseLook`, `SmoothMouseLook` and `MainCamera` use `Input.GetAxis("Mouse X"/"Mouse Y")`. Interactions use `Input.GetMouseButton*` and `Input.GetAxis("Mouse ScrollWheel")`. These are not patchable, hence `SendInput`.

## Gotchas
- **`Setup` name clash:** `Assembly-CSharp` has a global class `Setup`. When referencing Assembly-CSharp, MSCLoader's enum must be written `Mod.Setup.OnLoad` (it is nested: `MSCLoader.Mod.Setup`).
- **Mixed vehicle control:** some vehicles, such as the taxi job car, are partly driven by their own FSM logic. In testing the taxi refused to move even with keyboard only and the mod removed. Always compare against the keyboard with the mod removed before blaming input.
- **Unity's view of the pad:** Unity lists the raw DualSense as `"DualSense Wireless Controller"` and Steam's virtual pad as `"Controller (XBOX 360 For Windows)"` (see `output_log.txt`).
