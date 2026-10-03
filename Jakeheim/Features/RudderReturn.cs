using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Jakeheim.Features
{
    // rudder decays back to center when the helmsman isn't steering
    internal static class RudderReturn
    {
        private const string Section = "RudderReturn";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<DecayMode> Mode;
        internal static ConfigEntry<float> ReturnRate;
        internal static ConfigEntry<float> InputDeadzone;
        internal static ConfigEntry<float> SnapThreshold;

        public enum DecayMode { Linear, Exponential }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Rudder returns to center when no steering input is held.");
            Mode = config.Bind(Section, "DecayMode", DecayMode.Exponential,
                "Linear = constant return speed. Exponential = fast at full lock, eases in near center.");
            ReturnRate = config.Bind(Section, "ReturnRate", 1.5f,
                "Linear: rudder units/second (range -1..1). Exponential: k in value *= e^(-k*dt).");
            InputDeadzone = config.Bind(Section, "InputDeadzone", 0.1f,
                "Steering input below this magnitude counts as released.");
            SnapThreshold = config.Bind(Section, "SnapThreshold", 0.01f,
                "Below this magnitude the rudder snaps to exactly 0.");
        }

        // runs before vanilla so its throttled "Rudder" rpc syncs the decayed value to the zdo owner
        [HarmonyPatch(typeof(Ship), nameof(Ship.ApplyControlls))]
        private static class Ship_ApplyControlls_Patch
        {
            private static readonly AccessTools.FieldRef<Ship, float> RudderValue =
                AccessTools.FieldRefAccess<Ship, float>("m_rudderValue");

            // m_rudder is the per-tick steering input; only Hud reads it (via GetRudder) to spin the wheel icon
            private static readonly AccessTools.FieldRef<Ship, float> RudderInput =
                AccessTools.FieldRefAccess<Ship, float>("m_rudder");

            private static void Prefix(Ship __instance, Vector3 dir, out float __state)
            {
                __state = 0f;
                if (!Enabled.Value) return;
                if (Mathf.Abs(dir.x) > InputDeadzone.Value) return;

                ref float rudder = ref RudderValue(__instance);
                if (rudder == 0f) return;

                // vanilla integrates with fixedDeltaTime here; called from PlayerController.FixedUpdate
                float dt = Time.fixedDeltaTime;
                float rate = ReturnRate.Value;
                float before = rudder;

                rudder = Mode.Value == DecayMode.Linear
                    ? Mathf.MoveTowards(rudder, 0f, rate * dt)
                    : rudder * Mathf.Exp(-rate * dt);

                if (Mathf.Abs(rudder) < SnapThreshold.Value) rudder = 0f;

                __state = rudder - before;
            }

            private static void Postfix(Ship __instance, float __state)
            {
                if (__state == 0f) return;

                // vanilla just zeroed m_rudder for released input; back-solve the input that would
                // produce this tick's decay so the hud wheel counter-spins at vanilla's ratio
                float speed = __instance.m_rudderSpeed * Time.fixedDeltaTime;
                if (speed > 0f) RudderInput(__instance) = __state / speed;
            }
        }
    }
}
