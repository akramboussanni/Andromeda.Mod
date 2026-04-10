using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Andromeda.Mod.Settings;
using LobbySettings = Andromeda.Mod.Settings.AndromedaSettings;

namespace Andromeda.Mod.Features
{
    public static class LobbyOptionsAutoUiFeature
    {
        private static readonly HashSet<int> InjectedPanels = new HashSet<int>();

        public static void EnsureInjected(PlayPanel panel)
        {
            if (panel == null)
                return;

            // Only relevant when actually in a custom party lobby.
            if (Singleton.Existing<CustomPartyClient>() == null)
                return;

            int id = panel.GetInstanceID();
            if (InjectedPanels.Contains(id))
            {
                RefreshPanel(panel);
                return;
            }

            try
            {
                var root = ResolveOptionsContentRoot(panel);
                if (root == null)
                    return;

                var switchTemplate = FindChildByNameContains(root, "Row-Generators double ammo")
                                     ?? FindToggleRowTemplate(root);
                if (switchTemplate == null)
                    return;

                InjectFirstPersonRow(root, switchTemplate);
                InjectCheatsRow(root, switchTemplate);
                RefreshPanel(panel);

                InjectedPanels.Add(id);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning("[LOBBY-SETTINGS-UI] Injection failed: " + ex.Message);
            }
        }

        public static void RefreshInjectedRows()
        {
            var panels = UnityEngine.Object.FindObjectsOfType<PlayPanel>();
            for (int i = 0; i < panels.Length; i++)
                RefreshPanel(panels[i]);
        }

        private static Transform ResolveOptionsContentRoot(PlayPanel panel)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var contentRootField = typeof(PlayPanel).GetField("customTabContentRoot", flags);
            var customTabContentRoot = contentRootField?.GetValue(panel) as RectTransform;
            if (customTabContentRoot == null)
                return null;

            var optionsPane = customTabContentRoot.Find("Options Pane");
            if (optionsPane == null)
                return null;

            return optionsPane.Find("Scroll View/Viewport/Content");
        }

        private static void InjectFirstPersonRow(Transform contentRoot, Transform switchTemplate)
        {
            const string rowName = "Andromeda_Row_FirstPerson";
            if (contentRoot.Find(rowName) != null)
                return;

            var row = UnityEngine.Object.Instantiate(switchTemplate.gameObject, contentRoot);
            row.name = rowName;
            row.SetActive(true);

            SetLabel(row, "First Person Mode");

            // Keep leader-gating behavior from vanilla switch rows.
            DestroyComponentByName(row, "SwitchDataBinder");

            bool current = LobbySettings.FirstPersonEnabled.Value;

            var toggleUi = row.GetComponentInChildren<ToggleUI>(true);
            if (toggleUi != null)
            {
                toggleUi.Initialize(current, value =>
                {
                    LobbySettings.SetFirstPersonEnabled(value, persist: true);
                    LobbySettingsReplicationFeature.PublishFromLocalHost("first-person-toggle");
                });
            }
            else
            {
                var toggle = row.GetComponentInChildren<Toggle>(true);
                if (toggle != null)
                {
                    toggle.isOn = current;
                    toggle.onValueChanged.RemoveAllListeners();
                    toggle.onValueChanged.AddListener(value =>
                    {
                        LobbySettings.SetFirstPersonEnabled(value, persist: true);
                        LobbySettingsReplicationFeature.PublishFromLocalHost("first-person-toggle");
                    });
                }
            }

            PlaceAfterGeneralHeader(contentRoot, row.transform);
        }

        private static void InjectCheatsRow(Transform contentRoot, Transform switchTemplate)
        {
            const string rowName = "Andromeda_Row_Cheats";
            if (contentRoot.Find(rowName) != null)
                return;

            var row = UnityEngine.Object.Instantiate(switchTemplate.gameObject, contentRoot);
            row.name = rowName;
            row.SetActive(true);

            SetLabel(row, "Cheats");

            DestroyComponentByName(row, "SwitchDataBinder");

            bool current = LobbySettings.CheatsEnabled.Value;

            var toggleUi = row.GetComponentInChildren<ToggleUI>(true);
            if (toggleUi != null)
            {
                toggleUi.Initialize(current, value =>
                {
                    LobbySettings.SetCheatsEnabled(value, persist: true);
                    LobbySettingsReplicationFeature.PublishFromLocalHost("cheats-toggle");
                });
            }
            else
            {
                var toggle = row.GetComponentInChildren<Toggle>(true);
                if (toggle != null)
                {
                    toggle.isOn = current;
                    toggle.onValueChanged.RemoveAllListeners();
                    toggle.onValueChanged.AddListener(value =>
                    {
                        LobbySettings.SetCheatsEnabled(value, persist: true);
                        LobbySettingsReplicationFeature.PublishFromLocalHost("cheats-toggle");
                    });
                }
            }

            PlaceAfterGeneralHeader(contentRoot, row.transform);
        }

        private static void RefreshPanel(PlayPanel panel)
        {
            if (panel == null)
                return;

            var contentRoot = ResolveOptionsContentRoot(panel);
            if (contentRoot == null)
                return;

            RefreshRow(contentRoot, "Andromeda_Row_FirstPerson",
                LobbySettings.FirstPersonEnabled.Value,
                LobbySettingsReplicationFeature.FirstPersonKey);

            RefreshRow(contentRoot, "Andromeda_Row_Cheats",
                LobbySettings.CheatsEnabled.Value,
                LobbySettingsReplicationFeature.CheatsKey);
        }

        private static void RefreshRow(Transform contentRoot, string rowName, bool defaultValue, string networkKey)
        {
            var row = contentRoot.Find(rowName);
            if (row == null)
                return;

            bool value = defaultValue;
            if (LobbySettingsReplicationFeature.TryGetBoolean(networkKey, out bool networked))
                value = networked;

            var toggle = row.GetComponentInChildren<Toggle>(true);
            if (toggle != null)
            {
                toggle.SetIsOnWithoutNotify(value);
                toggle.interactable = LobbySettingsReplicationFeature.IsLocalPartyLeader();
            }
        }

        private static void PlaceAfterGeneralHeader(Transform contentRoot, Transform row)
        {
            var header = contentRoot.Find("General Options");
            if (header == null)
                return;

            int idx = header.GetSiblingIndex() + 1;
            row.SetSiblingIndex(Mathf.Clamp(idx, 0, contentRoot.childCount - 1));
        }

        private static Transform FindChildByNameContains(Transform root, string text)
        {
            if (root == null || string.IsNullOrEmpty(text))
                return null;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;

                var nested = FindChildByNameContains(child, text);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static Transform FindToggleRowTemplate(Transform contentRoot)
        {
            if (contentRoot == null)
                return null;

            for (int i = 0; i < contentRoot.childCount; i++)
            {
                var child = contentRoot.GetChild(i);
                if (child == null)
                    continue;

                if ((child.name ?? string.Empty).StartsWith("Andromeda_", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (child.GetComponentInChildren<ToggleUI>(true) != null)
                    return child;
            }

            return null;
        }

        private static void DestroyComponentByName(GameObject root, string typeName)
        {
            if (root == null || string.IsNullOrEmpty(typeName))
                return;

            var components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                if (!string.Equals(c.GetType().Name, typeName, StringComparison.Ordinal))
                    continue;

                UnityEngine.Object.Destroy(c);
            }
        }

        private static void SetLabel(GameObject root, string label)
        {
            if (root == null || string.IsNullOrEmpty(label))
                return;

            var texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null)
                    continue;

                string n = t.name ?? string.Empty;
                if (n.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("label", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    t.text = label;
                    return;
                }
            }

            if (texts.Length > 0 && texts[0] != null)
                texts[0].text = label;
        }
    }
}
