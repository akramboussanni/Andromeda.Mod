using HarmonyLib;
using MelonLoader;
using System.Reflection;
using System.Collections.Generic;
using System;
using System.IO;
using System.Diagnostics;
using UnityEngine;
using UniRx.Async;
using Windwalk.Net;
using System.Linq;
using Dissonance;
using Dissonance.Integrations.UNet_LLAPI;

namespace Andromeda.Mod.Patches
{
    internal static class TransitionTrace
    {
        private static readonly object _sync = new object();
        private static readonly string _dir = Path.Combine(System.Environment.CurrentDirectory, "UserData", "AndromedaTransition");

        public static void Log(string message)
        {
            try
            {
                int pid = Process.GetCurrentProcess().Id;
                string role = DedicatedServerStartup.IsServer ? "server" : "client";
                string file = Path.Combine(_dir, $"transition_{role}_{pid}.log");
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{role}] {message}";

                lock (_sync)
                {
                    Directory.CreateDirectory(_dir);
                    File.AppendAllText(file, line + System.Environment.NewLine);
                }

                MelonLogger.Msg("[TRANSITION-TRACE] " + message);
            }
            catch
            {
                // Never let debug logging interfere with gameplay flow.
            }
        }
    }

    // Nuclear Fix: Redirect ALL entity messaging from NetClient to NetServer when running as a server.
    // This fixes the "0/0" issue because it ensures the server actually broadcasts the lobby state.
    [HarmonyPatch(typeof(Entity.Base), "SendReliable")]
    public static class EntityBaseSendReliablePatch
    {
        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            try {
                // Redirect to NetServer broadcast
                var msg = new Entity.Message() {
                    id = __instance.id,
                    componentType = __instance.ComponentType,
                    Body = body
                };
                Singleton.Get<NetServer>().SendAllReliable(msg);
                return false; // Skip original (which would try to use idle NetClient)
            } catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Entity.Base), "SendUnreliable")]
    public static class EntityBaseSendUnreliablePatch
    {
        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            try {
                var msg = new Entity.Message() {
                    id = __instance.id,
                    componentType = __instance.ComponentType,
                    Body = body
                };
                Singleton.Get<NetServer>().SendAllUnreliable(msg);
                return false;
            } catch { return true; }
        }
    }

    public static class ProgramServerPatch
    {
        [HarmonyPatch(typeof(ProgramServer), "Host")]
        [HarmonyPrefix]
        public static void PrefixHost(string region, string sessionId, string name, GamemodeList.Key gamemodeKey)
        {
            NetworkDebugger.LogLobbyEvent("--------------------------------------------------");
            NetworkDebugger.LogLobbyEvent($"[SERVER-STARTED] Official Host Method Called!");
            NetworkDebugger.LogLobbyEvent($"[SERVER-STARTED] Name: {name}");
            NetworkDebugger.LogLobbyEvent($"[SERVER-STARTED] Session: {sessionId}");
            NetworkDebugger.LogLobbyEvent($"[SERVER-STARTED] Region: {region}");
            NetworkDebugger.LogLobbyEvent($"[SERVER-STARTED] Gamemode: {gamemodeKey}");
            NetworkDebugger.LogLobbyEvent("--------------------------------------------------");
        }

        [HarmonyPatch(typeof(ProgramServer), "Host")]
        [HarmonyPostfix]
        public static void PostfixHost()
        {
            NetworkDebugger.LogLobbyEvent("[SERVER-STARTED] Official Host Method Completed!");

            if (!DedicatedServerStartup.IsServer) return;
            try
            {
                var server = Singleton.Existing<ProgramServer>();
                if ((UnityEngine.Object)server == (UnityEngine.Object)null)
                {
                    NetworkDebugger.LogLobbyEvent("[GATE-CHECK] ProgramServer instance missing after Host.", "Error");
                    return;
                }

                var gamemodeField = typeof(ProgramServer).GetField("gamemode", BindingFlags.NonPublic | BindingFlags.Instance);
                var keyField = typeof(ProgramServer).GetField("gamemodeKey", BindingFlags.NonPublic | BindingFlags.Instance);
                var gm = gamemodeField?.GetValue(server) as Gamemode;
                var key = keyField?.GetValue(server);
                string rawGamemodeData = server.GamemodeData;
                int rawLen = string.IsNullOrEmpty(rawGamemodeData) ? 0 : rawGamemodeData.Length;
                string preview = string.IsNullOrEmpty(rawGamemodeData)
                    ? "<empty>"
                    : (rawGamemodeData.Length > 180 ? rawGamemodeData.Substring(0, 180) + "..." : rawGamemodeData);

                var entrypointField = typeof(Gamemode).GetField("entrypoint", BindingFlags.NonPublic | BindingFlags.Instance);
                var spawnData = entrypointField?.GetValue(gm) as EntitySpawnData;
                string gmName = gm != null ? gm.name : "<null>";
                string entryName = spawnData != null ? spawnData.name : "<null>";
                string serverPrefabName = spawnData?.serverPrefab != null ? spawnData.serverPrefab.name : "<null>";
                bool hasAndromedaServer = spawnData?.serverPrefab != null
                    && spawnData.serverPrefab.GetComponent<AndromedaServer>() != null;

                NetworkDebugger.LogLobbyEvent(
                    $"[GATE-CHECK] Host selected key={key} gamemodeAsset={gmName} entrypoint={entryName} serverPrefab={serverPrefabName} hasAndromedaServer={hasAndromedaServer}"
                );
                NetworkDebugger.LogLobbyEvent(
                    $"[GATE-CHECK] ProgramServer.GamemodeData length={rawLen} preview={preview}"
                );
            }
            catch (Exception ex)
            {
                NetworkDebugger.LogLobbyEvent($"[GATE-CHECK] Host metadata probe failed: {ex.Message}", "Error");
            }
        }

        [HarmonyPatch(typeof(ProgramServer), "OnJoin")]
        [HarmonyPrefix]
        public static void PrefixOnJoin(ProgramServer __instance, PlayerId playerId, ProgramShared.JoinRequest request)
        {
            if (!DedicatedServerStartup.IsServer) return;

            // UNIVERSAL JOINER: Force the incoming request to look exactly like what the server expects.
            // This bypasses the version-mismatch check that causes the handshake to stall.
            if (request != null)
            {
                var versionField = typeof(ProgramShared.JoinRequest).GetField("version", BindingFlags.Public | BindingFlags.Instance);
                if (versionField != null)
                {
                    // Copy the server's internal version marker into the request
                    versionField.SetValue(request, Version.Value);
                }
                
                MelonLogger.Msg($"[JOIN-FORCE] Forcing handshake for player={playerId} (Forced Version: {Version.Value})");
            }
        }

        [HarmonyPatch(typeof(ProgramServer), "Update")]
        [HarmonyPrefix]
        public static void PrefixUpdate()
        {
            // Empty timeout is active.
        }

        public static bool PrefixClientAwakeStub()
        {
            if (DedicatedServerStartup.IsServer)
            {
                MelonLogger.Msg("[SERVER-BOOT] Skipping ProgramClient.Awake via Hard-Link (resolution crash fixed).");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ProgramServer), "OnLeave")]
        [HarmonyPrefix]
        public static void PrefixOnLeave(ProgramServer __instance, PlayerId playerId)
        {
            if (!DedicatedServerStartup.IsServer) return;
            try
            {
                int countBefore = __instance?.PlayerCount ?? -1;
                NetworkDebugger.LogLobbyEvent($"[SERVER-LEAVE] ProgramServer.OnLeave playerId={playerId} playersBefore={countBefore}");
            }
            catch { }
        }
    }

    public static class GamemodeBeginGatePatch
    {
        [HarmonyPatch(typeof(Gamemode), "Begin")]
        [HarmonyPrefix]
        public static void PrefixBegin(Gamemode __instance)
        {
            if (!DedicatedServerStartup.IsServer) return;
            try
            {
                var entrypointField = typeof(Gamemode).GetField("entrypoint", BindingFlags.NonPublic | BindingFlags.Instance);
                var spawnData = entrypointField?.GetValue(__instance) as EntitySpawnData;
                string gmName = __instance != null ? __instance.name : "<null>";
                string entryName = spawnData != null ? spawnData.name : "<null>";
                string serverPrefabName = spawnData?.serverPrefab != null ? spawnData.serverPrefab.name : "<null>";
                bool hasAndromedaServer = spawnData?.serverPrefab != null
                    && spawnData.serverPrefab.GetComponent<AndromedaServer>() != null;

                NetworkDebugger.LogLobbyEvent(
                    $"[GATE-CHECK] Gamemode.Begin asset={gmName} entrypoint={entryName} serverPrefab={serverPrefabName} hasAndromedaServer={hasAndromedaServer}"
                );
            }
            catch (Exception ex)
            {
                NetworkDebugger.LogLobbyEvent($"[GATE-CHECK] Gamemode.Begin probe failed: {ex.Message}", "Error");
            }
        }
    }

    public static class EntitySpawnGatePatch
    {
        [HarmonyPatch(typeof(EntityManagerServer), "Spawn", new Type[] { typeof(EntitySpawnData) })]
        [HarmonyPrefix]
        public static void PrefixSpawn(EntitySpawnData data)
        {
            if (!DedicatedServerStartup.IsServer) return;
            try
            {
                string dataName = data != null ? data.name : "<null>";
                string serverPrefabName = data?.serverPrefab != null ? data.serverPrefab.name : "<null>";
                bool hasAndromedaServer = data?.serverPrefab != null
                    && data.serverPrefab.GetComponent<AndromedaServer>() != null;

                if (hasAndromedaServer)
                {
                    NetworkDebugger.LogLobbyEvent(
                        $"[GATE-CHECK] Spawning Andromeda entry entity: spawnData={dataName} serverPrefab={serverPrefabName}"
                    );
                }
            }
            catch (Exception ex)
            {
                NetworkDebugger.LogLobbyEvent($"[GATE-CHECK] Entity spawn probe failed: {ex.Message}", "Error");
            }
        }
    }

    public static class LobbyServerPatch
    {
        [HarmonyPatch(typeof(LobbyServer), "OnEnable")]
        [HarmonyPostfix]
        public static void PostfixOnEnable()
        {
            NetworkDebugger.LogLobbyEvent("[SERVER-LIFECYCLE] LobbyServer Logic is now ACTIVE.");
        }

        [HarmonyPatch(typeof(LobbyServer), "OnJoin")]
        [HarmonyPrefix]
        public static void PrefixOnJoin(PlayerId playerId)
        {
            NetworkDebugger.LogLobbyEvent($"[SERVER-LOBBY] Player JOINED Lobby: {playerId}");
        }

        [HarmonyPatch(typeof(LobbyServer), "SendUpdate")]
        [HarmonyPrefix]
        public static void PrefixSendUpdate(LobbyServer __instance)
        {
            int playerCount = 0;
            var playersField = typeof(LobbyShared).GetField("players", BindingFlags.NonPublic | BindingFlags.Instance);
            if (playersField != null)
            {
                var players = playersField.GetValue(__instance) as Dictionary<PlayerId, LobbyShared.Player>;
                playerCount = players?.Count ?? 0;
            }

            int max = __instance.MaxPlayers ?? 8;
            NetworkDebugger.LogLobbyEvent($"[SERVER-LOBBY] Sending PlayerList Update: Count={playerCount}/{max}");
        }
    }

    // [HarmonyPatch] - Muted due to IL Compile Error (MonoMod bug with generics)
    public static class NetworkListenPatch
    {
        [HarmonyPatch(typeof(NetServer), "Host")]
        [HarmonyPostfix]
        public static void PostfixHost()
        {
            NetworkDebugger.LogLobbyEvent($"[SOCKET] NetServer.Host executed.");
        }
    }

    public static class GameliftPatch
    {
        [HarmonyPatch(typeof(Gamelift), "Initialize")]
        [HarmonyPrefix]
        public static bool PrefixInitialize()
        {
            // Gamelift.Initialize is a stub in EOB — it never calls onHost.
            // DedicatedServerStartup.InitializeServer() calls ProgramServer's private Host directly
            // via reflection, which opens the socket. So we just skip this no-op.
            MelonLogger.Msg("[GAMELIFT] Gamelift.Initialize intercepted and skipped.");
            return false;
        }

        [HarmonyPatch(typeof(Gamelift), "ValidatePlayerSession")]
        [HarmonyPrefix]
        public static bool PrefixValidate(ref bool __result)
        {
            // Always allow players to join without AWS Gamelift session validation.
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(Gamelift), "RemovePlayerSession")]
        [HarmonyPrefix]
        public static bool PrefixRemove() => false;

        [HarmonyPatch(typeof(Gamelift), "End")]
        [HarmonyPrefix]
        public static bool PrefixEnd() => false;

        [HarmonyPatch(typeof(Gamelift), "LockGameSession")]
        [HarmonyPrefix]
        public static bool PrefixLock() => false;

        [HarmonyPatch(typeof(Gamelift), "UnlockGameSession")]
        [HarmonyPrefix]
        public static bool PrefixUnlock() => false;
    }

    public static class ApiClientPartyJoinPatch
    {
        [HarmonyPatch(typeof(ApiShared), "GamesCustomNew")]
        [HarmonyPrefix]
        public static void PrefixGamesCustomNew(string region, GamemodeList.Key gamemodeKey)
        {
            MelonLogger.Msg($"[REST] Registering server with PythonBackend... (Region: {region}, Mode: {gamemodeKey})");
        }
    }

    public static class ProgramClientConnectPatch
    {
        [HarmonyPatch(typeof(ProgramClient), "Connect")]
        [HarmonyPrefix]
        public static void PrefixConnect(string region, ApiShared.JoinData data)
        {
            if (data == null)
            {
                TransitionTrace.Log($"[CLIENT-CONN] ProgramClient.Connect called with NULL JoinData (region={region}).");
                return;
            }

            TransitionTrace.Log(
                $"[CLIENT-CONN] ProgramClient.Connect starting: target={data.ipAddress}:{data.port} session={data.sessionId} region={region}"
            );
        }

        [HarmonyPatch(typeof(ProgramClient), "OnLeave")]
        [HarmonyPrefix]
        public static void PrefixOnLeave(ProgramShared.LeaveRequest msg)
        {
            MelonLogger.Msg($"[CLIENT-DISCONN] Leaving server. Reason: {msg?.reason ?? "Unknown"}");
        }
    }

    // [HarmonyPatch] - Muted due to IL Compile Error
    public static class NetClientSocketPatch
    {
        [HarmonyPatch(typeof(NetClient), "Join")]
        [HarmonyPrefix]
        public static void PrefixJoin(string host, int port)
        {
            MelonLogger.Msg($"[SOCKET-CONN] Low-level socket connecting to {host}:{port}...");
        }
    }

    public static class VoiceChatProbePatches
    {
        private static string Preview(string message, int maxLen = 80)
        {
            if (string.IsNullOrEmpty(message)) return "<empty>";
            if (message.Length <= maxLen) return message;
            return message.Substring(0, maxLen) + "...";
        }

        [HarmonyPatch(typeof(VoiceServer), "Host")]
        [HarmonyPrefix]
        public static void PrefixVoiceServerHost(int voicePort)
        {
            TransitionTrace.Log($"[VOICE-PROBE] VoiceServer.Host called argVoicePort={voicePort} envVoicePort={Environment.VoicePort}");
        }

        [HarmonyPatch(typeof(UNetCommsNetwork), "InitializeAsDedicatedServer")]
        [HarmonyPrefix]
        public static void PrefixVoiceDedicatedServerInit(UNetCommsNetwork __instance)
        {
            TransitionTrace.Log($"[VOICE-PROBE] Dissonance InitializeAsDedicatedServer port={__instance.Port}");
        }

        [HarmonyPatch(typeof(UNetCommsNetwork), "InitializeAsClient")]
        [HarmonyPrefix]
        public static void PrefixVoiceClientInit(UNetCommsNetwork __instance, string serverAddress)
        {
            TransitionTrace.Log($"[VOICE-PROBE] Dissonance InitializeAsClient target={serverAddress}:{__instance.Port}");
        }

        [HarmonyPatch(typeof(VoiceClient), "Connect")]
        [HarmonyPrefix]
        public static void PrefixVoiceConnect(string ipAddress, int port)
        {
            TransitionTrace.Log($"[VOICE-PROBE] VoiceClient.Connect target={ipAddress}:{port}");
        }

        [HarmonyPatch(typeof(VoiceClient), "JoinRoom")]
        [HarmonyPrefix]
        public static void PrefixJoinRoom(VoiceClient.Room room)
        {
            TransitionTrace.Log($"[VOICE-PROBE] VoiceClient.JoinRoom room={room}");
        }

        [HarmonyPatch(typeof(VoiceClient), "LeaveRoom")]
        [HarmonyPrefix]
        public static void PrefixLeaveRoom(VoiceClient.Room room)
        {
            TransitionTrace.Log($"[VOICE-PROBE] VoiceClient.LeaveRoom room={room}");
        }

        [HarmonyPatch(typeof(VoiceClient), "SendText")]
        [HarmonyPrefix]
        public static void PrefixSendText(VoiceClient.Room room, string message)
        {
            TransitionTrace.Log($"[VOICE-PROBE] VoiceClient.SendText room={room} len={(message?.Length ?? 0)} preview='{Preview(message)}'");
        }

        [HarmonyPatch(typeof(VoiceClient), "OnTextMessage")]
        [HarmonyPrefix]
        public static void PrefixOnTextMessage(object message)
        {
            if (message == null)
            {
                TransitionTrace.Log("[VOICE-PROBE] VoiceClient.OnTextMessage received NULL payload");
                return;
            }

            try
            {
                Type msgType = message.GetType();
                string sender = msgType.GetProperty("Sender")?.GetValue(message)?.ToString() ?? "<unknown>";
                string recipient = msgType.GetProperty("Recipient")?.GetValue(message)?.ToString() ?? "<unknown>";
                string text = msgType.GetProperty("Message")?.GetValue(message)?.ToString() ?? string.Empty;

                TransitionTrace.Log(
                    $"[VOICE-PROBE] VoiceClient.OnTextMessage sender={sender} room={recipient} len={text.Length} preview='{Preview(text)}'"
                );
            }
            catch (Exception ex)
            {
                TransitionTrace.Log($"[VOICE-PROBE] VoiceClient.OnTextMessage probe failed: {ex.Message}");
            }
        }
    }

    public static class ProgramClientProbePatch
    {
        public static bool PrefixClientAwakeStub()
        {
            if (DedicatedServerStartup.IsServer)
            {
                MelonLogger.Msg("[SERVER-BOOT] Skipping ProgramClient.Awake via Hard-Link (resolution crash fixed).");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ProgramClient), "Join")]
        [HarmonyPrefix]
        public static void PrefixJoin(string region, string gameId)
        {
            TransitionTrace.Log($"[CLIENT-PROBE] ProgramClient.Join called: region={region} gameId={gameId}");
        }
    }
    public static class MainMenuTransitionProbePatch
    {
        [HarmonyPatch(typeof(MainMenu), "JoinMatch")]
        [HarmonyPrefix]
        public static void PrefixJoinMatch(string region, string gameId, string rejoinToken, bool rejoinPublic)
        {
            TransitionTrace.Log(
                $"[CLIENT-PROBE] MainMenu.JoinMatch called: region={region} gameId={gameId} rejoinToken={(string.IsNullOrEmpty(rejoinToken) ? "none" : "set")} rejoinPublic={rejoinPublic}"
            );
        }
    }

    public static class CustomPartyClientJoinGamePatch
    {
        [HarmonyPatch(typeof(CustomPartyClient), "OnJoinGame")]
        [HarmonyPrefix]
        public static void PrefixOnJoinGame(Entity.Message entityMsg)
        {
            try
            {
                var msg = entityMsg?.ReadBody<CustomPartyShared.JoinGameMsg>();
                if (msg == null) return;
                MelonLogger.Msg(
                    $"[TRANSITION] JoinGameMsg received on client: region={msg.region} gameId={msg.gameId} rejoinPublic={msg.rejoinPublic}"
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TRANSITION] Failed to parse JoinGameMsg on client: {ex.Message}");
            }
        }
    }

    public static class AndromedaClientTransitionPatch
    {
        [HarmonyPatch(typeof(AndromedaClient), "OnLoadLevel")]
        [HarmonyPrefix]
        public static void PrefixOnLoadLevel()
        {
            MelonLogger.Msg("[TRANSITION] AndromedaClient.OnLoadLevel fired (LoadLevel message received).");
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnBeginRound")]
        [HarmonyPrefix]
        public static void PrefixOnBeginRound()
        {
            MelonLogger.Msg("[TRANSITION] AndromedaClient.OnBeginRound fired (loading screen should close now).");
        }
    }

    public static class AndromedaPhaseClockDesyncPatch
    {
        private static AndromedaShared.RoundPhase _lastPhase = AndromedaShared.RoundPhase.None;
        private static float _lastAllowedPhaseTimeAt = -999f;

        [HarmonyPatch(typeof(AndromedaClient), "OnLoadLevel")]
        [HarmonyPrefix]
        public static void ResetPhaseTimeGate()
        {
            _lastPhase = AndromedaShared.RoundPhase.None;
            _lastAllowedPhaseTimeAt = -999f;
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnPhaseTime")]
        [HarmonyPrefix]
        public static bool PrefixOnPhaseTime(AndromedaClient __instance)
        {
            if ((UnityEngine.Object)__instance == (UnityEngine.Object)null)
            {
                return true;
            }

            // Some sessions receive duplicate PhaseTime messages for the same phase.
            // Vanilla PhaseClock auto-increments the phase marker when phaseIndex == -1,
            // so duplicate packets can push the HUD 1-2 phases ahead while gameplay stays correct.
            var phase = __instance.Phase;
            bool samePhase = phase == _lastPhase;
            bool timerAlreadyActive = __instance.PhaseEndTime > Time.time + 1f;
            bool rapidRepeat = Time.time - _lastAllowedPhaseTimeAt < 3f;

            if (samePhase && timerAlreadyActive && rapidRepeat)
            {
                MelonLogger.Msg($"[ANDROMEDA-TIMER] Ignoring duplicate OnPhaseTime in phase={phase}");
                return false;
            }

            _lastPhase = phase;
            _lastAllowedPhaseTimeAt = Time.time;
            return true;
        }

        // Crisis phases (generator repair) have no countdown, so the server sends
        // PhaseTime with remaining=0. With phaseIndex=-1 the vanilla PhaseClock
        // auto-increments currentPhase on every such call, pushing the HUD phase
        // indicator ahead by up to 4 slots while gameplay stays correct.
        // Suppressing the increment for zero-duration messages fixes the visual desync.
        [HarmonyPatch(typeof(PhaseClock), "SetEndTime")]
        [HarmonyPrefix]
        public static void FixZeroDurationPhase(ref bool trackPhase, float time, int phaseIndex)
        {
            if (phaseIndex == -1 && time - Time.time < 1f)
                trackPhase = false;
        }
    }

    public static class AndromedaServerTransitionPatch
    {
        private static readonly HashSet<int> ObjectivesEntered = new HashSet<int>();

        private static bool IsEmergencyForceEnabled()
        {
            string value = System.Environment.GetEnvironmentVariable("Andromeda_FORCE_ANDROMEDA_START") ?? "1";
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static (int loadedCount, int totalPlayers) ReadLoadState(AndromedaServer instance)
        {
            var loadedField = typeof(AndromedaServer).GetField("playerLoaded", BindingFlags.NonPublic | BindingFlags.Instance);
            var playersField = typeof(AndromedaServer).GetField("players", BindingFlags.NonPublic | BindingFlags.Instance);
            if (instance == null || loadedField == null || playersField == null) return (0, 0);

            var loaded = loadedField.GetValue(instance) as Dictionary<PlayerId, bool>;
            var players = playersField.GetValue(instance) as Dictionary<PlayerId, AndromedaServer.Player>;
            int loadedCount = loaded?.Count(kv => kv.Value) ?? 0;
            int total = players?.Count ?? 0;
            return (loadedCount, total);
        }

        private static System.Collections.IEnumerator SetupWatchdog(AndromedaServer instance)
        {
            int id = instance.GetInstanceID();
            const float tickSeconds = 5f;
            const float timeoutSeconds = 35f;
            float elapsed = 0f;

            while (elapsed < timeoutSeconds)
            {
                yield return new WaitForSeconds(tickSeconds);
                elapsed += tickSeconds;

                if ((UnityEngine.Object)instance == (UnityEngine.Object)null) yield break;
                if (ObjectivesEntered.Contains(id)) yield break;

                var state = ReadLoadState(instance);
                NetworkDebugger.LogLobbyEvent($"[ANDROMEDA-BOOT] Watchdog t={elapsed:0}s loaded={state.loadedCount}/{state.totalPlayers} objectivesStarted={ObjectivesEntered.Contains(id)}");
            }

            if ((UnityEngine.Object)instance == (UnityEngine.Object)null) yield break;
            if (ObjectivesEntered.Contains(id)) yield break;

            var finalState = ReadLoadState(instance);
            NetworkDebugger.LogLobbyEvent($"[ANDROMEDA-BOOT] Watchdog timeout at t={timeoutSeconds:0}s loaded={finalState.loadedCount}/{finalState.totalPlayers}");

            if (!IsEmergencyForceEnabled())
            {
                NetworkDebugger.LogLobbyEvent("[ANDROMEDA-BOOT] Emergency force-start disabled (Andromeda_FORCE_ANDROMEDA_START=0).");
                yield break;
            }

            if (finalState.totalPlayers <= 0)
            {
                NetworkDebugger.LogLobbyEvent("[ANDROMEDA-BOOT] Emergency force-start skipped: no players tracked.");
                yield break;
            }

            try
            {
                MethodInfo objectivesMethod = typeof(AndromedaServer).GetMethod("Objectives", BindingFlags.NonPublic | BindingFlags.Instance);
                if (objectivesMethod != null)
                {
                    NetworkDebugger.LogLobbyEvent("[ANDROMEDA-BOOT] Emergency force-start: invoking Objectives().");
                    objectivesMethod.Invoke(instance, null);
                }
                else
                {
                    MethodInfo sendBeginMethod = typeof(AndromedaServer).GetMethod("SendBeginRound", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (sendBeginMethod != null)
                    {
                        NetworkDebugger.LogLobbyEvent("[ANDROMEDA-BOOT] Emergency fallback: invoking SendBeginRound() directly.");
                        sendBeginMethod.Invoke(instance, null);
                    }
                }
            }
            catch (Exception ex)
            {
                NetworkDebugger.LogLobbyEvent($"[ANDROMEDA-BOOT] Emergency force-start failed: {ex.Message}", "Error");
            }
        }

        [HarmonyPatch(typeof(AndromedaServer), "Setup")]
        [HarmonyPrefix]
        public static void PrefixSetup()
        {
            if (!DedicatedServerStartup.IsServer) return;
            NetworkDebugger.LogLobbyEvent("[ANDROMEDA-BOOT] Setup() entered.");
        }

        [HarmonyPatch(typeof(AndromedaServer), "LoadCustomData")]
        [HarmonyPrefix]
        public static void PrefixLoadCustomData(string data)
        {
            if (!DedicatedServerStartup.IsServer) return;
            int len = string.IsNullOrEmpty(data) ? 0 : data.Length;
            string preview = string.IsNullOrEmpty(data)
                ? "<empty>"
                : (data.Length > 180 ? data.Substring(0, 180) + "..." : data);
            NetworkDebugger.LogLobbyEvent($"[GATE-CHECK] LoadCustomData input length={len} preview={preview}");
        }

        [HarmonyPatch(typeof(AndromedaServer), "LoadCustomData")]
        [HarmonyPostfix]
        public static void PostfixLoadCustomData(AndromedaServer __instance, bool __result)
        {
            if (!DedicatedServerStartup.IsServer) return;
            try
            {
                var cd = __instance?.CustomData;
                if (cd == null)
                {
                    NetworkDebugger.LogLobbyEvent($"[GATE-CHECK] LoadCustomData result={__result} customData=<null>");
                    return;
                }

                NetworkDebugger.LogLobbyEvent(
                    $"[GATE-CHECK] LoadCustomData result={__result} aliens={cd.numberOfAliens} firstDown={cd.firstDowntimeTime} secondDown={cd.secondDowntimeTime} transformEP={cd.transformationEP}"
                );
            }
            catch (Exception ex)
            {
                NetworkDebugger.LogLobbyEvent($"[GATE-CHECK] LoadCustomData postfix probe failed: {ex.Message}", "Error");
            }
        }

        [HarmonyPatch(typeof(AndromedaServer), "Setup")]
        [HarmonyPostfix]
        public static void PostfixSetup(AndromedaServer __instance)
        {
            if (!DedicatedServerStartup.IsServer) return;
            if ((UnityEngine.Object)__instance == (UnityEngine.Object)null) return;
            ObjectivesEntered.Remove(__instance.GetInstanceID());
            MelonCoroutines.Start(SetupWatchdog(__instance));
        }

        [HarmonyPatch(typeof(AndromedaServer), "OnJoin")]
        [HarmonyPostfix]
        public static void PostfixOnJoin(PlayerId playerId)
        {
            if (!DedicatedServerStartup.IsServer) return;
            try
            {
                var p = Singleton.Existing<ProgramServer>()?.ByPlayerId(playerId);
                string steamId = p.HasValue ? (p.Value.profile?.steamId ?? "?") : "?";
                NetworkDebugger.LogLobbyEvent($"[ANDROMEDA-BOOT] Player joined match server: playerId={playerId} steamId={steamId}");
            }
            catch { }
        }

        [HarmonyPatch(typeof(AndromedaServer), "OnPlayerLoaded")]
        [HarmonyPrefix]
        public static void PrefixOnPlayerLoaded(Entity.Message entityMsg)
        {
            if (!DedicatedServerStartup.IsServer) return;
            try
            {
                NetworkDebugger.LogLobbyEvent($"[ANDROMEDA-BOOT] OnPlayerLoaded received from playerId={entityMsg.playerId}");
            }
            catch { }
        }

        [HarmonyPatch(typeof(AndromedaServer), "OnPlayerLoaded")]
        [HarmonyPostfix]
        public static void PostfixOnPlayerLoaded()
        {
            if (!DedicatedServerStartup.IsServer) return;
            try
            {
                var loadedField = typeof(AndromedaServer).GetField("playerLoaded", BindingFlags.NonPublic | BindingFlags.Instance);
                var playersField = typeof(AndromedaServer).GetField("players", BindingFlags.NonPublic | BindingFlags.Instance);
                var inst = AndromedaServer.Instance;
                if (inst == null || loadedField == null || playersField == null) return;

                var loaded = loadedField.GetValue(inst) as Dictionary<PlayerId, bool>;
                var players = playersField.GetValue(inst) as Dictionary<PlayerId, AndromedaServer.Player>;
                int loadedCount = loaded?.Count(kv => kv.Value) ?? 0;
                int total = players?.Count ?? 0;
                NetworkDebugger.LogLobbyEvent($"[ANDROMEDA-BOOT] Client level loaded ack: {loadedCount}/{total}");
            }
            catch { }
        }

        [HarmonyPatch(typeof(AndromedaServer), "Objectives")]
        [HarmonyPrefix]
        public static void PrefixObjectives(AndromedaServer __instance)
        {
            if (!DedicatedServerStartup.IsServer) return;
            if ((UnityEngine.Object)__instance != (UnityEngine.Object)null)
                ObjectivesEntered.Add(__instance.GetInstanceID());
            NetworkDebugger.LogLobbyEvent("[ANDROMEDA-BOOT] Objectives() entered.");
        }

        [HarmonyPatch(typeof(AndromedaServer), "SendBeginRound")]
        [HarmonyPrefix]
        public static void PrefixSendBeginRound()
        {
            if (!DedicatedServerStartup.IsServer) return;
            NetworkDebugger.LogLobbyEvent("[ANDROMEDA-BOOT] SendBeginRound called. Match should fully enter gameplay now.");
        }
    }
}