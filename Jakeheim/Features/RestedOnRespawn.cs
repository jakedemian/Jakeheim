using BepInEx.Configuration;
using HarmonyLib;

namespace Jakeheim.Features
{
    // respawning after death grants rested immediately, sized to the spawn point's comfort
    internal static class RestedOnRespawn
    {
        private const string Section = "RestedOnRespawn";

        internal static ConfigEntry<bool> Enabled;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "After dying, respawn with the Rested buff for your current comfort level instead of waiting 20s by a fire.");
        }

        // spawning only happens once ZNetScene.IsAreaReady, so the bed area's comfort pieces are loaded by now
        [HarmonyPatch(typeof(Game), "SpawnPlayer")]
        private static class Game_SpawnPlayer_Patch
        {
            private static readonly AccessTools.FieldRef<Game, bool> RespawnAfterDeath =
                AccessTools.FieldRefAccess<Game, bool>("m_respawnAfterDeath");

            private static readonly AccessTools.FieldRef<Player, float> CoverPercentage =
                AccessTools.FieldRefAccess<Player, float>("m_coverPercentage");

            private static readonly AccessTools.FieldRef<Player, bool> UnderRoof =
                AccessTools.FieldRefAccess<Player, bool>("m_underRoof");

            private static readonly AccessTools.FieldRef<Player, int> ComfortLevel =
                AccessTools.FieldRefAccess<Player, int>("m_comfortLevel");

            private static void Postfix(Game __instance, Player __result)
            {
                if (!Enabled.Value || __result == null || !RespawnAfterDeath(__instance)) return;

                // player only refreshes cover every 1s and comfort every 2s; compute both now so
                // InShelter() and GetComfortLevel() (which SE_Rested reads for its ttl) are correct
                Cover.GetCoverForPoint(__result.GetCenterPoint(), out CoverPercentage(__result), out UnderRoof(__result));
                ComfortLevel(__result) = SE_Rested.CalculateComfortLevel(__result);

                __result.GetSEMan().AddStatusEffect(SEMan.s_statusEffectRested, resetTime: true);
            }
        }
    }
}
