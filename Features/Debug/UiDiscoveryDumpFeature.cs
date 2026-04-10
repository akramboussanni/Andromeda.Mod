using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Windwalk.Net;

namespace Andromeda.Mod.Features
{
    public static class UiDiscoveryDumpFeature
    {
        private const KeyCode DumpKey = KeyCode.F8;
        private const int MaxTreeDepth = 5;

        public static void Update()
        {
            if (!Input.GetKeyDown(DumpKey))
                return;

            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
                return;

            DumpNow();
        }

        private static void DumpNow()
        {
            try
            {
                var sb = new StringBuilder(16 * 1024);
                sb.AppendLine("=== Andromeda UI Discovery Dump ===");
                sb.AppendLine("utc=" + DateTime.UtcNow.ToString("O"));

                var scene = SceneManager.GetActiveScene();
                sb.AppendLine("scene=" + scene.name + " (loaded=" + scene.isLoaded + ")");

                var programClient = Singleton.Existing<ProgramClient>();
                if (programClient != null)
                {
                    bool clientActive = false;
                    try
                    {
                        var netClient = Singleton.Existing<NetClient>();
                        clientActive = netClient != null && netClient.Active;
                    }
                    catch { }

                    sb.AppendLine("programClient.clientActive=" + clientActive);
                    sb.AppendLine("programClient.region=" + (programClient.Region ?? "<null>"));
                    sb.AppendLine("programClient.gameName=" + (programClient.GameName ?? "<null>"));
                    sb.AppendLine("programClient.gameId=" + (programClient.GameId ?? "<null>"));
                }

                DumpPlayPanel(sb);
                DumpModalRoots(sb);
                DumpComponentIndex<TabButton>(sb, "TabButton");
                DumpComponentIndex<ToggleUI>(sb, "ToggleUI");
                DumpComponentIndex<SliderUI>(sb, "SliderUI");
                DumpComponentIndex<Dialog>(sb, "Dialog");
                DumpComponentIndex<TMP_InputField>(sb, "TMP_InputField");
                DumpComponentIndex<TMP_Dropdown>(sb, "TMP_Dropdown");

                string outDir = Path.Combine(global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData), "Andromeda", "ui-dumps");
                Directory.CreateDirectory(outDir);
                string filePath = Path.Combine(outDir, "ui-dump-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(filePath, sb.ToString());

                MelonLogger.Msg("[UI-DUMP] Wrote UI snapshot to: " + filePath);
                NetworkDebugger.LogLobbyEvent("[UI-DUMP] Snapshot written. Path: " + filePath);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[UI-DUMP] Failed: " + ex.Message);
            }
        }

        private static void DumpPlayPanel(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("-- PlayPanel --");

            PlayPanel panel = null;
            try
            {
                panel = UnityEngine.Object.FindObjectOfType<PlayPanel>();
            }
            catch { }

            if (panel == null)
            {
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<PlayPanel>();
                    if (all != null && all.Length > 0)
                        panel = all[0];
                }
                catch { }
            }

            if (panel == null)
            {
                sb.AppendLine("PlayPanel not found.");
                return;
            }

            sb.AppendLine("PlayPanel found: " + GetPath(panel.transform));
            sb.AppendLine("activeInHierarchy=" + panel.gameObject.activeInHierarchy);

            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var customButtonRootField = typeof(PlayPanel).GetField("customTabButtonRoot", flags);
            var customContentRootField = typeof(PlayPanel).GetField("customTabContentRoot", flags);

            var buttonRoot = customButtonRootField?.GetValue(panel) as RectTransform;
            var contentRoot = customContentRootField?.GetValue(panel) as RectTransform;

            sb.AppendLine();
            sb.AppendLine("customTabButtonRoot=" + (buttonRoot != null ? GetPath(buttonRoot) : "<null>"));
            if (buttonRoot != null)
                DumpTransformTree(sb, buttonRoot, 0, MaxTreeDepth);

            sb.AppendLine();
            sb.AppendLine("customTabContentRoot=" + (contentRoot != null ? GetPath(contentRoot) : "<null>"));
            if (contentRoot != null)
                DumpTransformTree(sb, contentRoot, 0, MaxTreeDepth);
        }

        private static void DumpComponentIndex<T>(StringBuilder sb, string title) where T : Component
        {
            sb.AppendLine();
            sb.AppendLine("-- " + title + " Index --");

            T[] found;
            try
            {
                found = Resources.FindObjectsOfTypeAll<T>();
            }
            catch
            {
                sb.AppendLine("Failed to enumerate " + title + ".");
                return;
            }

            int count = 0;
            for (int i = 0; i < found.Length; i++)
            {
                var c = found[i];
                if (c == null || c.gameObject == null)
                    continue;
                if (!c.gameObject.scene.IsValid())
                    continue;

                count++;
                sb.AppendLine(count + ". " + GetPath(c.transform) + " active=" + c.gameObject.activeInHierarchy);
            }

            if (count == 0)
                sb.AppendLine("(none)");
        }

        private static void DumpModalRoots(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("-- Modal Roots --");

            Transform[] all;
            try
            {
                all = Resources.FindObjectsOfTypeAll<Transform>();
            }
            catch
            {
                sb.AppendLine("Failed to enumerate transforms.");
                return;
            }

            int printed = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t.gameObject == null)
                    continue;

                string path = GetPath(t);
                bool looksModal =
                    path.IndexOf("/Modals", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("/Dialog", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.name.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!looksModal)
                    continue;

                if (t.parent != null && GetPath(t.parent).IndexOf("/Modals", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // keep mostly top-level modal nodes readable
                }

                printed++;
                sb.AppendLine(printed + ". " + path + " active=" + t.gameObject.activeInHierarchy);

                if (printed >= 30)
                    break;
            }

            if (printed == 0)
                sb.AppendLine("(none)");
        }

        private static void DumpTransformTree(StringBuilder sb, Transform root, int depth, int maxDepth)
        {
            if (root == null || depth > maxDepth)
                return;

            string indent = new string(' ', depth * 2);
            sb.Append(indent).Append("- ").Append(root.name)
                .Append(" [active=").Append(root.gameObject.activeInHierarchy).Append("]")
                .Append(" path=").Append(GetPath(root))
                .AppendLine();

            var components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var comp = components[i];
                sb.Append(indent).Append("  * ").Append(comp == null ? "<MissingComponent>" : comp.GetType().Name).AppendLine();
            }

            for (int i = 0; i < root.childCount; i++)
            {
                DumpTransformTree(sb, root.GetChild(i), depth + 1, maxDepth);
            }
        }

        private static string GetPath(Transform t)
        {
            if (t == null)
                return "<null>";

            var parts = new List<string>();
            var cur = t;
            while (cur != null)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
