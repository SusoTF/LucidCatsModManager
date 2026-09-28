using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LucidCatsModManager
{
    internal sealed class SettingsEditor
    {
        private const float LabelX = 40f;
        private const float LabelWidth = 340f;
        private const float ControlX = 400f;
        private const float ValueWidth = 70f;
        private const float ResetWidth = 100f;
        private const float RightMargin = 40f;
        private const float ControlRightEdge = RightMargin + ResetWidth + 20f;
        private static readonly Color ResetColor = new Color(1f, 0.85f, 0.25f);

        private sealed class SettingRow
        {
            public ConfigEntryBase Entry;
            public GameObject Root;
            public TMP_Text ValueText;
            public Toggle Toggle;
            public Slider Slider;
            public TMP_Dropdown Dropdown;
            public List<object> DropdownValues;
            public TMP_InputField Field;
            public TextButton Reset;
            public bool ReadOnly;
        }

        public sealed class Templates
        {
            public GameObject Section;
            public GameObject SliderRow;
            public GameObject ToggleRow;
            public GameObject DropdownRow;
            public TMP_Text Action;

            public bool Complete => Section != null && SliderRow != null && ToggleRow != null && DropdownRow != null && Action != null;
        }

        private readonly RectTransform content;
        private readonly TMP_Text hint;
        private readonly Templates templates;
        private readonly List<SettingRow> rows = new List<SettingRow>();
        private readonly List<GameObject> built = new List<GameObject>();

        public const string DefaultHint = "Hover over a setting to see what it does. Changes are saved straight away; some mods only apply them after restarting the game.";

        public SettingsEditor(RectTransform content, TMP_Text hint, Templates templates)
        {
            this.content = content;
            this.hint = hint;
            this.templates = templates;
        }

        private static readonly string[] PreferredSliders = { "mouseSensitivitySlider", "fieldOfViewSlider", "masterVolumeSlider" };
        private static readonly string[] PreferredToggles = { "hideCrosshairToggle", "headBobToggle", "vSyncToggle" };
        private static readonly string[] PreferredDropdowns = { "qualityDropdown", "pixelationDropdown", "maxFpsDropdown", "displayModeDropdown" };

        public static Templates FindTemplates(Transform storage, TMP_Text actionTemplate, out string report)
        {
            var found = new Templates { Action = actionTemplate };
            report = "";

            Type menuType = HarmonyLib.AccessTools.TypeByName("Game.UI.SettingsMenu");
            Object settingsMenu = menuType != null ? Object.FindFirstObjectByType(menuType, FindObjectsInactive.Include) : null;
            if (settingsMenu == null)
            {
                report = "the game's Settings menu wasn't found";
                return found;
            }

            var controls = new Dictionary<string, Component>();
            for (Type t = menuType; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (field.GetValue(settingsMenu) is Component component && component != null &&
                        (component is Slider || component is Toggle || component is TMP_Dropdown))
                        controls[field.Name] = component;

            GameObject slider = PickRow<Slider>(controls, PreferredSliders);
            GameObject toggle = PickRow<Toggle>(controls, PreferredToggles);
            GameObject dropdown = PickRow<TMP_Dropdown>(controls, PreferredDropdowns);

            GameObject section = null;
            Transform rowsParent = (slider ?? toggle ?? dropdown)?.transform.parent;
            if (rowsParent != null)
                foreach (Transform child in rowsParent)
                    if (child.GetComponent<TMP_Text>() != null)
                    {
                        section = child.gameObject;
                        break;
                    }

            report = $"section: {Describe(section)}, slider row: {Describe(slider)}, toggle row: {Describe(toggle)}, dropdown row: {Describe(dropdown)}";

            found.Section = Store(section, storage, "Section Template");
            found.SliderRow = Store(slider, storage, "Slider Row Template");
            found.ToggleRow = Store(toggle, storage, "Toggle Row Template");
            found.DropdownRow = Store(dropdown, storage, "Dropdown Row Template");
            return found;
        }

        private static string Describe(GameObject go) => go != null ? "'" + go.name + "'" : "NOT FOUND";

        private static GameObject PickRow<T>(Dictionary<string, Component> controls, string[] preferred) where T : Component
        {
            IEnumerable<Component> candidates = preferred
                .Where(controls.ContainsKey).Select(name => controls[name])
                .Concat(controls.Values.Where(c => c is T))
                .Where(c => c is T);

            GameObject fallback = null;
            foreach (Component control in candidates)
            {
                Transform row = control.transform.parent;
                if (row == null)
                    continue;
                if (fallback == null)
                    fallback = row.gameObject;

                bool onlyThisKind =
                    (typeof(T) == typeof(Slider) || ActiveChild<Slider>(row) == null) &&
                    (typeof(T) == typeof(Toggle) || ActiveChild<Toggle>(row) == null) &&
                    (typeof(T) == typeof(TMP_Dropdown) || ActiveChild<TMP_Dropdown>(row) == null);
                if (onlyThisKind && IsPlainRow(row))
                    return row.gameObject;
            }
            return fallback;
        }

        public static Templates FindTemplatesInGrid(Transform settingsGrid, Transform storage, TMP_Text actionTemplate)
        {
            var found = new Templates { Action = actionTemplate };
            GameObject slider = null, toggle = null, dropdown = null, section = null;
            GameObject sliderAny = null, toggleAny = null, dropdownAny = null;

            foreach (Transform child in settingsGrid)
            {
                if (child.GetComponent<TMP_Text>() != null)
                {
                    if (section == null)
                        section = child.gameObject;
                    continue;
                }

                bool hasSlider = ActiveChild<Slider>(child) != null;
                bool hasToggle = ActiveChild<Toggle>(child) != null;
                bool hasDropdown = ActiveChild<TMP_Dropdown>(child) != null;
                bool plain = IsPlainRow(child);

                if (hasSlider && !hasToggle && !hasDropdown)
                {
                    if (plain && slider == null) slider = child.gameObject;
                    if (sliderAny == null) sliderAny = child.gameObject;
                }
                else if (hasToggle && !hasDropdown)
                {
                    if (plain && toggle == null) toggle = child.gameObject;
                    if (toggleAny == null) toggleAny = child.gameObject;
                }
                else if (hasDropdown && !hasToggle)
                {
                    if (plain && dropdown == null) dropdown = child.gameObject;
                    if (dropdownAny == null) dropdownAny = child.gameObject;
                }
            }

            found.Section = Store(section, storage, "Section Template");
            found.SliderRow = Store(slider ?? sliderAny, storage, "Slider Row Template");
            found.ToggleRow = Store(toggle ?? toggleAny, storage, "Toggle Row Template");
            found.DropdownRow = Store(dropdown ?? dropdownAny, storage, "Dropdown Row Template");
            return found;
        }

        private static T ActiveChild<T>(Transform row) where T : Component
        {
            foreach (T component in row.GetComponentsInChildren<T>(true))
                if (component.gameObject.activeSelf)
                    return component;
            return null;
        }

        private static bool IsPlainRow(Transform row)
        {
            foreach (MonoBehaviour mb in row.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Assembly.GetName().Name == "Assembly-CSharp")
                    return false;
            return true;
        }

        private static GameObject Store(GameObject source, Transform storage, string name)
        {
            if (source == null)
                return null;
            GameObject copy = Object.Instantiate(source, storage, false);
            copy.name = name;
            copy.SetActive(false);
            SilenceControls(copy);
            return copy;
        }

        private static void SilenceControls(GameObject go)
        {
            foreach (Slider slider in go.GetComponentsInChildren<Slider>(true))
                Silence(slider.onValueChanged);
            foreach (Toggle toggle in go.GetComponentsInChildren<Toggle>(true))
                Silence(toggle.onValueChanged);
            foreach (TMP_Dropdown dropdown in go.GetComponentsInChildren<TMP_Dropdown>(true))
                Silence(dropdown.onValueChanged);
        }

        private static void Silence(UnityEventBase unityEvent)
        {
            if (unityEvent == null)
                return;
            unityEvent.RemoveAllListeners();
            for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
                unityEvent.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        public static bool HasEditableSettings(ConfigFile config)
        {
            return config != null && VisibleEntries(config).Any();
        }

        private static IEnumerable<ConfigEntryBase> VisibleEntries(ConfigFile config)
        {
            var entries = new List<ConfigEntryBase>();
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in config)
                if (pair.Value != null && Tag<bool?>(pair.Value, "Browsable") != false)
                    entries.Add(pair.Value);
            return entries;
        }

        private static T Tag<T>(ConfigEntryBase entry, string name)
        {
            object[] tags = entry.Description?.Tags;
            if (tags == null)
                return default;

            foreach (object tag in tags)
            {
                if (tag == null)
                    continue;
                Type type = tag.GetType();
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
                object value = field != null ? field.GetValue(tag) : type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(tag);
                if (value is T typed)
                    return typed;
                if (value != null)
                {
                    try { return (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T)); }
                    catch { }
                }
            }
            return default;
        }

        public void Show(ConfigFile config)
        {
            Clear();
            hint.text = DefaultHint;

            List<ConfigEntryBase> entries = VisibleEntries(config).ToList();

            var sections = new List<string>();
            foreach (ConfigEntryBase entry in entries)
                if (!sections.Contains(entry.Definition.Section))
                    sections.Add(entry.Definition.Section);

            foreach (string section in sections)
            {
                GameObject title = Object.Instantiate(templates.Section, content, false);
                title.name = "Section " + section;
                title.SetActive(true);
                TMP_Text titleText = title.GetComponent<TMP_Text>();
                titleText.text = section;
                titleText.margin = new Vector4(LabelX, 0f, 0f, 0f);
                built.Add(title);

                IEnumerable<ConfigEntryBase> inSection = entries
                    .Where(e => e.Definition.Section == section)
                    .Select((e, index) => (entry: e, index))
                    .OrderByDescending(x => Tag<int?>(x.entry, "Order") ?? 0)
                    .ThenBy(x => x.index)
                    .Select(x => x.entry);

                foreach (ConfigEntryBase entry in inSection)
                {
                    try
                    {
                        SettingRow row = BuildRow(entry);
                        rows.Add(row);
                        built.Add(row.Root);
                        Refresh(row);
                    }
                    catch (Exception e)
                    {
                        ModManagerPlugin.Log.LogWarning($"Could not show the setting {entry.Definition}: {e.Message}");
                    }
                }
            }
        }

        public void Clear()
        {
            foreach (GameObject go in built)
                if (go != null)
                    Object.Destroy(go);
            built.Clear();
            rows.Clear();
        }

        public void ResetAll()
        {
            foreach (SettingRow row in rows)
            {
                if (row.ReadOnly)
                    continue;
                try { row.Entry.BoxedValue = row.Entry.DefaultValue; }
                catch (Exception e) { ModManagerPlugin.Log.LogWarning($"Could not reset {row.Entry.Definition}: {e.Message}"); }
                Refresh(row);
            }
        }

        private SettingRow BuildRow(ConfigEntryBase entry)
        {
            Type type = entry.SettingType;
            AcceptableValueBase acceptable = entry.Description?.AcceptableValues;
            bool isRange = acceptable != null && acceptable.GetType().IsGenericType &&
                           acceptable.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueRange<>);
            bool isList = acceptable != null && acceptable.GetType().IsGenericType &&
                          acceptable.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueList<>);
            bool isFlags = type.IsEnum && type.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0;

            SettingRow row;
            if (type == typeof(bool))
                row = BuildToggleRow(entry);
            else if (isRange && IsNumber(type))
                row = BuildSliderRow(entry, acceptable);
            else if (type.IsEnum && !isFlags)
                row = BuildDropdownRow(entry, Enum.GetValues(type).Cast<object>().ToList());
            else if (isList)
                row = BuildDropdownRow(entry, ((Array)acceptable.GetType().GetProperty("AcceptableValues").GetValue(acceptable)).Cast<object>().ToList());
            else
                row = BuildTextRow(entry);

            row.ReadOnly = Tag<bool?>(entry, "ReadOnly") == true;
            SetInteractable(row, !row.ReadOnly);

            TMP_Text label = FindLabel(row.Root.transform);
            if (label != null)
            {
                label.text = Tag<string>(entry, "DispName") ?? Humanize(entry.Definition.Key);
                float size = label.fontSize;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.enableAutoSizing = true;
                label.fontSizeMax = size;
                label.fontSizeMin = size * 0.6f;
                PlaceLeft(label.rectTransform, LabelX, LabelWidth, 44f);
                label.alignment = TextAlignmentOptions.Left;
            }

            Image hitArea = row.Root.GetComponent<Image>() ?? row.Root.AddComponent<Image>();
            hitArea.color = new Color(0f, 0f, 0f, 0f);
            RowHover hover = row.Root.AddComponent<RowHover>();
            hover.Entered = () => hint.text = BuildHint(entry);
            hover.Exited = () => hint.text = DefaultHint;

            LayoutControls(row);
            row.Reset = AddResetButton(row);
            return row;
        }

        private GameObject NewRow(GameObject template, string name)
        {
            GameObject go = Object.Instantiate(template, content, false);
            go.name = name;
            go.SetActive(true);
            return go;
        }

        private SettingRow BuildToggleRow(ConfigEntryBase entry)
        {
            var row = new SettingRow { Entry = entry, Root = NewRow(templates.ToggleRow, "Setting " + entry.Definition.Key) };
            row.Toggle = ActiveChild<Toggle>(row.Root.transform);
            row.Toggle.onValueChanged.AddListener(on => Apply(row, on));
            return row;
        }

        private SettingRow BuildSliderRow(ConfigEntryBase entry, AcceptableValueBase range)
        {
            var row = new SettingRow { Entry = entry, Root = NewRow(templates.SliderRow, "Setting " + entry.Definition.Key) };
            row.Slider = ActiveChild<Slider>(row.Root.transform);
            row.ValueText = FindValueText(row.Root.transform);
            if (row.ValueText != null)
                row.ValueText.gameObject.SetActive(true);

            Type rangeType = range.GetType();
            row.Slider.minValue = Convert.ToSingle(rangeType.GetProperty("MinValue").GetValue(range), CultureInfo.InvariantCulture);
            row.Slider.maxValue = Convert.ToSingle(rangeType.GetProperty("MaxValue").GetValue(range), CultureInfo.InvariantCulture);
            row.Slider.wholeNumbers = IsWholeNumber(entry.SettingType);
            row.Slider.onValueChanged.AddListener(value => Apply(row, value));
            return row;
        }

        private SettingRow BuildDropdownRow(ConfigEntryBase entry, List<object> values)
        {
            var row = new SettingRow { Entry = entry, Root = NewRow(templates.DropdownRow, "Setting " + entry.Definition.Key) };
            row.Dropdown = ActiveChild<TMP_Dropdown>(row.Root.transform);
            row.DropdownValues = values;
            row.Dropdown.ClearOptions();
            row.Dropdown.AddOptions(values.Select(v => Humanize(v.ToString())).ToList());
            row.Dropdown.onValueChanged.AddListener(index =>
            {
                if (index >= 0 && index < row.DropdownValues.Count)
                    Apply(row, row.DropdownValues[index]);
            });
            return row;
        }

        private SettingRow BuildTextRow(ConfigEntryBase entry)
        {
            var row = new SettingRow { Entry = entry, Root = NewRow(templates.SliderRow, "Setting " + entry.Definition.Key) };

            Slider slider = ActiveChild<Slider>(row.Root.transform);
            TMP_Text valueStyle = FindValueText(row.Root.transform);
            if (valueStyle != null)
                valueStyle.gameObject.SetActive(false);

            var boxObject = new GameObject("Value Box", typeof(RectTransform));
            boxObject.transform.SetParent(row.Root.transform, false);
            var box = (RectTransform)boxObject.transform;
            slider.gameObject.SetActive(false);

            Image background = boxObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.08f);

            RectTransform underline = MenuUtil.NewRect("Underline", box);
            underline.anchorMin = new Vector2(0f, 0f);
            underline.anchorMax = new Vector2(1f, 0f);
            underline.pivot = new Vector2(0.5f, 0f);
            underline.offsetMin = Vector2.zero;
            underline.offsetMax = new Vector2(0f, 2f);
            Image line = underline.gameObject.AddComponent<Image>();
            line.color = new Color(1f, 1f, 1f, 0.35f);
            line.raycastTarget = false;

            var areaObject = new GameObject("Text Area", typeof(RectTransform));
            areaObject.transform.SetParent(box, false);
            var area = (RectTransform)areaObject.transform;
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(8f, 0f);
            area.offsetMax = new Vector2(-8f, 0f);
            areaObject.AddComponent<RectMask2D>();

            TMP_Text text = valueStyle != null
                ? MenuUtil.CloneText(valueStyle.transform, area, "Text")
                : MenuUtil.CloneText(templates.Action.transform, area, "Text");
            MenuUtil.Stretch(text.rectTransform, 0f, 0f, 1f, 1f);
            text.alignment = TextAlignmentOptions.Left;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            boxObject.SetActive(false);
            TMP_InputField field = boxObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.onEndEdit.AddListener(value =>
            {
                if (!field.wasCanceled)
                    ApplyText(row, value);
                Refresh(row);
            });
            boxObject.SetActive(true);

            row.Field = field;
            return row;
        }

        private void Apply(SettingRow row, object value)
        {
            if (row.ReadOnly)
                return;
            try
            {
                Type type = row.Entry.SettingType;
                object converted = type.IsInstanceOfType(value) ? value : ConvertNumber(value, type);
                if (!Equals(row.Entry.BoxedValue, converted))
                    row.Entry.BoxedValue = converted;
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogWarning($"Could not change {row.Entry.Definition}: {e.Message}");
            }
            Refresh(row);
        }

        private void ApplyText(SettingRow row, string text)
        {
            if (row.ReadOnly)
                return;
            try
            {
                if (row.Entry.SettingType == typeof(string))
                    row.Entry.BoxedValue = text;
                else
                    row.Entry.SetSerializedValue(text.Trim());
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogWarning($"Could not change {row.Entry.Definition}: {e.Message}");
            }
        }

        private void Refresh(SettingRow row)
        {
            object value = row.Entry.BoxedValue;

            if (row.Toggle != null)
                row.Toggle.SetIsOnWithoutNotify(value is bool on && on);

            if (row.Slider != null)
            {
                float number = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                row.Slider.SetValueWithoutNotify(number);
                if (row.ValueText != null)
                    row.ValueText.text = FormatValue(value);
            }

            if (row.Dropdown != null)
            {
                int index = row.DropdownValues.FindIndex(v => Equals(v, value));
                if (index >= 0)
                    row.Dropdown.SetValueWithoutNotify(index);
                row.Dropdown.RefreshShownValue();
            }

            if (row.Field != null && !row.Field.isFocused)
                row.Field.text = row.Entry.SettingType == typeof(string) ? value as string ?? "" : row.Entry.GetSerializedValue();

            if (row.Reset != null)
                row.Reset.gameObject.SetActive(!row.ReadOnly && !SameValue(value, row.Entry.DefaultValue));
        }

        private static bool SameValue(object a, object b)
        {
            if (a is float fa && b is float fb)
                return Mathf.Abs(fa - fb) < 0.0001f;
            if (a is double da && b is double db)
                return Math.Abs(da - db) < 0.0001;
            return Equals(a, b);
        }

        private static string FormatValue(object value)
        {
            switch (value)
            {
                case bool b: return b ? "On" : "Off";
                case float f: return f.ToString("0.##", CultureInfo.InvariantCulture);
                case double d: return d.ToString("0.##", CultureInfo.InvariantCulture);
                case decimal m: return m.ToString("0.##", CultureInfo.InvariantCulture);
                case Enum e: return Humanize(e.ToString());
                case null: return "-";
                default: return value.ToString();
            }
        }

        private static string BuildHint(ConfigEntryBase entry)
        {
            var parts = new List<string>();
            string description = entry.Description?.Description;
            if (!string.IsNullOrWhiteSpace(description))
                parts.Add("<noparse>" + description.Trim().Replace("</noparse>", "") + "</noparse>");

            var details = new List<string> { "Default: " + FormatValue(entry.DefaultValue) };
            string limits = entry.Description?.AcceptableValues?.ToDescriptionString();
            if (!string.IsNullOrWhiteSpace(limits))
                details.Add(limits.TrimStart('#', ' '));
            parts.Add("<color=#8C8C8C>" + string.Join("  ·  ", details) + "</color>");

            return string.Join("\n", parts);
        }

        private static void LayoutControls(SettingRow row)
        {
            if (row.Slider != null)
            {
                if (row.ValueText != null)
                {
                    PlaceLeft(row.ValueText.rectTransform, ControlX, ValueWidth, 40f);
                    row.ValueText.alignment = TextAlignmentOptions.Left;
                    row.ValueText.textWrappingMode = TextWrappingModes.NoWrap;
                }
                float height = Mathf.Max(((RectTransform)row.Slider.transform).rect.height, 20f);
                PlaceStretch((RectTransform)row.Slider.transform, ControlX + ValueWidth + 20f, ControlRightEdge, height);
            }

            if (row.Toggle != null)
            {
                var rect = (RectTransform)row.Toggle.transform;
                Vector2 size = rect.rect.size.x > 1f ? rect.rect.size : new Vector2(30f, 30f);
                PlaceLeft(rect, ControlX, size.x, size.y);
            }

            if (row.Dropdown != null)
            {
                var rect = (RectTransform)row.Dropdown.transform;
                float height = Mathf.Max(rect.rect.height, 30f);
                PlaceLeft(rect, ControlX, 300f, height);
            }

            if (row.Field != null)
                PlaceStretch((RectTransform)row.Field.transform, ControlX, ControlRightEdge, 36f);
        }

        private static void PlaceLeft(RectTransform rect, float x, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void PlaceStretch(RectTransform rect, float left, float rightGap, float height)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(-(left + rightGap), height);
            rect.anchoredPosition = new Vector2((left - rightGap) / 2f, 0f);
        }

        private TextButton AddResetButton(SettingRow row)
        {
            TMP_Text text = MenuUtil.CloneText(templates.Action.transform, row.Root.transform, "Reset");
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-RightMargin, 0f);
            rect.sizeDelta = new Vector2(ResetWidth, 40f);
            text.text = "Reset";
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.Right;
            text.raycastTarget = true;
            text.fontSize *= 0.8f;

            TextButton button = text.gameObject.AddComponent<TextButton>();
            button.Label = text;
            button.SetColors(ResetColor, Color.white);
            button.Clicked = () => Apply(row, row.Entry.DefaultValue);
            return button;
        }

        private static void SetInteractable(SettingRow row, bool interactable)
        {
            if (row.Toggle != null) row.Toggle.interactable = interactable;
            if (row.Slider != null) row.Slider.interactable = interactable;
            if (row.Dropdown != null) row.Dropdown.interactable = interactable;
            if (row.Field != null) row.Field.interactable = interactable;
        }

        private static TMP_Text FindLabel(Transform row)
        {
            foreach (Transform child in row)
            {
                TMP_Text text = child.GetComponent<TMP_Text>();
                if (text != null && child.name.IndexOf("value", StringComparison.OrdinalIgnoreCase) < 0)
                    return text;
            }
            return null;
        }

        private static TMP_Text FindValueText(Transform row)
        {
            foreach (Transform child in row)
            {
                TMP_Text text = child.GetComponent<TMP_Text>();
                if (text != null && child.name.IndexOf("value", StringComparison.OrdinalIgnoreCase) >= 0)
                    return text;
            }
            return null;
        }

        private static string Humanize(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;
            string spaced = Regex.Replace(key, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
            return spaced.Replace('_', ' ').Trim();
        }

        private static bool IsNumber(Type type)
        {
            return type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long) ||
                   type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) ||
                   type == typeof(ushort) || type == typeof(sbyte) || type == typeof(decimal);
        }

        private static bool IsWholeNumber(Type type)
        {
            return IsNumber(type) && type != typeof(float) && type != typeof(double) && type != typeof(decimal);
        }

        private static object ConvertNumber(object value, Type type)
        {
            if (IsWholeNumber(type) && value is float f)
                value = Mathf.Round(f);
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
    }
}
