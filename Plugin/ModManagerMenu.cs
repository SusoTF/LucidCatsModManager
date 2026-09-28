using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LucidCatsModManager
{
    internal partial class ModManagerMenu : MonoBehaviour
    {
        private static readonly Color ActionColor = new Color(1f, 0.85f, 0.25f);
        private static readonly Color HoverColor = Color.white;
        private const string Green = "#5BD75B";
        private const string Grey = "#8C8C8C";
        private const string Orange = "#FF8C1A";
        private const string Red = "#FF4040";
        private const float HoverTint = 0.15f;
        private const float SelectedTint = 0.3f;
        private const int MaxErrorsShown = 5;

        private sealed class Row
        {
            public ModEntry Mod;
            public GameObject Root;
            public Image Background;
            public Color BaseColor;
            public TMP_Text Name;
            public TMP_Text Value;
        }

        private ExclusivePanel panel;
        private RectTransform grid;
        private ScrollRect scroll;
        private GameObject rowTemplate;
        private TMP_Text counterText;
        private TMP_Text titleText;
        private TMP_Text infoText;
        private RectTransform actionsArea;
        private TMP_Text actionTemplate;

        private RectTransform mainView;
        private RectTransform settingsView;
        private FullView settingsFull;
        private TMP_Text panelTitle;
        private TMP_Text modsButtonLabel;
        private LaunchGuard.Recovery recovery;
        private SettingsEditor settingsEditor;
        private ModEntry settingsMod;
        private GameObject templateStorage;
        private Transform menuRoot;
        private RectTransform settingsList;
        private TMP_Text settingsHint;

        private sealed class FullView
        {
            public RectTransform Root;
            public RectTransform List;
            public TMP_Text Hint;
            public RectTransform Footer;
            public readonly List<GameObject> FooterButtons = new List<GameObject>();
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly List<GameObject> actionButtons = new List<GameObject>();
        private List<ModEntry> mods = new List<ModEntry>();

        private int selected;
        private int hovered = -1;
        private bool showingErrors;
        private bool patcherInstalled;

        public static void Create(Scene scene)
        {
            var go = new GameObject("Mod Manager (mod)");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<ModManagerMenu>();
        }

        private void Start()
        {
            try
            {
                Build();
                ModManagerPlugin.Log.LogInfo("Mods button added to the main menu.");
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogError($"Could not build the Mod Manager menu: {e}");
            }
        }

        private void Update()
        {
            UpdateProfiles();

            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f);
            for (int i = 0; i < rows.Count; i++)
            {
                Image background = rows[i].Background;
                if (background != null)
                    background.color = Color.Lerp(background.color, RowColor(i), blend);
            }
        }

        private void Build()
        {
            Transform root = FindMenuRoot();
            if (root == null)
                throw new Exception("Could not find 'Canvas/4x3' in the main menu.");

            Transform statsButton = MenuUtil.Require(root, "Margins/grid/UIButton (stats)");
            Transform statsPanel = MenuUtil.Require(root, "Stats menu");

            UiSounds.CaptureFrom(statsButton.gameObject);

            BuildPanel(statsPanel);
            panel.ProtectedRoot = statsButton.parent;
            BuildSettingsView(root);
            BuildProfilesView();
            BuildModsButton(statsButton, root);

            recovery = LaunchGuard.ReadRecovery();
            UpdateModsButtonLabel();
        }

        private Transform FindMenuRoot()
        {
            foreach (GameObject go in gameObject.scene.GetRootGameObjects())
            {
                if (go.name != "Canvas")
                    continue;
                Transform found = go.transform.Find("4x3");
                if (found != null)
                    return found;
            }
            return null;
        }

        private void BuildModsButton(Transform template, Transform root)
        {
            GameObject button = Instantiate(template.gameObject, root, false);
            button.name = "UIButton (mods)";

            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-110f, -30f);
            rect.sizeDelta = new Vector2(250f, 80f);

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "Mods";
                label.alignment = TextAlignmentOptions.Center;
                MenuUtil.Stretch(label.rectTransform, 0f, 0f, 1f, 1f);
            }
            modsButtonLabel = label;

            UnityEvent click = MenuUtil.FindClickEvent(button);
            if (click == null)
                throw new Exception("Could not find the click event of the Mods button.");

            for (int i = 0; i < click.GetPersistentEventCount(); i++)
                click.SetPersistentListenerState(i, UnityEventCallState.Off);
            click.AddListener(OnModsClicked);
        }

        private void BuildPanel(Transform statsPanel)
        {
            var holder = new GameObject("Mods Holder");
            holder.SetActive(false);

            GameObject panelObject = Instantiate(statsPanel.gameObject, holder.transform, false);
            panelObject.name = "Mods menu";
            foreach (Game.UI.LifetimeStatsDisplay stats in panelObject.GetComponentsInChildren<Game.UI.LifetimeStatsDisplay>(true))
                DestroyImmediate(stats);

            Transform title = panelObject.transform.Find("Text (TMP)");
            panelTitle = title != null ? title.GetComponent<TMP_Text>() : null;
            if (panelTitle != null)
                panelTitle.text = "Mods";

            mainView = MenuUtil.NewRect("Main View", panelObject.transform);
            MenuUtil.Stretch(mainView, 0f, 0f, 1f, 1f);

            grid = (RectTransform)MenuUtil.Require(panelObject.transform, "grid");
            Transform header = MenuUtil.Require(grid, "Text name");
            Transform row = MenuUtil.Require(grid, "stat display");
            Transform rowText = MenuUtil.Require(row, "Text name");
            Transform rowValue = MenuUtil.Require(row, "Text value");

            rowTemplate = Instantiate(row.gameObject, panelObject.transform, false);
            rowTemplate.name = "Row Template";
            rowTemplate.SetActive(false);

            Vector2 gridOffsetMin = grid.offsetMin;
            Vector2 gridOffsetMax = grid.offsetMax;

            RectTransform details = MenuUtil.NewRect("Details", mainView);
            details.anchorMin = new Vector2(0.45f, 0f);
            details.anchorMax = new Vector2(1f, 1f);
            details.offsetMin = new Vector2(0f, gridOffsetMin.y);
            details.offsetMax = new Vector2(gridOffsetMax.x, gridOffsetMax.y);

            titleText = MenuUtil.CloneText(header, details, "Mod Name");
            MenuUtil.Stretch(titleText.rectTransform, 0f, 0.9f, 1f, 1f);
            titleText.alignment = TextAlignmentOptions.Left;
            titleText.textWrappingMode = TextWrappingModes.NoWrap;
            titleText.overflowMode = TextOverflowModes.Ellipsis;

            infoText = MenuUtil.CloneText(rowText, details, "Mod Info");
            MenuUtil.Stretch(infoText.rectTransform, 0f, 0.34f, 1f, 0.9f);
            infoText.alignment = TextAlignmentOptions.TopLeft;
            infoText.textWrappingMode = TextWrappingModes.Normal;
            infoText.overflowMode = TextOverflowModes.Truncate;
            infoText.enableAutoSizing = false;

            actionsArea = MenuUtil.NewRect("Actions", details);
            MenuUtil.Stretch(actionsArea, 0f, 0f, 1f, 0.32f);

            actionTemplate = MenuUtil.CloneText(rowValue, details, "Action Template");
            actionTemplate.gameObject.SetActive(false);

            var oldChildren = new List<GameObject>();
            foreach (Transform child in grid)
                oldChildren.Add(child.gameObject);

            counterText = MenuUtil.CloneText(header, grid, "Counter");

            foreach (GameObject old in oldChildren)
                DestroyImmediate(old);

            BuildScrollArea(mainView, gridOffsetMin, gridOffsetMax);

            panelObject.transform.SetParent(statsPanel.parent, false);
            panelObject.transform.SetSiblingIndex(statsPanel.GetSiblingIndex() + 1);
            Destroy(holder);

            panel = panelObject.AddComponent<ExclusivePanel>();
            panel.Menu = MenuUtil.FindMenuComponent(panelObject);
            panel.Group = panelObject.GetComponent<CanvasGroup>();
            if (panel.Menu == null)
                throw new Exception("The cloned panel has no Menu component.");
        }

        private void BuildScrollArea(Transform panelTransform, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform viewport = MenuUtil.NewRect("List Viewport", panelTransform);
            viewport.anchorMin = new Vector2(0f, 0f);
            viewport.anchorMax = new Vector2(0.42f, 1f);
            viewport.offsetMin = offsetMin;
            viewport.offsetMax = offsetMax;
            viewport.gameObject.AddComponent<RectMask2D>();

            Image hitArea = viewport.gameObject.AddComponent<Image>();
            hitArea.color = new Color(0f, 0f, 0f, 0f);

            grid.SetParent(viewport, false);
            grid.anchorMin = new Vector2(0f, 1f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.pivot = new Vector2(0.5f, 1f);
            grid.anchoredPosition = Vector2.zero;
            grid.sizeDelta = new Vector2(0f, grid.sizeDelta.y);

            ContentSizeFitter fitter = grid.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = grid;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            AddScrollbar(scroll, viewport);
        }

        private static void AddScrollbar(ScrollRect scrollRect, RectTransform viewport)
        {
            RectTransform bar = MenuUtil.NewRect("Scrollbar", viewport.parent);
            bar.anchorMin = new Vector2(viewport.anchorMax.x, viewport.anchorMin.y);
            bar.anchorMax = new Vector2(viewport.anchorMax.x, viewport.anchorMax.y);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.offsetMin = new Vector2(viewport.offsetMax.x + 2f, viewport.offsetMin.y);
            bar.offsetMax = new Vector2(viewport.offsetMax.x + 8f, viewport.offsetMax.y);

            Image track = bar.gameObject.AddComponent<Image>();
            track.color = new Color(1f, 1f, 1f, 0.08f);

            RectTransform area = MenuUtil.NewRect("Sliding Area", bar);
            MenuUtil.Stretch(area, 0f, 0f, 1f, 1f);
            RectTransform handle = MenuUtil.NewRect("Handle", area);
            MenuUtil.Stretch(handle, 0f, 0f, 1f, 1f);
            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(1f, 1f, 1f, 0.45f);

            Scrollbar scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private FullView BuildFullView(string name)
        {
            var view = new FullView();
            view.Root = MenuUtil.NewRect(name + " View", mainView.parent);
            MenuUtil.Stretch(view.Root, 0f, 0f, 1f, 1f);

            RectTransform viewport = MenuUtil.NewRect(name + " Viewport", view.Root);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(0f, 140f);
            viewport.offsetMax = new Vector2(-12f, -70f);
            viewport.gameObject.AddComponent<RectMask2D>();
            Image hitArea = viewport.gameObject.AddComponent<Image>();
            hitArea.color = new Color(0f, 0f, 0f, 0f);

            view.List = MenuUtil.NewRect(name + " List", viewport);
            view.List.anchorMin = new Vector2(0f, 1f);
            view.List.anchorMax = new Vector2(1f, 1f);
            view.List.pivot = new Vector2(0.5f, 1f);
            view.List.anchoredPosition = Vector2.zero;
            view.List.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = view.List.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 0f;
            ContentSizeFitter fitter = view.List.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect viewScroll = viewport.gameObject.AddComponent<ScrollRect>();
            viewScroll.content = view.List;
            viewScroll.viewport = viewport;
            viewScroll.horizontal = false;
            viewScroll.vertical = true;
            viewScroll.movementType = ScrollRect.MovementType.Clamped;
            viewScroll.scrollSensitivity = 30f;
            AddScrollbar(viewScroll, viewport);

            view.Hint = MenuUtil.CloneText(infoText.transform, view.Root, name + " Hint");
            RectTransform hintRect = view.Hint.rectTransform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0f, 0f);
            hintRect.offsetMin = new Vector2(40f, 65f);
            hintRect.offsetMax = new Vector2(-40f, 135f);
            view.Hint.alignment = TextAlignmentOptions.TopLeft;
            view.Hint.textWrappingMode = TextWrappingModes.Normal;
            view.Hint.overflowMode = TextOverflowModes.Truncate;
            view.Hint.fontSize *= 0.7f;

            view.Footer = MenuUtil.NewRect(name + " Actions", view.Root);
            view.Footer.anchorMin = new Vector2(0f, 0f);
            view.Footer.anchorMax = new Vector2(1f, 0f);
            view.Footer.pivot = new Vector2(0f, 0f);
            view.Footer.offsetMin = new Vector2(40f, 15f);
            view.Footer.offsetMax = new Vector2(-40f, 60f);

            view.Root.gameObject.SetActive(false);
            return view;
        }

        private void BuildSettingsView(Transform root)
        {
            settingsFull = BuildFullView("Settings");
            settingsView = settingsFull.Root;
            settingsList = settingsFull.List;
            settingsHint = settingsFull.Hint;

            templateStorage = new GameObject("Templates", typeof(RectTransform));
            templateStorage.transform.SetParent(settingsView, false);
            templateStorage.SetActive(false);
            menuRoot = root;
        }

        private void ShowFullView(FullView view, string title)
        {
            mainView.gameObject.SetActive(false);
            settingsFull.Root.gameObject.SetActive(view == settingsFull);
            if (profilesFull != null)
                profilesFull.Root.gameObject.SetActive(view == profilesFull);
            if (panelTitle != null)
                panelTitle.text = title;
        }

        private void ShowMainView()
        {
            settingsEditor?.Clear();
            ClearButtons(settingsFull.FooterButtons);
            settingsFull.Root.gameObject.SetActive(false);
            if (profilesFull != null)
            {
                ClearProfileRows();
                ClearButtons(profilesFull.FooterButtons);
                profilesFull.Root.gameObject.SetActive(false);
            }
            mainView.gameObject.SetActive(true);
            if (panelTitle != null)
                panelTitle.text = "Mods";
        }

        private bool EnsureSettingsEditor()
        {
            if (settingsEditor != null)
                return true;
            if (settingsView == null)
                return false;

            SettingsEditor.Templates templates = SettingsEditor.FindTemplates(templateStorage.transform, actionTemplate, out string report);
            if (!templates.Complete)
            {
                Transform grid = menuRoot != null ? menuRoot.Find("Settings Menu/grid") : null;
                if (grid != null)
                    templates = SettingsEditor.FindTemplatesInGrid(grid, templateStorage.transform, actionTemplate);
            }

            if (!templates.Complete)
            {
                ModManagerPlugin.Log.LogWarning($"Could not find the game's settings controls ({report}). The settings editor can't open; \"Open settings file\" still works.");
                return false;
            }

            ModManagerPlugin.Log.LogInfo($"Settings editor ready ({report}).");
            settingsEditor = new SettingsEditor(settingsList, settingsHint, templates);
            return true;
        }

        private void OpenSettings(ModEntry mod)
        {
            if (mod.Config == null)
                return;

            if (!EnsureSettingsEditor())
            {
                infoText.text = $"<color={Red}>The settings editor couldn't find the game's settings controls, so it can't open this time.</color>\n\n" +
                                "You can still change this mod's settings with \"Open settings file\". The log explains what went wrong.";
                return;
            }

            settingsMod = mod;
            ShowFullView(settingsFull, mod.Name + " settings");

            settingsEditor.Show(mod.Config);

            ClearButtons(settingsFull.FooterButtons);
            AddFooterButton(settingsFull, "Back", CloseSettings);
            AddFooterButton(settingsFull, "Reset all", () => settingsEditor.ResetAll());
            if (!string.IsNullOrEmpty(mod.ConfigPath) && File.Exists(mod.ConfigPath))
                AddFooterButton(settingsFull, "Open settings file", () => GameActions.OpenFile(mod.ConfigPath));
        }

        private void CloseSettings()
        {
            ShowMainView();
            settingsMod = null;
            ShowDetails();
        }

        private void OnModsClicked()
        {
            if (panel.IsOpen)
            {
                panel.Close();
                return;
            }

            Refresh(keepSelection: false);
            panel.Open();
        }

        private void Refresh(bool keepSelection)
        {
            ShowMainView();
            recovery = LaunchGuard.ReadRecovery();
            UpdateModsButtonLabel();

            string selectedGuid = keepSelection && selected > 0 && selected - 1 < mods.Count ? mods[selected - 1].Guid : null;

            PendingChanges.Load();
            patcherInstalled = PendingChanges.PatcherInstalled();
            mods = ModCatalog.Scan();
            LogScanner.Assign(mods, LogScanner.ReadErrors());

            RebuildRows();

            selected = 0;
            if (selectedGuid != null)
            {
                int index = mods.FindIndex(m => m.Guid == selectedGuid);
                if (index >= 0)
                    selected = index + 1;
            }
            hovered = -1;
            showingErrors = false;
            ShowDetails();
            SnapRowColors();
        }

        private void RebuildRows()
        {
            foreach (Row row in rows)
                if (row.Root != null)
                    DestroyImmediate(row.Root);
            rows.Clear();

            int active = mods.Count(m => m.State == ModState.Active);
            counterText.text = $"INSTALLED {mods.Count}  ·  ACTIVE {active}";

            rows.Add(CreateRow(null, 0));
            for (int i = 0; i < mods.Count; i++)
                rows.Add(CreateRow(mods[i], i + 1));

            UpdateRowTexts();
            if (scroll != null)
                scroll.verticalNormalizedPosition = 1f;
        }

        private Row CreateRow(ModEntry mod, int index)
        {
            GameObject go = Instantiate(rowTemplate, grid, false);
            go.name = mod != null ? "Row " + mod.Name : "Row General";
            go.SetActive(true);

            var row = new Row
            {
                Mod = mod,
                Root = go,
                Background = go.GetComponent<Image>(),
                Name = go.transform.Find("Text name")?.GetComponent<TMP_Text>(),
                Value = go.transform.Find("Text value")?.GetComponent<TMP_Text>(),
            };
            if (row.Background != null)
            {
                row.BaseColor = row.Background.color;
                row.Background.raycastTarget = true;
            }
            if (row.Name != null)
                row.Name.overflowMode = TextOverflowModes.Ellipsis;

            Button button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            if (row.Background != null)
                button.targetGraphic = row.Background;
            button.onClick.AddListener(() =>
            {
                UiSounds.PlayClick();
                Select(index);
            });

            RowHover hover = go.AddComponent<RowHover>();
            hover.Entered = () =>
            {
                hovered = index;
                UiSounds.PlayHover();
            };
            hover.Exited = () =>
            {
                if (hovered == index)
                    hovered = -1;
            };
            return row;
        }

        private void UpdateRowTexts()
        {
            foreach (Row row in rows)
            {
                if (row.Mod == null)
                {
                    if (row.Name != null)
                        row.Name.text = "General";
                    if (row.Value != null)
                        row.Value.text = recovery != null ? $"<color={Red}>Attention</color>"
                            : PendingChanges.Count > 0 ? $"<color={Orange}>Restart</color>" : "";
                    continue;
                }

                ModEntry mod = row.Mod;
                if (row.Name != null)
                    row.Name.text = mod.Errors.Count > 0 ? $"{mod.Name} <color={Red}>(!)</color>" : mod.Name;
                if (row.Value != null)
                    row.Value.text = StateLabel(mod);
            }
        }

        private static string StateLabel(ModEntry mod)
        {
            switch (mod.State)
            {
                case ModState.Active: return $"<color={Green}>Active</color>";
                case ModState.Disabled: return $"<color={Grey}>Disabled</color>";
                case ModState.Pending: return $"<color={Orange}>Pending</color>";
                default: return $"<color={Red}>Failed</color>";
            }
        }

        private void Select(int index)
        {
            selected = index;
            showingErrors = false;
            ShowDetails();
        }

        private void ShowDetails()
        {
            ClearActions();

            if (selected == 0 || selected - 1 >= mods.Count)
                ShowGeneral();
            else
                ShowMod(mods[selected - 1]);
        }

        private void ShowGeneral()
        {
            int active = mods.Count(m => m.State == ModState.Active);
            int disabled = mods.Count(m => m.State == ModState.Disabled);
            int failed = mods.Count(m => m.State == ModState.FailedToLoad);
            int withErrors = mods.Count(m => m.Errors.Count > 0);

            titleText.text = "GENERAL";

            var lines = new List<string>();
            if (recovery != null)
            {
                string names = string.Join(", ", recovery.Disabled.Select(ModNameForFile));
                lines.Add(recovery.SafeMode
                    ? $"<color={Red}>The game couldn't reach the main menu several times in a row, so the Mod Manager started it in safe mode and turned off: {names}.</color>"
                    : $"<color={Red}>The game couldn't reach the main menu twice in a row, so the Mod Manager turned off the mods added most recently: {names}.</color>");
            }
            lines.Add(
                $"{mods.Count} mod(s) installed: <color={Green}>{active} active</color>, <color={Grey}>{disabled} disabled</color>" +
                (failed > 0 ? $", <color={Red}>{failed} failed to load</color>" : ""));
            if (withErrors > 0)
                lines.Add($"<color={Red}>{withErrors} mod(s) had errors this session (marked with (!)).</color>");
            if (PendingChanges.Count > 0)
                lines.Add($"<color={Orange}>{PendingChanges.Count} change(s) will apply when you restart the game.</color>");
            if (!patcherInstalled)
                lines.Add($"<color={Red}>The Mod Manager patcher isn't installed, so turning mods on or off won't work. Check the installation guide.</color>");
            lines.Add($"<size=70%><color={Grey}>BepInEx {typeof(Paths).Assembly.GetName().Version}</color></size>");
            infoText.text = string.Join("\n\n", lines);

            if (recovery != null)
            {
                AddAction("Re-enable turned-off mods", ReEnableRecoveredMods);
                AddAction("Dismiss notice", () =>
                {
                    LaunchGuard.DismissRecovery();
                    recovery = null;
                    UpdateModsButtonLabel();
                    AfterChange();
                });
            }

            if (PendingChanges.Count > 0)
                AddAction("Restart game now", GameActions.RestartGame);

            if (recovery == null)
            {
                bool anyOtherActive = mods.Any(m => !m.IsSelf && m.WantsEnabled);
                if (anyOtherActive)
                    AddAction("Disable all mods (vanilla)", () => SetAllWanted(false));
                else if (mods.Any(m => !m.IsSelf))
                    AddAction("Enable all mods", () => SetAllWanted(true));
            }

            AddAction("Profiles", OpenProfiles);
            if (recovery == null)
                AddAction("Open mods folder", () => GameActions.OpenFolder(Paths.PluginPath));
            AddAction("Open log", () => GameActions.OpenFile(LogScanner.LogPath));
        }

        private void ShowMod(ModEntry mod)
        {
            titleText.text = mod.Name;

            if (showingErrors)
            {
                ShowErrors(mod);
                return;
            }

            var lines = new List<string>();

            string version = string.IsNullOrEmpty(mod.Version) ? "" : "v" + mod.Version;
            string author = string.IsNullOrEmpty(mod.Author) ? "" : "by " + mod.Author;
            string header = string.Join("  ·  ", new[] { version, author }.Where(s => s.Length > 0));
            if (header.Length > 0)
                lines.Add(header);

            lines.Add(StateDescription(mod));

            if (!string.IsNullOrEmpty(mod.Description))
                lines.Add(mod.Description);

            if (mod.Dependencies.Count > 0)
            {
                var needs = mod.Dependencies.Select(guid =>
                {
                    ModEntry dependency = mods.FirstOrDefault(m => string.Equals(m.Guid, guid, StringComparison.OrdinalIgnoreCase));
                    if (dependency == null)
                        return $"<color={Red}>{guid} (not installed)</color>";
                    return dependency.WantsEnabled ? dependency.Name : $"<color={Red}>{dependency.Name} (disabled)</color>";
                });
                lines.Add("Requires: " + string.Join(", ", needs));
            }

            List<ModEntry> neededBy = mods.Where(m => m.WantsEnabled && m.Dependencies.Any(d => string.Equals(d, mod.Guid, StringComparison.OrdinalIgnoreCase))).ToList();
            if (neededBy.Count > 0)
            {
                string names = string.Join(", ", neededBy.Select(m => m.Name));
                lines.Add(mod.WantsEnabled
                    ? $"Needed by: {names}"
                    : $"<color={Red}>Needed by: {names}. They won't work without it.</color>");
            }

            if (mod.Errors.Count > 0)
                lines.Add($"<color={Red}>{mod.Errors.Count} error(s) this session.</color>");

            if (!mod.Loaded && !string.IsNullOrEmpty(mod.ConfigPath))
                lines.Add($"<color={Grey}>Its settings can be changed here once it's enabled and the game is restarted.</color>");

            lines.Add($"<size=70%><color={Grey}>{mod.Guid}\n{mod.RelativePath}</color></size>");
            infoText.text = string.Join("\n\n", lines);

            if (!mod.IsSelf && patcherInstalled)
            {
                if (mod.HasPendingChange)
                    AddAction("Cancel change", () => SetWanted(mod, mod.EnabledOnDisk));
                else if (mod.WantsEnabled)
                    AddAction("Disable", () => SetWanted(mod, false));
                else
                    AddAction("Enable", () => SetWanted(mod, true));
            }

            if (mod.Loaded && settingsView != null && SettingsEditor.HasEditableSettings(mod.Config))
                AddAction("Settings", () => OpenSettings(mod));

            if (mod.Errors.Count > 0)
            {
                AddAction("View errors", () =>
                {
                    showingErrors = true;
                    ShowDetails();
                });
            }

            if (!string.IsNullOrEmpty(mod.ConfigPath) && File.Exists(mod.ConfigPath))
                AddAction("Open settings file", () => GameActions.OpenFile(mod.ConfigPath));

            AddAction("Show file", () => GameActions.ShowFile(mod.FilePath));
        }

        private string StateDescription(ModEntry mod)
        {
            if (mod.IsSelf)
                return $"<color={Green}>Active</color>. The Mod Manager can't be turned off from here.";

            switch (mod.State)
            {
                case ModState.Active:
                    return $"<color={Green}>Active</color>";
                case ModState.Disabled:
                    return $"<color={Grey}>Disabled</color>";
                case ModState.FailedToLoad:
                    return $"<color={Red}>Failed to load.</color> It's switched on, but it couldn't start this session. Check its errors.";
                default:
                    if (mod.AddedThisSession && !mod.HasPendingChange)
                        return $"<color={Orange}>Installed during this session.</color> It will load when you restart the game.";
                    return mod.WantsEnabled
                        ? $"<color={Orange}>Will be enabled when you restart the game.</color>"
                        : $"<color={Orange}>Will be disabled when you restart the game.</color>";
            }
        }

        private void ShowErrors(ModEntry mod)
        {
            IEnumerable<LogScanner.Entry> latest = mod.Errors.Skip(Math.Max(0, mod.Errors.Count - MaxErrorsShown));
            var text = new List<string> { $"<color={Red}>{mod.Errors.Count} error(s) this session. Latest:</color>" };
            foreach (LogScanner.Entry error in latest)
            {
                string message = error.Text;
                if (message.Length > 300)
                    message = message.Substring(0, 300) + "...";
                text.Add("<noparse>" + message.Replace("</noparse>", "") + "</noparse>");
            }
            infoText.text = "<size=65%>" + string.Join("\n\n", text) + "</size>";

            AddAction("Back", () =>
            {
                showingErrors = false;
                ShowDetails();
            });
            AddAction("Copy error report", () =>
            {
                GameActions.CopyToClipboard(GameActions.BuildErrorReport(mod));
                ModManagerPlugin.Log.LogInfo($"Error report for {mod.Name} copied to the clipboard.");
            });
            AddAction("Open log", () => GameActions.OpenFile(LogScanner.LogPath));
        }

        private void ReEnableRecoveredMods()
        {
            if (recovery == null)
                return;

            foreach (string file in recovery.Disabled)
            {
                ModEntry mod = mods.FirstOrDefault(m => string.Equals(m.RelativePath, file, StringComparison.OrdinalIgnoreCase));
                if (mod != null)
                    PendingChanges.SetWanted(mod, true);
            }

            LaunchGuard.DismissRecovery();
            recovery = null;
            UpdateModsButtonLabel();
            ModManagerPlugin.Log.LogInfo("The mods turned off by the crash protection will be enabled after a restart.");
            AfterChange();
        }

        private string ModNameForFile(string file)
        {
            ModEntry mod = mods.FirstOrDefault(m => string.Equals(m.RelativePath, file, StringComparison.OrdinalIgnoreCase));
            return mod != null ? mod.Name : Path.GetFileNameWithoutExtension(file);
        }

        private void UpdateModsButtonLabel()
        {
            if (modsButtonLabel != null)
                modsButtonLabel.text = recovery != null ? $"Mods <color={Red}>(!)</color>" : "Mods";
        }

        private void SetWanted(ModEntry mod, bool enable)
        {
            PendingChanges.SetWanted(mod, enable);
            ModManagerPlugin.Log.LogInfo($"{mod.Name} will be {(mod.WantsEnabled ? "enabled" : "disabled")} after a restart.");
            AfterChange();
        }

        private void SetAllWanted(bool enable)
        {
            foreach (ModEntry mod in mods)
                if (!mod.IsSelf)
                    PendingChanges.SetWanted(mod, enable);
            ModManagerPlugin.Log.LogInfo(enable ? "All mods will be enabled after a restart." : "All mods (except the Mod Manager) will be disabled after a restart.");
            AfterChange();
        }

        private void AfterChange()
        {
            int active = mods.Count(m => m.State == ModState.Active);
            counterText.text = $"INSTALLED {mods.Count}  ·  ACTIVE {active}";
            UpdateRowTexts();
            ShowDetails();
        }

        private void ClearActions() => ClearButtons(actionButtons);

        private static void ClearButtons(List<GameObject> buttons)
        {
            foreach (GameObject button in buttons)
                if (button != null)
                    Destroy(button);
            buttons.Clear();
        }

        private void AddAction(string label, Action onClick) => AddButton(label, onClick, actionsArea, actionButtons, 5);

        private TextButton AddFooterButton(FullView view, string label, Action onClick)
        {
            const float spacing = 45f;
            float x = 0f;
            foreach (GameObject existing in view.FooterButtons)
                if (existing != null)
                {
                    var existingRect = (RectTransform)existing.transform;
                    x = Mathf.Max(x, existingRect.anchoredPosition.x + existingRect.sizeDelta.x + spacing);
                }

            TMP_Text text = MenuUtil.CloneText(actionTemplate.transform, view.Footer, label);
            text.text = label;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.Left;
            text.raycastTarget = true;

            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            float width = text.GetPreferredValues(label).x + 10f;
            rect.sizeDelta = new Vector2(width, 0f);
            rect.anchoredPosition = new Vector2(x, 0f);

            TextButton button = text.gameObject.AddComponent<TextButton>();
            button.Label = text;
            button.SetColors(ActionColor, HoverColor);
            button.Clicked = onClick;

            view.FooterButtons.Add(text.gameObject);
            return button;
        }

        private void AddButton(string label, Action onClick, RectTransform area, List<GameObject> buttons, int slots)
        {
            int index = buttons.Count;
            if (index >= slots)
                return;

            TMP_Text text = MenuUtil.CloneText(actionTemplate.transform, area, label);
            RectTransform rect = text.rectTransform;
            float top = 1f - index / (float)slots;
            rect.anchorMin = new Vector2(0f, top - 1f / slots);
            rect.anchorMax = new Vector2(1f, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            text.text = label;
            text.alignment = TextAlignmentOptions.Left;
            text.raycastTarget = true;

            TextButton button = text.gameObject.AddComponent<TextButton>();
            button.Label = text;
            button.SetColors(ActionColor, HoverColor);
            button.Clicked = onClick;

            buttons.Add(text.gameObject);
        }

        private void SnapRowColors()
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Background != null)
                    rows[i].Background.color = RowColor(i);
        }

        private Color RowColor(int index)
        {
            Color c = rows[index].BaseColor;
            if (index == selected)
                return Tint(c, SelectedTint, 0.6f);
            if (index == hovered)
                return Tint(c, HoverTint, 0.45f);
            return c;
        }

        private static Color Tint(Color c, float amount, float minAlpha)
        {
            return new Color(
                Mathf.Lerp(c.r, 1f, amount),
                Mathf.Lerp(c.g, 1f, amount),
                Mathf.Lerp(c.b, 1f, amount),
                Mathf.Max(c.a, minAlpha));
        }
    }

    internal class RowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Entered;
        public Action Exited;

        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke();

        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();
    }
}
