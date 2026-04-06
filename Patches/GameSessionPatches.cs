using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Windwalk.Net;

namespace Andromeda.Mod.Patches
{
    // ---------------------------------------------------------------------------
    // CustomPartyServer patches — log the "start game" handoff to a match server
    // ---------------------------------------------------------------------------

    [HarmonyPatch(typeof(CustomPartyServer))]
    public static class CustomPartyServerSetupPatch
    {
        [HarmonyPatch("Setup")]
        [HarmonyPrefix]
        public static void PrefixSetup()
        {
            NetworkDebugger.LogLobbyEvent("[CUSTOM-PARTY] CustomPartyServer.Setup() started — lobby entity is live.");
        }
    }

    /// <summary>
    /// Logs when CustomPartyShared.JoinGameMsg is sent so we can confirm the
    /// lobby server is actually broadcasting the "switch to match" command.
    /// JoinGameMsg is a protected nested class so we identify it by type name.
    /// Note: NetworkPatches.cs already has a prefix on Entity.Base.SendReliable;
    /// Harmony will chain both prefixes correctly.
    /// </summary>
    [HarmonyPatch(typeof(Entity.Base), "SendReliable")]
    public static class JoinGameMsgLoggingPatch
    {
        private const string JoinGameMsgTypeName = "JoinGameMsg";

        public static void Prefix(BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return;
            if (body == null) return;
            if (body.GetType().Name != JoinGameMsgTypeName) return;
            try
            {
                Type t = body.GetType();
                string region = (string)t.GetField("region")?.GetValue(body) ?? "?";
                string gameId = (string)t.GetField("gameId")?.GetValue(body) ?? "?";
                bool   pub    = (bool)(t.GetField("rejoinPublic")?.GetValue(body) ?? false);
                NetworkDebugger.LogLobbyEvent(
                    $"[CUSTOM-PARTY] >>> Broadcasting JoinGameMsg! region={region} gameId={gameId} isPublic={pub} — clients will switch to match server."
                );
            }
            catch { /* best-effort */ }
        }
    }

    // ---------------------------------------------------------------------------
    // AndromedaServer — patch the min-player check from < 2 to < 1
    // ---------------------------------------------------------------------------

    /// <summary>
    /// AndromedaServer.Setup() is an `async void` method compiled into a state
    /// machine class named "&lt;Setup&gt;d__N".  It contains the check:
    ///
    ///     if (andromedaServer.players.Count &lt; 2 || andromedaServer.players.Count &gt; 8)
    ///         programServer.Quit("player failed to connect");
    ///
    /// We use a Harmony Transpiler on the state machine's MoveNext() method to
    /// replace the literal constant 2 (ldc.i4.2) that feeds the bge (branch if
    /// greater or equal) comparison with 1 (ldc.i4.1), effectively changing
    /// "Count &lt; 2" to "Count &lt; 1".
    ///
    /// The state machine type is searched for at runtime to avoid hard-coding the
    /// compiler-generated name suffix.
    /// </summary>
    [HarmonyPatch]
    public static class AndromedaServerMinPlayerPatch
    {
        private static MethodBase _moveNextMethod;

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Find the async state machine type for AndromedaServer.Setup()
                Type andromedaType = typeof(AndromedaServer);
                Type stateMachineType = null;
                foreach (Type nested in andromedaType.GetNestedTypes(
                    BindingFlags.NonPublic | BindingFlags.Public))
                {
                    // The name is something like "<Setup>d__5" or "<Setup>d__38"
                    if (nested.Name.StartsWith("<Setup>"))
                    {
                        stateMachineType = nested;
                        break;
                    }
                }

                if (stateMachineType == null)
                {
                    MelonLogger.Warning("[ANDROMEDA-PATCH] Could not find AndromedaServer Setup state machine type!");
                    return;
                }

                MethodInfo moveNext = stateMachineType.GetMethod("MoveNext",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (moveNext == null)
                {
                    MelonLogger.Warning("[ANDROMEDA-PATCH] Could not find MoveNext on state machine!");
                    return;
                }

                _moveNextMethod = moveNext;
                var transpiler = new HarmonyMethod(typeof(AndromedaServerMinPlayerPatch),
                    nameof(TranspileMinPlayerCheck));
                harmony.Patch(moveNext, transpiler: transpiler);
                MelonLogger.Msg("[ANDROMEDA-PATCH] Successfully patched AndromedaServer Setup — min player = 1.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ANDROMEDA-PATCH] Failed to apply patch: {ex}");
            }
        }

        /// <summary>
        /// Scans the MoveNext IL stream for the pattern:
        ///   call/callvirt get_Count  (get players.Count)
        ///   ldc.i4.2                 (push constant 2)
        ///   blt / bge / blt.s / bge.s  (compare and branch)
        /// and replaces ldc.i4.2 with ldc.i4.1,
        /// changing "Count &lt; 2" to "Count &lt; 1" (i.e. only quit if 0 players).
        /// We only replace the FIRST occurrence to avoid touching the &gt; 8 check.
        /// </summary>
        private static IEnumerable<CodeInstruction> TranspileMinPlayerCheck(
            IEnumerable<CodeInstruction> instructions)
        {
            bool patched = false;
            CodeInstruction prev = null;

            foreach (CodeInstruction instr in instructions)
            {
                if (!patched && prev != null
                    && prev.opcode == OpCodes.Ldc_I4_2
                    && (instr.opcode == OpCodes.Blt
                        || instr.opcode == OpCodes.Blt_S
                        || instr.opcode == OpCodes.Blt_Un
                        || instr.opcode == OpCodes.Blt_Un_S
                        || instr.opcode == OpCodes.Bge
                        || instr.opcode == OpCodes.Bge_S
                        || instr.opcode == OpCodes.Bge_Un
                        || instr.opcode == OpCodes.Bge_Un_S))
                {
                    // Replace ldc.i4.2 with ldc.i4.1
                    MelonLogger.Msg("[ANDROMEDA-PATCH] Transpiler: replacing ldc.i4.2 → ldc.i4.1 for min-player check");
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    patched = true;
                    prev = null;
                    yield return instr; // keep the original branch opcode
                    continue;
                }

                if (prev != null)
                    yield return prev;

                prev = instr;
            }

            if (prev != null)
                yield return prev;

            if (!patched)
            {
                MelonLogger.Warning("[ANDROMEDA-PATCH] Transpiler did not find ldc.i4.2+branch pattern — min-player check NOT patched!");
            }
        }
    }
}
