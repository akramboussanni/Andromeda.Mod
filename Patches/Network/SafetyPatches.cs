using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UniRx.Async;
using UnityEngine;

namespace Andromeda.Mod.Patches.Network
{
    [HarmonyPatch(typeof(Scenes), "Load", new[] { typeof(string[]) })]
    public static class ScenesLoadPathSafetyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string[] paths)
        {
            if (paths == null) return;

            int originalCount = paths.Length;
            paths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

            if (paths.Length != originalCount)
            {
                MelonLogger.Warning($"[SCENES] Removed {originalCount - paths.Length} invalid scene path(s) before load.");
            }
        }
    }

    [HarmonyPatch(typeof(PurchaseFundsModal), "Start")]
    public static class PurchaseFundsModalStartSafetyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PurchaseFundsModal __instance)
        {
            SafeStart(__instance);
            return false;
        }

        private static async void SafeStart(PurchaseFundsModal modal)
        {
            await UniTask.WaitUntil(() => ApiData.IsLoaded);

            var tr = Traverse.Create(modal);
            var offerRoot = tr.Field("offerRoot").GetValue<Transform>();
            var offerPrefab = tr.Field("fundOfferPrefab").GetValue<FundOfferListItem>();
            var offerDisplayData = tr.Field("offerDisplayData").GetValue<PurchaseFundsModal.FundOfferDisplayData[]>();

            if (offerRoot == null || offerPrefab == null || offerDisplayData == null)
            {
                MelonLogger.Warning("[FUNDS] PurchaseFundsModal missing references; skipping offer render.");
                return;
            }

            foreach (Transform child in offerRoot.Cast<Transform>().ToList())
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            var offers = ApiData.FundOffers;
            if (offers == null || offers.Count == 0)
            {
                MelonLogger.Warning("[FUNDS] No fund offers available from API; purchase modal will be empty.");
                return;
            }

            var baselineOffer = offers.Values
                .Where(o => o != null && o.cost > 0)
                .OrderBy(o => o.amount / o.cost)
                .FirstOrDefault();

            if (baselineOffer == null)
            {
                MelonLogger.Warning("[FUNDS] No valid fund offers with positive cost; purchase modal will be empty.");
                return;
            }

            float baselineRatio = baselineOffer.amount / baselineOffer.cost;
            if (baselineRatio <= 0f) baselineRatio = 1f;

            foreach (var display in offerDisplayData)
            {
                ApiClient.FundOfferData offer;
                if (!offers.TryGetValue(display.guid, out offer) || offer == null) continue;
                if (offer.cost <= 0) continue;

                float ratio = offer.amount / offer.cost;
                float discount = ratio / baselineRatio - 1f;

                UnityEngine.Object.Instantiate(offerPrefab, offerRoot).Initialize(
                    display.guid,
                    offer.description,
                    offer.cost,
                    discount,
                    display.icon,
                    () => tr.Method("InitiatePurchase", offer).GetValue()
                );
            }
        }
    }

    [HarmonyPatch]
    public static class VoiceUIHUDSafetyPatch
    {
        private static FieldInfo[] _cachedFields;
        public static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VoiceUIHUD"), "Initialize");

        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                // Optimization: Cache reflection fields to prevent performance hits if Initialize() is called repeatedly.
                if (_cachedFields == null)
                {
                    _cachedFields = __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }

                foreach (var field in _cachedFields)
                {
                    if (typeof(System.Collections.IDictionary).IsAssignableFrom(field.FieldType))
                    {
                        var dict = field.GetValue(__instance) as System.Collections.IDictionary;
                        dict?.Clear();
                    }
                }
            }
            catch { }
        }
    }

    [HarmonyPatch]
    public static class VoiceClientSafetyPatch
    {
        public static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VoiceClient"), "GetPlayerVolume");

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, ref float __result)
        {
            if (__exception != null)
            {
                // Safety: Silence NREs in VoiceClient during UI rendering loops.
                // These usually occur when the Sidebar UI is trying to render player volume data
                // that hasn't arrived over the network yet.
                __result = 0f;
                return null; // Suppress the exception
            }
            return __exception;
        }
    }
}
