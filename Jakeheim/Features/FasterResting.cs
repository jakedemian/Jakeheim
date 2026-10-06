using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Jakeheim.Features
{
    // higher comfort shortens the resting time before rested kicks in
    internal static class FasterResting
    {
        private const string Section = "FasterResting";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> ComfortPerTier;
        internal static ConfigEntry<float> SecondsPerTier;
        internal static ConfigEntry<float> MinimumSeconds;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Higher comfort reduces how long you must rest before becoming Rested.");
            ComfortPerTier = config.Bind(Section, "ComfortPerTier", 5,
                "Every this many comfort levels is one tier (0-4 = tier 0, 5-9 = tier 1, ...).");
            SecondsPerTier = config.Bind(Section, "SecondsPerTier", 4f,
                "Seconds removed from the vanilla 20s resting delay per tier.");
            MinimumSeconds = config.Bind(Section, "MinimumSeconds", 4f,
                "The resting delay never goes below this.");
        }

        // vanilla delay from the ObjectDB template; SEMan clones it per character, so the template stays untouched
        private static float VanillaDelay()
        {
            var template = ObjectDB.instance?.GetStatusEffect(SEMan.s_statusEffectResting) as SE_Cozy;
            return template != null ? template.m_delay : 20f;
        }

        // SE_Cozy.UpdateStatusEffect adds rested once m_time > m_delay; set the delay right before that check
        [HarmonyPatch(typeof(SE_Cozy), nameof(SE_Cozy.UpdateStatusEffect))]
        private static class SE_Cozy_UpdateStatusEffect_Patch
        {
            private static void Prefix(SE_Cozy __instance)
            {
                float vanilla = VanillaDelay();
                if (!Enabled.Value || !(__instance.m_character is Player player))
                {
                    __instance.m_delay = vanilla;
                    return;
                }

                int tier = player.GetComfortLevel() / Mathf.Max(1, ComfortPerTier.Value);
                __instance.m_delay = Mathf.Max(MinimumSeconds.Value, vanilla - tier * SecondsPerTier.Value);
            }
        }
    }
}
