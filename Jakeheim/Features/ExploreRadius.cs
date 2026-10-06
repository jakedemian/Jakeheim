using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Jakeheim.Features
{
    // scales how far around the player the map/minimap fog of war is cleared
    internal static class ExploreRadius
    {
        private const string Section = "ExploreRadius";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Multiplier;

        // vanilla m_exploreRadius as loaded from the prefab, captured once per minimap instance
        private static float _vanillaRadius = -1f;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Increase the radius around the player that reveals the map.");
            Multiplier = config.Bind(Section, "Multiplier", 2f,
                new ConfigDescription("Multiplier on the vanilla map reveal radius.",
                    new AcceptableValueRange<float>(0.1f, 10f)));
        }

        [HarmonyPatch(typeof(Minimap), "Awake")]
        private static class Minimap_Awake_Patch
        {
            private static void Postfix(Minimap __instance) => _vanillaRadius = __instance.m_exploreRadius;
        }

        // UpdateExplore reads m_exploreRadius every m_exploreInterval; setting it here keeps config edits live
        [HarmonyPatch(typeof(Minimap), "UpdateExplore")]
        private static class Minimap_UpdateExplore_Patch
        {
            private static void Prefix(Minimap __instance)
            {
                if (_vanillaRadius < 0f) return;
                __instance.m_exploreRadius = Enabled.Value
                    ? _vanillaRadius * Mathf.Max(0.1f, Multiplier.Value)
                    : _vanillaRadius;
            }
        }
    }
}
