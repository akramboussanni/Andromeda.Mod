using HarmonyLib;
using MelonLoader;
using System.Collections.Generic;
using System.Reflection;
using System;
using UniRx.Async;
using Windwalk.Net;
using UnityEngine;
using Andromeda.Mod.Settings;

namespace Andromeda.Mod.Patches
{
    // NO CLASS ATTRIBUTE - We use ManualPatch in Mod.cs
    [HarmonyPatch]
    public static class UserStorePatch
    {
        private static readonly FieldInfo UsersField = typeof(UserStore).GetField("users", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo RequestPendingField = typeof(UserStore).GetField("<RequestPending>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void DebugLog(string message)
        {
            if (AndromedaClientSettings.VerboseDebugLoggingEnabled.Value)
                MelonLogger.Msg(message);
        }

        [HarmonyPatch(typeof(UserStore), "AddAll")]
        public static void PrefixAddAll(IEnumerable<UserStore.AddInfo> info)
        {
            if (info == null) return;
            foreach (var i in info)
            {
                // AddInfo is a struct, it cannot be null.
                DebugLog($"[USERSTORE] Attempting to register player: {i.username} (Steam: {i.steamId})");
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
                    DebugLog("[USERSTORE] Player registration process COMPLETED.");
                    
                    // Force a Lobby update now that we have names/ranks
                    TriggerLobbyUpdate();
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[USERSTORE] Player registration API FAILED, applying fail-open: {ex}");
                    TryFailOpenUserStore();
                }
            });
        }

        private static void TryFailOpenUserStore()
        {
            try
            {
                var store = UserStore.Instance;
                if (store == null)
                    return;

                // Prevent WaitUntil(!RequestPending) deadlocks when API calls fault.
                RequestPendingField?.SetValue(store, false);

                var users = UsersField?.GetValue(store) as Dictionary<PlayerId, UserStore.Data>;
                if (users == null)
                    return;

                int fixedCount = 0;
                foreach (var id in new List<PlayerId>(users.Keys))
                {
                    var data = users[id];
                    if (data.isProfileLoaded)
                        continue;

                    data.isProfileLoaded = true;
                    if (data.items == null)
                        data.items = new string[0];
                    if (data.characters == null)
                        data.characters = new ApiShared.PlayerCharacterData[0];
                    users[id] = data;
                    fixedCount++;
                }

                if (fixedCount > 0)
                    MelonLogger.Warning($"[USERSTORE] Fail-open applied for {fixedCount} profile(s); continuing boot with defaults.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[USERSTORE] Fail-open recovery failed: {ex}");
            }
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
