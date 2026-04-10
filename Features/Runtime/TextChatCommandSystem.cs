using System;
using System.Collections.Generic;
using MelonLoader;
using Windwalk.Net;
using Andromeda.Mod.Features;
using Andromeda.Mod.Networking.Messaging;
using Andromeda.Mod.Settings;

namespace Andromeda.Mod.Features.TextChatCommands
{
    internal interface ITextChatCommand
    {
        string Name { get; }
        bool Execute(PlayerId playerId, string[] args, out string outcome);
    }

    internal sealed class StartTextChatCommand : ITextChatCommand
    {
        public string Name => "/start";

        public bool Execute(PlayerId playerId, string[] args, out string outcome)
        {
            bool ok = ForceStartFeature.TryForceStart("/start", out outcome);
            if (ok)
                NetworkDebugger.LogLobbyEvent($"[CHAT-CMD] {outcome}");
            else
                NetworkDebugger.LogLobbyEvent($"[CHAT-CMD] /start failed: {outcome}", "Warning");
            return true;
        }
    }

    internal sealed class EndTextChatCommand : ITextChatCommand
    {
        public string Name => "/end";

        public bool Execute(PlayerId playerId, string[] args, out string outcome)
        {
            bool ok = ForceStartFeature.TryForceEnd("/end", out outcome);
            if (ok)
                NetworkDebugger.LogLobbyEvent($"[CHAT-CMD] {outcome}");
            else
                NetworkDebugger.LogLobbyEvent($"[CHAT-CMD] /end failed: {outcome}", "Warning");
            return true;
        }
    }

    internal sealed class PhaseTextChatCommand : ITextChatCommand
    {
        public string Name => "/phase";

        public bool Execute(PlayerId playerId, string[] args, out string outcome)
        {
            if (args.Length < 2)
            {
                outcome = "Usage: /phase <ready|armory|crisis|loading|none|index>";
                NetworkDebugger.LogLobbyEvent($"[CHAT-CMD] {outcome}", "Warning");
                return true;
            }

            bool ok = ForceStartFeature.TryForcePhaseToken(args[1], out outcome);
            if (ok)
                NetworkDebugger.LogLobbyEvent($"[CHAT-CMD] {outcome}");
            else
                NetworkDebugger.LogLobbyEvent($"[CHAT-CMD] /phase failed: {outcome}", "Warning");

            return true;
        }
    }

    public static class TextChatCommandSystem
    {
        private static readonly List<ITextChatCommand> CommandList = new List<ITextChatCommand>
            {
                new StartTextChatCommand(),
                new EndTextChatCommand(),
                new PhaseTextChatCommand(),
            };

        private static readonly Dictionary<string, ITextChatCommand> Commands =
            new Dictionary<string, ITextChatCommand>(StringComparer.OrdinalIgnoreCase);

        static TextChatCommandSystem()
        {
            foreach (var command in CommandList)
            {
                if (command == null || string.IsNullOrWhiteSpace(command.Name))
                    continue;

                Commands[command.Name.Trim()] = command;
            }
        }

        public static void Initialize()
        {
            NetworkMessageService.RegisterServerHandler<ChatCommandMessage>(OnChatCommandReceived);
        }

        private static void OnChatCommandReceived(ChatCommandMessage msg, PlayerId sender)
        {
            TryHandle(sender, msg.command, out _);
        }

        private static bool IsPartyLeader(PlayerId sender)
        {
            if (DedicatedServerStartup.OnePlayerMode)
                return true;

            // CustomPartyServer is authoritative whenever it exists (lobby phase, both dedicated and listen server)
            var customPartyServer = UnityEngine.Object.FindObjectOfType<CustomPartyServer>();
            if ((UnityEngine.Object)customPartyServer != (UnityEngine.Object)null)
            {
                MelonLogger.Msg($"[CHAT-CMD] IsPartyLeader: leader={customPartyServer.leader} sender={sender}");
                return customPartyServer.leader.HasValue && customPartyServer.leader.Value == sender;
            }

            // Game phase on dedicated server: fall back to Steam ID passed via spawn config
            if (DedicatedServerStartup.IsServer && !string.IsNullOrEmpty(DedicatedServerStartup.PartyLeaderSteamId))
            {
                var store = UserStore.Instance;
                if ((UnityEngine.Object)store == (UnityEngine.Object)null)
                    return false;

                var fetched = store.Fetch(sender);
                MelonLogger.Msg($"[CHAT-CMD] IsPartyLeader: steamId={fetched.Item1.steamId} expected={DedicatedServerStartup.PartyLeaderSteamId}");
                return fetched.Item2 && fetched.Item1.steamId == DedicatedServerStartup.PartyLeaderSteamId;
            }

            // Listen server after lobby teardown: use cached leader from before transition
            if (LobbySettingsReplicationFeature.CachedLeaderId.HasValue)
            {
                MelonLogger.Msg($"[CHAT-CMD] IsPartyLeader: cached leader={LobbySettingsReplicationFeature.CachedLeaderId} sender={sender}");
                return LobbySettingsReplicationFeature.CachedLeaderId.Value == sender;
            }

            MelonLogger.Msg($"[CHAT-CMD] IsPartyLeader: no leader source found for sender={sender}");
            return false;
        }

        private static float _lastHandleTime = 0f;
        private static string _lastHandleMessage = null;

        public static bool TryHandle(PlayerId playerId, string message, out string outcome)
        {
            outcome = null;
            if (string.IsNullOrWhiteSpace(message) || !message.StartsWith("/"))
                return false;

            // Deduplication
            if (message == _lastHandleMessage && UnityEngine.Time.time - _lastHandleTime < 1.0f)
                return false;
            
            _lastHandleMessage = message;
            _lastHandleTime = UnityEngine.Time.time;

            MelonLogger.Msg($"[CHAT-CMD] '{message}' from {playerId}");

            if (!IsPartyLeader(playerId))
            {
                MelonLogger.Msg($"[CHAT-CMD] rejected: {playerId} is not the party leader.");
                return false;
            }

            string[] parts = message.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            if (!Commands.TryGetValue(parts[0], out var command))
            {
                MelonLogger.Msg($"[CHAT-CMD] unknown command '{parts[0]}'.");
                return false;
            }

            return command.Execute(playerId, parts, out outcome);
        }

    }
}
