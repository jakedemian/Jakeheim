# Handover: RudderReturn — Valheim self-centering rudder mod

## Goal

Build a client-side BepInEx/Harmony mod for Valheim. When the player at the helm is **not** pressing a steering input (A/D, or a gamepad stick past a deadzone), the ship's rudder should **decay back to center** on its own. Vanilla behavior is the problem: the rudder holds whatever angle you left it at until you steer back manually.

Out of scope: changing speed or sail settings, wind, physics forces, or UI art. The vanilla rudder indicator should animate back to center on its own, because it reads the same value we're changing. Confirm this during testing.

## Environment

- **OS:** Windows, Steam install of Valheim. Default path: `C:\Program Files (x86)\Steam\steamapps\common\Valheim`. Ask me if it isn't there.
- **Loader:** BepInEx 5 via **BepInExPack_Valheim** (Thunderstore). If `Valheim\BepInEx\core\BepInEx.dll` doesn't exist, stop and ask me to install it. Don't download or install it yourself.
- **Build:** `dotnet build` with an SDK-style csproj that references the game DLLs directly (see the csproj below). No NuGet packages are required.
- **Decompiler (for verification only):** `ilspycmd`, installed with `dotnet tool install -g ilspycmd`. dnSpy/ILSpy GUI are fine alternatives if I already have them.

## ⚠️ Step 1 — Verify game internals BEFORE writing code

The design depends on internal names that were **not confirmed against a source**. They come from recollection of the decompiled game and may be wrong or out of date for the current Valheim version. Decompile the `Ship` class first:

```
ilspycmd -t Ship "<ValheimDir>\valheim_Data\Managed\assembly_valheim.dll" > Ship.decompiled.cs
```

(If `-t` isn't the right flag in the installed ilspycmd version, check `ilspycmd --help`.)

Confirm or correct each assumption, and report the findings to me before continuing:

| # | Assumption | What to check |
|---|---|---|
| A1 | `Ship.ApplyControlls(Vector3 dir)` exists (note: double "l"), and `dir.x` is the steering input in roughly -1..1 | Exact name, signature, and how `dir.x` is used |
| A2 | `float Ship.m_rudderValue` holds the rudder position, clamped to -1..1 | Field name, type, clamp range |
| A3 | Inside `ApplyControlls`, the rudder changes only by accumulating input (something like `m_rudderValue += dir.x * m_rudderSpeed * dt`, then clamp). Nothing recenters it. | Read the method body |
| A4 | `ApplyControlls` also syncs the rudder to the ZDO owner via an RPC (believed to be named `"Rudder"`), throttled to a few times per second, sending `m_rudderValue` | Look for `InvokeRPC` and see which value it sends |
| A5 | `ApplyControlls` is called every tick while someone is at the helm, **including ticks with zero input**. It's believed to be reached through `ShipControlls` and the `IDoodadController` path from `Player`. | Trace the callers. If it's only called on input, the Prefix approach won't decay anything and a different hook point is needed (see "Fallback design"). |
| A6 | Non-owner clients overwrite `m_rudderValue` from the ZDO only if no local rudder send happened recently (in `UpdateControlls` or similar) | Check that the decay won't be immediately clobbered on non-owner clients |
| A7 | The rudder HUD indicator reads `m_rudderValue` (or a getter for it) | Search `Hud` for rudder indicator code |

If anything differs, adapt the design to fit the real code. Keep the intent: decay happens on the controlling client, before the value is synced.

## Design

- **Patch:** a Harmony **Prefix** on `Ship.ApplyControlls`.
- **Logic:** if `|dir.x| <= InputDeadzone`, move `m_rudderValue` toward 0 by the configured mode, then let vanilla run.
- **Why Prefix, not Postfix:** vanilla's own sync RPC (A4) then sends the already-decayed value. That keeps multiplayer consistent when the helmsman isn't the ship's ZDO owner, and avoids adding our own RPC.
- **Field access:** `AccessTools.FieldRefAccess<Ship, float>("m_rudderValue")`. It works whether the field is public or private, and it throws at plugin load if the name is wrong, so failures are loud.
- **Timing:** use `Time.deltaTime`. Inside FixedUpdate, Unity returns `fixedDeltaTime` for it, so this is correct whichever loop drives the call.
- **Decay modes:**
  - `Linear`: `Mathf.MoveTowards(v, 0, rate * dt)`. `rate` is in rudder units per second (1.0 = full lock to center in 1 s).
  - `Exponential` (default): `v *= Mathf.Exp(-rate * dt)`. Fast near full lock, eases in near center.
- **Snap:** if `|v| < SnapThreshold`, set it to exactly 0, so the rudder can actually reach true center.

### Config (BepInEx ConfigEntry, section "General")

| Key | Type | Default | Meaning |
|---|---|---|---|
| Enabled | bool | true | Master toggle |
| DecayMode | enum Linear/Exponential | Exponential | Return curve |
| ReturnRate | float | 1.5 | Linear: units/s. Exponential: decay constant k |
| InputDeadzone | float | 0.1 | Input magnitude treated as "released" |
| SnapThreshold | float | 0.01 | Snap-to-zero threshold |

### Fallback design (only if A5 fails)

If `ApplyControlls` isn't called when there's no input, add a Postfix on `Ship.FixedUpdate` (or `CustomFixedUpdate`, whatever exists). It should decay the rudder only when:
- this client is the one controlling the helm (find the equivalent of `ShipControlls`/`m_ship.IsPlayerInControl()` / `HaveControllingPlayer()` in the decompiled code), and
- no steering input has arrived in the last frame. Track this from a Prefix on `ApplyControlls` that records `Time.time` of the last non-zero `dir.x`.

Make sure the decayed value still gets synced. That may mean invoking the same RPC vanilla uses, with the same throttle.

### Optional (ask me first)

When nobody is at the helm, the rudder stays frozen at its last angle. A config toggle `CenterWhenUnmanned` could run the decay on the ship owner in `FixedUpdate` while no one is controlling. Don't implement this unless I say yes.

## Reference implementation (assumes A1–A7 hold)

`RudderReturnPlugin.cs`:

```csharp
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace RudderReturn
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class RudderReturnPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jake.rudderreturn";
        public const string PluginName = "RudderReturn";
        public const string PluginVersion = "0.1.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<DecayMode> Mode;
        internal static ConfigEntry<float> ReturnRate;
        internal static ConfigEntry<float> InputDeadzone;
        internal static ConfigEntry<float> SnapThreshold;

        public enum DecayMode { Linear, Exponential }

        private Harmony _harmony;

        private void Awake()
        {
            Enabled = Config.Bind("General", "Enabled", true,
                "Rudder returns to center when no steering input is held.");
            Mode = Config.Bind("General", "DecayMode", DecayMode.Exponential,
                "Linear = constant return speed. Exponential = fast at full lock, eases in near center.");
            ReturnRate = Config.Bind("General", "ReturnRate", 1.5f,
                "Linear: rudder units/second (range -1..1). Exponential: k in value *= e^(-k*dt).");
            InputDeadzone = Config.Bind("General", "InputDeadzone", 0.1f,
                "Steering input below this magnitude counts as released.");
            SnapThreshold = Config.Bind("General", "SnapThreshold", 0.01f,
                "Below this magnitude the rudder snaps to exactly 0.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.ApplyControlls))]
    internal static class Ship_ApplyControlls_Patch
    {
        private static readonly AccessTools.FieldRef<Ship, float> RudderValue =
            AccessTools.FieldRefAccess<Ship, float>("m_rudderValue");

        private static void Prefix(Ship __instance, Vector3 dir)
        {
            if (!RudderReturnPlugin.Enabled.Value) return;
            if (Mathf.Abs(dir.x) > RudderReturnPlugin.InputDeadzone.Value) return;

            ref float rudder = ref RudderValue(__instance);
            if (rudder == 0f) return;

            float dt = Time.deltaTime;
            float rate = RudderReturnPlugin.ReturnRate.Value;

            rudder = RudderReturnPlugin.Mode.Value == RudderReturnPlugin.DecayMode.Linear
                ? Mathf.MoveTowards(rudder, 0f, rate * dt)
                : rudder * Mathf.Exp(-rate * dt);

            if (Mathf.Abs(rudder) < RudderReturnPlugin.SnapThreshold.Value) rudder = 0f;
        }
    }
}
```

Note: if `ApplyControlls` isn't public, `nameof(Ship.ApplyControlls)` won't compile. In that case use the string `"ApplyControlls"`, or reference a publicized `assembly_valheim.dll`.

`RudderReturn.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net462</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AssemblyName>RudderReturn</AssemblyName>
    <ValheimDir>C:\Program Files (x86)\Steam\steamapps\common\Valheim</ValheimDir>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="BepInEx"><HintPath>$(ValheimDir)\BepInEx\core\BepInEx.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="0Harmony"><HintPath>$(ValheimDir)\BepInEx\core\0Harmony.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="assembly_valheim"><HintPath>$(ValheimDir)\valheim_Data\Managed\assembly_valheim.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine"><HintPath>$(ValheimDir)\valheim_Data\Managed\UnityEngine.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.CoreModule"><HintPath>$(ValheimDir)\valheim_Data\Managed\UnityEngine.CoreModule.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
  <Target Name="CopyToPlugins" AfterTargets="Build">
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(ValheimDir)\BepInEx\plugins\RudderReturn" />
  </Target>
</Project>
```

`net462` is an assumption based on how BepInEx 5 Valheim mods are typically built. If the build fails over the framework target, check what the installed BepInExPack targets and adjust.

## Build

1. Run `dotnet build -c Release` in the project folder.
2. Confirm `RudderReturn.dll` landed in `<ValheimDir>\BepInEx\plugins\RudderReturn\`.
3. Don't touch any other files in the Valheim install.

## Test plan (I'll run these in-game; give me this checklist)

1. **Load:** the BepInEx console or `BepInEx\LogOutput.log` shows `RudderReturn 0.1.0 loaded` and no Harmony or `FieldRefAccess` exceptions.
2. **Config generated:** `BepInEx\config\com.jake.rudderreturn.cfg` exists with the five keys.
3. **Core behavior:** take the helm, hold D to full lock, release. The rudder indicator returns to center, and the ship stops turning (aside from wind and wave drift).
4. **Holding input:** while A or D is held, the rudder still moves exactly as in vanilla.
5. **Both modes:** switch DecayMode, restart, and compare the feel. Tweak ReturnRate.
6. **Gamepad (if available):** small stick drift below the deadzone doesn't stop the rudder from centering.
7. **Multiplayer (if possible):** a second player sees the rudder recenter.
8. **Disabled:** `Enabled = false` gives exact vanilla behavior.

## Acceptance criteria

- Builds cleanly against the current game DLLs, with no warnings about missing references.
- All assumptions A1–A7 are either verified or replaced by verified equivalents, and documented in the README.
- Steering while input is held is byte-for-byte vanilla behavior. The patch does nothing when input exceeds the deadzone.
- No new RPCs, no ZDO writes of our own (unless the fallback design forces it, in which case explain why).
- A short `README.md` covering what the mod does, config keys, install steps, and the verified internal names and game version tested.

## Working rules

- Report the Step 1 findings before implementing. If any assumption is false, propose the adjusted design and wait for my OK.
- Don't redistribute or commit any game DLLs or decompiled game source to the repo. Add `Ship.decompiled.cs` and `*.dll` to `.gitignore`.
- Keep it to one patch class unless the fallback design is needed.
