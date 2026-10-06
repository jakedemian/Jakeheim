# Jakeheim

A personal, client-side BepInEx quality-of-life mod for Valheim. Each feature has its own config section and its own `Enabled` toggle.

## Features

| Feature | Config section | What it does |
|---|---|---|
| [RudderReturn](#rudderreturn) | `[RudderReturn]` | The rudder recenters on its own when you're at the helm and not steering. |
| [TieredRepair](#tieredrepair) | `[TieredRepair]` | The forge also repairs workbench gear, and the black forge also repairs workbench and forge gear. |
| [RestedOnRespawn](#restedonrespawn) | `[RestedOnRespawn]` | After death, you respawn already Rested at your spawn point's comfort level. |
| [ExploreRadius](#exploreradius) | `[ExploreRadius]` | Clears map fog of war in a larger radius around you. |
| [FasterResting](#fasterresting) | `[FasterResting]` | Higher comfort shortens the 20 s rest needed to become Rested. |
| [FasterMultiCraft](#fastermulticraft) | `[FasterMultiCraft]` | 5x crafts take 3 s instead of 6 s. |

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

To add a feature, create `Features/<Name>.cs` with a `Bind` method, call it from `JakeheimPlugin.Awake`, and add both a row to the Features table and a section to this README.

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

---

## RestedOnRespawn

After dying, you respawn with the Rested buff already applied, at the comfort level of wherever you spawn. Vanilla makes you sit by a fire for 20 s of Resting first. First login and logging back in are unaffected.

### Config (`[RestedOnRespawn]`)

| Key | Default | Meaning |
|---|---|---|
| Enabled | `true` | Feature toggle. `false` = vanilla behavior. |

### How it works

A Postfix on `Game.SpawnPlayer` (private) runs only when `Game.m_respawnAfterDeath` is true. That flag is set by `Player.OnDeath` via `RequestRespawn(10f, afterDeath: true)`. The Postfix does three things:

1. It runs `Cover.GetCoverForPoint` and writes `Player.m_coverPercentage` and `m_underRoof`, so `InShelter()` is correct immediately. Vanilla refreshes these every 1 s.
2. It sets `Player.m_comfortLevel = SE_Rested.CalculateComfortLevel(player)`. Vanilla refreshes this every 2 s, and `SE_Rested` reads it through `GetComfortLevel()` for its duration.
3. It calls `SEMan.AddStatusEffect(SEMan.s_statusEffectRested, resetTime: true)`. This takes the same path vanilla uses when `SE_Cozy` hands off to Rested and when you wake from sleep.

Comfort pieces are loaded by this point. `Game.FindSpawnPoint` only returns once `ZNetScene.IsAreaReady(point)` is true, and pieces register in `Piece.s_allComfortPieces` from `Awake`.

### Verified data (Valheim 1.0.16)

- `Rested` (`SE_Rested`): `m_baseTTL = 480`, `m_TTLPerComfortLevel = 60`, so the duration is `480 + (comfort - 1) * 60` s. Regen is health ×1.5, stamina ×2 and eitr ×2, plus +50% skill gain for all skills.
- `Resting` (`SE_Cozy`): `m_delay = 20` s before it adds Rested. Regen is health ×3, stamina ×4 and eitr ×4.
- Comfort: base 1, plus 1 and nearby pieces (within 10 m, highest per `ComfortGroup`) only when in shelter (at least 80% cover and under a roof).

`Cover` lives in `assembly_utils.dll`, which is why the csproj references it.

### Test checklist

1. Die with a bed spawn point in a comfy base. On respawn you get "Rested (Comfort: N)" immediately, and N matches what sitting by the fire there shows.
2. The Rested timer starts at `8:00 + (N - 1) min`.
3. Die with no bed (spawning at the start stone). You're Rested at comfort 1 (8:00).
4. Logging out and back in doesn't grant Rested.
5. With `Enabled = false`, there's no buff on respawn.

---

## ExploreRadius

Multiplies the radius around you that clears fog of war on the map and minimap.

### Config (`[ExploreRadius]`)

| Key | Default | Meaning |
|---|---|---|
| Enabled | `true` | Feature toggle. `false` = vanilla radius. |
| Multiplier | `2.0` | Scale on the vanilla reveal radius (range 0.1 to 10). |

### How it works

`Minimap.UpdateExplore` calls `Explore(player.position, m_exploreRadius)` every `m_exploreInterval` (2 s). That clears every fog pixel within the radius and marks it in `m_explored`, which is saved per character. A Postfix on `Minimap.Awake` captures the loaded `m_exploreRadius`, which is 100 m by the C# default but can be overridden by the prefab. A Prefix on `UpdateExplore` then sets it to that value times `Multiplier`. Already-explored areas and cartography-table sharing are untouched.

### Test checklist

1. Walk into unexplored land. The cleared circle on the minimap is visibly larger than vanilla.
2. Change `Multiplier` and restart. The radius scales accordingly.
3. With `Enabled = false`, the reveal radius is vanilla.

---

## FasterResting

Higher comfort shortens how long you have to be Resting before Rested kicks in.

| Comfort | Resting time |
|---|---|
| 0 to 4 | 20 s (vanilla) |
| 5 to 9 | 16 s |
| 10 to 14 | 12 s |
| 15 to 19 | 8 s |
| 20+ | 4 s |

### Config (`[FasterResting]`)

| Key | Default | Meaning |
|---|---|---|
| Enabled | `true` | Feature toggle. `false` = vanilla 20 s. |
| ComfortPerTier | `5` | Comfort levels per tier. |
| SecondsPerTier | `4` | Seconds removed from the vanilla delay per tier. |
| MinimumSeconds | `4` | Floor for the delay. |

The delay is `max(MinimumSeconds, vanilla - floor(comfort / ComfortPerTier) * SecondsPerTier)`.

### How it works

`Resting` is an `SE_Cozy`. Its `UpdateStatusEffect` adds `Rested` once `m_time > m_delay`, and `m_delay` is 20 in the game data. `SEMan` clones each status effect per character, so a Prefix on `SE_Cozy.UpdateStatusEffect` sets the clone's `m_delay` from `Player.GetComfortLevel()`. The vanilla value is read from the untouched `ObjectDB` template, so a game update that changes it carries through. Comfort refreshes every 2 s, so if comfort rises mid-rest, the new delay applies within a couple of seconds.

### Test checklist

1. At comfort 4 or less, Rested arrives 20 s after "You are resting".
2. At comfort 10 to 14, it arrives after about 12 s.
3. At comfort 20 or more, it arrives after about 4 s.
4. With `Enabled = false`, it's always 20 s.

---

## FasterMultiCraft

A 5x craft (holding AltPlace or the left stick) takes 3 s instead of vanilla's 6 s. A 1x craft stays at 2 s. Crafting skill still cuts both by up to 60%, so at skill 100 a 5x craft takes 1.2 s.

### Config (`[FasterMultiCraft]`)

| Key | Default | Meaning |
|---|---|---|
| Enabled | `true` | Feature toggle. `false` = vanilla 6 s. |
| MultiCraftSeconds | `3` | Base seconds for a 5x craft, before the skill reduction. |

### How it works

`InventoryGui.UpdateRecipe` computes the craft time each frame as `m_multiCrafting ? m_multiCraftDuration : m_craftDuration`, then multiplies by `1 - skillFactor * m_craftDurationSkillMaxDecrease` (0.6). A Postfix on `InventoryGui.Awake` captures the loaded `m_multiCraftDuration`, and a Prefix on `UpdateRecipe` sets it to `MultiCraftSeconds`. Upgrades and 1x crafts are untouched.

### Test checklist

1. A 5x craft at low skill fills the progress bar in about 3 s.
2. A 1x craft still takes about 2 s.
3. With `Enabled = false`, a 5x craft takes about 6 s.
