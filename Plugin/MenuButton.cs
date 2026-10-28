using HarmonyLib;
using OVR.OpenVR;
using UnityEngine;
using UnityEngine.UI;

namespace UnRevive
{
    // The left menu button (the game's Start button: its only way to open the menu).
    //  - SteamVR, through OpenVR or OpenXR, never passes it to the game; a press opens the
    //    SteamVR dashboard. Like games that pause when that happens, losing input focus opens
    //    the game menu (Focus).
    //  - Runtimes that do pass it (e.g. Meta's OpenXR runtime): held past MenuHold and released,
    //    it's Start; held on to RecenterHold, it recenters instead, like holding the Oculus
    //    button, with the game's salute radial filling up as you hold.
    //  - RecenterNow in the mod manager starts the same recenter, after a short countdown.
    internal static class MenuButton
    {
        private const float MenuHold = 0.5f;
        private const float RecenterHold = 1.5f;
        private const float RecenterCountdown = 1f;

        private static float? _pressedAt;
        private static bool _recentered;
        private static float? _countdownAt;

        // True for the one OVRInput update after a menu-length hold is released, so the game
        // sees a single Start press.
        internal static bool StartPressed { get; private set; }

        // 0..1 while a hold or countdown is heading for a recenter; null otherwise.
        internal static float? RecenterProgress { get; private set; }

        internal static void StartRecenterCountdown() => _countdownAt = Time.realtimeSinceStartup;

        // Once per frame, with whether the left menu button is down.
        internal static void Tick(bool down)
        {
            float now = Time.realtimeSinceStartup;
            StartPressed = false;
            RecenterProgress = null;

            if (_countdownAt.HasValue)
            {
                float t = (now - _countdownAt.Value) / RecenterCountdown;
                if (t >= 1f)
                {
                    _countdownAt = null;
                    TrackingPatches.Origin.Recenter();
                }
                else
                {
                    RecenterProgress = t;
                }
            }

            if (!down)
            {
                if (_pressedAt.HasValue && !_recentered && now - _pressedAt.Value >= MenuHold)
                    StartPressed = true;
                _pressedAt = null;
                _recentered = false;
                return;
            }

            if (!_pressedAt.HasValue)
                _pressedAt = now;
            float held = now - _pressedAt.Value;
            if (_recentered || held < MenuHold || _countdownAt.HasValue)
                return;
            if (held >= RecenterHold)
            {
                _recentered = true;
                TrackingPatches.Origin.Recenter();
                return;
            }
            RecenterProgress = (held - MenuHold) / (RecenterHold - MenuHold);
        }

        // Opens the game menu when the game loses input focus (the SteamVR dashboard opened).
        // Input has to have been available for a moment first, so start-up focus changes
        // don't count.
        internal static class Focus
        {
            private const float SettleSeconds = 1f;

            private static readonly AccessTools.FieldRef<OVRPlayerController, bool> MenuOpen =
                AccessTools.FieldRefAccess<OVRPlayerController, bool>("menuToggle");

            private static float? _focusedSince;
            private static OVRPlayerController _player;

            internal static void Tick()
            {
                CVRSystem system = OpenVR.System;
                if (system == null || !Plugin.MenuOnDashboard.Value)
                    return;
                float now = Time.realtimeSinceStartup;
                if (system.IsInputAvailable())
                {
                    if (!_focusedSince.HasValue)
                        _focusedSince = now;
                    return;
                }
                bool settled = _focusedSince.HasValue && now - _focusedSince.Value >= SettleSeconds;
                _focusedSince = null;
                if (settled)
                    OpenGameMenu();
            }

            // The game's own open flag can disagree with what's on screen (the menu starts
            // open on first load, and some multiplayer paths hide it directly), so go by the
            // menu object, and fix the flag before toggling or the toggle goes the wrong way.
            private static void OpenGameMenu()
            {
                if (!_player)
                    _player = Object.FindObjectOfType<OVRPlayerController>();
                if (!_player || !_player.nonInteractMenu)
                    return;
                bool visible = _player.nonInteractMenu.activeSelf;
                if (visible && !Plugin.DashboardClosesMenu.Value)
                    return;
                MenuOpen(_player) = visible;
                _player.MenuToggle();
            }
        }

        // Shows RecenterProgress on the game's salute radial (the ring that fills while you
        // salute, on the head-locked HUD). Runs in LateUpdate, after Salute.Update sets it.
        internal static class Radial
        {
            private static readonly AccessTools.FieldRef<Salute, Image> SaluteRadial =
                AccessTools.FieldRefAccess<Salute, Image>("saluteRadial");
            private static readonly AccessTools.FieldRef<Salute, bool> SaluteCompleted =
                AccessTools.FieldRefAccess<Salute, bool>("saluteCompleted");

            private static Salute _salute;
            private static bool _showing;

            internal static void LateUpdate()
            {
                float? progress = RecenterProgress;
                if (!progress.HasValue && !_showing)
                    return;
                if (!_salute)
                {
                    GameObject player = PlayerManager.instance ? PlayerManager.instance.player : null;
                    _salute = player ? player.GetComponent<Salute>() : Object.FindObjectOfType<Salute>();
                    if (!_salute)
                        return;
                }
                Image radial = SaluteRadial(_salute);
                if (!radial)
                    return;

                if (progress.HasValue)
                {
                    radial.enabled = true;
                    radial.fillAmount = progress.Value;
                    _showing = true;
                }
                else
                {
                    // Hand the ring back to the salute; it sets its own fill next Update.
                    radial.enabled = _salute.isActiveAndEnabled && !SaluteCompleted(_salute);
                    _showing = false;
                }
            }
        }
    }
}
