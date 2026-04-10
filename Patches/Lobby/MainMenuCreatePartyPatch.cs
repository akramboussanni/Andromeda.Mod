using System;
using HarmonyLib;
using MelonLoader;
using UniRx.Async;
using Windwalk.Net;
using Andromeda.Mod.Features;
using Andromeda.Mod.Settings;
using LobbySettings = Andromeda.Mod.Settings.AndromedaSettings;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch(typeof(MainMenu), "CreateParty")]
    public static class MainMenuCreatePartyPatch
    {
        private static readonly System.Reflection.MethodInfo PromptLeaveCurrentGameMethod =
            AccessTools.Method(typeof(MainMenu), "PromptLeaveCurrentGame");
        private static readonly System.Reflection.MethodInfo PromptLeaveSoloMatchingMethod =
            AccessTools.Method(typeof(MainMenu), "PromptLeaveSoloMatching");
        private static readonly System.Reflection.MethodInfo ShowPanelMethod =
            AccessTools.Method(typeof(MainMenu), "ShowPanel");
        private static readonly Type PanelEnumType =
            typeof(MainMenu).GetNestedType("Panel", System.Reflection.BindingFlags.NonPublic);

        [HarmonyPrefix]
        public static bool Prefix(MainMenu __instance, bool isPublic)
        {
            HandleCreateParty(__instance, isPublic).Forget();
            return false;
        }

        private static async UniTaskVoid HandleCreateParty(MainMenu menu, bool isPublic)
        {
            try
            {
                if (await PromptLeaveCurrentGame(menu))
                    return;

                if (await PromptLeaveSoloMatching(menu))
                    return;

                string name;
                if (isPublic)
                {
                    (string enteredName, bool canceledName) = await Dialog.String("Party name");
                    if (canceledName)
                        return;
                    name = enteredName;
                }
                else
                {
                    name = WindwalkUtilities.GenerateRandomName(32);
                }

                int selectedLobbySize;
                (string lobbyChoice, bool canceledLobbySize) = await Dialog.Choice("Lobby size", LobbySettings.GetLobbySizeOptions());
                if (canceledLobbySize)
                    return;

                if (!int.TryParse(lobbyChoice, out selectedLobbySize))
                    selectedLobbySize = 12;

                if (!LobbySettingsSystem.SetLobbySize(selectedLobbySize))
                    LobbySettingsSystem.SetLobbySize(12);

                LobbySettingsSystem.TryPublishCurrentSpawnConfig("create-party-dialog");

                string preferredRegion = LocalUserData.PreferredRegions[0];
                MainMenu.SavedPartyData = new MainMenu.PartyData
                {
                    customData = new CustomPartyClient.SavedData?(new CustomPartyClient.SavedData
                    {
                        wasLeader = true,
                        region = preferredRegion,
                        gamemodeData = (Gamemode.Data)LocalUserData.SavedGameOptions
                    })
                };

                Action closeModal = Dialog.Modal("Connecting");
                (bool connected, string reason) = await Singleton.Existing<ProgramClient>().PartyCreate(preferredRegion, name, isPublic);
                closeModal();

                if (!connected)
                {
                    Dialog.Prompt("Failed to join:\n" + reason).Forget();
                    return;
                }

                if (PanelEnumType != null && ShowPanelMethod != null)
                {
                    object playPanel = Enum.ToObject(PanelEnumType, 0);
                    ShowPanelMethod.Invoke(menu, new[] { playPanel });
                }

                DiscordSdk.SetActivity("In Party", true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[CREATE-PARTY] Failed to handle custom create flow: " + ex);
                Dialog.Prompt("Failed to create party.\nPlease try again.").Forget();
            }
        }

        private static async UniTask<bool> PromptLeaveCurrentGame(MainMenu menu)
        {
            if (PromptLeaveCurrentGameMethod == null)
                return false;

            object result = PromptLeaveCurrentGameMethod.Invoke(menu, null);
            if (result is UniTask<bool> task)
                return await task;

            return false;
        }

        private static async UniTask<bool> PromptLeaveSoloMatching(MainMenu menu)
        {
            if (PromptLeaveSoloMatchingMethod == null)
                return false;

            object result = PromptLeaveSoloMatchingMethod.Invoke(menu, null);
            if (result is UniTask<bool> task)
                return await task;

            return false;
        }
    }
}
