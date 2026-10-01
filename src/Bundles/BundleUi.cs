using System;
using HarmonyLib;
using Jotunn.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LoadoutBuffs
{
    /// <summary>
    /// The "Bundles" button in the inventory screen: a copy of the crafting panel's CRAFT tab, labelled
    /// BUNDLES and kept right after the last visible tab (the game shows CRAFT, UPGRADE or both depending on
    /// the station). The player panel's icon row was tried first, but it's full (it also holds the PvP toggle).
    /// Fallback: a Jötunn button on the player panel. Which one was used is logged once per scene.
    /// </summary>
    internal static class InventoryButton
    {
        private const string ObjectName = "LoadoutBuffs_BundlesButton";
        private const string Label = "BUNDLES";
        private static GameObject s_button;
        private static TMP_Text s_label;
        private static bool s_isTab;

        public static void Ensure(InventoryGui gui)
        {
            if (s_button != null || gui == null) return;
            s_isTab = false;
            s_button = TryCloneTab(gui) ?? JotunnButton(gui);
            Layout(gui);
        }

        private static GameObject TryCloneTab(InventoryGui gui)
        {
            var tab = gui.m_tabCraft;
            if (tab == null || gui.m_tabUpgrade == null)
            {
                Plugin.Log.LogInfo("Bundles button: crafting tabs not found, using a Jötunn button.");
                return null;
            }
            var copy = Object.Instantiate(tab.gameObject, tab.transform.parent);
            copy.name = ObjectName;
            var button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent(); // drops the copied persistent OnTabCraftPressed call
            button.onClick.AddListener(BundleWindow.Toggle);
            button.interactable = true; // the game marks the current tab by making it non-interactable
            s_label = copy.GetComponentInChildren<TMP_Text>(true);
            if (s_label != null) s_label.text = Label;
            s_isTab = true;
            copy.SetActive(true);
            Plugin.Log.LogInfo($"Bundles button: copied the CRAFT tab (parent '{copy.transform.parent.name}', " +
                               $"label {(s_label != null ? "set" : "not found")}).");
            return copy;
        }

        /// <summary>Every inventory frame: after the last visible tab, one tab-width step on.</summary>
        public static void Layout(InventoryGui gui)
        {
            if (!s_isTab || s_button == null || gui == null) return;
            var craft = (RectTransform)gui.m_tabCraft.transform;
            var upgrade = (RectTransform)gui.m_tabUpgrade.transform;
            var step = upgrade.anchoredPosition - craft.anchoredPosition;
            var last = upgrade.gameObject.activeSelf ? upgrade : craft.gameObject.activeSelf ? craft : null;
            var position = last != null ? last.anchoredPosition + step : craft.anchoredPosition;
            var rt = (RectTransform)s_button.transform;
            if (rt.anchoredPosition != position) rt.anchoredPosition = position;
            if (!s_button.activeSelf) s_button.SetActive(true);
            if (s_label != null && s_label.text != Label) s_label.text = Label; // in case something re-localizes it
        }

        private static GameObject JotunnButton(InventoryGui gui)
        {
            var parent = gui.m_player != null ? gui.m_player : (RectTransform)gui.transform;
            var go = GUIManager.Instance.CreateButton("Bundles", parent, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(70f, -24f), 120f, 34f);
            go.name = ObjectName;
            go.GetComponent<Button>().onClick.AddListener(BundleWindow.Toggle);
            Plugin.Log.LogInfo($"Bundles button: Jötunn button on '{parent.name}'.");
            return go;
        }
    }

    [HarmonyPatch]
    internal static class BundleUiPatches
    {
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        [HarmonyPostfix]
        private static void InventoryShow(InventoryGui __instance) =>
            Guard(() => InventoryButton.Ensure(__instance));

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        [HarmonyPostfix]
        private static void InventoryHide() => Guard(BundleWindow.Close);

        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPostfix]
        private static void InventoryUpdateLayout(InventoryGui __instance)
        {
            try
            {
                InventoryButton.Layout(__instance);
                BundleWindow.RefreshIfGearChanged();
            }
            catch (Exception e)
            {
                LogOnce(e);
            }
        }

        /// <summary>Esc / gamepad B closes the bundles window, not the whole inventory (skip one inventory frame).</summary>
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPrefix]
        private static bool InventoryUpdate()
        {
            try
            {
                if (!BundleWindow.IsOpen || UnifiedPopup.IsVisible()) return true;
                if (!ZInput.GetKeyDown(KeyCode.Escape) && !ZInput.GetButtonDown("JoyButtonB")) return true;
                BundleWindow.Close();
                return false;
            }
            catch (Exception e)
            {
                LogOnce(e);
                return true;
            }
        }

        private static void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                LogOnce(e);
            }
        }

        private static bool s_logged;

        private static void LogOnce(Exception e)
        {
            if (s_logged) return;
            s_logged = true;
            Plugin.Log.LogError($"Bundles UI failed (logged once): {e}");
        }
    }
}
