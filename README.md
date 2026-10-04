# Jakeheim

A personal, client-side BepInEx quality-of-life mod for Valheim. Each feature has its own config section and its own `Enabled` toggle.

## Features

| Feature | Config section | What it does |
|---|---|---|
| [RudderReturn](#rudderreturn) | `[RudderReturn]` | The rudder recenters on its own when you're at the helm and not steering. |
| [TieredRepair](#tieredrepair) | `[TieredRepair]` | The forge also repairs workbench gear, and the black forge also repairs workbench and forge gear. |

## Install

1. Install [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
2. Copy `Jakeheim.dll` to `<Valheim>\BepInEx\plugins\Jakeheim\`.
3. Launch the game once to generate `BepInEx\config\com.jake.jakeheim.cfg`.

## Build

Requires the .NET SDK (built with 8.0). The csproj references the game's DLLs directly and needs no NuGet packages.

```
cd Jakeheim
dotnet build -c Release
```

`ValheimDir` defaults to `D:\Steam\steamapps\common\Valheim` on Windows and `/mnt/d/Steam/steamapps/common/Valheim` elsewhere (WSL). Override it with `-p:ValheimDir=...`. After a build, the DLL is copied into `BepInEx\plugins\Jakeheim\`.

The target is `netstandard2.1`, which matches the Mono profile the game ships. A `net462` build hits a `netstandard` 2.0 vs 2.1 facade conflict with Unity's DLLs. `MSB3277` is demoted to a message because the game's DLLs pull in Mono's `System.*` versions transitively. Those conflicts aren't missing references, and the mod doesn't use those assemblies.

## Layout

- `Jakeheim/JakeheimPlugin.cs`: the plugin entry point. It binds each feature's config and calls `Harmony.PatchAll()`.
- `Jakeheim/Features/<Feature>.cs`: one static class per feature. Each holds a `Bind(ConfigFile)` method plus its nested Harmony patch classes.
- `decompiled/`: local decompiled game sources used for verification. These are gitignored and never committed.

To add a feature, create `Features/<Name>.cs` with a `Bind` method, call it from `JakeheimPlugin.Awake`, and add a section to this README.

To regenerate a decompiled class:

```
ilspycmd -t <Class> "<Valheim>/valheim_Data/Managed/assembly_valheim.dll" > decompiled/<Class>.decompiled.cs
```

---

## RudderReturn

When you're at the helm and not steering (A/D released, or gamepad stick inside the deadzone), the rudder returns to center on its own. Vanilla keeps the rudder at whatever angle you left it. Both the HUD rudder indicator and the wheel icon animate back to center.

### Config (`[RudderReturn]`)

| Key | Default | Meaning |
|---|---|---|
| Enabled | `true` | Feature toggle. `false` = vanilla behavior. |
| DecayMode | `Exponential` | `Linear` = constant return speed. `Exponential` = fast at full lock, eases in near center. |
| ReturnRate | `1.5` | Linear: rudder units per second (full lock is 1.0). Exponential: `k` in `value *= e^(-k*dt)`. |
| InputDeadzone | `0.1` | Steering input at or below this magnitude counts as released. |
| SnapThreshold | `0.01` | Below this magnitude the rudder snaps to exactly 0. |

For reference, vanilla steers at `m_rudderSpeed = 0.5` scaled by 0.5 to 1.0, so going from center to full lock by hand takes roughly 2 to 4 seconds.

### How it works

A Harmony Prefix on `Ship.ApplyControlls(Vector3 dir)` runs when `|dir.x| <= InputDeadzone`. It moves `m_rudderValue` toward 0, then lets vanilla run. Running before vanilla means vanilla's own throttled `"Rudder"` RPC carries the decayed value to the ship's ZDO owner. The feature adds no RPCs and makes no ZDO writes of its own. When input is above the deadzone, the patch returns immediately and steering is unchanged from vanilla.

A Postfix on the same method handles the HUD. The wheel icon spins from `m_rudder` (`GetRudder()`), which is the per-tick steering input, and vanilla sets it to 0 when you're not steering. While the rudder is decaying, the Postfix sets `m_rudder` to the input that would have produced that tick's change, so the wheel spins back toward center at vanilla's ratio. Only `Hud` reads `m_rudder`, it's never synced, and vanilla overwrites it at the start of every `ApplyControlls`.

### Verified internals

Verified against Valheim **1.0.16** (network version 40) with BepInExPack_Valheim **5.4.2351**, by decompiling `assembly_valheim.dll` with ilspycmd 9.1.

| Item | Finding |
|---|---|
| `Ship.ApplyControlls(Vector3 dir)` | The method is `public`. `dir.x` is steering and `dir.z` is forward/backward. |
| `Ship.m_rudderValue` | A `private float`, clamped with `Utils.Clamp(v, -1f, 1f)`. |
| Rudder accumulation | `m_rudder = dir.x * Lerp(0.5, 1, abs(m_rudderValue))`, then `m_rudderValue += m_rudder * m_rudderSpeed * Time.fixedDeltaTime`, then a clamp. Nothing recenters it. |
| Sync | `m_nview.InvokeRPC("Rudder", m_rudderValue)` fires when `Time.time - m_sendRudderTime > 0.2f`. The owner's `RPC_Rudder` sets the value, and `UpdateControlls` writes it to `ZDOVars.s_rudder`. |
| Call path | `PlayerController.FixedUpdate` calls `Player.SetControls` every tick, including `Vector3.zero` when there's no input. That leads to `Player.SetDoodadControlls`, then `ShipControlls.ApplyControlls` (`IDoodadController`), then `Ship.ApplyControlls`. |
| Non-owner clobber | `Ship.UpdateControlls` only reloads `m_rudderValue` from the ZDO if `Time.time - m_sendRudderTime > 1f`. At the helm, sends happen every 0.2 s. |
| HUD | `Hud` reads `GetRudderValue()` (`m_rudderValue`) for `m_shipRudderIndicator`, which hides when `abs(value) < 0.02`. It reads `GetRudder()` (`m_rudder`) to spin `m_shipRudderIcon` at `200 * -rudder` deg/s. |

Notes:

- The feature uses `Time.fixedDeltaTime`, matching vanilla's integration in this method.
- Vanilla already sets `m_rudderValue = 0` on the owner when no players are aboard (`Ship.CustomFixedUpdate`). An unmanned ship with players aboard keeps its last rudder angle.
- When a menu is open at the helm, the game sends zero steering input, so the rudder recenters then too.

### Test checklist

1. **Load:** `BepInEx\LogOutput.log` shows `Jakeheim 0.1.0 loaded`, with no Harmony or `FieldRefAccess` exceptions.
2. **Config:** `BepInEx\config\com.jake.jakeheim.cfg` has a `[RudderReturn]` section with the five keys.
3. **Core:** take the helm, hold D to full lock, then release. The indicator and wheel return to center and the ship stops turning.
4. **Holding input:** while A or D is held, steering behaves exactly like vanilla.
5. **Modes:** switch DecayMode, restart, and compare. Then tune ReturnRate.
6. **Gamepad:** stick drift below the deadzone doesn't prevent centering.
7. **Multiplayer:** a second player sees the rudder recenter.
8. **Disabled:** `Enabled = false` behaves exactly like vanilla.

---

## TieredRepair

Higher-tier stations also repair lower-tier gear. The tiers are workbench < forge < black forge. A forge repairs workbench and forge items, and a black forge repairs items from all three. The galdr table and every other station keep vanilla behavior.

### Config (`[TieredRepair]`)

| Key | Default | Meaning |
|---|---|---|
| Enabled | `true` | Feature toggle. `false` = vanilla behavior. |

### How it works

A Postfix on `InventoryGui.CanRepair(ItemDrop.ItemData)` (private) runs only when vanilla returned `false`. That method backs both the repair button's glow (`HaveRepairableItems`) and `RepairOneItem`, so both pick up the change. The Postfix grants repair when all of these hold:

- the item is `m_canBeReparied`;
- the current station (`Player.GetCurrentCraftingStation()`) is a forge or black forge;
- the item's recipe names a tiered station, as either `m_craftingStation` or `m_repairStation`, at a lower tier.

Stations match by `CraftingStation.m_name`: `$piece_workbench`, `$piece_forge`, `$piece_blackforge`. Vanilla's `m_minStationLevel` check is skipped for these cross-tier repairs, since the higher station supersedes the lower one's upgrades. Same-tier repairs still go through vanilla's level check unchanged.

### Verified data

Verified against Valheim 1.0.16. The source is `InventoryGui.CanRepair`, plus the game's asset bundles read with UnityPy. Every `CraftingStation` prefab has `m_canRepair = true`. Repairable items name only these stations:

| Station | Repairable recipes |
|---|---|
| Workbench | 56 as craft station, plus 37 as repair-only station (hammer, club, stone axe, Feaster, cosmetic clothes) |
| Forge | 58 |
| Black forge | 102 |
| Galdr table (`$piece_magetable`) | 25 |

### Test checklist

1. Damage a workbench item (e.g. leather armor) and a forge item (e.g. a bronze axe).
2. At a forge, the repair button glows, and pressing it repairs both items.
3. At a black forge, both items repair too.
4. At a workbench, only the workbench item repairs.
5. At a galdr table, neither item repairs.
6. With `Enabled = false`, each station repairs only its own items, as in vanilla.
