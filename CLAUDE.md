# Jakeheim

Personal client-side BepInEx/Harmony QoL mod for Valheim. One file per feature in `Jakeheim/Features/`, each with its own config section and `Enabled` toggle, bound from `JakeheimPlugin.Awake`.

## README.md must stay in sync

`README.md` is the canonical list of features. Any change that adds, removes, renames or changes the behavior or config of a feature must update `README.md` in the same change:

- the **Features** table at the top: one row per file in `Jakeheim/Features/`, in the order they're bound in `JakeheimPlugin.Awake`;
- that feature's own section: behavior, config table (keys, defaults, meaning), how it works (patched methods and fields), and a test checklist.

Before finishing any feature work, check that the Features table matches `ls Jakeheim/Features/`.

## Working notes

- Verify game internals against decompiled source in `decompiled/` (gitignored). Never commit game DLLs or decompiled source.
- Build from `Jakeheim/` with `dotnet build -c Release`. The SDK lives in `~/.dotnet`. The build copies the DLL into the game's `BepInEx/plugins/Jakeheim/`.
- Game data values such as prefab and status-effect fields are in the asset bundles, not the code. They were read with UnityPy, kept in the session scratchpad rather than in this repo.
