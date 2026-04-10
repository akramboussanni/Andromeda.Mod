using Andromeda.Mod.Features;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace Andromeda.Mod.Patches
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
            ForceStartFeature.OnLoadLevel();
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnPhaseTime")]
        [HarmonyPrefix]
        public static bool PrefixOnPhaseTime(AndromedaClient __instance)
        {
            if ((UnityEngine.Object)__instance == (UnityEngine.Object)null) return true;

            var currentPhase = __instance.Phase;
            bool samePhase = currentPhase == _lastPhaseHandled;
            bool recentUpdate = Time.time - _lastPhaseTimeReceivedAt < 2.0f;

            if (samePhase && recentUpdate)
            {
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

            if (ForceStartFeature.ShouldBlockAutoPhaseAdvance(client.Phase))
            {
                trackPhase = false;
                return;
            }

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

    [HarmonyPatch]
    public static class EarlyArmoryItemGuard
    {
        private static float _warningUntil;
        private static GUIStyle _warningStyle;
        private static bool _armoryReachedThisRound;
        private static bool _generatorPhaseReachedThisRound;
        private static bool _cheatLockActiveThisRound;
        private static bool _alertPlayedThisRound;
        private const float VerticalExploitThresholdY = 0.5f;

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
                    OneShotAudioPlayer.Instance.PlayNonSpatialized(clip, mixer, 1f, 0.75f);
                    OneShotAudioPlayer.Instance.PlayNonSpatialized(clip, mixer, 1f, 0.8f);
                    OneShotAudioPlayer.Instance.PlayNonSpatialized(clip, mixer, 1f, 0.7f);
                }
            }
            catch { }
        }

        private static void CheckVerticalExploit()
        {
            if (_cheatLockActiveThisRound) return;
            if (!_generatorPhaseReachedThisRound) return;

            try
            {
                var tuple = PlayerManagerClient.Instance.FetchLocal();
                if (!tuple.Item2) return;

                var localGo = tuple.Item1.GetGameObject();
                if ((UnityEngine.Object)localGo == (UnityEngine.Object)null) return;

                if (localGo.transform.position.y > VerticalExploitThresholdY)
                {
                    TriggerWarning();
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
            _generatorPhaseReachedThisRound = false;
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

            if (__instance.Phase == AndromedaShared.RoundPhase.Crisis)
                _generatorPhaseReachedThisRound = true;

            if (__instance.Phase == AndromedaShared.RoundPhase.Armory)
                _armoryReachedThisRound = true;
        }

        public static void DrawWarningOverlay()
        {
            CheckVerticalExploit();

            if (_cheatLockActiveThisRound)
            {
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
                return false;
            }
            _lastSelectionsReceivedAt = now;
            return true;
        }
    }
}