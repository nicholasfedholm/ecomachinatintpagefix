# Eco Machina Tint Page Fix 0.2.0

Client-side Harmony compat mod for Vintage Story 1.22.7 and Eco Machina (vsecomachina 0.6.1).

## What it does
Eco Machina's own tinted branch billboard textures are inserted into the live block atlas after it is created (BlockTexturesLoaded), can land on atlas page 1, and render black (the billboard bucket is keyed by atlasTextureId and routed to a mesh pool slot). 0.2.0 replaces them with pre-baked textures that are registered in the block atlas at startup, so they are packed on page 0 with everything else.

## How baked textures reach the atlas (startup registration)
- ModSystem.AssetsLoaded (client) runs from ClientSystemStartup.AfterAssetsLoaded (VintagestoryLib ClientSystemStartup.cs:307-310, modHandler.OnAssetsLoaded()). The next lines there start the block texture collection thread (StartLoadingBlockShapesWhenReady, :317); that thread only starts once both prerequisites (Step1 and AfterAssetsLoaded) have incremented the counter (:395-401), so AssetsLoaded always precedes collection.
- Collection thread: loadBlockAtlasManagerAsync (:458-470) -> BlockTextureAtlasManager.CollectTextures -> PopulateTextureAtlassesFromTextures (TextureAtlasManager.cs:484-505, packs every textureNamesDict entry) -> FinaliseTextureAtlas (:481). 
- capi.BlockTextureAtlas is the BlockTextureAtlasManager, which implements the public ITextureLocationDictionary (TextureAtlasManager.cs:16, AddTextureLocation :183-191). We call AddTextureLocation(ecomachinatintpagefix:branch/<domain>/<wood>-<N>) for every PNG found under our assets textures/branch/. No Harmony needed for registration; no block JSON is touched.
- Atlas key = asset path without "textures/" and ".png"; file is loaded by LoadCompositeBitmap via ToTextureAssetLocation (:686-693).
- Rejected: GetOrInsertTexture in BlockTexturesLoaded (live atlas, the bug); ModSystem.AssetsFinalize (OnLevelFinalize, after atlas); Block.OnCollectTextures (needs per-block Harmony).

## Lookup (TryGetTintedAtlasPosition postfix)
For (domain, woodType, variantIndex) EM passes a 0-based variantIndex (cache key variantIndex0, connector N=variantIndex+1). Baked mode looks up ecomachinatintpagefix:branch/<domain>/<woodType>-<variantIndex+1> in the block atlas; if present it sets position and result=true and logs once `[tintpagefix] baked <domain>:<wood> v<i> page=<n> texId=<id>`. Otherwise it falls back to untint behavior (atlasNumber > 0 reports false so EM uses the untinted connector).

## Modes
Config file ecomachinatintpagefix.json (ModConfig folder), created on first run. NOTE: a file written by 0.1.0 holds Mode "untint" and keeps winning; set it to "baked" or delete the file.
- "baked" (default): baked textures, falling back to untint.
- "untint": 0.1.0 behavior only.
- "logonly": diagnostics only, no behavior change.
- MaxLogLines (default 200): cap on log lines from this mod.

## Adding a wood
Bake (or hand-make) six 64x64 PNGs and drop them at
src/assets/ecomachinatintpagefix/textures/branch/<domain>/<woodType>-1.png .. -6.png
(domain lowercase, woodType as in EM's bark name). No code change.

## Bake script
tools/bake_branches.py (run with `python -I`; default wood sleepwillow, bark from test-mod/sleepwillowtest-0.1.12.zip in the sleep-willow design folder). Reproduces EM's BarkColorExtractor/TextureTinter/HsvUtils math with the shipped ecomachina.json values (GradientBlend 0.7, SaturationMul 1.0, ValueMul 1.0). Computed in double precision, EM uses float, so pixel values may differ by 1 level.
Sleepwillow shift: hue -0.0688, sat x0.0552, val x3.2933.

## Stopgap
Remove this mod once Eco Machina fixes page-1 tinted billboards.

## Log tags to grep
- `[tintpagefix] registered`, `[tintpagefix] baked`, `[tintpagefix] untinted`
- `[tintpagefix] billboard route`, `[tintpagefix] block atlas page`
- `[tintpagefix] patched` and `[tintpagefix]` warnings (missing targets)

## Status

- Untint/fallback mode: verified in game. It removes the black branch planes, and branches show EM's untinted brown.
- Baked mode: experimental and NOT yet working in game. The textures register, but the game still showed the brown fallback.
