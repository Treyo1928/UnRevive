using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace UnRevive
{
    // OVRInput has an OpenVR path, but it reads the legacy controller state and only maps the
    // trigger, grip, thumbstick and B/Y. The game also uses A/X, the capacitive touch states
    // and the left menu button (Start), and it ignores any controller not reported as Oculus
    // Touch. With SteamVR Input (SteamVRInput.cs) the whole Touch state comes from actions; if
    // that couldn't be set up, the missing legacy inputs are added instead.
    internal static class InputPatches
    {
        // EVRButtonId bits in VRControllerState_t.ulButtonPressed / ulButtonTouched
        private const ulong ButtonSystem  = 1UL << 0;   // left menu
        private const ulong ButtonAppMenu = 1UL << 1;   // B / Y
        private const ulong ButtonA       = 1UL << 7;   // A / X
        private const ulong ButtonAxis0   = 1UL << 32;  // thumbstick
        private const ulong ButtonAxis1   = 1UL << 33;  // index trigger

        private const int LeftHand  = 0;
        private const int RightHand = 1;
        private const uint NoDevice = 64; // OVRInput's "not connected" device index

        [HarmonyPatch]
        private static class GetOpenVRControllerStatePatch
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(AccessTools.Inner(typeof(OVRInput), "OVRControllerBase"), "GetOpenVRControllerState");

            private static void Postfix(OVRInput.Controller controllerType, ref OVRPlugin.ControllerState4 __result)
            {
                if ((controllerType & OVRInput.Controller.LTouch) == OVRInput.Controller.LTouch)
                    FillHand(LeftHand, ref __result);
                if ((controllerType & OVRInput.Controller.RTouch) == OVRInput.Controller.RTouch)
                    FillHand(RightHand, ref __result);
            }
        }

        // Once per frame, before OVRInput reads the controllers: read SteamVR Input and run the
        // menu button. Legacy controller state is from the previous frame here, which is fine
        // for timing a hold.
        [HarmonyPatch(typeof(OVRInput), nameof(OVRInput.Update))]
        private static class UpdatePatch
        {
            private static void Prefix()
            {
                if (!Plugin.OnOpenVR)
                    return;
                SteamVRInput.Update();
                bool menu;
                if (SteamVRInput.Active)
                    menu = SteamVRInput.Get(LeftHand).Menu;
                else
                    menu = OVRInput.openVRControllerDetails[LeftHand].deviceID < NoDevice &&
                           (OVRInput.openVRControllerDetails[LeftHand].state.ulButtonPressed & ButtonSystem) != 0;
                MenuButton.Tick(menu);
                MenuButton.Focus.Tick();
            }
        }

        // Legacy haptic pulses don't reach apps that use SteamVR Input; use its haptic action.
        [HarmonyPatch(typeof(OVRInput), "PlayHapticImpulse")]
        private static class PlayHapticImpulsePatch
        {
            private static bool Prefix(float amplitude, XRNode deviceNode)
            {
                if (!SteamVRInput.Active)
                    return true;
                if (amplitude > 0f)
                    SteamVRInput.Vibrate(deviceNode == XRNode.LeftHand ? LeftHand : RightHand, amplitude, Time.deltaTime * 2f);
                return false;
            }
        }

        // Read controllers that aren't reported as Oculus Touch (Index, Pico, ...) with the Touch
        // layout; otherwise OVRInput ignores them.
        [HarmonyPatch(typeof(OVRInput), "UpdateXRControllerNodeIds")]
        private static class UpdateXRControllerNodeIdsPatch
        {
            private static void Postfix()
            {
                if (OVRInput.openVRControllerDetails == null)
                    return;
                for (int hand = LeftHand; hand <= RightHand; hand++)
                {
                    if (OVRInput.openVRControllerDetails[hand].deviceID < NoDevice &&
                        OVRInput.openVRControllerDetails[hand].controllerType == OVRInput.OpenVRController.Unknown)
                        OVRInput.openVRControllerDetails[hand].controllerType = OVRInput.OpenVRController.OculusTouch;
                }
            }
        }

        private static void FillHand(int hand, ref OVRPlugin.ControllerState4 state)
        {
            if (OVRInput.openVRControllerDetails[hand].deviceID >= NoDevice)
                return;
            if (SteamVRInput.Active)
                FromActions(hand, ref state);
            else
                AddMissingInputs(hand, ref state);
            if (hand == LeftHand && MenuButton.StartPressed)
                state.Buttons |= (uint)OVRInput.RawButton.Start;
        }

        private static void FromActions(int hand, ref OVRPlugin.ControllerState4 state)
        {
            SteamVRInput.Hand h = SteamVRInput.Get(hand);
            bool left = hand == LeftHand;
            if (left)
            {
                state.LIndexTrigger = h.Trigger;
                state.LHandTrigger = h.Grip;
                state.LThumbstick = new OVRPlugin.Vector2f { x = h.Thumbstick.x, y = h.Thumbstick.y };
            }
            else
            {
                state.RIndexTrigger = h.Trigger;
                state.RHandTrigger = h.Grip;
                state.RThumbstick = new OVRPlugin.Vector2f { x = h.Thumbstick.x, y = h.Thumbstick.y };
            }

            if (h.AX)    state.Buttons |= (uint)(left ? OVRInput.RawButton.X : OVRInput.RawButton.A);
            if (h.BY)    state.Buttons |= (uint)(left ? OVRInput.RawButton.Y : OVRInput.RawButton.B);
            if (h.Thumb) state.Buttons |= (uint)(left ? OVRInput.RawButton.LThumbstick : OVRInput.RawButton.RThumbstick);

            if (h.TouchAX)        state.Touches |= (uint)(left ? OVRInput.RawTouch.X : OVRInput.RawTouch.A);
            if (h.TouchBY)        state.Touches |= (uint)(left ? OVRInput.RawTouch.Y : OVRInput.RawTouch.B);
            if (h.TouchThumb)     state.Touches |= (uint)(left ? OVRInput.RawTouch.LThumbstick : OVRInput.RawTouch.RThumbstick);
            if (h.TouchThumbRest) state.Touches |= (uint)(left ? OVRInput.RawTouch.LThumbRest : OVRInput.RawTouch.RThumbRest);
            if (h.TouchTrigger)   state.Touches |= (uint)(left ? OVRInput.RawTouch.LIndexTrigger : OVRInput.RawTouch.RIndexTrigger);

            // SteamVR has no proximity sensing; a touch is the closest stand-in for "near".
            if (h.TouchAX || h.TouchBY || h.TouchThumb || h.TouchThumbRest)
                state.NearTouches |= (uint)(left ? OVRInput.RawNearTouch.LThumbButtons : OVRInput.RawNearTouch.RThumbButtons);
            if (h.TouchTrigger)
                state.NearTouches |= (uint)(left ? OVRInput.RawNearTouch.LIndexTrigger : OVRInput.RawNearTouch.RIndexTrigger);
        }

        private static void AddMissingInputs(int hand, ref OVRPlugin.ControllerState4 state)
        {
            OVRInput.OpenVRControllerDetails details = OVRInput.openVRControllerDetails[hand];
            bool left = hand == LeftHand;
            ulong pressed = details.state.ulButtonPressed;
            ulong touched = details.state.ulButtonTouched;

            if ((pressed & ButtonA) != 0)
                state.Buttons |= (uint)(left ? OVRInput.RawButton.X : OVRInput.RawButton.A);

            bool touchA     = (touched & ButtonA) != 0;
            bool touchB     = (touched & ButtonAppMenu) != 0;
            bool touchStick = (touched & ButtonAxis0) != 0;
            bool touchIndex = (touched & ButtonAxis1) != 0;

            if (touchA)     state.Touches |= (uint)(left ? OVRInput.RawTouch.X : OVRInput.RawTouch.A);
            if (touchB)     state.Touches |= (uint)(left ? OVRInput.RawTouch.Y : OVRInput.RawTouch.B);
            if (touchStick) state.Touches |= (uint)(left ? OVRInput.RawTouch.LThumbstick : OVRInput.RawTouch.RThumbstick);
            if (touchIndex) state.Touches |= (uint)(left ? OVRInput.RawTouch.LIndexTrigger : OVRInput.RawTouch.RIndexTrigger);

            // SteamVR has no proximity sensing; a touch is the closest stand-in for "near".
            if (touchA || touchB || touchStick)
                state.NearTouches |= (uint)(left ? OVRInput.RawNearTouch.LThumbButtons : OVRInput.RawNearTouch.RThumbButtons);
            if (touchIndex)
                state.NearTouches |= (uint)(left ? OVRInput.RawNearTouch.LIndexTrigger : OVRInput.RawNearTouch.RIndexTrigger);
        }
    }
}
