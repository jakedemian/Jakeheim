using BepInEx.Configuration;
using HarmonyLib;

namespace Jakeheim.Features
{
    // higher-tier stations also repair lower-tier gear: workbench < forge < black forge
    internal static class TieredRepair
    {
        private const string Section = "TieredRepair";

        internal static ConfigEntry<bool> Enabled;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Forge also repairs workbench items; black forge also repairs workbench and forge items. Galdr table and other stations are unchanged.");
        }

        // CraftingStation.m_name values; anything not listed (galdr table, etc.) has no tier
        private static int GetTier(CraftingStation station)
        {
            if (station == null) return -1;
            switch (station.m_name)
            {
                case "$piece_workbench": return 0;
                case "$piece_forge": return 1;
                case "$piece_blackforge": return 2;
                default: return -1;
            }
        }

        // CanRepair backs both the repair button glow (HaveRepairableItems) and RepairOneItem
        [HarmonyPatch(typeof(InventoryGui), "CanRepair")]
        private static class InventoryGui_CanRepair_Patch
        {
            private static void Postfix(ItemDrop.ItemData item, ref bool __result)
            {
                if (__result || !Enabled.Value) return;
                if (Player.m_localPlayer == null || !item.m_shared.m_canBeReparied) return;

                int stationTier = GetTier(Player.m_localPlayer.GetCurrentCraftingStation());
                if (stationTier <= 0) return;

                Recipe recipe = ObjectDB.instance.GetRecipe(item);
                if (recipe == null) return;

                // vanilla accepts either station, so the item's tier is whichever tiered one it names.
                // the station level check is skipped: a higher tier supersedes the lower table's upgrades
                int itemTier = System.Math.Max(GetTier(recipe.m_craftingStation), GetTier(recipe.m_repairStation));
                if (itemTier >= 0 && itemTier < stationTier) __result = true;
            }
        }
    }
}
