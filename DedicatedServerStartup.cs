using System;
using System.Text;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod
{
    public static class DedicatedServerStartup
    {
        public static bool IsServer { get; private set; }
        public static string SessionId { get; private set; }
        public static string Region { get; private set; }
        public static string GameName { get; private set; }
        public static GamemodeList.Key GamemodeKey { get; private set; }
        public static bool IsPublicSession { get; private set; }
        public static string GamemodeData { get; private set; }
        private static bool initialized = false;
        private static DateTime startupTime;
        private static DateTime lastHeartbeat;
        private static string lastGateSnapshot;
        private static bool forcedAndromedaObjectives;

        public static void Check(string[] args)
        {
            // Robust detection: check flags, batch mode, and common game server env vars
            bool hasServerFlag = false;
            foreach (var arg in args)
            {
                if (arg == "--server" || arg == "-server" || arg == "-batchmode")
                {
                    hasServerFlag = true;
                    break;
                }
            }

            // Gamelift and common headless environment detection
            bool isHeadless = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("GAMELIFT_SDK_VERSION"))
                           || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("GAMELIFT_REGION"))
                           || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AWS_REGION"))
                           || args.Any(a => a.Contains("Gamelift") || a.Contains("batchmode"));

            // If we find Gamelift vars, we are ABSOLUTELY the server.
            IsServer = hasServerFlag || isHeadless;

            if (IsServer)
            {
                startupTime = DateTime.Now;
                lastHeartbeat = DateTime.Now;
                forcedAndromedaObjectives = false;
                MelonLogger.Msg($"[STARTUP] Dedicated Server detected. Environment Markers FOUND.");
                MelonLogger.Msg("!!! DEDICATED SERVER MODE DETECTED !!!");

                // Check for UPnP enable flag (command line or env var)
                bool upnpFromArgs = args.Contains("--enable-upnp");
                bool upnpFromEnv = System.Environment.GetEnvironmentVariable("ENABLE_UPNP")?.ToLower() == "true";

                if (upnpFromArgs || upnpFromEnv)
                {
                    NetworkDebugger.SetUpnpEnabled(true);
                    MelonLogger.Msg("[SERVER] UPnP enabled via command line or environment variable.");
                }
                else
                {
                    MelonLogger.Msg("[SERVER] UPnP disabled by default. Use --enable-upnp or set ENABLE_UPNP=true to enable.");
                }
            }
        }

        public static void Update()
        {
            if (!IsServer) return;

            // Heartbeat every 10 seconds to show we are still running
            if ((DateTime.Now - lastHeartbeat).TotalSeconds > 10)
            {
                lastHeartbeat = DateTime.Now;
                int playerCount = 0;
                try {
                    var server = Singleton.Existing<ProgramServer>();
                    if (server != null) playerCount = server.PlayerCount;
                } catch {}

                bool netActive = false;
                try { netActive = Singleton.Existing<NetServer>()?.Active ?? false; } catch {}
                bool envIsServer = Environment.IsServer;
                MelonLogger.Msg($"[HEARTBEAT] Ticking... (Uptime: {(DateTime.Now - startupTime).TotalSeconds:F0}s, IsServer={envIsServer}, Players={playerCount}, SocketListening={netActive})");

                ProbeAndromedaState("heartbeat");

                // Trigger Lobby Sync and Remote Heartbeat
                SyncLobby();
                if (!string.IsNullOrEmpty(SessionId))
                {
                    RestApi.SendHeartbeat(SessionId);
                }
            }

            if (initialized) return;

            // Wait 3 seconds of real-world time to ensure Unity is stable
            if ((DateTime.Now - startupTime).TotalSeconds < 3) return;

            initialized = true;
            InitializeServer();
        }

        private static void SyncLobby()
        {
            try {
                var lobbies = UnityEngine.Object.FindObjectsOfType<LobbyServer>();
                foreach (var lobby in lobbies)
                {
                    var method = typeof(LobbyServer).GetMethod("SendUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (method != null) method.Invoke(lobby, null);
                }
            } catch {}
        }

        private static void InitializeServer()
        {
            NetworkDebugger.LogLobbyEvent("--------------------------------------------------");
            NetworkDebugger.LogLobbyEvent("[SERVER-INIT] PHASE 1: Gathering Environment Data");
            
            string[] args = System.Environment.GetCommandLineArgs();
            int port = 7777;
            string portArg = GetArg(args, "--port");
            if (!string.IsNullOrEmpty(portArg) && int.TryParse(portArg, out int p)) port = p;

            string region = GetArg(args, "--region") ?? "us-east";
            SessionId = GetArg(args, "--session-id") ?? Guid.NewGuid().ToString();
            string gameName = GetArg(args, "--name") ?? "Dedicated Server";
            bool isPublic = args.Contains("--public");

            string modeStr = GetArg(args, "--mode") ?? "CustomParty";
            GamemodeList.Key gamemodeKey = GamemodeList.Key.CustomParty;
            try {
                if (Enum.TryParse<GamemodeList.Key>(modeStr, true, out var key)) {
                    gamemodeKey = key;
                }
            } catch {}

            // Default to empty object to match game expectations when no custom data is provided.
            string gamemodeData = "{}";
            string modeDataB64 = GetArg(args, "--mode-data-b64");
            if (!string.IsNullOrEmpty(modeDataB64))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(modeDataB64);
                    string decoded = Encoding.UTF8.GetString(bytes);
                    if (!string.IsNullOrWhiteSpace(decoded))
                        gamemodeData = decoded;
                }
                catch (Exception ex)
                {
                    NetworkDebugger.LogLobbyEvent($"[SERVER-INIT] Failed to decode --mode-data-b64: {ex.Message}", "Error");
                }
            }

            string modeDataPlain = GetArg(args, "--mode-data");
            if (!string.IsNullOrWhiteSpace(modeDataPlain))
                gamemodeData = modeDataPlain;

            Region = region;
            GameName = gameName;
            GamemodeKey = gamemodeKey;
            IsPublicSession = isPublic;
            GamemodeData = gamemodeData;

            NetworkDebugger.LogLobbyEvent($"[SERVER-INIT] Targeting: Port={port}, Mode={gamemodeKey}, Region={region}, Session={SessionId}");

            try
            {
                NetworkDebugger.LogLobbyEvent("[SERVER-INIT] PHASE 1.5: Patching Environment");
                // Force Environment to Server Mode
                typeof(global::Environment).GetField("port", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, (int?)port);
                typeof(global::Environment).GetField("voicePort", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, (int?)(port + 1));

                NetworkDebugger.LogLobbyEvent($"[SERVER-INIT] PHASE 2: Spawning ProgramServer Singleton (Current Port: {Environment.Port})");

                var server = Singleton.Get<ProgramServer>();
                if (server == null)
                {
                    MelonLogger.Error("[SERVER-FATAL] Failed to initialize ProgramServer via Singleton!");
                    NetworkDebugger.LogLobbyEvent("[SERVER-FATAL] ProgramServer is NULL!", "Error");
                    return;
                }

                // Inject GamemodeList if missing (batchmode often loses serialized refs)
                var gamemodeListField = typeof(ProgramShared).GetField("gamemodeList", BindingFlags.NonPublic | BindingFlags.Instance);
                if (gamemodeListField != null && gamemodeListField.GetValue(server) == null)
                {
                    var lists = Resources.FindObjectsOfTypeAll<GamemodeList>();
                    if (lists.Length > 0)
                    {
                        gamemodeListField.SetValue(server, lists[0]);
                        NetworkDebugger.LogLobbyEvent("[SERVER-INIT] Re-linked GamemodeList asset.");
                    }
                }

                NetworkDebugger.LogLobbyEvent($"[SERVER-INIT] PHASE 3: Searching for Host method (Mode={gamemodeKey})");
                var hostMethod = typeof(ProgramServer).GetMethod("Host", BindingFlags.NonPublic | BindingFlags.Instance);
                if (hostMethod != null)
                {
                    NetworkDebugger.LogLobbyEvent("[SERVER-INIT] Invoking Host method...");
                    hostMethod.Invoke(server, new object[] { 
                        region, 
                        SessionId, 
                        gameName, 
                        gamemodeKey, 
                        isPublic, 
                        gamemodeData 
                    });
                    NetworkDebugger.LogLobbyEvent($"[SERVER-INIT] SUCCESS: Host command sent to Engine (Gamemode: {gamemodeKey}).");

                    // Gamelift normally calls NetServer.Host(port) via its onHost callback.
                    // Since we bypass Gamelift entirely, we must open the socket ourselves.

                    ProbeAndromedaState("post-host");

                }
                else
                {
                    MelonLogger.Error("[SERVER-FATAL] Missing Host method on ProgramServer!");
                    NetworkDebugger.LogLobbyEvent("[SERVER-FATAL] Host method NOT FOUND!", "Error");
                }

                if (isPublic)
                {
                    NetworkDebugger.LogLobbyEvent("[SERVER-INIT] Registering with Python API...");
                    MelonCoroutines.Start(RestApi.RegisterServerCoro(SessionId, port, region));
                    
                    // Open ports via UPnP if enabled
                    if (NetworkDebugger.IsUpnpEnabled)
                    {
                        NetworkDebugger.LogLobbyEvent($"[UPNP] Attempting to open ports {port} and {port+1}...");
                        Features.UPnPFeature.OpenPort(port, $"Andromeda Game ({SessionId})");
                        Features.UPnPFeature.OpenPort(port + 1, $"Andromeda Voice ({SessionId})");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SERVER-ERROR] Startup Crash: {ex}");
            }
            MelonLogger.Msg("--------------------------------------------------");
        }

        private static void ProbeAndromedaState(string phase)
        {
            try
            {
                var server = Singleton.Existing<ProgramServer>();
                if (server == null)
                {
                    NetworkDebugger.LogLobbyEvent($"[GATE-CHECK-DS] phase={phase} ProgramServer=<null>");
                    return;
                }

                var gmField = typeof(ProgramServer).GetField("gamemode", BindingFlags.NonPublic | BindingFlags.Instance);
                var keyField = typeof(ProgramServer).GetField("gamemodeKey", BindingFlags.NonPublic | BindingFlags.Instance);
                var gm = gmField?.GetValue(server) as Gamemode;
                var key = keyField?.GetValue(server);

                var epField = typeof(Gamemode).GetField("entrypoint", BindingFlags.NonPublic | BindingFlags.Instance);
                var spawnData = epField?.GetValue(gm) as EntitySpawnData;
                bool hasAndromedaPrefab = spawnData?.serverPrefab != null
                    && spawnData.serverPrefab.GetComponent<AndromedaServer>() != null;

                string data = server.GamemodeData;
                int dataLen = string.IsNullOrEmpty(data) ? 0 : data.Length;
                int liveAndromedaCount = UnityEngine.Object.FindObjectsOfType<AndromedaServer>().Length;
                bool hasInstance = (UnityEngine.Object)AndromedaServer.Instance != (UnityEngine.Object)null;

                int andromedaPlayers = -1;
                int loadedTrue = -1;
                int loadedTotal = -1;
                int profilesLoaded = -1;
                int profilesTotal = -1;
                bool profileRequestPending = false;

                if (hasInstance)
                {
                    var playersField = typeof(AndromedaServer).GetField("players", BindingFlags.NonPublic | BindingFlags.Instance);
                    var loadedField = typeof(AndromedaServer).GetField("playerLoaded", BindingFlags.NonPublic | BindingFlags.Instance);
                    var pDict = playersField?.GetValue(AndromedaServer.Instance) as System.Collections.IDictionary;
                    var lDict = loadedField?.GetValue(AndromedaServer.Instance) as System.Collections.IDictionary;
                    andromedaPlayers = pDict?.Count ?? 0;
                    loadedTotal = lDict?.Count ?? 0;
                    loadedTrue = 0;
                    if (lDict != null)
                    {
                        foreach (System.Collections.DictionaryEntry entry in lDict)
                        {
                            if (entry.Value is bool b && b) loadedTrue++;
                        }
                    }

                    try
                    {
                        var store = UserStore.Instance;
                        profileRequestPending = store != null && store.RequestPending;
                        profilesTotal = 0;
                        profilesLoaded = 0;
                        if (pDict != null)
                        {
                            foreach (System.Collections.DictionaryEntry entry in pDict)
                            {
                                profilesTotal++;
                                if (entry.Key is PlayerId pid)
                                {
                                    var fetched = store.Fetch(pid);
                                    if (fetched.Item2 && fetched.Item1.isProfileLoaded)
                                        profilesLoaded++;
                                }
                            }
                        }
                    }
                    catch { }
                }

                string snapshot =
                    $"key={key}|prefab={hasAndromedaPrefab}|dataLen={dataLen}|live={liveAndromedaCount}|inst={hasInstance}|progPlayers={server.PlayerCount}|andrPlayers={andromedaPlayers}|loaded={loadedTrue}/{loadedTotal}|profiles={profilesLoaded}/{profilesTotal}|pending={profileRequestPending}";

                // On heartbeat, log only when state changes; always log on post-host.
                if (phase == "heartbeat" && snapshot == lastGateSnapshot)
                    return;

                lastGateSnapshot = snapshot;

                NetworkDebugger.LogLobbyEvent(
                    $"[GATE-CHECK-DS] phase={phase} key={key} gmAsset={(gm != null ? gm.name : "<null>")} entry={(spawnData != null ? spawnData.name : "<null>")} hasAndromedaPrefab={hasAndromedaPrefab} dataLen={dataLen} liveAndromeda={liveAndromedaCount} instance={hasInstance} programPlayers={server.PlayerCount} andromedaPlayers={andromedaPlayers} loadedAcks={loadedTrue}/{loadedTotal} profilesLoaded={profilesLoaded}/{profilesTotal} profileRequestPending={profileRequestPending}"
                );

                TryForceAndromedaObjectives(key, hasInstance, andromedaPlayers, loadedTrue, loadedTotal, profilesLoaded, profilesTotal, profileRequestPending);
            }
            catch (Exception ex)
            {
                NetworkDebugger.LogLobbyEvent($"[GATE-CHECK-DS] phase={phase} probe failed: {ex.Message}", "Error");
            }
        }

        private static void TryForceAndromedaObjectives(
            object key,
            bool hasInstance,
            int andromedaPlayers,
            int loadedTrue,
            int loadedTotal,
            int profilesLoaded,
            int profilesTotal,
            bool profileRequestPending)
        {
            if (forcedAndromedaObjectives) return;
            if (!(key is GamemodeList.Key k) || k != GamemodeList.Key.Andromeda) return;
            if (!hasInstance) return;
            if (andromedaPlayers <= 0) return;
            if (loadedTotal != andromedaPlayers) return;
            if (loadedTotal <= 0 || loadedTrue != loadedTotal) return;
            if (profilesTotal != andromedaPlayers) return;
            if (profilesTotal <= 0 || profilesLoaded != profilesTotal) return;
            if (profileRequestPending) return;

            var inst = AndromedaServer.Instance;
            if ((UnityEngine.Object)inst == (UnityEngine.Object)null) return;

            // If phase already advanced, the normal path likely already ran.
            if (inst.Phase != AndromedaShared.RoundPhase.None && inst.Phase != AndromedaShared.RoundPhase.Loading)
                return;

            try
            {
                MethodInfo objectivesMethod = typeof(AndromedaServer).GetMethod("Objectives", BindingFlags.NonPublic | BindingFlags.Instance);
                if (objectivesMethod == null)
                {
                    NetworkDebugger.LogLobbyEvent("[GATE-CHECK-DS] Force Objectives skipped: method not found.", "Error");
                    return;
                }

                forcedAndromedaObjectives = true;
                NetworkDebugger.LogLobbyEvent("[GATE-CHECK-DS] All gates passed but phase idle. Forcing Andromeda Objectives().");
                objectivesMethod.Invoke(inst, null);
            }
            catch (Exception ex)
            {
                NetworkDebugger.LogLobbyEvent($"[GATE-CHECK-DS] Force Objectives failed: {ex.Message}", "Error");
            }
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i+1];
            }
            return null;
        }
    }
}
