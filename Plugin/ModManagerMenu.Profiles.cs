using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LucidCatsModManager
{
    internal partial class ModManagerMenu
    {
        private const float DeleteConfirmSeconds = 4f;

        private sealed class ProfileRow
        {
            public Profile Profile;
            public GameObject Root;
            public Image Background;
            public Color BaseColor;
            public TMP_Text Name;
            public TMP_Text Value;
        }

        private FullView profilesFull;
        private List<Profile> profiles = new List<Profile>();
        private readonly List<ProfileRow> profileRows = new List<ProfileRow>();
        private int selectedProfile = -1;
        private int hoveredProfile = -1;

        private TMP_InputField profileRenameField;
        private bool profilesRebuildRequested;
        private string renameAfterRebuild;

        private TextButton deleteButton;
        private float deleteConfirmUntil;

        private void BuildProfilesView()
        {
            profilesFull = BuildFullView("Profiles");
        }

        private void OpenProfiles()
        {
            ShowFullView(profilesFull, "Mod profiles");
            profiles = ProfileStore.Load();
            selectedProfile = profiles.Count > 0 ? 0 : -1;
            RebuildProfileRows();
            BuildProfileFooter();
            UpdateProfileHint();
        }

        private void CloseProfiles()
        {
            ShowMainView();
            ShowDetails();
        }

        private void BuildProfileFooter()
        {
            ClearButtons(profilesFull.FooterButtons);
            AddFooterButton(profilesFull, "Back", CloseProfiles);
            AddFooterButton(profilesFull, "Save current", SaveCurrentAsProfile);
            AddFooterButton(profilesFull, "Apply", ApplySelectedProfile);
            AddFooterButton(profilesFull, "Rename", () => StartProfileRename(selectedProfile));
            deleteButton = AddFooterButton(profilesFull, "Delete", DeleteSelectedProfile);
            deleteConfirmUntil = 0f;
        }

        private void UpdateProfiles()
        {
            if (profilesRebuildRequested)
            {
                profilesRebuildRequested = false;
                RebuildProfileRows();
                UpdateProfileHint();

                if (renameAfterRebuild != null)
                {
                    int index = profiles.FindIndex(p => p.Name == renameAfterRebuild);
                    renameAfterRebuild = null;
                    if (index >= 0)
                        StartProfileRename(index);
                }
            }

            if (deleteConfirmUntil > 0f && Time.unscaledTime > deleteConfirmUntil)
                CancelDeleteConfirmation();

            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f);
            for (int i = 0; i < profileRows.Count; i++)
            {
                Image background = profileRows[i].Background;
                if (background != null)
                    background.color = Color.Lerp(background.color, ProfileRowColor(i), blend);
            }
        }

        private void ClearProfileRows()
        {
            foreach (ProfileRow row in profileRows)
                if (row.Root != null)
                    Object.DestroyImmediate(row.Root);
            profileRows.Clear();
            profileRenameField = null;
        }

        private void RebuildProfileRows()
        {
            ClearProfileRows();

            for (int i = 0; i < profiles.Count; i++)
            {
                int index = i;
                Profile profile = profiles[i];

                GameObject go = Instantiate(rowTemplate, profilesFull.List, false);
                go.name = "Profile " + profile.Name;
                go.SetActive(true);

                var row = new ProfileRow
                {
                    Profile = profile,
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
                {
                    row.Name.text = profile.Name;
                    row.Name.color = Color.white;
                    row.Name.overflowMode = TextOverflowModes.Ellipsis;
                }
                if (row.Value != null)
                {
                    int on = profile.Mods.Count(m => m.Value);
                    row.Value.text = ProfileStore.Matches(profile, mods)
                        ? $"<color={Green}>Current</color>"
                        : $"<color={Grey}>{on} on · {profile.Mods.Count - on} off</color>";
                }

                Button button = go.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                if (row.Background != null)
                    button.targetGraphic = row.Background;
                button.onClick.AddListener(() =>
                {
                    UiSounds.PlayClick();
                    selectedProfile = index;
                    CancelDeleteConfirmation();
                    UpdateProfileHint();
                });

                RowHover hover = go.AddComponent<RowHover>();
                hover.Entered = () =>
                {
                    hoveredProfile = index;
                    UiSounds.PlayHover();
                };
                hover.Exited = () =>
                {
                    if (hoveredProfile == index)
                        hoveredProfile = -1;
                };

                profileRows.Add(row);
            }

            for (int i = 0; i < profileRows.Count; i++)
                if (profileRows[i].Background != null)
                    profileRows[i].Background.color = ProfileRowColor(i);
        }

        private Color ProfileRowColor(int index)
        {
            Color c = profileRows[index].BaseColor;
            if (index == selectedProfile)
                return Tint(c, SelectedTint, 0.6f);
            if (index == hoveredProfile)
                return Tint(c, HoverTint, 0.45f);
            return c;
        }

        private void UpdateProfileHint()
        {
            if (profiles.Count == 0)
            {
                profilesFull.Hint.text = "You don't have any profiles yet. Set your mods up the way you like and press \"Save current\" to save that setup as a profile.";
                return;
            }
            if (selectedProfile < 0 || selectedProfile >= profiles.Count)
            {
                profilesFull.Hint.text = "Select a profile to see which mods it turns on and off.";
                return;
            }

            Profile profile = profiles[selectedProfile];
            string on = string.Join(", ", profile.Mods.Where(m => m.Value).Select(m => ModNameForFile(m.Key)));
            string off = string.Join(", ", profile.Mods.Where(m => !m.Value).Select(m => ModNameForFile(m.Key)));
            profilesFull.Hint.text =
                $"<color={Green}>On:</color> {(on.Length > 0 ? on : "-")}\n" +
                $"<color={Grey}>Off:</color> {(off.Length > 0 ? off : "-")}\n" +
                $"<color={Grey}>Mods installed after saving it keep their current state.</color>";
        }

        private void SaveCurrentAsProfile()
        {
            string name = ProfileStore.NextDefaultName(profiles);
            profiles.Add(ProfileStore.Capture(name, mods));
            ProfileStore.Save(profiles);
            selectedProfile = profiles.Count - 1;
            ModManagerPlugin.Log.LogInfo($"Saved the current setup as \"{name}\".");

            renameAfterRebuild = name;
            profilesRebuildRequested = true;
        }

        private void ApplySelectedProfile()
        {
            if (selectedProfile < 0 || selectedProfile >= profiles.Count)
                return;

            Profile profile = profiles[selectedProfile];
            foreach (ModEntry mod in mods)
                if (!mod.IsSelf && profile.Mods.TryGetValue(mod.RelativePath, out bool on))
                    PendingChanges.SetWanted(mod, on);

            ModManagerPlugin.Log.LogInfo($"Profile \"{profile.Name}\" applied. It will take effect after a restart.");

            ShowMainView();
            selected = 0;
            AfterChange();
            SnapRowColors();
        }

        private void DeleteSelectedProfile()
        {
            if (selectedProfile < 0 || selectedProfile >= profiles.Count || deleteButton == null)
                return;

            if (deleteConfirmUntil <= 0f)
            {
                deleteConfirmUntil = Time.unscaledTime + DeleteConfirmSeconds;
                deleteButton.Label.text = "Sure?";
                deleteButton.SetColors(new Color(1f, 0.25f, 0.25f), Color.white);
                return;
            }

            string name = profiles[selectedProfile].Name;
            profiles.RemoveAt(selectedProfile);
            ProfileStore.Save(profiles);
            selectedProfile = Mathf.Min(selectedProfile, profiles.Count - 1);
            CancelDeleteConfirmation();
            ModManagerPlugin.Log.LogInfo($"Profile \"{name}\" deleted.");
            profilesRebuildRequested = true;
        }

        private void CancelDeleteConfirmation()
        {
            deleteConfirmUntil = 0f;
            if (deleteButton != null && deleteButton.Label != null)
            {
                deleteButton.Label.text = "Delete";
                deleteButton.SetColors(ActionColor, HoverColor);
            }
        }

        private void StartProfileRename(int index)
        {
            if (index < 0 || index >= profileRows.Count || profileRenameField != null)
                return;

            ProfileRow row = profileRows[index];
            if (row.Name == null)
                return;

            RectTransform labelRect = row.Name.rectTransform;

            var boxObject = new GameObject("Rename Box", typeof(RectTransform));
            boxObject.transform.SetParent(labelRect.parent, false);
            var box = (RectTransform)boxObject.transform;
            box.anchorMin = labelRect.anchorMin;
            box.anchorMax = labelRect.anchorMax;
            box.pivot = labelRect.pivot;
            box.anchoredPosition = labelRect.anchoredPosition;
            box.sizeDelta = labelRect.sizeDelta;
            Image background = boxObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.75f);

            var areaObject = new GameObject("Text Area", typeof(RectTransform));
            areaObject.transform.SetParent(box, false);
            var area = (RectTransform)areaObject.transform;
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(4f, 0f);
            area.offsetMax = new Vector2(-4f, 0f);
            areaObject.AddComponent<RectMask2D>();

            TMP_Text text = MenuUtil.CloneText(row.Name.transform, area, "Text");
            MenuUtil.Stretch(text.rectTransform, 0f, 0f, 1f, 1f);
            text.text = string.Empty;
            text.color = Color.white;

            row.Name.gameObject.SetActive(false);

            boxObject.SetActive(false);
            TMP_InputField field = boxObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.characterLimit = ProfileStore.MaxNameLength;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.text = row.Profile.Name;
            Profile profile = row.Profile;
            field.onEndEdit.AddListener(value => FinishProfileRename(profile, field, value));
            boxObject.SetActive(true);

            profileRenameField = field;
            field.Select();
            field.ActivateInputField();
        }

        private void FinishProfileRename(Profile profile, TMP_InputField field, string value)
        {
            if (profileRenameField != field)
                return;
            profileRenameField = null;

            string cleaned = ProfileStore.CleanName(value);
            bool taken = profiles.Any(p => p != profile && string.Equals(p.Name, cleaned, StringComparison.OrdinalIgnoreCase));
            if (!field.wasCanceled && cleaned.Length > 0 && cleaned != profile.Name && !taken)
            {
                profile.Name = cleaned;
                ProfileStore.Save(profiles);
                ModManagerPlugin.Log.LogInfo($"Profile renamed to \"{cleaned}\".");
            }

            profilesRebuildRequested = true;
        }
    }
}
