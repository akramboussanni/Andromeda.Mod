using HarmonyLib;
using MelonLoader;
using System.Collections.Generic;
using System.Reflection;
using System;
using UniRx.Async;
using Windwalk.Net;
using UnityEngine;

namespace Andromeda.Mod.Patches
{
    // NO CLASS ATTRIBUTE - We use ManualPatch in Mod.cs
    [HarmonyPatch]
    public static class UserStorePatch
    {
        [HarmonyPatch(typeof(UserStore), "AddAll")]
        [HarmonyPrefix]
        public static void PrefixAddAll(IEnumerable<UserStore.AddInfo> info)
        {
            foreach (var i in info)
            {
                MelonLogger.Msg($"[USERSTORE] Attempting to register player: {i.username} (Steam: {i.steamId})");
            }
        }

        [HarmonyPatch(typeof(UserStore), "AddAll")]
        [HarmonyPostfix]
        public static void PostfixAddAll(UniTask __result)
        {
            __result.GetAwaiter().OnCompleted(() => {
                try 
                {
                    __result.GetAwaiter().GetResult();
                    MelonLogger.Msg("[USERSTORE] Player registration process COMPLETED.");
                    
                    // Force a Lobby update now that we have names/ranks
                    TriggerLobbyUpdate();
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[USERSTORE] CRITICAL: Player registration FAILED: {ex.Message}");
                }
            });
        }

        private static void TriggerLobbyUpdate()
        {
            var em = EntityManagerServer.Instance;
            bool found = false;
            
            if (em != null)
            {
                var entitiesField = typeof(EntityManagerShared).GetField("entities", BindingFlags.NonPublic | BindingFlags.Instance);
                if (entitiesField != null)
                {
                    var entities = entitiesField.GetValue(em) as Dictionary<int, EntityManagerShared.TrackedEntity>;
                    if (entities != null)
                    {
                        foreach (var tracked in entities.Values)
                        {
                            var lobby = tracked.gameObject?.GetComponent<LobbyServer>();
                            if (lobby != null)
                            {
                                found = true;
                                InvokeSendUpdate(lobby);
                            }
                        }
                    }
                }
            }

            if (!found)
            {
                var lobbies = UnityEngine.Object.FindObjectsOfType<LobbyServer>();
                foreach (var lobby in lobbies)
                {
                    InvokeSendUpdate(lobby);
                }
            }
        }

        private static void InvokeSendUpdate(LobbyServer lobby)
        {
            var method = typeof(LobbyServer).GetMethod("SendUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(lobby, null);
            }
        }
    }
}
