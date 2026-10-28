using System;
using System.IO;
using System.Runtime.InteropServices;
using OVR.OpenVR;
using UnityEngine;

namespace UnRevive
{
    // Reads the controllers through SteamVR Input (an action manifest with default bindings),
    // the way Revive does, instead of the legacy controller state: it covers every Touch input
    // (including the menu button, where the runtime passes it), and players can rebind it in
    // SteamVR's Controller Bindings. Once a manifest is set the app stops getting legacy input,
    // so if anything here fails we never set it and the legacy path (InputPatches) keeps working.
    internal static class SteamVRInput
    {
        private const string ActionSet = "/actions/aoq";

        internal struct Hand
        {
            public bool AX, BY, Thumb, Menu;
            public bool TouchAX, TouchBY, TouchThumb, TouchThumbRest, TouchTrigger;
            public float Trigger, Grip;
            public Vector2 Thumbstick;
        }

        internal static bool Active { get; private set; }

        private static bool _initTried;
        private static int _updatedFrame = -1;
        private static CVRInput _input;
        private static VRActiveActionSet_t[] _sets;
        private static ulong[] _hands; // input source handles, left then right
        private static ulong _ax, _by, _thumb, _menu, _touchAX, _touchBY, _touchThumb, _touchThumbRest,
                             _touchTrigger, _trigger, _grip, _thumbstick, _haptic;
        private static readonly Hand[] State = new Hand[2];

        private static readonly uint DigitalSize = (uint)Marshal.SizeOf(typeof(InputDigitalActionData_t));
        private static readonly uint AnalogSize  = (uint)Marshal.SizeOf(typeof(InputAnalogActionData_t));
        private static readonly uint SetSize     = (uint)Marshal.SizeOf(typeof(VRActiveActionSet_t));

        internal static Hand Get(int hand) => State[hand];

        // Once per frame, before OVRInput reads the controllers.
        internal static void Update()
        {
            if (_updatedFrame == Time.frameCount)
                return;
            _updatedFrame = Time.frameCount;
            if (!_initTried)
            {
                _initTried = true;
                Active = Init();
            }
            if (!Active)
                return;

            EVRInputError err = _input.UpdateActionState(_sets, SetSize);
            if (err != EVRInputError.None)
                return;
            for (int i = 0; i < 2; i++)
            {
                ulong h = _hands[i];
                State[i] = new Hand
                {
                    AX             = Digital(_ax, h),
                    BY             = Digital(_by, h),
                    Thumb          = Digital(_thumb, h),
                    Menu           = Digital(_menu, h),
                    TouchAX        = Digital(_touchAX, h),
                    TouchBY        = Digital(_touchBY, h),
                    TouchThumb     = Digital(_touchThumb, h),
                    TouchThumbRest = Digital(_touchThumbRest, h),
                    TouchTrigger   = Digital(_touchTrigger, h),
                    Trigger        = Analog(_trigger, h).x,
                    Grip           = Analog(_grip, h).x,
                    Thumbstick     = Analog(_thumbstick, h),
                };
            }
        }

        internal static void Vibrate(int hand, float amplitude, float seconds)
        {
            if (Active)
                _input.TriggerHapticVibrationAction(_haptic, 0f, seconds, 160f, Mathf.Clamp01(amplitude), _hands[hand]);
        }

        private static bool Digital(ulong action, ulong hand)
        {
            var data = new InputDigitalActionData_t();
            return _input.GetDigitalActionData(action, ref data, DigitalSize, hand) == EVRInputError.None &&
                   data.bActive && data.bState;
        }

        private static Vector2 Analog(ulong action, ulong hand)
        {
            var data = new InputAnalogActionData_t();
            if (_input.GetAnalogActionData(action, ref data, AnalogSize, hand) != EVRInputError.None || !data.bActive)
                return Vector2.zero;
            return new Vector2(data.x, data.y);
        }

        private static bool Init()
        {
            try
            {
                _input = OpenVR.Input;
                if (_input == null)
                {
                    Plugin.Log.LogWarning("SteamVR Input isn't available; using legacy controller input.");
                    return false;
                }

                string dir = Path.Combine(Path.GetDirectoryName(typeof(SteamVRInput).Assembly.Location), "UnRevive-input");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "actions.json"), ActionManifest);
                File.WriteAllText(Path.Combine(dir, "bindings_oculus_touch.json"), TouchBindings);
                File.WriteAllText(Path.Combine(dir, "bindings_knuckles.json"), KnucklesBindings);
                string manifest = Path.GetFullPath(Path.Combine(dir, "actions.json"));

                ulong set = 0;
                Check(_input.SetActionManifestPath(manifest), "SetActionManifestPath " + manifest);
                Check(_input.GetActionSetHandle(ActionSet, ref set), "action set");
                _sets = new[] { new VRActiveActionSet_t { ulActionSet = set } };

                _ax             = Action("in/ax");
                _by             = Action("in/by");
                _thumb          = Action("in/thumbclick");
                _menu           = Action("in/menu");
                _touchAX        = Action("in/touchax");
                _touchBY        = Action("in/touchby");
                _touchThumb     = Action("in/touchthumb");
                _touchThumbRest = Action("in/touchthumbrest");
                _touchTrigger   = Action("in/touchtrigger");
                _trigger        = Action("in/trigger");
                _grip           = Action("in/grip");
                _thumbstick     = Action("in/thumbstick");
                _haptic         = Action("out/haptic");

                _hands = new ulong[2];
                Check(_input.GetInputSourceHandle("/user/hand/left", ref _hands[0]), "left hand");
                Check(_input.GetInputSourceHandle("/user/hand/right", ref _hands[1]), "right hand");

                Plugin.Log.LogInfo("Using SteamVR Input");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"SteamVR Input setup failed, using legacy controller input: {e.Message}");
                return false;
            }
        }

        private static ulong Action(string name)
        {
            ulong handle = 0;
            Check(_input.GetActionHandle(ActionSet + "/" + name, ref handle), name);
            return handle;
        }

        private static void Check(EVRInputError err, string what)
        {
            if (err != EVRInputError.None)
                throw new Exception($"{what}: {err}");
        }

        private const string ActionManifest = @"{
  ""default_bindings"": [
    { ""controller_type"": ""oculus_touch"", ""binding_url"": ""bindings_oculus_touch.json"" },
    { ""controller_type"": ""knuckles"", ""binding_url"": ""bindings_knuckles.json"" }
  ],
  ""action_sets"": [ { ""name"": ""/actions/aoq"", ""usage"": ""single"" } ],
  ""actions"": [
    { ""name"": ""/actions/aoq/in/trigger"",         ""type"": ""vector1"", ""requirement"": ""mandatory"" },
    { ""name"": ""/actions/aoq/in/grip"",            ""type"": ""vector1"", ""requirement"": ""mandatory"" },
    { ""name"": ""/actions/aoq/in/thumbstick"",      ""type"": ""vector2"", ""requirement"": ""mandatory"" },
    { ""name"": ""/actions/aoq/in/thumbclick"",      ""type"": ""boolean"", ""requirement"": ""suggested"" },
    { ""name"": ""/actions/aoq/in/ax"",              ""type"": ""boolean"", ""requirement"": ""mandatory"" },
    { ""name"": ""/actions/aoq/in/by"",              ""type"": ""boolean"", ""requirement"": ""mandatory"" },
    { ""name"": ""/actions/aoq/in/menu"",            ""type"": ""boolean"", ""requirement"": ""suggested"" },
    { ""name"": ""/actions/aoq/in/touchax"",         ""type"": ""boolean"", ""requirement"": ""optional"" },
    { ""name"": ""/actions/aoq/in/touchby"",         ""type"": ""boolean"", ""requirement"": ""optional"" },
    { ""name"": ""/actions/aoq/in/touchthumb"",      ""type"": ""boolean"", ""requirement"": ""optional"" },
    { ""name"": ""/actions/aoq/in/touchthumbrest"",  ""type"": ""boolean"", ""requirement"": ""optional"" },
    { ""name"": ""/actions/aoq/in/touchtrigger"",    ""type"": ""boolean"", ""requirement"": ""optional"" },
    { ""name"": ""/actions/aoq/out/haptic"",         ""type"": ""vibration"" }
  ],
  ""localization"": [ {
    ""language_tag"": ""en_US"",
    ""/actions/aoq"": ""Attack on Quest"",
    ""/actions/aoq/in/trigger"": ""Trigger (hooks)"",
    ""/actions/aoq/in/grip"": ""Grip (blades)"",
    ""/actions/aoq/in/thumbstick"": ""Thumbstick"",
    ""/actions/aoq/in/thumbclick"": ""Thumbstick click"",
    ""/actions/aoq/in/ax"": ""A / X"",
    ""/actions/aoq/in/by"": ""B / Y"",
    ""/actions/aoq/in/menu"": ""Menu"",
    ""/actions/aoq/in/touchax"": ""Touch A / X"",
    ""/actions/aoq/in/touchby"": ""Touch B / Y"",
    ""/actions/aoq/in/touchthumb"": ""Touch thumbstick"",
    ""/actions/aoq/in/touchthumbrest"": ""Touch thumb rest"",
    ""/actions/aoq/in/touchtrigger"": ""Touch trigger"",
    ""/actions/aoq/out/haptic"": ""Vibration""
  } ]
}";

        // The left menu button is /input/menu on Quest controllers through Steam Link (what
        // SteamVR binds for OpenXR games like Beat Saber) and /input/system on the Oculus
        // driver's Touch profile; bind both.
        private const string TouchBindings = @"{
  ""action_manifest_version"": 0,
  ""controller_type"": ""oculus_touch"",
  ""name"": ""UnRevive defaults"",
  ""description"": ""Same layout as the Oculus runtime"",
  ""bindings"": {
    ""/actions/aoq"": {
      ""haptics"": [
        { ""output"": ""/actions/aoq/out/haptic"", ""path"": ""/user/hand/left/output/haptic"" },
        { ""output"": ""/actions/aoq/out/haptic"", ""path"": ""/user/hand/right/output/haptic"" }
      ],
      ""sources"": [
        { ""path"": ""/user/hand/left/input/trigger"", ""mode"": ""trigger"", ""inputs"": { ""pull"": { ""output"": ""/actions/aoq/in/trigger"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchtrigger"" } } },
        { ""path"": ""/user/hand/right/input/trigger"", ""mode"": ""trigger"", ""inputs"": { ""pull"": { ""output"": ""/actions/aoq/in/trigger"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchtrigger"" } } },
        { ""path"": ""/user/hand/left/input/grip"", ""mode"": ""trigger"", ""inputs"": { ""pull"": { ""output"": ""/actions/aoq/in/grip"" } } },
        { ""path"": ""/user/hand/right/input/grip"", ""mode"": ""trigger"", ""inputs"": { ""pull"": { ""output"": ""/actions/aoq/in/grip"" } } },
        { ""path"": ""/user/hand/left/input/joystick"", ""mode"": ""joystick"", ""inputs"": { ""position"": { ""output"": ""/actions/aoq/in/thumbstick"" }, ""click"": { ""output"": ""/actions/aoq/in/thumbclick"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchthumb"" } } },
        { ""path"": ""/user/hand/right/input/joystick"", ""mode"": ""joystick"", ""inputs"": { ""position"": { ""output"": ""/actions/aoq/in/thumbstick"" }, ""click"": { ""output"": ""/actions/aoq/in/thumbclick"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchthumb"" } } },
        { ""path"": ""/user/hand/left/input/x"", ""mode"": ""button"", ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/ax"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchax"" } } },
        { ""path"": ""/user/hand/left/input/y"", ""mode"": ""button"", ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/by"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchby"" } } },
        { ""path"": ""/user/hand/right/input/a"", ""mode"": ""button"", ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/ax"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchax"" } } },
        { ""path"": ""/user/hand/right/input/b"", ""mode"": ""button"", ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/by"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchby"" } } },
        { ""path"": ""/user/hand/left/input/thumbrest"", ""mode"": ""button"", ""inputs"": { ""touch"": { ""output"": ""/actions/aoq/in/touchthumbrest"" } } },
        { ""path"": ""/user/hand/right/input/thumbrest"", ""mode"": ""button"", ""inputs"": { ""touch"": { ""output"": ""/actions/aoq/in/touchthumbrest"" } } },
        { ""path"": ""/user/hand/left/input/menu"", ""mode"": ""button"", ""parameters"": { ""force_input"": ""click"" }, ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/menu"" } } },
        { ""path"": ""/user/hand/left/input/system"", ""mode"": ""button"", ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/menu"" } } }
      ]
    }
  }
}";

        // Index: same layout, with the left trackpad click as the menu button (the system
        // buttons are SteamVR's).
        private const string KnucklesBindings = @"{
  ""action_manifest_version"": 0,
  ""controller_type"": ""knuckles"",
  ""name"": ""UnRevive defaults"",
  ""description"": ""Touch layout on Index controllers"",
  ""bindings"": {
    ""/actions/aoq"": {
      ""haptics"": [
        { ""output"": ""/actions/aoq/out/haptic"", ""path"": ""/user/hand/left/output/haptic"" },
        { ""output"": ""/actions/aoq/out/haptic"", ""path"": ""/user/hand/right/output/haptic"" }
      ],
      ""sources"": [
        { ""path"": ""/user/hand/left/input/trigger"", ""mode"": ""trigger"", ""inputs"": { ""pull"": { ""output"": ""/actions/aoq/in/trigger"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchtrigger"" } } },
        { ""path"": ""/user/hand/right/input/trigger"", ""mode"": ""trigger"", ""inputs"": { ""pull"": { ""output"": ""/actions/aoq/in/trigger"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchtrigger"" } } },
        { ""path"": ""/user/hand/left/input/grip"", ""mode"": ""trigger"", ""inputs"": { ""pull"": { ""output"": ""/actions/aoq/in/grip"" } } },
        { ""path"": ""/user/hand/right/input/grip"", ""mode"": ""trigger"", ""inputs"": { ""pull"": { ""output"": ""/actions/aoq/in/grip"" } } },
        { ""path"": ""/user/hand/left/input/thumbstick"", ""mode"": ""joystick"", ""inputs"": { ""position"": { ""output"": ""/actions/aoq/in/thumbstick"" }, ""click"": { ""output"": ""/actions/aoq/in/thumbclick"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchthumb"" } } },
        { ""path"": ""/user/hand/right/input/thumbstick"", ""mode"": ""joystick"", ""inputs"": { ""position"": { ""output"": ""/actions/aoq/in/thumbstick"" }, ""click"": { ""output"": ""/actions/aoq/in/thumbclick"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchthumb"" } } },
        { ""path"": ""/user/hand/left/input/a"", ""mode"": ""button"", ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/ax"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchax"" } } },
        { ""path"": ""/user/hand/left/input/b"", ""mode"": ""button"", ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/by"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchby"" } } },
        { ""path"": ""/user/hand/right/input/a"", ""mode"": ""button"", ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/ax"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchax"" } } },
        { ""path"": ""/user/hand/right/input/b"", ""mode"": ""button"", ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/by"" }, ""touch"": { ""output"": ""/actions/aoq/in/touchby"" } } },
        { ""path"": ""/user/hand/left/input/trackpad"", ""mode"": ""button"", ""inputs"": { ""click"": { ""output"": ""/actions/aoq/in/menu"" } } }
      ]
    }
  }
}";
    }
}
