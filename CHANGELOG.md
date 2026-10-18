# Changelog

## 1.0.0 (unreleased)
First version.

### Controls
- Native DualSense over USB and Bluetooth (raw HID), with automatic reconnect.
- Controls are merged into cInput on top of the keyboard: keyboard and mouse keep working, and no in-game remapping is needed.
- Default layouts for on foot and in a vehicle, every button remappable in settings. Menu mode: the sticks move the cursor.
- Analog steering with a curve and speed sensitivity; analog throttle and brake; clutch button; sequential shifting with auto-clutch.
- Option to ignore the game's own gamepad bindings while the mod is active.

### Feedback
- Rumble:
  - shift-up marker near the learned per-car redline (none in neutral or top gear, fades with the clutch);
  - lugging kicks and a stall thump;
  - road feel from the steering aligning torque (the wheel FFB signal);
  - bumps and crashes, tyre slide, rough surfaces.
- Adaptive triggers:
  - progressive brake pedal, juddering on wheel lock;
  - light throttle resistance that goes soft instantly on wheelspin (tyre vs ground speed);
  - throttle vibration at the rev limiter.
- Lightbar RPM colour.

### Tools
- Debug readout panel.
