using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace DualSensePad
{
    public class PadBehaviour : MonoBehaviour
    {
        public static PadBehaviour Instance;

        enum Ctx { Foot, Car, Menu }

        DualSenseHid hid;
        bool focused = true;
        bool wasConnected;
        string lastStatus = "";
        Ctx ctx = Ctx.Foot;

        // synthesized mouse / keyboard state
        readonly bool[] mouseHeld = new bool[3];
        bool escHeld, tabHeld;
        float lookRemX, lookRemY;
        readonly Dictionary<uint, float> wheelRepeat = new Dictionary<uint, float>();

        // pad edges
        uint prevButtons;
        bool l2Digital, r2Digital;

        // run toggle
        bool runLatched;
        float runIdle;

        // auto clutch
        float shiftT = -1f;
        string shiftAction;

        // PlayMaker globals
        FsmString gVehicle;
        FsmBool gInMenu;
        float nextGlobalsTry;

        // current vehicle
        CarController car;
        Drivetrain drivetrain;
        CarDynamics dynamics;
        Rigidbody body;
        Wheel[] wheels;
        float nextCarSearch;
        Vector3 lastVel;
        float accelBase, jolt, ffbSmooth, roadHp;
        float lastOutputTime;
        byte[] lastOutput;

        void Awake()
        {
            Instance = this;
            hid = new DualSenseHid();
            hid.Start();
        }

        float peakAccel;
        CarController redlineCar;
        readonly Dictionary<string, float> redlines = new Dictionary<string, float>();
        float prevRpm, shiftPhase, kickPhase, lugTimer, stallKick;
        bool shiftMarker;
        float learnedRedline = 6000f;
        bool spinning;
        float spinHold;
        float dbgRedline, dbgEngaged, dbgSpinTire, dbgSpinRoad, dbgBig, dbgSmall;
        string dbgR2 = "", dbgL2 = "";

        void OnGUI()
        {
            if (!DualSensePadMod.B(DualSensePadMod.DebugReadout, false)) return;
            string t = "DualSensePad  ctx=" + ctx + "  pad=" + (hid != null && hid.Connected ? hid.DeviceInfo : "none");
            if (ctx == Ctx.Car && car != null && drivetrain != null)
            {
                t += "\nrpm " + drivetrain.rpm.ToString("0") + " / redline " + dbgRedline.ToString("0") +
                     "  (maxPowerRPM " + drivetrain.maxPowerRPM.ToString("0") + ", maxRPM " + drivetrain.maxRPM.ToString("0") + ")" +
                     "\ngear " + drivetrain.gear + "  ratio " + drivetrain.ratio.ToString("0.00") + "  clutch engaged " + dbgEngaged.ToString("0.00") +
                     "\ndriven tyre " + dbgSpinTire.ToString("0") + " km/h vs ground " + dbgSpinRoad.ToString("0") + " km/h" + (spinning ? "  SPIN (R2 soft)" : "") + (shiftMarker ? "  SHIFT UP" : "") + (stallKick > 0f ? "  STALL" : "") +
                     "\ncar gets: throttle " + car.throttleInput.ToString("0.00") + "  brake " + car.brakeInput.ToString("0.00") +
                     "  clutch " + car.clutchInput.ToString("0.00") + "  handbrake " + car.handbrakeInput.ToString("0.00") +
                     "  steer " + car.steerInput.ToString("0.00") +
                     "\npad sends: " + CInputBridge.Describe() + "   pad L2 " + dbgL2Raw.ToString("0.00") + " R2 " + dbgR2Raw.ToString("0.00") +
                     NormFlags() +
                     "\ngame bindings: clutch " + CInputBridge.Binding("ClutchOn") + " | throttle " + CInputBridge.Binding("ThrottleOn") +
                     " | brake " + CInputBridge.Binding("BrakeOn") + " | handbrake " + CInputBridge.Binding("Handbrake") +
                     "\nffb " + (dynamics != null ? dynamics.forceFeedback.ToString("0.0") : "-") +
                     "\nmotors big " + dbgBig.ToString("0.00") + " small " + dbgSmall.ToString("0.00") + "   R2 " + dbgR2 + "  L2 " + dbgL2;
            }
            GUI.color = Color.white;
            GUI.Box(new Rect(10, 10, 760, ctx == Ctx.Car ? 180 : 26), "");
            GUI.Box(new Rect(10, 10, 760, ctx == Ctx.Car ? 180 : 26), "");
            GUI.Label(new Rect(16, 12, 750, 180), t);
        }

        float dbgL2Raw, dbgR2Raw;

        string NormFlags()
        {
            AxisCarController a = car as AxisCarController;
            if (a == null) return "";
            if (!a.normalizeThrottleInput && !a.normalizeBrakesInput && !a.normalizeClutchInput) return "";
            return "\n<color=orange>game pedal mode: normalize throttle " + a.normalizeThrottleInput + " brake " + a.normalizeBrakesInput + " clutch " + a.normalizeClutchInput + "</color>";
        }

        void FixedUpdate()
        {
            if (ctx != Ctx.Car || car == null || body == null) return;
            Vector3 v = body.velocity;
            float a = (v - lastVel).magnitude / Mathf.Max(Time.fixedDeltaTime, 0.001f);
            lastVel = v;
            if (a > peakAccel) peakAccel = a;
        }

        void OnDestroy()
        {
            CInputBridge.BlockGameJoystick = false;
            ReleaseAll();
            if (hid != null) hid.Shutdown();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit()
        {
            ReleaseAll();
            if (hid != null) hid.Shutdown();
        }

        void OnApplicationFocus(bool f)
        {
            focused = f;
            if (!f)
            {
                ReleaseAll();
                CInputBridge.ClearFrame();
                SendOutput(OutputBuilder.Neutral(), true);
            }
        }

        void Update()
        {
            UpdateStatus();
            CInputBridge.ClearFrame();

            PadState s = hid.GetState();
            bool enabled = DualSensePadMod.B(DualSensePadMod.Enabled, true);
            CInputBridge.BlockGameJoystick = s.Connected && enabled && DualSensePadMod.B(DualSensePadMod.IgnoreGamePad, true);
            if (!s.Connected || !enabled || !focused)
            {
                ReleaseAll();
                prevButtons = 0;
                if (s.Connected && !enabled) SendOutput(OutputBuilder.Neutral(), false);
                return;
            }

            // digital L2 / R2 with hysteresis
            l2Digital = s.L2 > (l2Digital ? 0.25f : 0.4f);
            r2Digital = s.R2 > (r2Digital ? 0.25f : 0.4f);
            uint buttons = s.Buttons & ~(Btn.L2 | Btn.R2);
            if (l2Digital) buttons |= Btn.L2;
            if (r2Digital) buttons |= Btn.R2;
            uint pressed = buttons & ~prevButtons;

            ctx = DetectContext();
            if (ctx != Ctx.Car) { car = null; }
            else FindCar();

            bool wantML = false, wantMR = false, wantMM = false, wantEsc = false, wantTab = false;
            float dt = Time.unscaledDeltaTime;
            float dz = DualSensePadMod.F(DualSensePadMod.StickDeadzone, 0.1f);

            if (ctx == Ctx.Menu)
            {
                // both sticks move the cursor
                float mx = s.LX + s.RX, my = s.LY + s.RY;
                Vector2 v = Radial(Mathf.Clamp(mx, -1f, 1f), Mathf.Clamp(my, -1f, 1f), dz, 2f);
                MoveMouse(v.x * 900f * dt, -v.y * 900f * dt);
                wantML = s.Has(Btn.Cross) || r2Digital;
                wantMR = s.Has(Btn.Square);
                wantEsc = s.Has(Btn.Circle) || s.Has(Btn.Options);
                WheelButton(Btn.DUp, buttons, pressed, 1, dt);
                WheelButton(Btn.R1, buttons, pressed, 1, dt);
                WheelButton(Btn.DDown, buttons, pressed, -1, dt);
                WheelButton(Btn.L1, buttons, pressed, -1, dt);
                runLatched = false;
                shiftT = -1f;
            }
            else
            {
                // ---- look ----
                float sens = DualSensePadMod.F(DualSensePadMod.LookSens, 1f);
                Vector2 look = Radial(s.RX, s.RY, dz, 2f);
                float invert = DualSensePadMod.B(DualSensePadMod.InvertY, false) ? -1f : 1f;
                MoveMouse(look.x * 1100f * sens * dt, -look.y * 750f * sens * invert * dt);

                if (ctx == Ctx.Foot)
                {
                    Vector2 m = Radial(s.LX, s.LY, Mathf.Max(dz, 0.12f), 1f);
                    if (m.magnitude > 0.85f) m = m.normalized;
                    CInputBridge.Set("PlayerRight", m.x);
                    CInputBridge.Set("PlayerLeft", -m.x);
                    CInputBridge.Set("PlayerUp", m.y);
                    CInputBridge.Set("PlayerDown", -m.y);

                    if (m.magnitude < 0.2f) runIdle += dt; else runIdle = 0f;
                    if (runIdle > 0.35f) runLatched = false;

                    for (int i = 0; i < Layout.FootButtons.Length; i++)
                        ApplyButton(Layout.FootButtons[i], DualSensePadMod.FootTarget(i), buttons, pressed, dt, ref wantML, ref wantMR, ref wantMM, ref wantEsc, ref wantTab);
                    if (runLatched) CInputBridge.Press("Run");
                    shiftT = -1f;
                }
                else
                {
                    DriveInputs(s, dz);
                    for (int i = 0; i < Layout.CarButtons.Length; i++)
                        ApplyButton(Layout.CarButtons[i], DualSensePadMod.CarTarget(i), buttons, pressed, dt, ref wantML, ref wantMR, ref wantMM, ref wantEsc, ref wantTab);
                    UpdateAutoClutch(dt);
                    runLatched = false;
                }
            }

            SetMouse(0, wantML);
            SetMouse(1, wantMR);
            SetMouse(2, wantMM);
            if (wantEsc != escHeld) { NativeInput.Key(NativeInput.VK_ESCAPE, wantEsc); escHeld = wantEsc; }
            if (wantTab != tabHeld) { NativeInput.Key(NativeInput.VK_TAB, wantTab); tabHeld = wantTab; }

            prevButtons = buttons;
            UpdateFeedback(s, dt);
        }

        // ------------------------------------------------------------------ context

        Ctx DetectContext()
        {
            if (Time.time > nextGlobalsTry && (gVehicle == null || gInMenu == null))
            {
                nextGlobalsTry = Time.time + 2f;
                try
                {
                    gVehicle = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
                    gInMenu = FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu");
                }
                catch { }
            }
            if (gInMenu != null && gInMenu.Value) return Ctx.Menu;
            if (Cursor.visible && Cursor.lockState == CursorLockMode.None) return Ctx.Menu;
            if (gVehicle != null && !string.IsNullOrEmpty(gVehicle.Value)) return Ctx.Car;
            return Ctx.Foot;
        }

        void FindCar()
        {
            if (car != null && car.enabled && Time.time < nextCarSearch) return;
            if (Time.time < nextCarSearch && car == null) return;
            nextCarSearch = Time.time + 1f;
            car = null;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position;
            float best = 8f;
            AxisCarController[] all = FindObjectsOfType<AxisCarController>();
            for (int i = 0; i < all.Length; i++)
            {
                AxisCarController c = all[i];
                if (!c.enabled) continue;
                float d = Vector3.Distance(c.transform.position, p);
                if (d < best) { best = d; car = c; }
            }
            if (car != null)
            {
                drivetrain = car.GetComponent<Drivetrain>();
                dynamics = car.GetComponent<CarDynamics>();
                body = car.GetComponent<Rigidbody>();
                Axles ax = car.GetComponent<Axles>();
                wheels = ax != null && ax.allWheels != null ? ax.allWheels : car.GetComponentsInChildren<Wheel>();
                if (body != null) lastVel = body.velocity;
            }
        }

        // ------------------------------------------------------------------ driving

        void DriveInputs(PadState s, float dz)
        {
            // steering with curve and speed sensitivity
            float x = s.LX;
            float a = Mathf.Abs(x);
            float steer = 0f;
            if (a > dz)
            {
                float curve = DualSensePadMod.F(DualSensePadMod.SteerCurve, 1.6f);
                steer = Mathf.Pow((a - dz) / (1f - dz), curve) * Mathf.Sign(x);
            }
            if (body != null)
            {
                float kmh = body.velocity.magnitude * 3.6f;
                float reduce = DualSensePadMod.F(DualSensePadMod.SteerSpeedReduce, 35f) / 100f;
                steer *= 1f - reduce * Mathf.Clamp01((kmh - 20f) / 100f);
            }
            CInputBridge.Set("Right", steer);
            CInputBridge.Set("Left", -steer);

            float thr = Trig(s.R2);
            float brk = Trig(s.L2);
            CInputBridge.Set("ThrottleOn", thr);
            CInputBridge.Set("Up", thr);
            CInputBridge.Set("BrakeOn", brk);
            CInputBridge.Set("Down", brk);
        }

        static float Trig(float v)
        {
            const float dz = 0.04f;
            if (v <= dz) return 0f;
            return Mathf.Clamp01((v - dz) / (0.97f - dz));
        }

        void StartShift(string action)
        {
            if (!DualSensePadMod.B(DualSensePadMod.AutoClutch, true))
            {
                CInputBridge.Press(action);
                return;
            }
            shiftAction = action;
            shiftT = 0f;
        }

        void UpdateAutoClutch(float dt)
        {
            if (shiftT < 0f) return;
            shiftT += dt;
            // clutch in -> shift -> ease the clutch back out
            float clutch;
            if (shiftT < 0.28f) clutch = 1f;
            else clutch = 1f - (shiftT - 0.28f) / 0.22f;
            if (clutch > 0f) CInputBridge.Set("ClutchOn", clutch);
            if (shiftT >= 0.07f && shiftT < 0.16f) CInputBridge.Press(shiftAction);
            if (shiftT > 0.5f) shiftT = -1f;
        }

        // ------------------------------------------------------------------ buttons

        void ApplyButton(uint b, string target, uint buttons, uint pressed, float dt,
            ref bool ml, ref bool mr, ref bool mm, ref bool esc, ref bool tab)
        {
            if (string.IsNullOrEmpty(target)) return;
            bool held = (buttons & b) != 0;
            bool down = (pressed & b) != 0;
            switch (target)
            {
                case "#ML": if (held) ml = true; return;
                case "#MR": if (held) mr = true; return;
                case "#MM": if (held) mm = true; return;
                case "#ESC": if (held) esc = true; return;
                case "#TAB": if (held) tab = true; return;
                case "#WU": WheelButton(b, buttons, pressed, 1, dt); return;
                case "#WD": WheelButton(b, buttons, pressed, -1, dt); return;
                case "#RUNTOGGLE":
                    if (down) { runLatched = !runLatched; runIdle = 0f; }
                    return;
            }
            if (ctx == Ctx.Car && (target == "ShiftUp" || target == "ShiftDown"))
            {
                if (down) StartShift(target);
                return;
            }
            if (held) CInputBridge.Press(target);
        }

        void WheelButton(uint b, uint buttons, uint pressed, int dir, float dt)
        {
            if ((pressed & b) != 0)
            {
                NativeInput.Wheel(dir);
                wheelRepeat[b] = 0.4f;
                return;
            }
            if ((buttons & b) == 0) { wheelRepeat.Remove(b); return; }
            float t;
            if (!wheelRepeat.TryGetValue(b, out t)) t = 0.4f;
            t -= dt;
            if (t <= 0f) { NativeInput.Wheel(dir); t = 0.08f; }
            wheelRepeat[b] = t;
        }

        // ------------------------------------------------------------------ mouse helpers

        static Vector2 Radial(float x, float y, float dz, float curve)
        {
            Vector2 v = new Vector2(x, y);
            float m = v.magnitude;
            if (m <= dz) return Vector2.zero;
            if (m > 1f) m = 1f;
            float k = Mathf.Pow((m - dz) / (1f - dz), curve);
            return v / v.magnitude * k;
        }

        void MoveMouse(float dx, float dy)
        {
            lookRemX += dx;
            lookRemY += dy;
            int ix = (int)lookRemX, iy = (int)lookRemY;
            lookRemX -= ix;
            lookRemY -= iy;
            NativeInput.MoveRelative(ix, iy);
        }

        void SetMouse(int i, bool want)
        {
            if (mouseHeld[i] == want) return;
            mouseHeld[i] = want;
            NativeInput.MouseButton(i, want);
        }

        void ReleaseAll()
        {
            for (int i = 0; i < 3; i++) if (mouseHeld[i]) { mouseHeld[i] = false; NativeInput.MouseButton(i, false); }
            if (escHeld) { escHeld = false; NativeInput.Key(NativeInput.VK_ESCAPE, false); }
            if (tabHeld) { tabHeld = false; NativeInput.Key(NativeInput.VK_TAB, false); }
            wheelRepeat.Clear();
            runLatched = false;
            shiftT = -1f;
        }

        // ------------------------------------------------------------------ feedback

        void UpdateFeedback(PadState s, float dt)
        {
            float big = 0f, small = 0f;
            dbgL2Raw = s.L2; dbgR2Raw = s.R2;
            byte[] r2 = OutputBuilder.TriggerOff(), l2 = OutputBuilder.TriggerOff();
            byte red = 0, green = 0, blue = 40;
            bool paused = Time.timeScale < 0.01f;

            if (ctx == Ctx.Car && car != null && drivetrain != null && body != null && !paused && dt > 0f)
            {
                float rpm = drivetrain.rpm;
                bool engineOn = rpm > 300f;
                float throttleIn = Trig(s.R2);
                float engaged = drivetrain.clutch != null ? Mathf.Clamp01(drivetrain.clutch.GetClutchPosition()) : 1f;
                bool inGear = drivetrain.ratio != 0f;

                // Real redline: the cars' maxRPM (8000-9000) is never reached, so start from the power peak and learn
                // the highest rpm the engine actually holds on throttle in gear (= where it hits the limiter).
                if (redlineCar != car)
                {
                    redlineCar = car;
                    if (!redlines.TryGetValue(car.name, out learnedRedline))
                        learnedRedline = Mathf.Max(1500f, drivetrain.maxPowerRPM);
                    prevRpm = rpm;
                }
                if (engineOn && inGear && engaged > 0.9f && throttleIn > 0.6f && rpm > learnedRedline)
                {
                    learnedRedline = Mathf.Min(rpm, Mathf.Max(drivetrain.maxRPM, learnedRedline));
                    redlines[car.name] = learnedRedline;
                }
                float redline = learnedRedline;
                float rpm01 = Mathf.Clamp01(rpm / redline);
                dbgRedline = redline;
                dbgEngaged = engaged;
                Vector3 vel = body.velocity;
                float kmh = vel.magnitude * 3.6f;
                float speed01 = Mathf.Clamp01(kmh / 60f);

                // ---- shift-up marker: short sharp pulses when you're near this car's redline in gear ----
                // nothing in neutral or top gear; faded out by the clutch pedal (position 1 = engaged, 0 = pressed)
                bool topGear = drivetrain.gearRatios != null && drivetrain.gear >= drivetrain.gearRatios.Length - 1;
                shiftMarker = false;
                if (engineOn && inGear && !topGear && drivetrain.gear > drivetrain.neutral && rpm01 > 0.92f)
                {
                    shiftPhase += dt;
                    // pulses get faster the closer you are: 4/s at 92%, 8/s at the limiter
                    float rate = Mathf.Lerp(4f, 8f, Mathf.Clamp01((rpm01 - 0.92f) / 0.08f));
                    if (shiftPhase * rate % 1f < 0.4f)
                    {
                        float ev = DualSensePadMod.F(DualSensePadMod.EngineVibration, 0.4f) * engaged;
                        small += ev * 1.6f;
                        big += ev * 0.5f;
                        shiftMarker = true;
                    }
                }
                else shiftPhase = 0f;

                // ---- engine lugging and stalling ----
                // below idle in gear with the clutch out = the engine is being dragged down: low "kicks" at firing rate
                float minRpm = Mathf.Max(400f, drivetrain.minRPM);
                float stallK = DualSensePadMod.F(DualSensePadMod.StallVibration, 1f);
                bool loaded = inGear && engaged > 0.3f;
                if (engineOn && loaded && rpm < minRpm * 0.97f)
                {
                    float lug = Mathf.Clamp01((minRpm * 0.97f - rpm) / (minRpm * 0.6f));
                    float kickRate = Mathf.Clamp(rpm / 90f, 3f, 9f);   // slower kicks as rpm sinks
                    kickPhase += dt * kickRate;
                    if (kickPhase % 1f < 0.3f) big += (0.12f + 0.3f * lug) * engaged * stallK;
                    lugTimer = 0.6f;
                }
                else { kickPhase = 0f; lugTimer -= dt; }
                // the moment the engine dies under load (or right after lugging): one hard thump, then silence
                if (prevRpm >= 300f && rpm < 300f && (loaded || lugTimer > 0f)) stallKick = 0.18f;
                if (stallKick > 0f)
                {
                    big += 0.9f * stallK;
                    small += 0.35f * stallK;
                    stallKick -= dt;
                }
                prevRpm = rpm;

                // road texture: high-pass of the steering self-aligning torque (what the wheel FFB uses)
                float ffb = dynamics != null ? dynamics.forceFeedback : 0f;
                ffbSmooth = Mathf.Lerp(ffbSmooth, ffb, Mathf.Clamp01(dt * 6f));
                float hp = Mathf.Abs(ffb - ffbSmooth);
                roadHp = Mathf.Lerp(roadHp, hp, Mathf.Clamp01(dt * 20f));
                float road = DualSensePadMod.F(DualSensePadMod.RoadFeel, 1f);
                big += Mathf.Clamp01(roadHp / 25f) * 0.5f * road;
                // steering load: a light constant buzz proportional to |FFB| when cornering
                small += Mathf.Clamp01(Mathf.Abs(ffbSmooth) / 60f) * 0.12f * road * speed01;

                // bumps / crashes: acceleration spikes (measured per physics step in FixedUpdate)
                float accel = peakAccel;
                peakAccel = 0f;
                accelBase = Mathf.Lerp(accelBase, accel, Mathf.Clamp01(dt * 2f));
                float spike = accel - accelBase - 12f;
                if (spike > 0f) jolt = Mathf.Max(jolt, Mathf.Clamp01(spike / 50f));
                big += jolt;
                small += jolt * 0.6f;
                jolt = Mathf.Max(0f, jolt - dt * 3f);

                // tyres: slip, surface
                float slipLong = 0f, slipLat = 0f, offroad = 0f;
                int grounded = 0;
                bool lock_ = false;
                if (wheels != null)
                {
                    for (int i = 0; i < wheels.Length; i++)
                    {
                        Wheel w = wheels[i];
                        if (w == null || !w.onGroundDown) continue;
                        grounded++;
                        slipLong = Mathf.Max(slipLong, Mathf.Abs(w.longitudinalSlip));
                        slipLat = Mathf.Max(slipLat, Mathf.Abs(w.lateralSlip));
                        if (w.surfaceType != CarDynamics.SurfaceType.track) offroad += 1f;
                        if (w.longitudinalSlip < -1.3f) lock_ = true;
                    }
                    if (grounded > 0) offroad /= grounded;
                }
                // driven wheels only: spinning under power
                // Wheelspin = driven tyre surface moving faster than the ground under it (same direction).
                // wheelTireVelo = angularVelocity * loaded radius, wheelRoadVelo = ground speed at the contact patch (m/s).
                float spinExcess = 0f, spinRoad = 0f, spinTire = 0f;
                Wheel[] pw = drivetrain.poweredWheels;
                if (pw != null)
                    for (int i = 0; i < pw.Length; i++)
                    {
                        Wheel w = pw[i];
                        if (w == null || !w.onGroundDown) continue;
                        float tireV = w.wheelTireVelo, roadV = w.wheelRoadVelo;
                        // reverse: flip so "faster than the ground" is positive either way
                        float sgn = Mathf.Abs(tireV) > 0.01f ? Mathf.Sign(tireV) : 1f;
                        float excess = (tireV - roadV) * sgn;
                        if (excess > spinExcess) { spinExcess = excess; spinRoad = Mathf.Abs(roadV); spinTire = Mathf.Abs(tireV); }
                    }
                dbgSpinTire = spinTire * 3.6f;
                dbgSpinRoad = spinRoad * 3.6f;
                // spinning if the tyre is > 5 km/h AND > 15% faster than the ground; ends below half of that
                float limit = Mathf.Max(1.4f, 0.15f * spinRoad);
                bool spinNow = engineOn && inGear && engaged > 0.3f && throttleIn > 0.05f && spinExcess > (spinning ? limit * 0.5f : limit);
                if (spinNow) { spinning = true; spinHold = 0.15f; }
                else if (spinning) { spinHold -= dt; if (spinHold <= 0f) spinning = false; }
                float slide = Mathf.Clamp01((Mathf.Max(slipLong, slipLat) - 1.1f) / 1.5f) * Mathf.Clamp01(kmh / 8f);
                small += slide * 0.4f;
                small += offroad * 0.10f * speed01;

                // adaptive triggers
                if (DualSensePadMod.B(DualSensePadMod.Triggers, true))
                {
                    float ts = DualSensePadMod.F(DualSensePadMod.TriggerStrength, 0.8f);
                    float throttle = Trig(s.R2), brake = Trig(s.L2);
                    bool revLimit = engineOn && inGear && rpm01 > 0.97f && throttle > 0.3f;
                    if (spinning)
                        r2 = OutputBuilder.TriggerOff();   // wheels spin (snow/ice): the pedal goes light instantly
                    else if (revLimit)
                        r2 = OutputBuilder.TriggerVibration(3, Mathf.Max(1, Mathf.RoundToInt(6 * ts)), 45);
                    else if (engineOn)
                        r2 = OutputBuilder.TriggerFeedback(1, Mathf.Max(1, Mathf.RoundToInt(2 * ts)));

                    bool abs = (car.ABSTriggered || lock_) && brake > 0.15f && kmh > 6f;
                    if (abs)
                        l2 = OutputBuilder.TriggerVibration(2, Mathf.Max(1, Mathf.RoundToInt(6 * ts)), 20);
                    else
                    {
                        // brake pedal: free play, then firmer the deeper you press
                        int[] z = new int[10];
                        for (int i = 1; i < 10; i++) z[i] = Mathf.Clamp(Mathf.RoundToInt((1.5f + i * 0.7f) * ts), 1, 8);
                        l2 = OutputBuilder.TriggerFeedback(z);
                    }
                }

                // lightbar: green -> yellow -> red with rpm, flashing at the limiter
                if (engineOn)
                {
                    float t = Mathf.Clamp01((rpm01 - 0.25f) / 0.65f);
                    red = (byte)(255 * Mathf.Clamp01(t * 2f));
                    green = (byte)(255 * Mathf.Clamp01(2f - t * 2f));
                    blue = 0;
                    if (inGear && rpm01 > 0.97f && (Time.time * 8f) % 2f < 1f) { red = 0; green = 0; }
                }
                else { red = 60; green = 30; blue = 0; }
            }
            else
            {
                jolt = 0f;
                peakAccel = 0f;
                spinning = false;
                stallKick = 0f;
                redlineCar = null;
            }

            if (!DualSensePadMod.B(DualSensePadMod.Rumble, true) || paused) { big = 0f; small = 0f; }
            float rs = DualSensePadMod.F(DualSensePadMod.RumbleStrength, 1f);
            big = Mathf.Clamp01(big * rs);
            small = Mathf.Clamp01(small * rs);

            int lbMode = DualSensePadMod.Lightbar != null ? DualSensePadMod.Lightbar.GetSelectedItemIndex() : 0;
            bool lightbar = lbMode != 1;
            if (lbMode == 2) { red = 25; green = 25; blue = 25; }

            dbgBig = big; dbgSmall = small;
            dbgR2 = EffectName(r2); dbgL2 = EffectName(l2);
            byte[] o = OutputBuilder.Build(big, small, r2, l2, lightbar, red, green, blue);
            // trigger mode changes (e.g. pedal going soft on wheelspin) are sent immediately
            bool triggerChanged = lastOutput != null && (lastOutput[10] != o[10] || lastOutput[21] != o[21]);
            SendOutput(o, triggerChanged);
        }

        void SendOutput(byte[] o, bool force)
        {
            if (hid == null || !hid.Connected) return;
            bool same = lastOutput != null && Equal(lastOutput, o);
            if (same && !force) return;
            // limit to ~30 reports/s, but always let "stop" through quickly
            if (!force && Time.unscaledTime - lastOutputTime < 0.033f && !(o[2] == 0 && o[3] == 0 && lastOutput != null && (lastOutput[2] != 0 || lastOutput[3] != 0))) return;
            lastOutput = o;
            lastOutputTime = Time.unscaledTime;
            hid.SetOutput(o);
        }

        static string EffectName(byte[] e)
        {
            switch (e[0])
            {
                case 0x05: return "off";
                case 0x21: return "resist";
                case 0x26: return "vibrate";
            }
            return e[0].ToString();
        }

        static bool Equal(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ------------------------------------------------------------------ status

        void UpdateStatus()
        {
            bool c = hid.Connected;
            if (c != wasConnected)
            {
                wasConnected = c;
                if (c) ModConsole.Print("[DualSensePad] connected: " + hid.DeviceInfo);
                else ModConsole.Print("[DualSensePad] controller disconnected");
                lastOutput = null;
            }
            string st = c ? "Controller: <color=lime>" + hid.DeviceInfo + "</color>"
                          : "Controller: <color=orange>not found</color> (plug in USB or pair via Bluetooth)" + (hid.LastError.Length > 0 ? " - " + hid.LastError : "");
            if (!CInputBridge.Patched) st += "\n<color=red>Input hook: " + CInputBridge.Status + "</color>";
            if (st != lastStatus && DualSensePadMod.StatusText != null)
            {
                lastStatus = st;
                DualSensePadMod.StatusText.SetValue(st);
            }
        }
    }
}
