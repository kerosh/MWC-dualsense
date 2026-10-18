# DualSense Pad — PS5 controller support for My Winter Car

An [MSCLoader](https://github.com/piotrulos/MSCModLoader) mod for **My Winter Car** that adds native Sony DualSense (PS5) support:

- a ready-made gamepad layout, no in-game remapping;
- keyboard and mouse keep working at the same time;
- analog steering, throttle and brake;
- **rumble** in place of wheel force feedback;
- **adaptive trigger** effects and a lightbar RPM indicator.

> AI disclosure: most of the code was written with AI assistance (Claude). The mod says so in its MSCLoader description and its assembly attributes.

---

## Why

The game supports wheels with pedals and force feedback. A DualSense works only through manual remapping, with three problems:

1. You have to remap every action yourself.
2. An action remapped to the gamepad no longer works on the keyboard.
3. There is no vibration and no trigger feedback at all.

This mod fixes all three. It reads the controller directly over USB or Bluetooth and feeds it into the game's input system **on top of** the keyboard.

## Features

### Controls
- **On foot:** analog walking (left stick) and mouse look (right stick). Run is a toggle that ends when you stop moving. The shoulder buttons scroll the mouse wheel, with auto-repeat, which helps when tightening bolts.
- **In a vehicle:**
  - **Steering:** analog, with an adjustable response curve and less steering at high speed.
  - **Throttle and brake:** analog R2 and L2.
  - **Clutch:** on a button.
  - **Shifting:** sequential on R1/L1, with an optional **auto-clutch** (clutch in → shift → ease out).
- **Menus** (cursor visible): both sticks move the cursor. Cross clicks, Circle goes back.
- **Remapping:** every button except steer, throttle and brake can be reassigned in the mod settings, with separate on-foot and vehicle layouts.
- **Keyboard and mouse:** they keep working. Leftover gamepad bindings in the game's own controls menu are ignored (optional).

### Rumble (replaces force feedback)
- **Shift-up marker:** short, sharp pulses from 92% of this car's redline, getting faster up to the limiter. There are no pulses in neutral, reverse or top gear, and they fade with the clutch pedal. The mod learns the redline per car.
- **Lugging and stall:**
  - **Lugging:** soft low "kicks" when the engine is dragged below idle in gear.
  - **Stall:** one hard thump when the engine dies under load. Stopping the engine in neutral gives no thump.
- **Road feel:** a high-pass of the steering self-aligning torque. This is the same signal the game sends to wheels as force feedback.
- **Bumps, crashes and landings:** acceleration spikes.
- **Tyres and surface:** tyre sliding, and a light buzz on gravel or grass.

### Adaptive triggers
- **L2 (brake):** a pedal with a little free play, getting firmer the deeper you press. It judders when the wheels lock.
- **R2 (throttle):** light resistance while the engine runs.
  - **Wheelspin:** goes soft **instantly** when a driven wheel spins (snow, ice). This is detected as the tyre surface moving faster than the ground under it.
  - **Rev limiter:** vibrates at the rev limiter.
- **Lightbar:** green → yellow → red with RPM, flashing at the limiter. It can also be left alone or set to dim white.

## Requirements
- My Winter Car (Steam, Windows).
- MSCLoader 1.3+ (tested with 1.3.1 / 1.4.2). Harmony (`0Harmony.dll`) comes with it.
- A Sony DualSense controller. The DualSense Edge is detected but untested.

## Installation

### From a release
Copy `DualSensePad.dll` into `<game>\Mods\`.

### From source
See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). In short: put the repo folder inside the game folder (or set `MWC_DIR`) and double-click `build.bat`.

### Recommended setup
1. **Steam:** set My Winter Car → Properties → Controller → **Disable Steam Input**. Otherwise Steam also drives rumble and the lightbar, and sends a second, duplicate virtual Xbox pad.
2. **Game controls menu:** set any action you bound to the gamepad back to keyboard keys. Or keep the setting *Ignore gamepad bindings from the game's own controls menu* on (the default).
3. **Connection:** connect the controller over USB or Bluetooth. The mod settings show whether it was found.

## Default layout

| Button | On foot | In a vehicle |
|---|---|---|
| Left stick | walk | steer |
| Right stick | look | look |
| R2 | left click (grab / use) | throttle (analog) |
| L2 | right click | brake (analog) |
| Cross | Jump | Clutch |
| Circle | Crouch | left click (key, dashboard) |
| Square | Use (F) | Handbrake |
| Triangle | Driving mode / seat (Enter) | Driving mode / seat |
| R1 / L1 | mouse wheel up / down (repeat) | shift up / down (auto-clutch) |
| L3 | Run (toggle) | high beam toggle |
| R3 | Zoom | Zoom |
| D-pad ↑ / ↓ | Smoking / Watch | lights / wipers |
| D-pad ← / → | reach left / right | indicators |
| Options | Esc (pause menu) | Esc |
| Create | Hitchhike | Ignition |
| Touchpad click | Swear | Start engine |
| Mic mute | – | right click |

**Menus:** both sticks move the cursor. Cross is left click, Square is right click, Circle and Options are Esc. The D-pad up/down and R1/L1 scroll.

## Settings
All settings are under *MSCLoader → Mods → DualSense Pad*.

| Setting | Default | Notes |
|---|---|---|
| Enable DualSense controls | on | |
| Ignore gamepad bindings from the game's own controls menu | on | Blocks the game's `Joy…` axis and button bindings while the mod is active |
| Look sensitivity | 1.00 | |
| Invert look up/down | off | |
| Stick deadzone | 0.10 | |
| Steering curve | 1.6 | 1 = linear |
| Less steering at high speed | 35 % | Applies from 20 to 120 km/h |
| Auto-clutch when shifting with the pad | on | |
| Rumble | on | |
| Rumble strength | 1.00 | |
| Shift-up marker | 0.40 | |
| Engine lugging / stall kick | 1.00 | |
| Road / steering feel | 1.00 | |
| Adaptive triggers | on | |
| Trigger effect strength | 0.80 | |
| Show debug readout | off | On-screen panel with rpm, redline, clutch, tyre vs ground speed, car inputs, bindings and motor levels |
| Lightbar | Engine RPM colour | |
| Buttons – on foot / in a vehicle | see the layout table | One dropdown per button, plus *Reset button layout to defaults* |

## Troubleshooting
- **"Controller: not found":**
  - Check the controller is connected in Windows.
  - Close apps that hold it exclusively, such as DS4Windows in exclusive mode or DSX.
  - Over Bluetooth, re-pair it if needed.
- **Double input, or rumble and lightbar flickering:** Steam Input is still on for the game.
- **The car revs but won't move:** turn on the debug readout and check the `car gets:` line (clutch/brake/handbrake), the `pad sends:` line and the `game bindings:` line. Compare with the mod removed: if it happens without the mod too, it's a game or car issue. For example, the taxi job car behaved like this in testing.
- **Strength of effects:** every effect has a slider. Values were tuned on one Bluetooth DualSense, so tell us what feels off.

## Known limitations
- Mouse look and clicks are synthesized as real Windows mouse events, because the game reads the raw mouse. They are only sent while the game window has focus.
- Menu detection is a heuristic: PlayMaker `PlayerInMenu`, or the cursor being visible and unlocked.
- The road-feel and slip thresholds are first guesses and need more tuning across cars and surfaces.
- The gyro, touchpad surface, speaker and microphone are not used.

## Credits
- Author: hotkeros
- The DualSense HID report layout follows the public reverse-engineering work in the Linux `hid-playstation` driver, DS4Windows and Nielk1's trigger-effect notes.
- Built on MSCLoader, Harmony and the game's own cInput / UnityCar scripts.

## License
[MIT](LICENSE)
