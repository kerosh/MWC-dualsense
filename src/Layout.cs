namespace DualSensePad
{
    /// <summary>What a pad button can be assigned to (index = position in the settings dropdown).</summary>
    public static class Layout
    {
        // ids starting with '#' are special (mouse / keyboard / mod features), others are cInput action names
        public static readonly string[] Ids = new string[]
        {
            "",
            "#ML", "#MR", "#MM", "#WU", "#WD", "#ESC", "#TAB",
            "Use", "Jump", "Run", "#RUNTOGGLE", "Zoom", "Crouch", "ReachLeft", "ReachRight",
            "DrivingMode", "Hitchhike", "Swear", "Hit", "Push", "Finger", "Urinate", "Drunk", "Smoking", "Watch",
            "ClutchOn", "Handbrake", "ShiftUp", "ShiftDown", "StartEngine", "Ignition", "ACC", "Range", "Boost",
            "LightsDrive", "LightsPark", "LightsOff", "HiBeamToggle", "HiBeamFlash", "Wipers", "Washer",
            "IndicatorLeft", "IndicatorRight", "IndicatorReturn",
            "Lift1Up", "Lift1Down", "Lift2Up", "Lift2Down",
            "reverse", "neutral", "first", "second", "third", "fourth", "fifth", "sixth"
        };

        public static readonly string[] Names = new string[]
        {
            "- nothing -",
            "Mouse: left click (grab / use)", "Mouse: right click", "Mouse: middle click", "Mouse: wheel up", "Mouse: wheel down",
            "Key: Esc (pause menu)", "Key: Tab",
            "Use (F)", "Jump (Space)", "Run (hold)", "Run (toggle, ends when you stop)", "Zoom (Ctrl)", "Crouch (C)",
            "Reach left (Q)", "Reach right (E)",
            "Driving mode / seat (Enter)", "Hitchhike (O)", "Swear (N)", "Hit (H)", "Push (J)", "Finger (M)", "Urinate (P)",
            "Drunk (K)", "Smoking (I)", "Watch (U)",
            "Clutch (X)", "Handbrake (Space)", "Shift up (G)", "Shift down (B)", "Start engine", "Ignition", "ACC", "Range (R)",
            "Boost (T)",
            "Lights (L)", "Parking lights", "Lights off", "High beam toggle (,)", "High beam flash", "Wipers (/)", "Washer",
            "Indicator left", "Indicator right", "Indicator return",
            "Lift 1 up", "Lift 1 down", "Lift 2 up", "Lift 2 down",
            "Gear R", "Gear N", "Gear 1", "Gear 2", "Gear 3", "Gear 4", "Gear 5", "Gear 6"
        };

        public static int IndexOf(string id)
        {
            for (int i = 0; i < Ids.Length; i++) if (Ids[i] == id) return i;
            return 0;
        }

        // ---- buttons shown in settings ----
        public static readonly uint[] FootButtons = new uint[]
        {
            Btn.Cross, Btn.Circle, Btn.Square, Btn.Triangle, Btn.L1, Btn.R1, Btn.L2, Btn.R2, Btn.L3, Btn.R3,
            Btn.DUp, Btn.DDown, Btn.DLeft, Btn.DRight, Btn.Options, Btn.Create, Btn.Touchpad, Btn.Mute
        };
        public static readonly string[] FootDefaults = new string[]
        {
            "Jump", "Crouch", "Use", "DrivingMode", "#WD", "#WU", "#MR", "#ML", "#RUNTOGGLE", "Zoom",
            "Smoking", "Watch", "ReachLeft", "ReachRight", "#ESC", "Hitchhike", "Swear", ""
        };

        // in a vehicle L2 / R2 are always the analog brake / throttle
        public static readonly uint[] CarButtons = new uint[]
        {
            Btn.Cross, Btn.Circle, Btn.Square, Btn.Triangle, Btn.L1, Btn.R1, Btn.L3, Btn.R3,
            Btn.DUp, Btn.DDown, Btn.DLeft, Btn.DRight, Btn.Options, Btn.Create, Btn.Touchpad, Btn.Mute
        };
        public static readonly string[] CarDefaults = new string[]
        {
            "ClutchOn", "#ML", "Handbrake", "DrivingMode", "ShiftDown", "ShiftUp", "HiBeamToggle", "Zoom",
            "LightsDrive", "Wipers", "IndicatorLeft", "IndicatorRight", "#ESC", "Ignition", "StartEngine", "#MR"
        };

        public static string ButtonName(uint b)
        {
            switch (b)
            {
                case Btn.Cross: return "Cross (X)";
                case Btn.Circle: return "Circle (O)";
                case Btn.Square: return "Square";
                case Btn.Triangle: return "Triangle";
                case Btn.L1: return "L1";
                case Btn.R1: return "R1";
                case Btn.L2: return "L2";
                case Btn.R2: return "R2";
                case Btn.L3: return "L3 (left stick click)";
                case Btn.R3: return "R3 (right stick click)";
                case Btn.DUp: return "D-pad up";
                case Btn.DDown: return "D-pad down";
                case Btn.DLeft: return "D-pad left";
                case Btn.DRight: return "D-pad right";
                case Btn.Options: return "Options";
                case Btn.Create: return "Create";
                case Btn.Touchpad: return "Touchpad click";
                case Btn.Mute: return "Mic mute";
            }
            return "?";
        }

        public static string ButtonId(uint b)
        {
            for (int i = 0; i < 32; i++) if (b == (1u << i)) return "b" + i;
            return "b";
        }
    }
}
