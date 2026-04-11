using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;
using Windwalk.Net;
using Andromeda.Mod.Features.TextChatCommands;

namespace Andromeda.Mod.Features
{
    public static class ForceStartFeature
    {
        private static bool _singlePlayerLimboActive;

        public static bool IsSinglePlayerLimboActive => _singlePlayerLimboActive;
        public static bool ForceStartTriggered { get; internal set; }

        public static bool ConsumeForceStart()
        {
            if (ForceStartTriggered)
            {
                ForceStartTriggered = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Called when AndromedaServer.Setup() completes on the game server.
        public static void OnGameServerSetup()
        {
            if (!DedicatedServerStartup.IsServer || !DedicatedServerStartup.OnePlayerMode) return;
            MelonLogger.Msg("[FORCE-START] One-player mode: phases will auto-advance normally.");
        }

        public static void OnLoadLevel()
        {
        }

        public static void OnPlayerCountChanged(int count)
        {
        }

        public static bool ShouldBlockAutoPhaseAdvance(AndromedaShared.RoundPhase phase)
        {
            return false;
        }

        public static bool TryHandleChatCommand(PlayerId playerId, string message, out string outcome)
        {
            return TextChatCommandSystem.TryHandle(playerId, message, out outcome);
        }

        public static bool TryForceEnd(string source, out string outcome)
        {
            outcome = null;

            var server = UnityEngine.Object.FindObjectOfType<AndromedaServer>();
            if (server == null)
            {
                outcome = "AndromedaServer not found — must be in an active game session.";
                return false;
            }

            try
            {
                server.EndGame(false, false);
                outcome = $"Game ended by {source}.";
                return true;
            }
            catch (Exception ex)
            {
                outcome = $"EndGame failed: {ex.Message}";
                return false;
            }
        }

        public static bool TryForceStart(string source, out string outcome)
        {
            outcome = null;

            var lobby = UnityEngine.Object.FindObjectOfType<LobbyServer>();
            if (lobby == null)
            {
                outcome = "LobbyServer not found.";
                return false;
            }

            int playerCount = SetEveryoneReady(lobby);

            if (playerCount <= 1)
            {
                DedicatedServerStartup.OnePlayerMode = true;
                PublishOnePlayerSpawnConfigToCore(source);
            }

            ForceStartTriggered = true;

            outcome = $"Players were readied from {source} ({playerCount} player(s)); CustomPartyServer async loop will handle transition.";
            return playerCount > 0;
        }

        public static bool TryForcePhase(AndromedaShared.RoundPhase phase, out string outcome)
        {
            outcome = null;

            var server = UnityEngine.Object.FindObjectOfType<AndromedaServer>();
            if (server != null)
            {
                try
                {
                    server.Phase = server.SendRoundPhase(phase);
                    outcome = $"Forced phase to {phase} via AndromedaServer.SendRoundPhase().";
                    return true;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[FORCE-START] SendRoundPhase failed: {ex.Message}");
                }
            }

            var client = AndromedaClient.Instance;
            if (client != null)
            {
                try
                {
                    client.Phase = phase;
                    outcome = $"Forced phase to {phase} via AndromedaClient.Phase (client-only fallback).";
                    return true;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[FORCE-START] Client phase fallback failed: {ex.Message}");
                }
            }

            outcome = "No AndromedaServer or AndromedaClient found.";
            return false;
        }

        public static bool TryForcePhaseToken(string token, out string outcome)
        {
            if (!TryParsePhase(token, out var phase))
            {
                outcome = $"Unknown phase '{token}'.";
                return false;
            }

            return TryForcePhase(phase, out outcome);
        }

        public static void PublishOnePlayerSpawnConfigToCore(string source)
        {
            MelonCoroutines.Start(CoreSessionMessageClient.PublishSpawnConfigCoro(
                onePlayerMode: true,
                maxPlayers: 1,
                ttlSeconds: 900,
                source: source
            ));
        }

        private static int SetEveryoneReady(LobbyServer lobby)
        {
            try
            {
                foreach (var key in lobby.players.Keys.ToList())
                {
                    var player = lobby.players[key];
                    player.isReady = true;
                    lobby.players[key] = player;
                }

                lobby.SendUpdate();
                return lobby.players.Count;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FORCE-START] Failed to set lobby ready flags: {ex.Message}");
                return 0;
            }
        }

        private static bool TryParsePhase(string token, out AndromedaShared.RoundPhase phase)
        {
            phase = AndromedaShared.RoundPhase.None;
            if (string.IsNullOrWhiteSpace(token))
                return false;

            switch (token.Trim().ToLowerInvariant())
            {
                case "0": case "none":      phase = AndromedaShared.RoundPhase.None;      return true;
                case "1": case "loading":   phase = AndromedaShared.RoundPhase.Loading;   return true;
                case "2": case "ready": case "readyroom": case "lobby":
                                            phase = AndromedaShared.RoundPhase.ReadyRoom; return true;
                case "3": case "armory":    phase = AndromedaShared.RoundPhase.Armory;    return true;
                case "4": case "crisis":    phase = AndromedaShared.RoundPhase.Crisis;    return true;
            }

            return Enum.TryParse(token.Trim(), true, out phase);
        }
    }
}
