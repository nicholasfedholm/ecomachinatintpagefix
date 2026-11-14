# Eco Machina Tint Page Fix 0.2.2

Client-side compat patch for Eco Machina (vsecomachina) 0.6.1 on Vintage Story 1.22.7.

## What it does
Eco Machina tints branch billboard textures per wood type at runtime and inserts them into the block atlas after the atlas is built. When page 0 is full, they land on block atlas page 1, and the billboard renderer draws page-1 textures black. This mod either serves pre-baked branch textures that are packed into the atlas at startup (on page 0), or falls back to EM's untinted connector texture.

## How baked textures reach the atlas (0.2.2)
- StartPre (client) loads the config. In "baked" mode it Harmony-prefixes `Vintagestory.Client.NoObf.BlockTextureAtlasManager.CollectTextures`.
- The prefix runs on the block atlas collect thread, right before the block atlas is composed, and adds every PNG under `assets/ecomachinatintpagefix/textures/branch/` to the atlas texture list. Logged as `registered N of M baked branch texture(s) ... (CollectTextures prefix)`.
- 0.2.0 and 0.2.1 tried this in `AssetsLoaded`. That never worked: in 0.2.0 the config (and so the "baked" flag) was only loaded later in StartClientSide, so the method returned silently; and AssetsLoaded runs before `CreateNewAtlas("blocks")`, so there is no block atlas to register into yet. StartClientSide runs after the atlas is built (too late).

## Lookup (TryGetTintedAtlasPosition postfix)
For each EM request (domain, woodType, variantIndex), if `ecomachinatintpagefix:branch/<domain>/<woodType>-<variantIndex+1>` is in the block atlas, return that position (`[tintpagefix] baked ...`). Otherwise, if EM's own tinted position is on a page above 0, return "not tinted" so EM uses its untinted connector (`[tintpagefix] untinted ...`).

## Modes
Config file ecomachinatintpagefix.json (ModConfig folder), created on first run. A file written by 0.1.0 holds Mode "untint" and keeps winning; set it to "baked" or delete the file.
- "baked" (default): baked textures, falling back to untint.
- "untint": untint fallback only (EM's untinted connector, dark brown for every page-1 wood).
- "logonly": diagnostics only, no behavior change (page-1 tinted branches draw black).
- MaxLogLines (default 200): cap on log lines from this mod.
- SelfTest (default false): diagnostic. 30 s after the block atlas exists, calls EM's TryGetTintedAtlasPosition for lopcore:sleepwillow v0..v5 (through this mod's patches) and logs `[tintpagefix] selftest mode=... v<i>: result=... page=... texId=...`. Leave off in play.

The mode is read once at startup; change it with the game closed.

## Adding a wood
Bake (or hand-make) six 64x64 PNGs and drop them at
src/assets/ecomachinatintpagefix/textures/branch/<domain>/<woodType>-1.png .. -6.png
(domain lowercase, woodType as in EM's bark name). No code change. Woods without baked files still get the untint fallback when they land on page 1 (seen in testing: floralzonesmediterraneanregion populusalba and taxusbaccata).

## Bake script
tools/bake_branches.py (run with `python -I`; default wood sleepwillow, bark from test-mod/sleepwillowtest-0.1.12.zip in the sleep-willow design folder; byte-identical to the LoP-Core 1.5.8 bark). Reproduces EM's BarkColorExtractor/TextureTinter/HsvUtils math with the shipped ecomachina.json values (GradientBlend 0.7, SaturationMul 1.0, ValueMul 1.0). Computed in double precision, EM uses float, so pixel values may differ slightly. Checked against EM's own page-1 output: mean difference 0.26 to 1.85 levels per variant.
Sleepwillow shift: hue -0.0688, sat x0.0552, val x3.2933.

## Stopgap
Remove this mod once Eco Machina fixes page-1 tinted billboards.

## Log tags to grep
- `[tintpagefix] StartPre`, `[tintpagefix] registered` (must appear before `Composed ... blocks texture atlases`)
- `[tintpagefix] baked`, `[tintpagefix] untinted`, `[tintpagefix] selftest`
- `[tintpagefix] billboard route`, `[tintpagefix] block atlas page`
- `[tintpagefix] patched` and `[tintpagefix]` warnings (missing targets)
