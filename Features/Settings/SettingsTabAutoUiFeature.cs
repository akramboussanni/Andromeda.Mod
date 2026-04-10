using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Andromeda.Mod.Settings;

namespace Andromeda.Mod.Features
{
    public static class SettingsTabAutoUiFeature
    {
        private static readonly HashSet<int> InjectedTabs = new HashSet<int>();

        public static void EnsureInjected(GeneralSettingsTab tab)
        {
            if (tab == null)
                return;

            AndromedaClientSettings.Initialize();

            int id = tab.GetInstanceID();
            if (InjectedTabs.Contains(id))
                return;

            try
            {
                var tr = HarmonyLib.Traverse.Create(tab);
                var baseToggle = tr.Field("debugToggle").GetValue<ToggleUI>();
                var baseSlider = tr.Field("mouseSensitivitySlider").GetValue<SliderUI>();

                if (baseToggle == null || baseSlider == null)
                    return;

                var toggleRowTemplate = ResolveRowTemplate(baseToggle);
                var sliderRowTemplate = ResolveRowTemplate(baseSlider);
                if (toggleRowTemplate == null || sliderRowTemplate == null)
                    return;

                var rows = new List<(ISettingDefinition Definition, SettingUiDescriptor Ui, SettingControlKind Kind)>();
                foreach (var kv in AndromedaClientSettings.Definitions)
                {
                    var definition = kv.Value;
                    if (!AndromedaClientSettings.TryGetUiDescriptor(definition, out SettingUiDescriptor ui))
                        continue;

                    rows.Add((definition, ui, AndromedaClientSettings.GetAutoControlKind(definition)));
                }

                rows.Sort((a, b) =>
                {
                    string aSection = a.Ui.Section ?? string.Empty;
                    string bSection = b.Ui.Section ?? string.Empty;
                    int sectionCmp = string.Compare(aSection, bSection, StringComparison.OrdinalIgnoreCase);
                    if (sectionCmp != 0) return sectionCmp;

                    int orderCmp = a.Ui.Order.CompareTo(b.Ui.Order);
                    if (orderCmp != 0) return orderCmp;

                    return string.Compare(a.Ui.Label ?? string.Empty, b.Ui.Label ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                });

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];

                    if (row.Kind == SettingControlKind.Toggle)
                    {
                        SpawnToggleRow(row.Definition, row.Ui, toggleRowTemplate);
                    }
                    else if (row.Kind == SettingControlKind.Slider)
                    {
                        SpawnSliderRow(row.Definition, row.Ui, sliderRowTemplate);
                    }
                }

                InjectedTabs.Add(id);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning("[SETTINGS-UI] Injection failed: " + ex.Message);
            }
        }

        private static Transform ResolveRowTemplate(Component control)
        {
            if (control == null)
                return null;

            var row = control.transform.parent;
            if (row == null)
                return null;

            // If the direct parent is only a control wrapper, step up once to the full row.
            if (row.GetComponentsInChildren<TextMeshProUGUI>(true).Length < 1 && row.parent != null)
                row = row.parent;

            return row;
        }

        private static void SpawnToggleRow(ISettingDefinition definition, SettingUiDescriptor ui, Transform rowTemplate)
        {
            string nodeName = "AndromedaSetting_" + definition.Key;
            var parent = rowTemplate.parent;
            if (parent == null || parent.Find(nodeName) != null)
                return;

            var instance = UnityEngine.Object.Instantiate(rowTemplate.gameObject, parent);
            instance.name = nodeName;

            SetLabel(instance, ui.Label);

            var toggle = instance.GetComponentInChildren<ToggleUI>(true);
            bool current = false;
            if (AndromedaClientSettings.TryGetValue(definition.Key, out object boxed))
            {
                try { current = Convert.ToBoolean(boxed); }
                catch { }
            }

            if (toggle != null)
            {
                toggle.Initialize(current, value =>
                {
                    AndromedaClientSettings.TrySetValue(definition.Key, value, persist: true);
                });
            }
        }

        private static void SpawnSliderRow(ISettingDefinition definition, SettingUiDescriptor ui, Transform rowTemplate)
        {
            string nodeName = "AndromedaSetting_" + definition.Key;
            var parent = rowTemplate.parent;
            if (parent == null || parent.Find(nodeName) != null)
                return;

            var instance = UnityEngine.Object.Instantiate(rowTemplate.gameObject, parent);
            instance.name = nodeName;

            SetLabel(instance, ui.Label);

            var slider = instance.GetComponentInChildren<SliderUI>(true);
            float current = 0f;
            if (AndromedaClientSettings.TryGetValue(definition.Key, out object boxed))
            {
                try { current = Convert.ToSingle(boxed); }
                catch { }
            }

            float min = ui.Min ?? 0f;
            float max = ui.Max ?? 1f;
            if (slider != null)
            {
                slider.Initialize(min, max, current, textEnabled: true, roundValue: false, onValueChanged: value =>
                {
                    AndromedaClientSettings.TrySetValue(definition.Key, value, persist: true);
                });
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
                    || n.IndexOf("label", StringComparison.OrdinalIgnoreCase) >= 0)
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
