using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace UnRevive
{
    // Game bugs that only bite outside the Oculus PC setup the game shipped for.
    internal static class GameFixes
    {
        private const float MicStartTimeout = 3f;

        // VoiceHandler (menu avatar mouth movement) starts recording on Microphone.devices[0],
        // then busy-waits on Microphone.GetPosition(null), i.e. the *default* device. When the
        // two differ (SteamVR makes the headset mic the default; devices[0] is whatever is
        // listed first) the position never moves and the main thread spins forever: the
        // headset shows the Unity splash, then black. Same setup, but wait on the device we
        // actually started, from a coroutine with a timeout.
        [HarmonyPatch(typeof(VoiceHandler), "Start")]
        private static class VoiceHandlerStartPatch
        {
            private static bool Prefix(VoiceHandler __instance, AudioSource ____audioSource,
                                       GameObject ____customizer, ref SkinnedMeshRenderer ____headMesh)
            {
                ____headMesh = __instance.GetComponent<SkinnedMeshRenderer>();
                if (!____customizer)
                    return false;

                string[] devices = Microphone.devices;
                if (devices.Length == 0)
                {
                    Plugin.Log.LogWarning("No microphone found; avatar mouth movement disabled.");
                    return false;
                }

                string device = devices[0];
                ____audioSource.clip = Microphone.Start(device, true, 10, 44100);
                __instance.StartCoroutine(PlayWhenRecording(____audioSource, device));
                return false;
            }

            private static IEnumerator PlayWhenRecording(AudioSource source, string device)
            {
                float deadline = Time.realtimeSinceStartup + MicStartTimeout;
                while (Microphone.GetPosition(device) <= 0)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Plugin.Log.LogWarning($"Microphone '{device}' didn't start recording; avatar mouth movement disabled.");
                        yield break;
                    }
                    yield return null;
                }
                if (source)
                    source.Play();
            }
        }
    }
}
