using HarmonyLib;
using MelonLoader;
using UniRx.Async;

namespace Andromeda.Mod.Patches
{
    // Avoid generic ApiShared patch points. Intercept non-generic ProgramClient methods
    // and route pending create responses (port=0) through Join retry flow.
    [HarmonyPatch(typeof(ProgramClient), "PartyCreate")]
    public static class ProgramClientPartyCreateWarmupFallbackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            ProgramClient __instance,
            string region,
            string name,
            bool isPublic,
            string token,
            ref UniTask<(bool, string)> __result)
        {
            __result = Handle(__instance, region, name, isPublic, token);
            return false;
        }

        private static async UniTask<(bool, string)> Handle(
            ProgramClient client,
            string region,
            string name,
            bool isPublic,
            string token)
        {
            ApiShared.JoinData data = await ApiClient.PartyCreate(region, name, isPublic, token);
            if (data == null)
                return (false, "could not create party");

            if (data.port <= 0 && !string.IsNullOrEmpty(data.sessionId))
            {
                MelonLogger.Msg($"[WARMUP-FALLBACK] PartyCreate pending session={data.sessionId}; using Join retry flow.");
                _ = Dialog.Prompt("Please wait a little, servers are booting.");
                return await client.Join(region, data.sessionId);
            }

            return await client.Connect(region, data);
        }
    }

    [HarmonyPatch(typeof(ProgramClient), "Join")]
    public static class ProgramClientJoinWarmupStopOn503Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            ProgramClient __instance,
            string region,
            string gameId,
            ref UniTask<(bool, string)> __result)
        {
            __result = Handle(__instance, region, gameId);
            return false;
        }

        private static async UniTask<(bool, string)> Handle(ProgramClient client, string region, string gameId)
        {
            ApiShared.JoinData data = null;
            bool shownWaitDialog = false;

            for (int i = 0; i < 18; ++i)
            {
                data = await ApiClient.GamesJoin(region, gameId);
                if (data != null)
                    break;

                if (ApiShared.LastParsedStatus == 503)
                {
                    if (!shownWaitDialog)
                    {
                        string reason = ApiShared.LastParsedMessage;
                        if (string.IsNullOrWhiteSpace(reason))
                            reason = "Server is booting, please wait…";
                        MelonLogger.Msg($"[WARMUP-FALLBACK] Join 503 for game={gameId}; polling until ready.");
                        _ = Dialog.Prompt(reason);
                        shownWaitDialog = true;
                    }
                    await UniTask.Delay(5000);
                    continue;
                }

                await UniTask.Delay(10);
            }

            return data == null ? (false, "could not reserve player slot") : await client.Connect(region, data);
        }
    }

    [HarmonyPatch(typeof(ProgramClient), "Host")]
    public static class ProgramClientHostWarmupFallbackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            ProgramClient __instance,
            string region,
            bool isPublic,
            string gameName,
            GamemodeList.Key gamemodeKey,
            Gamemode.Data gamemodeData,
            ref UniTask<(bool, string)> __result)
        {
            __result = Handle(__instance, region, isPublic, gameName, gamemodeKey, gamemodeData);
            return false;
        }

        private static async UniTask<(bool, string)> Handle(
            ProgramClient client,
            string region,
            bool isPublic,
            string gameName,
            GamemodeList.Key gamemodeKey,
            Gamemode.Data gamemodeData)
        {
            ApiShared.JoinData data = await ApiShared.GamesNew(region, isPublic, gameName, gamemodeKey, gamemodeData);
            if (data == null)
                return (false, "could not host game");

            if (data.port <= 0 && !string.IsNullOrEmpty(data.sessionId))
            {
                MelonLogger.Msg($"[WARMUP-FALLBACK] Host pending session={data.sessionId}; using Join retry flow.");
                _ = Dialog.Prompt("Please wait a little, servers are booting.");
                return await client.Join(region, data.sessionId);
            }

            return await client.Connect(region, data);
        }
    }
}
