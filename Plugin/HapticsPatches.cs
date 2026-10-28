using System;
using HarmonyLib;
using UnityEngine.XR;

namespace UnRevive
{
    // OVRHaptics (the clip-based haptics the game's blades use) gets its sample rate and buffer
    // sizes from the Oculus runtime. Without it every value is 0, and building a clip from an
    // AudioClip divides by that rate: the resample step becomes (int)Infinity, the loop index
    // wraps back to 0 and the main thread spins forever. The sword objects build their clips in
    // OnEnable, so the menu scene never finishes loading (Unity splash, then black).
    internal static class HapticsPatches
    {
        // Oculus Touch's values, as the Oculus runtime reports them.
        private const int TouchSampleRateHz        = 320;
        private const int TouchSampleSizeInBytes   = 1;
        private const int TouchMinSafeSamples      = 16;
        private const int TouchMinBufferSamples    = 16;
        private const int TouchOptimalBufferSamples = 20;
        private const int TouchMaxBufferSamples    = 256;

        [HarmonyPatch(typeof(OVRHaptics.Config), nameof(OVRHaptics.Config.Load))]
        private static class ConfigLoadPatch
        {
            private static readonly Action<int>[] Setters =
            {
                Setter(nameof(OVRHaptics.Config.SampleRateHz)),
                Setter(nameof(OVRHaptics.Config.SampleSizeInBytes)),
                Setter(nameof(OVRHaptics.Config.MinimumSafeSamplesQueued)),
                Setter(nameof(OVRHaptics.Config.MinimumBufferSamplesCount)),
                Setter(nameof(OVRHaptics.Config.OptimalBufferSamplesCount)),
                Setter(nameof(OVRHaptics.Config.MaximumBufferSamplesCount)),
            };

            private static readonly int[] TouchValues =
            {
                TouchSampleRateHz, TouchSampleSizeInBytes, TouchMinSafeSamples,
                TouchMinBufferSamples, TouchOptimalBufferSamples, TouchMaxBufferSamples,
            };

            private static void Postfix()
            {
                if (OVRHaptics.Config.SampleRateHz > 0)
                    return; // Oculus runtime answered; leave its values alone
                for (int i = 0; i < Setters.Length; i++)
                    Setters[i](TouchValues[i]);
            }

            private static Action<int> Setter(string property) =>
                AccessTools.MethodDelegate<Action<int>>(
                    AccessTools.PropertySetter(typeof(OVRHaptics.Config), property));
        }

        // OVRHaptics only plays through OVRPlugin, which does nothing on OpenVR. Play each clip
        // as a SteamVR vibration instead, using OVRInput's own OpenVR haptics path.
        [HarmonyPatch]
        private static class ChannelPlayPatch
        {
            private static readonly Action<float, float, XRNode> StartVibration =
                AccessTools.MethodDelegate<Action<float, float, XRNode>>(
                    AccessTools.Method(typeof(OVRInput), "StartVibration"));

            private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                foreach (string name in new[] { "Preempt", "Queue", "Mix" })
                    yield return AccessTools.Method(typeof(OVRHaptics.OVRHapticsChannel), name);
            }

            private static void Postfix(OVRHaptics.OVRHapticsChannel __instance, OVRHapticsClip clip)
            {
                if (!Plugin.OnOpenVR || clip == null || clip.Count == 0 || clip.Samples == null)
                    return;

                int peak = 0;
                int count = Math.Min(clip.Count, clip.Samples.Length);
                for (int i = 0; i < count; i++)
                    if (clip.Samples[i] > peak) peak = clip.Samples[i];
                if (peak == 0)
                    return;

                XRNode hand = __instance == OVRHaptics.LeftChannel ? XRNode.LeftHand : XRNode.RightHand;
                StartVibration(peak / 255f, (float)clip.Count / OVRHaptics.Config.SampleRateHz, hand);
            }
        }
    }
}
