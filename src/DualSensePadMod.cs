using System.Reflection;
using MSCLoader;
using UnityEngine;

[assembly: AssemblyTitle("DualSensePad")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyTrademark("AI generated code (Claude)")]

namespace DualSensePad
{
    public class DualSensePadMod : Mod
    {
        public override string ID { get { return "DualSensePad"; } }
        public override string Name { get { return "DualSense Pad"; } }
        public override string Author { get { return "hotkeros"; } }
        public override string Version { get { return "1.0.0"; } }
        public override string Description
        {
            get
            {
                return "Native PS5 DualSense support: ready-made gamepad layout that works together with keyboard & mouse, " +
                       "analog steering / throttle / brake, rumble instead of force feedback and adaptive trigger effects. " +
                       "(Code written with AI assistance - Claude)";
            }
        }
        public override MSCLoader.Game SupportedGames { get { return MSCLoader.Game.MyWinterCar; } }

        // ---- settings (read live by PadBehaviour) ----
        public static SettingsCheckBox IgnoreGamePad, Enabled, InvertY, AutoClutch, Rumble, Triggers, DebugReadout;
        public static SettingsSlider StallVibration, LookSens, StickDeadzone, SteerCurve, SteerSpeedReduce, RumbleStrength, EngineVibration, RoadFeel, TriggerStrength;
        public static SettingsDropDownList Lightbar;
        public static SettingsText StatusText;
        public static SettingsDropDownList[] FootMap, CarMap;

        public override void ModSetup()
        {
            SetupFunction(Mod.Setup.ModSettings, Mod_Settings);
            SetupFunction(Mod.Setup.OnMenuLoad, Mod_OnMenuLoad);
            SetupFunction(Mod.Setup.OnLoad, Mod_OnLoad);
            SetupFunction(Mod.Setup.OnModEnabled, Mod_OnLoad);
            SetupFunction(Mod.Setup.OnModDisabled, Mod_OnDisabled);
        }

        void Mod_Settings()
        {
            Settings.AddHeader("DualSense");
            StatusText = Settings.AddText("Controller: searching...");
            Enabled = Settings.AddCheckBox("enabled", "Enable DualSense controls", true, null);
            IgnoreGamePad = Settings.AddCheckBox("ignoreGamePad", "Ignore gamepad bindings from the game's own controls menu (recommended)", true, null);
            Settings.AddText("<color=orange>Tips:</color> in Steam set this game's Properties > Controller to <b>Disable Steam Input</b> " +
                             "(otherwise Steam fights over rumble/triggers and sends duplicate input). " +
                             "If you bound gamepad buttons in the game's own controls menu, set those back to keyboard keys - " +
                             "this mod adds the pad on top of the keyboard, so you don't need any in-game remapping.");

            Settings.AddHeader("Sticks & driving");
            LookSens = Settings.AddSlider("lookSens", "Look sensitivity (right stick)", 0.2f, 3f, 1f, null, 2);
            InvertY = Settings.AddCheckBox("invertY", "Invert look up/down", false, null);
            StickDeadzone = Settings.AddSlider("deadzone", "Stick deadzone", 0.02f, 0.3f, 0.1f, null, 2);
            SteerCurve = Settings.AddSlider("steerCurve", "Steering curve (1 = linear, higher = finer centre)", 1f, 3f, 1.6f, null, 2);
            SteerSpeedReduce = Settings.AddSlider("steerSpeed", "Less steering at high speed (%)", 0f, 70f, 35f, null, 0);
            AutoClutch = Settings.AddCheckBox("autoClutch", "Auto-clutch when shifting with the pad", true, null);

            Settings.AddHeader("Vibration & adaptive triggers");
            Rumble = Settings.AddCheckBox("rumble", "Rumble (replaces wheel force feedback)", true, null);
            RumbleStrength = Settings.AddSlider("rumbleStrength", "Rumble strength", 0f, 1.5f, 1f, null, 2);
            EngineVibration = Settings.AddSlider("engineVib", "Shift-up marker (pulses near this car's redline, not in neutral / top gear)", 0f, 1f, 0.4f, null, 2);
            StallVibration = Settings.AddSlider("stallVib", "Engine lugging / stall kick", 0f, 2f, 1f, null, 2);
            RoadFeel = Settings.AddSlider("roadFeel", "Road / steering feel", 0f, 2f, 1f, null, 2);
            Triggers = Settings.AddCheckBox("triggers", "Adaptive triggers (brake pedal, soft gas on wheelspin, wheel lock, rev limiter)", true, null);
            TriggerStrength = Settings.AddSlider("triggerStrength", "Trigger effect strength", 0.2f, 1f, 0.8f, null, 2);
            DebugReadout = Settings.AddCheckBox("debug", "Show debug readout (rpm, clutch, slip, motors)", false, null);
            Lightbar = Settings.AddDropDownList("lightbar", "Lightbar", new string[] { "Engine RPM colour", "Don't touch", "Dim white" }, 0, null);

            Settings.AddHeader("Buttons - on foot", true);
            Settings.AddText("Left stick = walk, right stick = look. In menus both sticks move the cursor, Cross = click, Circle = back.");
            FootMap = new SettingsDropDownList[Layout.FootButtons.Length];
            for (int i = 0; i < Layout.FootButtons.Length; i++)
            {
                uint b = Layout.FootButtons[i];
                FootMap[i] = Settings.AddDropDownList("foot_" + Layout.ButtonId(b), Layout.ButtonName(b), Layout.Names, Layout.IndexOf(Layout.FootDefaults[i]), null);
            }

            Settings.AddHeader("Buttons - in a vehicle", true);
            Settings.AddText("Left stick = steer, R2 = throttle, L2 = brake (analog), right stick = look.");
            CarMap = new SettingsDropDownList[Layout.CarButtons.Length];
            for (int i = 0; i < Layout.CarButtons.Length; i++)
            {
                uint b = Layout.CarButtons[i];
                CarMap[i] = Settings.AddDropDownList("car_" + Layout.ButtonId(b), Layout.ButtonName(b), Layout.Names, Layout.IndexOf(Layout.CarDefaults[i]), null);
            }
            Settings.AddButton("Reset button layout to defaults", ResetLayout);
        }

        static void ResetLayout()
        {
            for (int i = 0; i < FootMap.Length; i++) FootMap[i].SetSelectedItemIndex(Layout.IndexOf(Layout.FootDefaults[i]));
            for (int i = 0; i < CarMap.Length; i++) CarMap[i].SetSelectedItemIndex(Layout.IndexOf(Layout.CarDefaults[i]));
        }

        void Mod_OnMenuLoad() { Ensure(); }
        void Mod_OnLoad() { Ensure(); }

        void Mod_OnDisabled()
        {
            CInputBridge.ClearFrame();
            if (PadBehaviour.Instance != null) Object.Destroy(PadBehaviour.Instance.gameObject);
        }

        static void Ensure()
        {
            CInputBridge.Install();
            if (PadBehaviour.Instance == null)
            {
                GameObject go = new GameObject("DualSensePad");
                Object.DontDestroyOnLoad(go);
                go.AddComponent<PadBehaviour>();
            }
        }

        public static string FootTarget(int i)
        {
            if (FootMap == null || i >= FootMap.Length) return Layout.FootDefaults[i];
            int k = FootMap[i].GetSelectedItemIndex();
            return k >= 0 && k < Layout.Ids.Length ? Layout.Ids[k] : "";
        }

        public static string CarTarget(int i)
        {
            if (CarMap == null || i >= CarMap.Length) return Layout.CarDefaults[i];
            int k = CarMap[i].GetSelectedItemIndex();
            return k >= 0 && k < Layout.Ids.Length ? Layout.Ids[k] : "";
        }

        public static bool B(SettingsCheckBox c, bool def) { return c == null ? def : c.GetValue(); }
        public static float F(SettingsSlider s, float def) { return s == null ? def : s.GetValue(); }
    }
}
