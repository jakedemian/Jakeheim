using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Jakeheim.Features
{
    // shortens the 5x (alt-held) craft duration
    internal static class FasterMultiCraft
    {
        private const string Section = "FasterMultiCraft";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> MultiCraftSeconds;

        // vanilla m_multiCraftDuration as loaded from the prefab
        private static float _vanillaDuration = -1f;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Shorten the base duration of a 5x craft.");
            MultiCraftSeconds = config.Bind(Section, "MultiCraftSeconds", 3f,
                "Base seconds for a 5x craft (vanilla 6). Crafting skill still reduces it by up to 60%, as in vanilla.");
        }

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        private static class InventoryGui_Awake_Patch
        {
            private static void Postfix(InventoryGui __instance) => _vanillaDuration = __instance.m_multiCraftDuration;
        }

        // UpdateRecipe reads m_multiCraftDuration every frame while a craft is in progress
        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        private static class InventoryGui_UpdateRecipe_Patch
        {
            private static void Prefix(InventoryGui __instance)
            {
                if (_vanillaDuration < 0f) return;
                __instance.m_multiCraftDuration = Enabled.Value
                    ? Mathf.Max(0.1f, MultiCraftSeconds.Value)
                    : _vanillaDuration;
            }
        }
    }
}
