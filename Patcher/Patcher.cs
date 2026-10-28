using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;

namespace UnRevive.Patcher
{
    // BepInEx preloader patcher. It doesn't patch any assemblies; it runs this early so the
    // game files are in place before Unity starts its XR device:
    //   1. puts "OpenVR" first in the build's VR SDK list in globalgamemanagers (the shipped
    //      build only lists "Oculus", so Unity never tries anything else)
    //   2. drops an openvr_api.dll into <Game>_Data/Plugins/x86_64 for Unity (and Oculus.VR's
    //      own OpenVR input path) to load: OpenComposite's, which runs OpenVR on whatever
    //      OpenXR runtime is active (UseOpenXR, the default), or Valve's, which needs SteamVR
    public static class Patcher
    {
        // The plugin's config file (its GUID) and setting, read here before Unity starts.
        private const string ConfigFileName = "aoq.unrevive.cfg";
        private const string UseOpenXRDesc =
            "Run through OpenXR (OpenComposite) on whatever OpenXR runtime is active. Off: " +
            "Valve's OpenVR, which needs SteamVR. Takes effect on restart.";

        private const string OpenVRDevice = "OpenVR";
        private const string BackupSuffix = ".unrevive-backup";

        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        public static void Patch(AssemblyDefinition assembly) { }

        private static readonly ManualLogSource Log = Logger.CreateLogSource("UnRevive.Patcher");

        public static void Initialize()
        {
            try
            {
                var config = new ConfigFile(Path.Combine(Paths.ConfigPath, ConfigFileName), true);
                bool useOpenXR = config.Bind("Runtime", "UseOpenXR", true, UseOpenXRDesc).Value;

                string dataDir = Directory.GetParent(Paths.ManagedPath).FullName;
                InstallOpenVRApi(dataDir, useOpenXR);
                PatchVRDeviceList(dataDir);
            }
            catch (Exception e)
            {
                Log.LogError($"Setup failed, the game will start with its original VR settings: {e}");
            }
        }

        private static void InstallOpenVRApi(string dataDir, bool useOpenXR)
        {
            string patcherDir = Path.GetDirectoryName(typeof(Patcher).Assembly.Location);
            string source = useOpenXR
                ? Path.Combine(Path.Combine(patcherDir, "OpenComposite"), "openvr_api.dll")
                : Path.Combine(patcherDir, "openvr_api.dll");
            string target = Path.Combine(Path.Combine(dataDir, "Plugins"), Path.Combine("x86_64", "openvr_api.dll"));
            string what = useOpenXR ? "OpenComposite (OpenXR)" : "Valve OpenVR (SteamVR)";

            if (!File.Exists(source))
            {
                Log.LogError($"{source} is missing; reinstall UnRevive.");
                return;
            }
            if (!File.Exists(target) || !File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(source)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target, true);
            }
            Log.LogInfo($"Runtime: {what}");
        }

        private static void PatchVRDeviceList(string dataDir)
        {
            string path = Path.Combine(dataDir, "globalgamemanagers");
            var ggm = new GlobalGameManagers(File.ReadAllBytes(path));

            var wanted = new List<string> { OpenVRDevice };
            wanted.AddRange(ggm.VRDevices.Where(d => d != OpenVRDevice));
            if (ggm.VRDevices.SequenceEqual(wanted))
                return;

            // Keep a pristine copy the first time we touch an unmodded file.
            string backup = path + BackupSuffix;
            if (!ggm.VRDevices.Contains(OpenVRDevice) && !File.Exists(backup))
                File.Copy(path, backup);

            byte[] patched = ggm.WithVRDevices(wanted);
            new GlobalGameManagers(patched); // re-parse as a sanity check before replacing the file

            string temp = path + ".tmp";
            File.WriteAllBytes(temp, patched);
            File.Copy(temp, path, true);
            File.Delete(temp);

            Log.LogInfo($"VR SDK list [{string.Join(", ", ggm.VRDevices.ToArray())}] -> [{string.Join(", ", wanted.ToArray())}]");
        }
    }
}
