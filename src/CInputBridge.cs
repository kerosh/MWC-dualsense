using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;
using MSCLoader;

namespace DualSensePad
{
    /// <summary>
    /// Merges gamepad values into cInput's per-frame result arrays (right after cInput computed
    /// the keyboard state), so every game script / PlayMaker FSM sees gamepad presses exactly
    /// like key presses - and the keyboard keeps working.
    /// </summary>
    public static class CInputBridge
    {
        // cInput input names (from the game's "Setup" FSM: SetKey action names)
        public static readonly string[] Actions = new string[]
        {
            // on foot
            "PlayerLeft", "PlayerRight", "PlayerUp", "PlayerDown",
            "Jump", "Run", "Zoom", "Use", "Crouch", "ReachLeft", "ReachRight", "Hitchhike", "Swear", "Hit",
            "Push", "Finger", "Urinate", "Drunk", "DrivingMode", "Smoking", "Watch",
            // driving
            "Left", "Right", "Up", "Down", "ThrottleOn", "ThrottleOff", "BrakeOn", "BrakeOff", "ClutchOn", "ClutchOff",
            "StartEngine", "ShiftUp", "ShiftDown", "reverse", "neutral", "first", "second", "third", "fourth", "fifth", "sixth",
            "Handbrake", "Handbrake1", "Range", "Boost", "ACC", "Ignition", "Lift1Up", "Lift1Down", "Lift2Up", "Lift2Down",
            "LightsOff", "LightsPark", "LightsDrive", "HiBeamToggle", "HiBeamFlash", "Wipers", "Washer",
            "IndicatorLeft", "IndicatorRight", "IndicatorReturn"
        };

        static readonly Dictionary<string, int> slotOf = new Dictionary<string, int>();
        static float[] value;      // what the pad wants this frame (0..1)
        static bool[] prevHeld;
        static int[] cIndex;       // cInput input index per slot (-1 = unknown)
        static bool resolved;
        static int lastFrame = -1;
        public static bool Enabled = true;
        public static bool Patched;
        public static string Status = "not patched";

        // reflected cInput statics
        static bool[] keyArr, downArr, upArr;
        static float[] axisArr, rawArr, axisOut, rawOut;
        static int[,] makeAxis;
        static FieldInfo fInputLength, fAxisLength, fLoaded, fScanning;
        static Dictionary<int, int> nameHash;
        static UnityEngine.KeyCode[] inPrimary, inSecondary;
        static string[] axPrimary, axSecondary;

        /// <summary>True while the mod reads the DualSense itself: the game's own joystick bindings are then ignored.</summary>
        public static bool BlockGameJoystick;

        public static bool AxisPrefix(string description, ref float __result)
        {
            if (BlockGameJoystick && description != null && description.StartsWith("Joy"))
            {
                __result = 0f;
                return false;
            }
            return true;
        }

        static bool IsJoyKey(UnityEngine.KeyCode k)
        {
            return (int)k >= (int)UnityEngine.KeyCode.JoystickButton0 && (int)k <= 509;
        }

        /// <summary>Current game binding of an action, for the debug readout.</summary>
        public static string Binding(string action)
        {
            if (nameHash == null || inPrimary == null) return "?";
            int i;
            if (!nameHash.TryGetValue(action.GetHashCode(), out i)) return "?";
            string a = !string.IsNullOrEmpty(axPrimary[i]) ? axPrimary[i] : inPrimary[i].ToString();
            string b = !string.IsNullOrEmpty(axSecondary[i]) ? axSecondary[i] : inSecondary[i].ToString();
            return a + " / " + b;
        }

        static CInputBridge()
        {
            for (int i = 0; i < Actions.Length; i++) slotOf[Actions[i]] = i;
            value = new float[Actions.Length];
            prevHeld = new bool[Actions.Length];
            cIndex = new int[Actions.Length];
            for (int i = 0; i < cIndex.Length; i++) cIndex[i] = -1;
        }

        public static bool IsAction(string name) { return slotOf.ContainsKey(name); }

        /// <summary>Clear all pad requests (call once per frame before setting).</summary>
        public static void ClearFrame()
        {
            for (int i = 0; i < value.Length; i++) value[i] = 0f;
        }

        public static void Set(string action, float v)
        {
            int s;
            if (!slotOf.TryGetValue(action, out s)) return;
            if (v > value[s]) value[s] = v > 1f ? 1f : v;
        }

        public static void Press(string action) { Set(action, 1f); }

        /// <summary>Non-zero pad requests, for the debug readout.</summary>
        public static string Describe()
        {
            string r = "";
            for (int i = 0; i < value.Length; i++)
                if (value[i] > 0.001f) r += Actions[i] + "=" + value[i].ToString("0.00") + " ";
            return r.Length == 0 ? "-" : r;
        }

        public static void Install()
        {
            if (Patched) return;
            try
            {
                Type t = typeof(cInput);
                BindingFlags sf = BindingFlags.Static | BindingFlags.NonPublic;
                keyArr = (bool[])t.GetField("_getKeyArray", sf).GetValue(null);
                downArr = (bool[])t.GetField("_getKeyDownArray", sf).GetValue(null);
                upArr = (bool[])t.GetField("_getKeyUpArray", sf).GetValue(null);
                axisArr = (float[])t.GetField("_getAxis", sf).GetValue(null);
                rawArr = (float[])t.GetField("_getAxisRaw", sf).GetValue(null);
                axisOut = (float[])t.GetField("_getAxisArray", sf).GetValue(null);
                rawOut = (float[])t.GetField("_getAxisArrayRaw", sf).GetValue(null);
                makeAxis = (int[,])t.GetField("_makeAxis", sf).GetValue(null);
                nameHash = (Dictionary<int, int>)t.GetField("_inputNameHash", sf).GetValue(null);
                fInputLength = t.GetField("_inputLength", sf);
                fAxisLength = t.GetField("_axisLength", sf);
                fLoaded = t.GetField("_cKeysLoaded", sf);
                fScanning = t.GetField("_scanning", sf);

                inPrimary = (UnityEngine.KeyCode[])t.GetField("_inputPrimary", sf).GetValue(null);
                inSecondary = (UnityEngine.KeyCode[])t.GetField("_inputSecondary", sf).GetValue(null);
                axPrimary = (string[])t.GetField("_axisPrimary", sf).GetValue(null);
                axSecondary = (string[])t.GetField("_axisSecondary", sf).GetValue(null);

                MethodInfo orig = t.GetMethod("_CheckInputs", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo post = typeof(CInputBridge).GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public);
                HarmonyInstance h = HarmonyInstance.Create("hotkeros.dualsensepad");
                h.Patch(orig, null, new HarmonyMethod(post), null);

                // game's own gamepad axis bindings ("Joy Axis 3+" etc.) -> 0 while this mod drives the pad
                MethodInfo cal = t.GetMethod("_GetCalibratedAxisInput", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo pre = typeof(CInputBridge).GetMethod("AxisPrefix", BindingFlags.Static | BindingFlags.Public);
                h.Patch(cal, new HarmonyMethod(pre), null, null);
                Patched = true;
                Status = "ok";
            }
            catch (Exception e)
            {
                Status = "patch failed: " + e.Message;
                ModConsole.Error("[DualSensePad] cInput patch failed: " + e);
            }
        }

        static void Resolve()
        {
            if (!(bool)fLoaded.GetValue(null)) return;
            int found = 0;
            for (int s = 0; s < Actions.Length; s++)
            {
                int idx;
                if (nameHash.TryGetValue(Actions[s].GetHashCode(), out idx)) { cIndex[s] = idx; found++; }
                else cIndex[s] = -1;
            }
            resolved = found > 0;
            if (resolved) ModConsole.Print("[DualSensePad] linked " + found + "/" + Actions.Length + " game actions");
        }

        /// <summary>Harmony postfix of cInput._CheckInputs (runs once per frame).</summary>
        public static void Postfix()
        {
            try
            {
                int frame = UnityEngine.Time.frameCount;
                if (frame == lastFrame) return;
                lastFrame = frame;
                if (!Enabled) return;
                if (!resolved) { Resolve(); if (!resolved) return; }
                if ((bool)fScanning.GetValue(null)) return; // player is rebinding a key in the options menu

                int inputLength = (int)fInputLength.GetValue(null);
                bool anyAnalog = false;
                if (BlockGameJoystick)
                {
                    for (int i = 0; i <= inputLength && i < inPrimary.Length; i++)
                    {
                        bool j1 = IsJoyKey(inPrimary[i]), j2 = IsJoyKey(inSecondary[i]);
                        if (!j1 && !j2) continue;
                        if (!string.IsNullOrEmpty(axPrimary[i]) || !string.IsNullOrEmpty(axSecondary[i])) continue;
                        UnityEngine.KeyCode k = j1 ? inSecondary[i] : inPrimary[i];
                        bool other = !(j1 && j2) && k != UnityEngine.KeyCode.None;
                        keyArr[i] = other && UnityEngine.Input.GetKey(k);
                        downArr[i] = other && UnityEngine.Input.GetKeyDown(k);
                        upArr[i] = other && UnityEngine.Input.GetKeyUp(k);
                        float v = keyArr[i] ? 1f : 0f;
                        rawArr[i] = v;
                        if (!keyArr[i] && axisArr[i] > 0f) axisArr[i] = 0f;
                        anyAnalog = true;
                    }
                }
                for (int s = 0; s < value.Length; s++)
                {
                    int i = cIndex[s];
                    if (i < 0 || i > inputLength) continue;
                    float v = value[s];
                    bool held = v > 0.25f;
                    bool prev = prevHeld[s];
                    if (held) keyArr[i] = true;
                    if (held && !prev) downArr[i] = true;
                    if (!held && prev) upArr[i] = true;
                    prevHeld[s] = held;
                    if (v > 0f)
                    {
                        if (v > rawArr[i]) rawArr[i] = v;
                        if (v > axisArr[i]) axisArr[i] = v;
                        anyAnalog = true;
                    }
                }
                if (anyAnalog)
                {
                    int axisLength = (int)fAxisLength.GetValue(null);
                    for (int k = 0; k <= axisLength; k++)
                    {
                        int a = makeAxis[k, 0];
                        int b = makeAxis[k, 1];
                        if (b == -1)
                        {
                            axisOut[k] = axisArr[a];
                            rawOut[k] = rawArr[a];
                        }
                        else
                        {
                            axisOut[k] = axisArr[b] - axisArr[a];
                            rawOut[k] = rawArr[b] - rawArr[a];
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Enabled = false;
                ModConsole.Error("[DualSensePad] input merge disabled after error: " + e);
            }
        }
    }
}
