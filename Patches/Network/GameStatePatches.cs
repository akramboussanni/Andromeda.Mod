using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Andromeda.Mod.Patches.Network
{
    [HarmonyPatch]
    public static class AndromedaPhaseClockDesyncPatch
    {
        private static AndromedaShared.RoundPhase _lastPhaseHandled = AndromedaShared.RoundPhase.None;
        private static float _lastPhaseTimeReceivedAt = -999f;
        private static bool _initialPhaseAdvanceApplied;

        [HarmonyPatch(typeof(AndromedaClient), "OnLoadLevel")]
        [HarmonyPrefix]
        public static void ResetPhaseGate()
        {
            _lastPhaseHandled = AndromedaShared.RoundPhase.None;
            _lastPhaseTimeReceivedAt = -999f;
            _initialPhaseAdvanceApplied = false;
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnPhaseTime")]
        [HarmonyPrefix]
        public static bool PrefixOnPhaseTime(AndromedaClient __instance)
        {
            if ((UnityEngine.Object)__instance == (UnityEngine.Object)null) return true;

            // Rate-limit identical phase updates to prevent "double-step" bugs
            // Some server transitions send redundant PhaseTime packets
            var currentPhase = __instance.Phase;
            bool samePhase = currentPhase == _lastPhaseHandled;
            bool recentUpdate = Time.time - _lastPhaseTimeReceivedAt < 2.0f;

            if (samePhase && recentUpdate)
            {
                // We already handled a PhaseTime message for this phase recently, skip UI re-advance
                return false;
            }

            _lastPhaseHandled = currentPhase;
            _lastPhaseTimeReceivedAt = Time.time;
            return true;
        }

        [HarmonyPatch(typeof(PhaseClock), "SetEndTime")]
        [HarmonyPrefix]
        public static void RefinedPhaseAdvanceSync(ref bool trackPhase)
        {
            var client = AndromedaClient.Instance;
            if (client == null) return;

            // ReadyRoom can fire multiple SetEndTime updates.
            // Apply the first phase advance once at match start, then suppress repeats.
            if (client.Phase == AndromedaShared.RoundPhase.ReadyRoom)
            {
                if (!_initialPhaseAdvanceApplied)
                {
                    _initialPhaseAdvanceApplied = true;
                    trackPhase = true;
                    return;
                }

                trackPhase = false;
            }
        }
    }

    [HarmonyPatch(typeof(AndromedaClient), "OnSetPlayerSelections")]
    public static class AndromedaSelectionDesyncPatch
    {
        private static float _lastSelectionsReceivedAt = -10f;

        [HarmonyPrefix]
        public static bool Prefix()
        {
            float now = Time.time;
            if (now - _lastSelectionsReceivedAt < 1.0f)
            {
                // Rate-limit selection updates to prevent "Key already added" crashes in UI
                // caused by redundant network packets or double-initialization.
                return false;
            }
            _lastSelectionsReceivedAt = now;
            return true;
        }
    }

    [HarmonyPatch]
    public static class EarlyArmoryItemGuard
    {
        private static float _warningUntil;
        private static GUIStyle _warningStyle;
        private static bool _armoryReachedThisRound;
        private static bool _cheatLockActiveThisRound;
        private static bool _alertPlayedThisRound;

        private static readonly HashSet<ItemSpawnList.Key> EarlyArmoryRestrictedItems = new HashSet<ItemSpawnList.Key>
        {
            ItemSpawnList.Key.Wrench_charged,
            ItemSpawnList.Key.Wrench_uncharged,
            ItemSpawnList.Key.Sledgehammer_charged,
            ItemSpawnList.Key.Sledgehammer_uncharged,
            ItemSpawnList.Key.ThrowingAxe_charged,
            ItemSpawnList.Key.ThrowingAxe_uncharged,
            ItemSpawnList.Key.Knife_charged,
            ItemSpawnList.Key.Knife_uncharged,
        };

        private static bool ShouldBlock(ItemSpawnList.Key key)
        {
            var client = AndromedaClient.Instance;
            if (client == null) return false;
            if (!EarlyArmoryRestrictedItems.Contains(key)) return false;

            if (client.Phase == AndromedaShared.RoundPhase.Armory)
                _armoryReachedThisRound = true;

            return !_armoryReachedThisRound;
        }

        private static void TriggerWarning()
        {
            _cheatLockActiveThisRound = true;
            _warningUntil = Time.time + 99999f;

            if (_alertPlayedThisRound)
                return;

            _alertPlayedThisRound = true;
            try
            {
                var client = AndromedaClient.Instance;
                if ((UnityEngine.Object)client == (UnityEngine.Object)null)
                    return;

                var tr = Traverse.Create(client);
                var clip = tr.Field("RoundStartVoiceLine").GetValue<AudioClip>()
                    ?? tr.Field("RoundStartAudio").GetValue<AudioClip>();
                var mixer = tr.Field("mixerGroup").GetValue<UnityEngine.Audio.AudioMixerGroup>();
                if ((UnityEngine.Object)clip != (UnityEngine.Object)null && (UnityEngine.Object)OneShotAudioPlayer.Instance != (UnityEngine.Object)null)
                {
                    // Stack the same alert 3x to make the punishment cue much louder.
                    OneShotAudioPlayer.Instance.PlayNonSpatialized(clip, mixer, 1f, 0.75f);
                    OneShotAudioPlayer.Instance.PlayNonSpatialized(clip, mixer, 1f, 0.8f);
                    OneShotAudioPlayer.Instance.PlayNonSpatialized(clip, mixer, 1f, 0.7f);
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(WorldItem), "Interact")]
        [HarmonyPrefix]
        public static bool PrefixWorldItemInteract(WorldItem __instance)
        {
            if ((UnityEngine.Object)__instance == (UnityEngine.Object)null) return true;
            if (!ShouldBlock(__instance.Key)) return true;

            TriggerWarning();
            return false;
        }

        [HarmonyPatch(typeof(ToolbeltServer), "PickupItem")]
        [HarmonyPrefix]
        public static bool PrefixToolbeltPickup(ItemSpawnList.Key key)
        {
            if (!ShouldBlock(key)) return true;

            TriggerWarning();
            return false;
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnLoadLevel")]
        [HarmonyPrefix]
        public static void ResetRoundArmoryState()
        {
            _armoryReachedThisRound = false;
            _cheatLockActiveThisRound = false;
            _alertPlayedThisRound = false;
            _warningUntil = -1f;
            GameInput.IsBlockedCinematic = false;
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnPhaseTime")]
        [HarmonyPrefix]
        public static void TrackArmoryReached(AndromedaClient __instance)
        {
            if ((UnityEngine.Object)__instance == (UnityEngine.Object)null) return;
            if (__instance.Phase == AndromedaShared.RoundPhase.Armory)
                _armoryReachedThisRound = true;
        }

        public static void DrawWarningOverlay()
        {
            if (_cheatLockActiveThisRound)
            {
                // Keep gameplay input blocked for the current round as anti-cheat punishment.
                GameInput.IsBlockedCinematic = true;
            }

            if (Time.time > _warningUntil) return;

            if (_warningStyle == null)
            {
                _warningStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 72,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.2f, 0.2f, 1f) }
                };
            }

            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.Label(full, "PISS YOURSELF", _warningStyle);
        }
    }
}
