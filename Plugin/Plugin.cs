using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine.XR;

// The built-in (legacy) XR APIs below are marked obsolete in 2019.4 in favour of XR plugin
// subsystems, but the game uses built-in XR, where these are the calls that apply.
#pragma warning disable CS0618

namespace UnRevive
{
    // Runtime half of UnRevive. The preloader patcher (UnRevive.Patcher) makes Unity start
    // OpenVR (OpenComposite on OpenXR, or SteamVR); this plugin fills in what the game's
    // Oculus-first code expects from the Oculus runtime. Everything is a no-op on Oculus.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid    = "aoq.unrevive";
        public const string PluginName    = "UnRevive";
        public const string PluginVersion = "1.0.0";

        internal const string OpenVRDevice = "OpenVR";

        internal static BepInEx.Logging.ManualLogSource Log;

        // In the order the mod manager lists them.
        internal static ConfigEntry<bool> RecenterNow;
        internal static ConfigEntry<bool> RealHeight;
        internal static ConfigEntry<float> EyeHeight;
        internal static ConfigEntry<float> ControllerPitch;
        internal static ConfigEntry<bool> MenuOnDashboard;
        internal static ConfigEntry<bool> DashboardClosesMenu;

        private bool _trackingConfigured;

        internal static bool OnOpenVR => OVRManager.loadedXRDevice == OVRManager.XRDevice.OpenVR;

        private void Awake()
        {
            Log = Logger;

            RecenterNow = Config.Bind("Tracking", "RecenterNow", false,
                "A button: press it while standing normally. The salute ring fills for a second, then " +
                "your current height and position become the starting point.");
            RecenterNow.SettingChanged += (_, __) =>
            {
                if (!RecenterNow.Value)
                    return;
                RecenterNow.Value = false;
                MenuButton.StartRecenterCountdown();
            };
            RecenterNow.Value = false;

            RealHeight = Config.Bind("Tracking", "RealHeight", false,
                "Off: like the Oculus build, your head height when the game starts (or recenters) " +
                "becomes EyeHeight. On: your real height above the runtime's floor, which is only " +
                "right if the runtime's floor is set right.");

            EyeHeight = Config.Bind("Tracking", "EyeHeight", 1.675f,
                "Eye height in metres when RealHeight is off. The Oculus runtime takes it from your " +
                "Oculus profile; 1.675 is its default.");

            ControllerPitch = Config.Bind("Input", "ControllerPitch", 40f,
                "Degrees the hands/blades are tilted down from the raw controller pose to match the " +
                "Oculus pose. Raise it if blades point too far up, lower it if they point down.");

            MenuOnDashboard = Config.Bind("Input", "MenuOnDashboard", true,
                "SteamVR keeps the left menu button for its dashboard. When a press opens the " +
                "dashboard (or anything else takes focus from the game), open the game menu.");

            DashboardClosesMenu = Config.Bind("Input", "DashboardClosesMenu", false,
                "Also close the game menu on that press if it's already open, like the Oculus menu " +
                "button. Off: close it with the laser.");

            // Read by the patcher before Unity starts; bound here so the mod manager shows it.
            Config.Bind("Runtime", "UseOpenXR", true,
                "Run through OpenXR (OpenComposite) on whatever OpenXR runtime is active. Off: " +
                "Valve's OpenVR, which needs SteamVR. Takes effect on restart.");

            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            // Patching OVRHaptics.Config ran its static constructor (and so Load) unpatched;
            // load again so the fallback haptics values are in place before any scene uses them.
            OVRHaptics.Config.Load();

            if (!XRSettings.supportedDevices.Contains(OpenVRDevice))
                Log.LogWarning("OpenVR isn't in the game's VR SDK list; is BepInEx/patchers/UnRevive installed?");
        }

        private void LateUpdate() => MenuButton.Radial.LateUpdate();

        private void Update()
        {
            if (_trackingConfigured || XRSettings.loadedDeviceName != OpenVRDevice || !XRDevice.isPresent)
                return;
            _trackingConfigured = true;

            // Always the standing (floor) space: it's well defined on every runtime, while the
            // seated one is wherever the user last reset it. The Oculus eye-level origin is
            // emulated on top of it (TrackingPatches.Origin).
            XRDevice.SetTrackingSpaceType(TrackingSpaceType.RoomScale);
            Log.LogInfo($"Running on OpenVR ({XRDevice.model})");
        }
    }
}
