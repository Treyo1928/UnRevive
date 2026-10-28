using HarmonyLib;
using UnityEngine;

namespace UnRevive
{
    // Tracking values the game normally gets from the Oculus runtime.
    internal static class TrackingPatches
    {
        // OVRPlayerController places the camera rig at (profile eye height - half the capsule)
        // in eye-level mode. The eye height comes from the Oculus user profile; with no Oculus
        // runtime it's 0, which leaves your head at the bottom of the body.
        [HarmonyPatch(typeof(OVRProfile), nameof(OVRProfile.eyeHeight), MethodType.Getter)]
        private static class EyeHeightPatch
        {
            private static void Postfix(ref float __result)
            {
                if (Plugin.OnOpenVR)
                    __result = Plugin.EyeHeight.Value;
            }
        }

        // OVRManager reads the origin type back from OVRPlugin, which answers EyeLevel (0) when
        // it isn't running. Report the mode we emulate. In FloorLevel, OVRPlayerController puts
        // the tracking origin (the runtime's floor) at the bottom of the player capsule.
        [HarmonyPatch(typeof(OVRManager), nameof(OVRManager.trackingOriginType), MethodType.Getter)]
        private static class TrackingOriginPatch
        {
            private static void Postfix(ref OVRManager.TrackingOrigin __result)
            {
                if (Plugin.OnOpenVR)
                    __result = Plugin.RealHeight.Value
                        ? OVRManager.TrackingOrigin.FloorLevel
                        : OVRManager.TrackingOrigin.EyeLevel;
            }
        }

        // The game's own recenter (OVRDisplay.RecenterPose) also resets our origin.
        [HarmonyPatch(typeof(OVRDisplay), nameof(OVRDisplay.RecenterPose))]
        private static class RecenterPatch
        {
            private static void Postfix()
            {
                if (Plugin.OnOpenVR)
                    Origin.Recenter();
            }
        }

        // Emulates the Oculus tracking origin on the runtime's standing space (Plugin.Update).
        // The Oculus origin is where the head was at the last recenter: at eye level, or on the
        // floor below it in floor-level mode. The standing origin is wherever the runtime put
        // the floor and the playspace centre. So once the head is tracked (and on every
        // recenter) take its position, and offset the tracked anchors by it so they move
        // relative to that spot. The offset goes on a node the anchors are moved under, not on
        // the tracking space: the game hangs UI (the tutorial panel) directly off the tracking
        // space, placed for an eye-level origin. Yaw isn't recentered; the game turns the
        // player to follow the head anyway.
        internal static class Origin
        {
            // A head below this in the standing space isn't on someone's shoulders yet.
            private const float MinHeadHeight = 0.3f;

            private static Vector3? _origin;
            private static bool _wasRealHeight = true;
            private static Transform _node;

            // Take a new origin from the next tracked head pose.
            internal static void Recenter() => _origin = null;

            internal static void Apply(OVRCameraRig rig)
            {
                Transform node = Node(rig);
                if (!node)
                    return;
                bool realHeight = Plugin.RealHeight.Value;
                if (!realHeight && _wasRealHeight)
                    Recenter();
                _wasRealHeight = realHeight;

                if (!_origin.HasValue && rig.centerEyeAnchor && rig.centerEyeAnchor.localPosition.y > MinHeadHeight)
                {
                    _origin = rig.centerEyeAnchor.localPosition;
                    Plugin.Log.LogInfo($"Origin set: head {_origin.Value.y:F2} m above the runtime's floor");
                }
                Vector3 origin = _origin.GetValueOrDefault();
                Vector3 offset = new Vector3(-origin.x, realHeight ? 0f : -origin.y, -origin.z);
                if (node.localPosition != offset)
                    node.localPosition = offset;
            }

            // The offset node for this rig, created (and the anchors moved under it) the first
            // time a rig is seen. A new scene has a new rig; the origin carries over, as on Oculus.
            private static Transform Node(OVRCameraRig rig)
            {
                Transform space = rig.trackingSpace;
                if (!space)
                    return null;
                if (_node && _node.parent == space)
                    return _node;

                _node = new GameObject("UnReviveOrigin").transform;
                _node.SetParent(space, false);
                foreach (Transform anchor in new[] { rig.leftEyeAnchor, rig.centerEyeAnchor, rig.rightEyeAnchor,
                                                      rig.leftHandAnchor, rig.rightHandAnchor, rig.trackerAnchor })
                {
                    if (anchor && anchor.parent == space)
                        anchor.SetParent(_node, false);
                }
                return _node;
            }
        }

        // On OpenVR, OVRCameraRig puts the raw controller pose on the hand anchors and only
        // applies its raw->Touch correction (OVRManager.GetOpenVRControllerOffset) to the
        // controller anchors under them. The game hangs its blades off the hand anchors, so
        // they come out pitched up. Move the correction onto the hand anchors themselves, just
        // before UpdateAnchors raises UpdatedAnchors so its listeners see the corrected pose.
        [HarmonyPatch(typeof(OVRCameraRig), "RaiseUpdatedAnchorsEvent")]
        private static class HandAnchorPatch
        {
            // OVRManager.OpenVRTouchPositionOffsetLeft/Right
            private static readonly Vector3 LeftOffset  = new Vector3(0.0075f, -0.005f, -0.0525f);
            private static readonly Vector3 RightOffset = new Vector3(-0.0075f, -0.005f, -0.0525f);

            private static Quaternion _lastLeft, _lastRight;

            private static void Prefix(OVRCameraRig __instance)
            {
                if (!Plugin.OnOpenVR)
                    return;
                Origin.Apply(__instance);
                Quaternion pitch = Quaternion.Euler(Plugin.ControllerPitch.Value, 0f, 0f);
                Correct(__instance.leftHandAnchor, __instance.leftControllerAnchor, LeftOffset, pitch, ref _lastLeft);
                Correct(__instance.rightHandAnchor, __instance.rightControllerAnchor, RightOffset, pitch, ref _lastRight);
            }

            private static void Correct(Transform hand, Transform controller, Vector3 offset,
                                        Quaternion pitch, ref Quaternion lastApplied)
            {
                if (!hand)
                    return;
                // OVRCameraRig skips a hand when tracking is lost (and when only the eyes were
                // updated); don't stack the correction onto a pose we already corrected.
                if (hand.localRotation.Equals(lastApplied))
                    return;

                Quaternion raw = hand.localRotation;
                hand.localPosition += raw * offset;
                hand.localRotation = raw * pitch;
                lastApplied = hand.localRotation;

                if (controller)
                {
                    controller.localPosition = Vector3.zero;
                    controller.localRotation = Quaternion.identity;
                }
            }
        }
    }
}
