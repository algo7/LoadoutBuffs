using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LoadoutBuffs
{
    /// <summary>
    /// The in-game bundle editor: a Jötunn wood panel on top of the inventory screen, which already frees
    /// the cursor and pauses player input. Built lazily on first open (Jötunn rebuilds its GUI root on every
    /// scene change, destroying the panel with it). Every change is written to the bundles file and applied.
    /// Naming and delete confirmation use the game's own popups; while one is up the panel hides (it would
    /// cover the popup) and the inventory ignores its close keys (Menu.IsVisible()).
    /// </summary>
    internal static class BundleWindow
    {
        private const float Width = 1000f;
        private const float Height = 724f;
        private const float Top = 132f; // below title, status line and column headers
        private const float RightTop = 172f; // right column: below its header and the Effect | Stats tabs

        private static GameObject s_panel;
        private static Text s_status;
        private static Text s_slotsHeader;
        private static Text s_effectsHeader;
        private static Text s_description;
        private static RectTransform s_bundleList;
        private static RectTransform s_effectList;
        private static GameObject s_effectView;
        private static RectTransform s_statsList;
        private static GameObject s_statsView;
        private static Button s_effectTab;
        private static Button s_statsTab;
        private static Text s_totals;
        private static readonly List<Button> s_slotButtons = new List<Button>();
        private static Button s_rename;
        private static Button s_delete;
        private static Button s_use;

        private static string s_selectedBundle;
        private static BundleSlot s_selectedSlot = BundleSlot.Chest;
        private static string s_hoverEffect; // code under the pointer; "" = the None row; null = none hovered
        private static string s_error;
        private static bool s_statsTabOpen;

        /// <summary>Groups the player opened or closed; others use their default (see IsGroupOpen).</summary>
        private static readonly Dictionary<string, bool> s_groupOpen = new Dictionary<string, bool>();

        public static bool IsOpen => s_panel != null && s_panel.activeSelf;

        public static void Open()
        {
            var gui = InventoryGui.instance;
            if (Player.m_localPlayer == null || gui == null)
            {
                Plugin.Log.LogInfo("Buffs window: load into a world first.");
                return;
            }
            if (!InventoryGui.IsVisible()) gui.Show(null);
            if (!Build()) return;
            var file = BundleEffects.File;
            if (file.Find(s_selectedBundle) == null)
                s_selectedBundle = file.Find(file.Active)?.Name ?? file.Bundles.FirstOrDefault()?.Name;
            s_error = null;
            s_hiddenForPopup = false;
            s_panel.SetActive(true);
            s_panel.transform.SetAsLastSibling();
            Refresh();
        }

        public static void Close()
        {
            if (s_panel != null) s_panel.SetActive(false);
        }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        // ---- building -----------------------------------------------------------------------

        private static bool Build()
        {
            if (s_panel != null) return true;
            var root = GUIManager.CustomGUIFront;
            if (root == null)
            {
                Plugin.Log.LogWarning("Buffs window: Jötunn's GUI isn't ready.");
                return false;
            }
            var gm = GUIManager.Instance;
            s_panel = gm.CreateWoodpanel(root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Width, Height, true);
            s_panel.name = "LoadoutBuffs_Bundles";
            var p = s_panel.transform;

            Label(p, "Buffs", 30, gm.ValheimOrange, 0, 18, Width, 40, TextAnchor.MiddleCenter);
            s_status = Label(p, "", 16, Color.white, 30, 62, Width - 60, 44, TextAnchor.UpperCenter);
            MakeButton(p, "Close", Width - 130, 18, 100, 34, Close);

            // Left: bundles.
            Label(p, "Your buffs", 20, gm.ValheimOrange, 30, 104, 220, 26, TextAnchor.MiddleLeft);
            s_bundleList = ScrollList(p, 30, Top, 220, 330);
            MakeButton(p, "New", 30, 474, 106, 36, NewBundle);
            s_rename = MakeButton(p, "Rename", 144, 474, 106, 36, RenameBundle);
            s_delete = MakeButton(p, "Delete", 30, 516, 106, 36, DeleteBundle);
            s_use = MakeButton(p, "Use", 144, 516, 106, 36, UseOrTurnOff);
            Label(p, "Changes are saved and applied at once.", 13, new Color(0.8f, 0.8f, 0.8f), 30, 560, 220, 50, TextAnchor.UpperLeft);

            // Middle: the selected bundle's slots.
            s_slotsHeader = Label(p, "", 20, gm.ValheimOrange, 270, 104, 330, 26, TextAnchor.MiddleLeft);
            s_slotButtons.Clear();
            var y = Top;
            foreach (var slot in BundleSlots.All)
            {
                var s = slot;
                var button = MakeButton(p, "", 270, y, 330, 54, () => SelectSlot(s));
                StyleRowText(button, 15);
                s_slotButtons.Add(button);
                y += 58;
            }

            // Right: the selected slot's effect (picker + description) or its stats, by tab.
            s_effectsHeader = Label(p, "", 20, gm.ValheimOrange, 620, 104, 350, 26, TextAnchor.MiddleLeft);
            s_effectTab = MakeButton(p, "Effect", 620, Top, 172, 32, () => ShowTab(false));
            s_statsTab = MakeButton(p, "Stats", 798, Top, 172, 32, () => ShowTab(true));
            s_effectList = ScrollList(p, 620, RightTop, 350, 250, out s_effectView);
            s_description = Label(p, "", 15, Color.white, 620, RightTop + 262, 350, 200, TextAnchor.UpperLeft);
            s_statsList = ScrollList(p, 620, RightTop, 350, 462, out s_statsView);

            // Bottom: what the selected bundle's stats add up to with the gear worn now.
            s_totals = Label(p, "", 15, Color.white, 30, 642, Width - 60, 68, TextAnchor.UpperLeft);
            // Many stats: shrink the font rather than cut the line off.
            s_totals.resizeTextForBestFit = true;
            s_totals.resizeTextMinSize = 10;
            s_totals.resizeTextMaxSize = 15;

            s_panel.SetActive(false);
            return true;
        }

        private static void Place(GameObject go, float x, float y, float w, float h)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static Text Label(Transform parent, string text, int size, Color color, float x, float y, float w, float h, TextAnchor align)
        {
            var gm = GUIManager.Instance;
            var go = gm.CreateText(text, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero,
                gm.AveriaSerifBold, size, color, true, Color.black, w, h, false);
            Place(go, x, y, w, h);
            var label = go.GetComponent<Text>();
            label.alignment = align;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private static Button MakeButton(Transform parent, string text, float x, float y, float w, float h, UnityAction onClick)
        {
            var go = GUIManager.Instance.CreateButton(text, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, w, h);
            Place(go, x, y, w, h);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        private static void StyleRowText(Button button, int size)
        {
            var label = button.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.supportRichText = true;
            label.fontSize = size;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            var rt = label.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10f, 2f);
            rt.offsetMax = new Vector2(-8f, -2f);
        }

        /// <summary>A Jötunn scroll view; returns its content (vertical layout).</summary>
        private static RectTransform ScrollList(Transform parent, float x, float y, float w, float h) => ScrollList(parent, x, y, w, h, out _);

        private static RectTransform ScrollList(Transform parent, float x, float y, float w, float h, out GameObject root)
        {
            var gm = GUIManager.Instance;
            var go = gm.CreateScrollView(parent, false, true, 8f, 4f, gm.ValheimScrollbarHandleColorBlock, new Color(0f, 0f, 0f, 0.35f), w, h);
            root = go;
            Place(go, x, y, w, h);
            var content = go.GetComponentInChildren<ScrollRect>().content;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.padding = new RectOffset(2, 2, 2, 2);
            return content;
        }

        private static Button Row(RectTransform content, string text, float height, UnityAction onClick, bool interactable = true)
        {
            var width = content.rect.width - 4f;
            var go = GUIManager.Instance.CreateButton(text, content, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, width, height);
            var element = go.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = height;
            element.preferredWidth = width;
            var button = go.GetComponent<Button>();
            StyleRowText(button, 15);
            button.interactable = interactable;
            button.onClick.AddListener(onClick);
            return button;
        }

        private static void Clear(RectTransform content)
        {
            foreach (Transform child in content)
            {
                child.gameObject.SetActive(false); // out of the layout now; destroyed at the end of the frame
                Object.Destroy(child.gameObject);
            }
        }

        // ---- showing ------------------------------------------------------------------------

        private static ItemDrop.ItemData[] s_gear = new ItemDrop.ItemData[0];

        /// <summary>
        /// Every inventory frame: refresh when the item in a slot or the weapon in hand changed, so the slot rows, the
        /// lines at the bottom and the damage rows (which follow the weapon) never show stale gear. Window open only.
        /// </summary>
        public static void RefreshIfGearChanged()
        {
            if (!IsOpen) return;
            var gear = BundleEffects.GearSnapshot(Player.m_localPlayer);
            if (gear.Length == s_gear.Length && gear.Zip(s_gear, ReferenceEquals).All(same => same)) return;
            Refresh();
        }

        private static void Refresh()
        {
            if (s_panel == null) return;
            s_gear = BundleEffects.GearSnapshot(Player.m_localPlayer);
            var file = BundleEffects.File;
            var state = BundleEffects.State;
            var bundle = file.Find(s_selectedBundle);
            var isActive = bundle != null && string.Equals(file.Active, bundle.Name, StringComparison.OrdinalIgnoreCase);

            s_status.text = StatusText(state);
            s_rename.interactable = s_delete.interactable = s_use.interactable = bundle != null;
            s_use.GetComponentInChildren<Text>().text = isActive ? "Turn off" : "Use";

            Clear(s_bundleList);
            foreach (var b in file.Bundles)
            {
                var name = b.Name;
                var active = string.Equals(file.Active, name, StringComparison.OrdinalIgnoreCase);
                var selected = b == bundle;
                var text = (selected ? "<color=orange>" : "") + Escape(name) + (selected ? "</color>" : "") +
                           (active ? "  <size=13><color=#9fdf9f>in use</color></size>" : "");
                Row(s_bundleList, text, 34, () => SelectBundle(name));
            }
            if (file.Bundles.Count == 0) Row(s_bundleList, "<color=#bbbbbb>No buffs yet: press New</color>", 34, () => { }, false);

            s_slotsHeader.text = bundle == null ? "Slots" : "Slots of " + Escape(bundle.Name);
            var player = Player.m_localPlayer;
            for (var i = 0; i < BundleSlots.All.Length; i++)
            {
                var slot = BundleSlots.All[i];
                var worn = player != null ? BundleEffects.Worn(player, slot) : null;
                var code = bundle?.Get(slot);
                var statCount = bundle?.GetStats(slot)?.Count ?? 0;
                var effect = (code == null ? "<color=#999999>no effect</color>" : "<color=orange>" + Escape(EffectName(code)) + "</color>") +
                             (statCount > 0 ? $"  <color=#9fdf9f>+{statCount} stat{(statCount == 1 ? "" : "s")}</color>" : "");
                var label = (slot == s_selectedSlot ? "» " : "") + $"<b>{SlotLabel(slot)}</b>  " +
                            $"<size=13><color=#bbbbbb>{(worn != null ? Escape(EffectText.DisplayName(worn.m_shared.m_name)) : "nothing equipped")}</color></size>\n" + effect;
                var button = s_slotButtons[i];
                button.GetComponentInChildren<Text>().text = label;
                button.interactable = bundle != null;
            }

            s_effectsHeader.text = $"{SlotLabel(s_selectedSlot)}: effect and stats";
            s_effectTab.interactable = s_statsTab.interactable = bundle != null;
            if (bundle != null)
            {
                // The game marks the current tab by making it non-interactable; do the same.
                s_effectTab.interactable = s_statsTabOpen;
                s_statsTab.interactable = !s_statsTabOpen;
            }
            s_effectView.SetActive(!s_statsTabOpen);
            s_description.gameObject.SetActive(!s_statsTabOpen);
            s_statsView.SetActive(s_statsTabOpen);
            RefreshTotals(bundle, isActive);
            if (s_statsTabOpen)
            {
                RefreshStats(bundle);
                return;
            }
            Clear(s_effectList);
            if (bundle != null)
            {
                var chosen = bundle.Get(s_selectedSlot);
                EffectRow("", chosen == null ? "<color=orange>None</color>  <size=13>(chosen)</size>" : "None", true);
                foreach (var info in BundleEffects.Catalog?.Allowed ?? new List<EffectInfo>())
                {
                    var usedIn = bundle.Entries.FirstOrDefault(e => e.Slot != s_selectedSlot && SameEffect(e.Effect, info.Code));
                    var text = Escape(info.DisplayName);
                    if (SameEffect(chosen, info.Code)) text = $"<color=orange>{text}</color>  <size=13>(chosen)</size>";
                    else if (usedIn != null) text = $"<color=#888888>{text}  <size=13>(in {SlotLabel(usedIn.Slot).ToLowerInvariant()})</size></color>";
                    var onGear = GearWith(player, info.Code);
                    if (onGear != null) text += $"  <size=13><color=#9fdf9f>(on your {Escape(onGear)})</color></size>";
                    EffectRow(info.Code, text, usedIn == null);
                }
            }
            UpdateDescription();
        }

        private static void EffectRow(string code, string text, bool interactable)
        {
            var button = Row(s_effectList, text, 30, () => PickEffect(code), interactable);
            var hover = button.gameObject.AddComponent<BundleHoverHandler>();
            hover.Enter = () =>
            {
                s_hoverEffect = code;
                UpdateDescription();
            };
            hover.Exit = () =>
            {
                s_hoverEffect = null;
                UpdateDescription();
            };
        }

        private static void UpdateDescription()
        {
            if (s_description == null) return;
            var bundle = BundleEffects.File.Find(s_selectedBundle);
            if (bundle == null)
            {
                s_description.text = "Create a buff with New, pick an effect for each slot, then press Use.\n\n" +
                                     "A slot's effect is on while anything is equipped in that slot. Your gear keeps its own effects.";
                return;
            }
            var code = s_hoverEffect ?? bundle.Get(s_selectedSlot);
            if (string.IsNullOrEmpty(code))
            {
                s_description.text = $"No effect in the {SlotLabel(s_selectedSlot).ToLowerInvariant()} slot.\n\n" +
                                     "Point at an effect to read what it does; click to put it in this slot.";
                return;
            }
            var se = BundleEffects.Catalog?.Get(Resolve(code));
            if (se == null)
            {
                s_description.text = $"<color=#ff8a70>'{Escape(code)}' isn't an effect buffs can use (see LogOutput.log).</color>";
                return;
            }
            var lines = EffectText.Describe(se).ToList();
            s_description.text = $"<color=orange>{Escape(EffectText.DisplayName(se.m_name))}</color>\n" + Escape(string.Join("\n", lines));
        }

        private static string StatusText(BundleState state)
        {
            string text;
            if (!state.Enabled) text = $"<color=#ff8a70>Buffs are off: {Escape(state.OffReason)}</color>";
            else if (state.Active == null) text = "No buff in use. Pick one and press Use.";
            else text = $"In use: <color=orange>{Escape(state.Active.Name)}</color> ({state.Effects.Count} effect{(state.Effects.Count == 1 ? "" : "s")})";
            if (s_error != null) text += $"\n<color=#ff8a70>{Escape(s_error)}</color>";
            else if (state.Warnings.Count > 0)
                text += $"\n<size=13><color=#ffcf70>{state.Warnings.Count} problem{(state.Warnings.Count == 1 ? "" : "s")} in the buffs file: {Escape(state.Warnings[0])}</color></size>";
            return text;
        }

        private static void RefreshTotals(BundleDef bundle, bool isActive)
        {
            if (bundle == null)
            {
                s_totals.text = "";
                return;
            }
            var totals = BundleEffects.WornTotals(Player.m_localPlayer, bundle);
            var note = !BundleEffects.State.Enabled ? "  <color=#ff8a70>(buffs are off)</color>"
                : !isActive ? "  <color=#bbbbbb>(not in use)</color>" : "";
            var player = Player.m_localPlayer;
            var effects = BundleWindowRules.WornEffectNames(bundle, BundleEffects.Catalog, slot => player != null && BundleEffects.Worn(player, slot) != null);
            s_totals.text = $"<color=orange>Effects of {Escape(bundle.Name)}</color> with what you wear now: " +
                            (effects.Count == 0 ? "<color=#bbbbbb>none</color>" : Escape(string.Join(", ", effects))) + note +
                            "\n<color=orange>Custom stats:</color> " + (totals.IsEmpty ? "<color=#bbbbbb>none</color>" : Escape(totals.Summary()));
        }

        private static void RefreshStats(BundleDef bundle)
        {
            Clear(s_statsList);
            if (bundle == null) return;
            var stats = bundle.GetStats(s_selectedSlot) ?? new StatBlock();
            if (!stats.IsEmpty) Row(s_statsList, "<color=#ffcf70>Reset this slot's stats</color>", 28, ResetStats);

            foreach (var group in new[] { BundleStatCatalog.General, BundleStatCatalog.Regen, BundleStatCatalog.Costs, BundleStatCatalog.OnParry })
            {
                var defs = BundleStatCatalog.Scalars.Where(d => d.Group == group).ToList();
                var used = defs.Count(d => stats.Scalars.ContainsKey(d.Key));
                if (!GroupHeader(group, used, true)) continue;
                foreach (var def in defs)
                {
                    var d = def;
                    var has = stats.Scalars.TryGetValue(d.Key, out var v);
                    string value;
                    if (d.TakesLargest) value = (has ? v : d.Default).ToString("0.##", CultureInfo.InvariantCulture) + d.Unit;
                    else value = has ? StatBlock.Number(v) + (d.IsPercent ? "%" : "") : "0";
                    StatRow(d.Label, value, has, !has || d.TakesLargest || d.Helps(v),
                        () => ChangeNumber(b => b.Scalars, d.Key, -d.Step, d.Min, d.Max, d.Default),
                        () => ChangeNumber(b => b.Scalars, d.Key, d.Step, d.Min, d.Max, d.Default));
                }
            }

            var classes = BundleStatCatalog.Toggles.Count(d => stats.Scalars.ContainsKey(d.Key));
            if (BundleStatCatalog.Toggles.All(d => d.Allows(s_selectedSlot)) && GroupHeader(BundleStatCatalog.Class, classes, true))
            {
                var hits = s_selectedSlot == BundleSlot.Ranged ? "Your shots (arrows, bolts, spells with blunt / slash / pierce)" : "Your melee hits";
                Row(s_statsList, $"<size=13><color=#bbbbbb>{hits}: Woodcutter fells any tree, Miner breaks any rock or ore.</color></size>",
                    40, () => { }, false);
                foreach (var def in BundleStatCatalog.Toggles)
                {
                    var key = def.Key;
                    var on = stats.Scalars.ContainsKey(key);
                    StatRow(def.Label, on ? "On" : "Off", on, true, () => SetToggle(key, false), () => SetToggle(key, true), "Off", "On", 40f);
                }
            }

            if (GroupHeader(BundleStatCatalog.Resistances, stats.Resist.Count, false))
                foreach (var type in BundleStatCatalog.DamageTypes)
                {
                    var t = type;
                    var has = stats.Resist.TryGetValue(t, out var modifier);
                    StatRow(t, has ? BundleStatCatalog.ModifierLabel(modifier) : "Normal", has,
                        !has || BundleStatCatalog.Protection(modifier) < BundleStatCatalog.Protection("Normal"),
                        () => CycleResist(t, -1), () => CycleResist(t, 1), "<", ">", 110);
                }

            // Damage rows follow the weapon in hand: a % multiplies what it deals, so a % of a type it lacks does nothing.
            var weapon = BundleEffects.CurrentWeapon();
            var own = weapon != null ? BundleEffects.OwnDamage(weapon) : null;
            var weaponName = weapon == null ? null
                : weapon.m_shared.m_skillType == Skills.SkillType.Unarmed ? "fists"
                : Escape(EffectText.DisplayName(weapon.m_shared.m_name));
            Dictionary<string, float> dealt = null;
            if (own != null)
            {
                dealt = new Dictionary<string, float>(own);
                var worn = BundleEffects.WornTotals(Player.m_localPlayer, bundle);
                foreach (var p in worn.AddDamage.Concat(stats.AddDamage))
                    dealt[p.Key] = (dealt.TryGetValue(p.Key, out var x) ? x : 0f) + p.Value;
            }

            // Damage stats only on weapon slots: on armor they'd change whatever weapon you hold.
            var weaponSlot = BundleStatCatalog.IsWeaponSlot(s_selectedSlot);
            if (weaponSlot && GroupHeader(BundleStatCatalog.Damage, stats.Damage.Count, false))
            {
                if (weaponName != null)
                    Row(s_statsList, $"<size=13><color=#bbbbbb>Damage types of your {weaponName}; others appear once added below.</color></size>", 28, () => { }, false);
                foreach (var type in BundleWindowRules.DamagePercentRows(dealt, stats.Damage))
                {
                    var key = type.ToLowerInvariant();
                    var has = stats.Damage.TryGetValue(key, out var v);
                    StatRow(type + " damage", has ? StatBlock.Number(v) + "%" : "0", has, !has || v > 0f,
                        () => ChangeNumber(b => b.Damage, key, -BundleStatCatalog.DamageStep, BundleStatCatalog.DamageMin, BundleStatCatalog.DamageMax),
                        () => ChangeNumber(b => b.Damage, key, BundleStatCatalog.DamageStep, BundleStatCatalog.DamageMin, BundleStatCatalog.DamageMax));
                }
            }

            if (weaponSlot && GroupHeader(BundleStatCatalog.AddedDamage, stats.AddDamage.Count, false))
            {
                if (weaponName != null)
                    Row(s_statsList, $"<size=13><color=#bbbbbb>Added on top of your {weaponName}{(weaponName == "fists" ? "'" : "'s")} own damage.</color></size>", 28, () => { }, false);
                foreach (var type in BundleStatCatalog.AddDamageTypes)
                {
                    var key = type.ToLowerInvariant();
                    var has = stats.AddDamage.TryGetValue(key, out var v);
                    var ownValue = own != null && own.TryGetValue(key, out var o) ? o : 0f;
                    StatRow(type + " damage", has ? StatBlock.Number(v) : "0", has, true,
                        () => ChangeNumber(b => b.AddDamage, key, -BundleStatCatalog.AddDamageStep, BundleStatCatalog.AddDamageMin, BundleStatCatalog.AddDamageMax),
                        () => ChangeNumber(b => b.AddDamage, key, BundleStatCatalog.AddDamageStep, BundleStatCatalog.AddDamageMin, BundleStatCatalog.AddDamageMax),
                        note: BundleWindowRules.AddedDamageNote(ownValue));
                }
            }

            if (GroupHeader(BundleStatCatalog.Skills, stats.Skills.Count, false))
                foreach (var skill in BundleStatCatalog.WindowSkills.Concat(stats.Skills.Keys.Where(k => !BundleStatCatalog.WindowSkills.Contains(k))))
                {
                    var name = skill;
                    var has = stats.Skills.TryGetValue(name, out var v);
                    StatRow(name, has ? StatBlock.Number(v) : "0", has, !has || v > 0f,
                        () => ChangeNumber(b => b.Skills, name, -BundleStatCatalog.SkillStep, BundleStatCatalog.SkillMin, BundleStatCatalog.SkillMax),
                        () => ChangeNumber(b => b.Skills, name, BundleStatCatalog.SkillStep, BundleStatCatalog.SkillMin, BundleStatCatalog.SkillMax));
                }

            if (stats.Fields.Count > 0)
                Row(s_statsList, $"<color=#bbbbbb>Also set in the file: {Escape(string.Join(", ", stats.Fields.Select(f => f.Key)))}</color>", 28, () => { }, false);
        }

        /// <summary>A clickable group title; returns whether the group is open. Groups holding a value open by default.</summary>
        private static bool GroupHeader(string group, int used, bool openByDefault)
        {
            var open = s_groupOpen.TryGetValue(group, out var o) ? o : openByDefault || used > 0;
            var text = $"<color=orange>{(open ? "[-]" : "[+]")} {group}</color>" + (used > 0 ? $"  <size=13>({used} set)</size>" : "");
            Row(s_statsList, text, 28, () =>
            {
                s_groupOpen[group] = !open;
                Refresh();
            });
            return open;
        }

        private static readonly Color s_good = new Color(0.55f, 0.9f, 0.5f);
        private static readonly Color s_bad = new Color(1f, 0.5f, 0.42f);

        /// <summary><c>label   value  [-] [+]</c>; a changed value is green when it helps you, red when it hurts.</summary>
        private static void StatRow(string label, string value, bool changed, bool helps, UnityAction down, UnityAction up,
            string downText = "-", string upText = "+", float valueWidth = 70f, string note = null)
        {
            var width = s_statsList.rect.width - 4f;
            var row = new GameObject("StatRow", typeof(RectTransform));
            row.transform.SetParent(s_statsList, false);
            ((RectTransform)row.transform).sizeDelta = new Vector2(width, 28f);
            var element = row.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = 28f;
            element.preferredWidth = width;
            var labelColor = changed ? GUIManager.Instance.ValheimOrange : Color.white;
            var valueColor = !changed ? Color.white : helps ? s_good : s_bad;
            var text = Escape(label) + (note != null ? $"  <size=12><color=#bbbbbb>{Escape(note)}</color></size>" : "");
            Label(row.transform, text, 15, labelColor, 8, 2, width - valueWidth - 100, 24, TextAnchor.MiddleLeft);
            Label(row.transform, value, 15, valueColor, width - valueWidth - 94, 2, valueWidth, 24, TextAnchor.MiddleRight);
            MakeButton(row.transform, downText, width - 88, 1, 40, 26, down);
            MakeButton(row.transform, upText, width - 44, 1, 40, 26, up);
        }

        // ---- actions --------------------------------------------------------------------------

        private static void ShowTab(bool stats)
        {
            s_statsTabOpen = stats;
            s_hoverEffect = null;
            Refresh();
        }

        /// <summary>One step up or down, stopping at the window's limits (values set further in the file stay reachable).</summary>
        private static void ChangeNumber(Func<StatBlock, Dictionary<string, float>> part, string key, float step, float min, float max, float unset = 0f)
        {
            var bundle = BundleEffects.File.Find(s_selectedBundle);
            if (bundle == null) return;
            var stats = bundle.GetStats(s_selectedSlot)?.Clone() ?? new StatBlock();
            var numbers = part(stats);
            var v = numbers.TryGetValue(key, out var current) ? current : unset;
            var next = v + step;
            if (step > 0 && next > max) next = Math.Max(v, max);
            if (step < 0 && next < min) next = Math.Min(v, min);
            if (Math.Abs(next - v) < 0.0001f) return;
            if (unset != 0f && Math.Abs(next - unset) < 0.0001f) numbers.Remove(key); // back at the default: not stored
            else StatBlock.SetNumber(numbers, key, next);
            bundle.SetStats(s_selectedSlot, stats);
            Save();
        }

        private static void SetToggle(string key, bool on)
        {
            var bundle = BundleEffects.File.Find(s_selectedBundle);
            if (bundle == null) return;
            var stats = bundle.GetStats(s_selectedSlot)?.Clone() ?? new StatBlock();
            if (stats.Scalars.ContainsKey(key) == on) return;
            if (on) stats.Scalars[key] = 1f;
            else stats.Scalars.Remove(key);
            bundle.SetStats(s_selectedSlot, stats);
            Save();
        }

        private static void CycleResist(string type, int direction)
        {
            var bundle = BundleEffects.File.Find(s_selectedBundle);
            if (bundle == null) return;
            var stats = bundle.GetStats(s_selectedSlot)?.Clone() ?? new StatBlock();
            var cycle = BundleStatCatalog.ModifierCycle;
            var current = stats.Resist.TryGetValue(type, out var m) ? Array.IndexOf(cycle, m) : 0;
            if (current < 0) current = 0; // e.g. Ignore, set in the file
            var next = cycle[(current + direction + cycle.Length) % cycle.Length];
            stats.SetResist(type, next);
            bundle.SetStats(s_selectedSlot, stats);
            Save();
        }

        private static void ResetStats()
        {
            var bundle = BundleEffects.File.Find(s_selectedBundle);
            if (bundle == null) return;
            bundle.SetStats(s_selectedSlot, null);
            Save();
        }

        private static void SelectBundle(string name)
        {
            s_selectedBundle = name;
            s_hoverEffect = null;
            Refresh();
        }

        private static void SelectSlot(BundleSlot slot)
        {
            s_selectedSlot = slot;
            s_hoverEffect = null;
            Refresh();
        }

        private static void PickEffect(string code)
        {
            var bundle = BundleEffects.File.Find(s_selectedBundle);
            if (bundle == null) return;
            bundle.Set(s_selectedSlot, string.IsNullOrEmpty(code) ? null : code);
            Save();
        }

        private static void UseOrTurnOff()
        {
            var file = BundleEffects.File;
            var bundle = file.Find(s_selectedBundle);
            if (bundle == null) return;
            file.Active = string.Equals(file.Active, bundle.Name, StringComparison.OrdinalIgnoreCase) ? null : bundle.Name;
            Save();
        }

        private static void NewBundle()
        {
            if (!UnifiedPopup.IsAvailable())
            {
                var n = 1;
                while (BundleEffects.File.Find($"Buff {n}") != null) n++;
                CreateBundle($"Buff {n}");
                return;
            }
            AskForName("New buff", "Name of the new buff:", name => IsFreeName(name, null), CreateBundle);
        }

        private static void CreateBundle(string name)
        {
            BundleEffects.File.Bundles.Add(new BundleDef { Name = name });
            s_selectedBundle = name;
            Save();
        }

        private static void RenameBundle()
        {
            var bundle = BundleEffects.File.Find(s_selectedBundle);
            if (bundle == null) return;
            if (!UnifiedPopup.IsAvailable())
            {
                s_error = "Renaming needs the game's text popup, which isn't available here. Rename it in the buffs file.";
                Refresh();
                return;
            }
            var old = bundle.Name;
            AskForName("Rename buff", $"New name for '{old}':", name => IsFreeName(name, old), name =>
            {
                var file = BundleEffects.File;
                var b = file.Find(old);
                if (b == null) return;
                if (string.Equals(file.Active, old, StringComparison.OrdinalIgnoreCase)) file.Active = name;
                b.Name = name;
                s_selectedBundle = name;
                Save();
            });
        }

        private static void DeleteBundle()
        {
            var bundle = BundleEffects.File.Find(s_selectedBundle);
            if (bundle == null) return;
            var name = bundle.Name;
            if (!UnifiedPopup.IsAvailable())
            {
                RemoveBundle(name);
                return;
            }
            HideForPopup();
            UnifiedPopup.Push(new YesNoPopup("Delete buff", $"Delete the buff '{name}'?",
                () =>
                {
                    UnifiedPopup.Pop();
                    RemoveBundle(name);
                    ShowAfterPopup();
                },
                () =>
                {
                    UnifiedPopup.Pop();
                    ShowAfterPopup();
                }, localizeText: false));
        }

        private static void RemoveBundle(string name)
        {
            var file = BundleEffects.File;
            file.Bundles.RemoveAll(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(file.Active, name, StringComparison.OrdinalIgnoreCase)) file.Active = null;
            s_selectedBundle = file.Bundles.FirstOrDefault()?.Name;
            Save();
        }

        private static void AskForName(string header, string text, Func<string, bool> valid, Action<string> done)
        {
            HideForPopup();
            UnifiedPopup.Push(new TextEntryPopup(header, text, "Buff name",
                () =>
                {
                    UnifiedPopup.Pop();
                    ShowAfterPopup();
                },
                entry => valid(entry),
                entry =>
                {
                    if (!valid(entry)) return;
                    UnifiedPopup.Pop();
                    done(entry.Trim());
                    ShowAfterPopup();
                }, localizeText: false));
            UnifiedPopup.SetFocus();
        }

        private static bool IsFreeName(string name, string renaming)
        {
            var n = name?.Trim();
            if (string.IsNullOrEmpty(n) || n.Length > 32 || string.Equals(n, "none", StringComparison.OrdinalIgnoreCase)) return false;
            var existing = BundleEffects.File.Find(n);
            return existing == null || (renaming != null && string.Equals(existing.Name, renaming, StringComparison.OrdinalIgnoreCase));
        }

        // Jötunn's GUI root is drawn in front of the game's, so the popup would be hidden behind the panel.
        private static bool s_hiddenForPopup;

        private static void HideForPopup()
        {
            if (!IsOpen) return;
            s_hiddenForPopup = true;
            s_panel.SetActive(false);
        }

        private static void ShowAfterPopup()
        {
            if (!s_hiddenForPopup) return;
            s_hiddenForPopup = false;
            if (s_panel != null && InventoryGui.IsVisible())
            {
                s_panel.SetActive(true);
                Refresh();
            }
        }

        private static void Save()
        {
            try
            {
                s_error = null;
                BundleEffects.Save(BundleEffects.File);
            }
            catch (Exception e)
            {
                s_error = "Could not save the buffs file: " + e.Message;
                Plugin.Log.LogError($"Saving buffs failed: {e}");
            }
            s_hoverEffect = null;
            Refresh();
        }

        // ---- helpers ------------------------------------------------------------------------

        private static string SlotLabel(BundleSlot slot)
        {
            var key = BundleSlots.Key(slot);
            return char.ToUpperInvariant(key[0]) + key.Substring(1);
        }

        /// <summary>Code name for a file entry (which may be an in-game name typed by hand).</summary>
        private static string Resolve(string entry)
        {
            var catalog = BundleEffects.Catalog;
            return catalog == null ? entry : BundleRules.Resolve(entry, catalog, out _) ?? entry;
        }

        private static bool SameEffect(string entry, string code) => entry != null && Resolve(entry) == code;

        private static string EffectName(string entry) => BundleEffects.Catalog?.DisplayName(Resolve(entry)) ?? entry;

        /// <summary>In-game name of a worn item whose own effect is this one, if any.</summary>
        private static string GearWith(Player player, string code)
        {
            if (player == null) return null;
            foreach (var slot in BundleSlots.All)
            {
                var item = BundleEffects.Worn(player, slot);
                var own = item?.m_shared.m_equipStatusEffect;
                if (own != null && own.name == code) return EffectText.DisplayName(item.m_shared.m_name);
            }
            return null;
        }

        /// <summary>Unity rich text has no escaping; neutralize tags in names typed by players.</summary>
        private static string Escape(string s) => s?.Replace("<", "<​") ?? "";
    }

    /// <summary>Pointer enter/exit only: an EventTrigger would also swallow scroll-wheel events.</summary>
    internal sealed class BundleHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Enter;
        public Action Exit;

        public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
    }
}
