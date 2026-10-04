using BepInEx;
using HarmonyLib;
using Jakeheim.Features;

namespace Jakeheim
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class JakeheimPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jake.jakeheim";
        public const string PluginName = "Jakeheim";
        public const string PluginVersion = "0.1.0";

        private Harmony _harmony;

        private void Awake()
        {
            // each feature binds its own config section; patches check their feature's Enabled at runtime
            RudderReturn.Bind(Config);
            TieredRepair.Bind(Config);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
