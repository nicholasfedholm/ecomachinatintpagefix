using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace EcoMachinaTintPageFix
{
    public class TintPageFixConfig
    {
        // "baked" (default), "untint" or "logonly"
        public string Mode = "baked";
        public int MaxLogLines = 200;
        // Diagnostic only, default off: 30 s after join, ask Eco Machina for lopcore:sleepwillow v0..v5 and log the result.
        public bool SelfTest = false;
    }

    public class EcoMachinaTintPageFixSystem : ModSystem
    {
        public const string HarmonyId = "ecomachinatintpagefix";
        const string RenderTypeName = "vsecomachina.Integration.Rendering.RenderOnlyTaperedWoodPatches";
        const string TintTypeName = "vsecomachina.Integration.Rendering.BranchColorTintingSystem";

        static ILogger log;
        static TintPageFixConfig cfg = new TintPageFixConfig();
        static bool untint;
        static bool baked;
        static ICoreClientAPI staticApi;
        const string BakedPrefix = "textures/branch/";
        static int logLines;
        static int failedOnce;
        static readonly ConcurrentDictionary<string, byte> seen = new ConcurrentDictionary<string, byte>();

        [ThreadStatic] static bool inBillboard;

        Harmony harmony;
        ICoreClientAPI capi;
        long tickId;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        // 0.2.1: the config must be read before AssetsLoaded. On the client, AssetsLoaded runs before
        // StartClientSide (0.2.0 read the config in StartClientSide, so AssetsLoaded always saw baked=false
        // and silently skipped registration). Called from StartPre, AssetsLoaded and StartClientSide; loads once.
        static bool cfgLoaded;
        static void EnsureConfig(ICoreAPI api)
        {
            if (log == null) log = api.Logger;
            if (cfgLoaded) return;
            cfgLoaded = true;
            try
            {
                var loaded = api.LoadModConfig<TintPageFixConfig>("ecomachinatintpagefix.json");
                if (loaded == null)
                {
                    loaded = new TintPageFixConfig();
                    api.StoreModConfig(loaded, "ecomachinatintpagefix.json");
                }
                cfg = loaded;
            }
            catch (Exception e)
            {
                log.Warning("[tintpagefix] config load failed, using defaults: {0}", e.Message);
            }
            baked = string.Equals(cfg.Mode, "baked", StringComparison.OrdinalIgnoreCase);
            untint = string.Equals(cfg.Mode, "untint", StringComparison.OrdinalIgnoreCase);
        }

        static ICoreAPI preApi;
        static Harmony collectHarmony;

        // 0.2.2: StartPre runs during "pre-starting client side mods", before the block texture
        // collection thread. Patch BlockTextureAtlasManager.CollectTextures so our baked textures are
        // added to the very atlas instance that is about to be packed (AssetsLoaded runs before
        // CreateNewAtlas("blocks") and proved unreliable in 0.2.0/0.2.1).
        public override void StartPre(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Client) return;
            EnsureConfig(api);
            preApi = api;
            if (!baked) { Line("[tintpagefix] StartPre: mode={0}, baked registration not installed.", untint ? "untint" : "logonly"); return; }
            try
            {
                Type t = AccessTools.TypeByName("Vintagestory.Client.NoObf.BlockTextureAtlasManager") ?? AccessTools.TypeByName("BlockTextureAtlasManager");
                MethodInfo m = t == null ? null : AccessTools.Method(t, "CollectTextures");
                if (m == null) { log.Warning("[tintpagefix] BlockTextureAtlasManager.CollectTextures not found; baked textures not registered."); return; }
                collectHarmony = new Harmony(HarmonyId + ".collect");
                collectHarmony.Patch(m, prefix: new HarmonyMethod(typeof(EcoMachinaTintPageFixSystem), nameof(CollectPrefix)));
                Line("[tintpagefix] StartPre: patched {0}.CollectTextures for baked registration.", t.Name);
            }
            catch (Exception e) { log.Warning("[tintpagefix] CollectTextures patch failed: {0}", e); }
        }

        public static void CollectPrefix(object __instance)
        {
            try
            {
                var dict = __instance as ITextureLocationDictionary;
                if (dict == null || preApi == null) { log?.Warning("[tintpagefix] CollectPrefix: atlas not an ITextureLocationDictionary; nothing registered."); return; }
                int added = 0, found = 0;
                foreach (AssetLocation loc in preApi.Assets.GetLocations(BakedPrefix, HarmonyId))
                {
                    string path = loc.Path;
                    if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    found++;
                    string key = path.Substring("textures/".Length, path.Length - "textures/".Length - 4);
                    if (dict.AddTextureLocation(new AssetLocationAndSource(new AssetLocation(HarmonyId, key)))) added++;
                }
                Line("[tintpagefix] registered {0} of {1} baked branch texture(s) in block atlas (CollectTextures prefix).", added, found);
                if (added == 0) log.Warning("[tintpagefix] baked mode but 0 baked textures registered; tinted page-1 branches will fall back to untinted.");
            }
            catch (Exception e) { log?.Warning("[tintpagefix] baked texture registration failed: {0}", e); }
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            staticApi = api;
            EnsureConfig(api);
            Line("[tintpagefix] mode={0} maxLogLines={1}", baked ? "baked" : untint ? "untint" : "logonly", cfg.MaxLogLines);

            harmony = new Harmony(HarmonyId);
            int ok = 0;

            Type tint = AccessTools.TypeByName(TintTypeName);
            Type render = AccessTools.TypeByName(RenderTypeName);

            if (untint || baked)
            {
                ok += Patch(api, tint, "TryGetTintedAtlasPosition", null, nameof(TintPostfix), TintTypeName);
            }
            ok += Patch(api, render, "TryInjectBillboardBucket", nameof(BucketPrefix), nameof(BucketPostfix), RenderTypeName);
            ok += Patch(api, render, "TryResolveMeshPoolIndexForAtlasTextureId", null, nameof(ResolvePostfix), RenderTypeName);
            log.Notification("[tintpagefix] patched {0} method(s).", ok);

            tickId = api.Event.RegisterGameTickListener(AtlasTick, 1000);
        }

        // Runs before the block atlas texture collection thread starts (see README): register our
        // pre-baked branch textures into the block atlas texture list so they are packed at atlas creation.
        int Patch(ICoreClientAPI api, Type t, string name, string prefix, string postfix, string typeName)
        {
            try
            {
                if (t == null)
                {
                    api.Logger.Warning("[tintpagefix] type {0} not found; skipped patch {1}.", typeName, name);
                    return 0;
                }
                MethodInfo original = AccessTools.Method(t, name);
                if (original == null)
                {
                    api.Logger.Warning("[tintpagefix] method {0}.{1} not found; patch skipped.", typeName, name);
                    return 0;
                }
                var self = typeof(EcoMachinaTintPageFixSystem);
                HarmonyMethod pre = prefix == null ? null : new HarmonyMethod(AccessTools.Method(self, prefix));
                HarmonyMethod post = postfix == null ? null : new HarmonyMethod(AccessTools.Method(self, postfix));
                harmony.Patch(original, pre, post);
                return 1;
            }
            catch (Exception e)
            {
                api.Logger.Warning("[tintpagefix] patch {0} failed: {1}", name, e);
                return 0;
            }
        }

        void AtlasTick(float dt)
        {
            try
            {
                var atlas = capi?.BlockTextureAtlas;
                if (atlas == null || atlas.AtlasTextures == null || atlas.AtlasTextures.Count == 0) return;
                capi.Event.UnregisterGameTickListener(tickId);
                Line("[tintpagefix] block atlas pages={0}", atlas.AtlasTextures.Count);
                for (int i = 0; i < atlas.AtlasTextures.Count; i++)
                {
                    Line("[tintpagefix] block atlas page {0} texId={1}", i, atlas.AtlasTextures[i].TextureId);
                }
                if (cfg.SelfTest) capi.Event.RegisterCallback(_ => SelfTest(), 30000);
            }
            catch (Exception e)
            {
                Fail("AtlasTick", e);
            }
        }

        // Opt-in diagnostic: calls EM's TryGetTintedAtlasPosition (through any of our patches) for the willow.
        void SelfTest()
        {
            try
            {
                string mode = baked ? "baked" : untint ? "untint" : "logonly";
                Type t = AccessTools.TypeByName(TintTypeName);
                MethodInfo m = t == null ? null : AccessTools.Method(t, "TryGetTintedAtlasPosition");
                if (m == null) { Line("[tintpagefix] selftest: {0}.TryGetTintedAtlasPosition not found.", TintTypeName); return; }
                object inst = null;
                if (!m.IsStatic)
                {
                    // EM keeps the instance the renderer uses in FoliageBillboardMesher.tintingSystem (static),
                    // and another reference in EcoMachinaModSystem.branchColorTintingSystem.
                    Type mesher = AccessTools.TypeByName("vsecomachina.Integration.TaperedWood.FoliageBillboardMesher");
                    FieldInfo sf = mesher == null ? null : AccessTools.Field(mesher, "tintingSystem");
                    if (sf != null && sf.IsStatic) inst = sf.GetValue(null);
                    string src = "FoliageBillboardMesher.tintingSystem";
                    if (inst == null)
                    {
                        src = "EcoMachinaModSystem.branchColorTintingSystem";
                        foreach (var sys in capi.ModLoader.Systems)
                        {
                            FieldInfo f = AccessTools.Field(sys.GetType(), "branchColorTintingSystem");
                            if (f != null && !f.IsStatic) { inst = f.GetValue(sys); if (inst != null) break; }
                        }
                    }
                    if (inst == null) { Line("[tintpagefix] selftest: no {0} instance found.", t.Name); return; }
                    Line("[tintpagefix] selftest: using {0}.", src);
                }
                var ps = m.GetParameters();
                for (int v = 0; v < 6; v++)
                {
                    object[] args = new object[ps.Length];
                    for (int i = 0; i < ps.Length; i++)
                    {
                        Type pt = ps[i].ParameterType;
                        if (ps[i].Name == "domain") args[i] = "lopcore";
                        else if (ps[i].Name == "woodType") args[i] = "sleepwillow";
                        else if (ps[i].Name == "variantIndex") args[i] = v;
                        else if (!pt.IsByRef && pt.IsValueType) args[i] = Activator.CreateInstance(pt);
                    }
                    object r = m.Invoke(inst, args);
                    TextureAtlasPosition pos = null;
                    foreach (object o in args) if (o is TextureAtlasPosition tp) pos = tp;
                    Line("[tintpagefix] selftest mode={0} lopcore:sleepwillow v{1}: result={2} page={3} texId={4}",
                        mode, v, r, pos == null ? "null" : pos.atlasNumber.ToString(), pos == null ? "null" : pos.atlasTextureId.ToString());
                }
            }
            catch (Exception e)
            {
                log?.Warning("[tintpagefix] selftest failed: {0}", e);
            }
        }

        // Thread-safe, capped logging.
        static void Line(string fmt, params object[] args)
        {
            if (log == null) return;
            if (Interlocked.Increment(ref logLines) > cfg.MaxLogLines) return;
            log.Notification(fmt, args);
        }

        static void LineOnce(string key, string fmt, params object[] args)
        {
            if (seen.TryAdd(key, 0)) Line(fmt, args);
        }

        static void Fail(string where, Exception e)
        {
            if (Interlocked.Exchange(ref failedOnce, 1) == 0)
                log?.Warning("[tintpagefix] patch error in {0} (logged once): {1}", where, e);
        }

        // Patch 1: fall back to untinted texture when the tinted entry is on a non-zero atlas page.
        // Dotted note: param names "domain", "woodType", "variantIndex", "position" match the target method.
        static void TintPostfix(string domain, string woodType, int variantIndex, ref TextureAtlasPosition position, ref bool __result)
        {
            try
            {
                if (baked && !string.IsNullOrEmpty(domain) && !string.IsNullOrEmpty(woodType))
                {
                    // EM variantIndex is 0-based (connector em_connector_<variantIndex+1>); our files are <wood>-<variantIndex+1>.
                    var loc = new AssetLocation(HarmonyId, "branch/" + domain.ToLowerInvariant() + "/" + woodType + "-" + (variantIndex + 1));
                    TextureAtlasPosition bp = staticApi?.BlockTextureAtlas[loc];
                    if (bp != null)
                    {
                        LineOnce("baked:" + loc, "[tintpagefix] baked {0}:{1} v{2} page={3} texId={4}",
                            domain, woodType, variantIndex, bp.atlasNumber, bp.atlasTextureId);
                        position = bp;
                        __result = true;
                        return;
                    }
                }
                if (!__result || position == null || position.atlasNumber <= 0) return;
                LineOnce("untint:" + domain + ":" + woodType + ":" + variantIndex,
                    "[tintpagefix] untinted {0}:{1} v{2} atlasNumber={3} atlasTextureId={4}",
                    domain, woodType, variantIndex, position.atlasNumber, position.atlasTextureId);
                position = null;
                __result = false;
            }
            catch (Exception e)
            {
                Fail("TintPostfix", e);
            }
        }

        // Patch 2: diagnostics for the billboard route.
        static void BucketPrefix() { inBillboard = true; }
        static void BucketPostfix() { inBillboard = false; }

        static void ResolvePostfix(MeshData[] meshPool, int atlasTextureId, bool __result, int poolIndex)
        {
            try
            {
                if (!inBillboard) return;
                int len = meshPool == null ? -1 : meshPool.Length;
                LineOnce("route:" + atlasTextureId + ":" + len + ":" + __result + ":" + poolIndex,
                    "[tintpagefix] billboard route texId={0} poolLen={1} ok={2} pool={3}",
                    atlasTextureId, len, __result, poolIndex);
            }
            catch (Exception e)
            {
                Fail("ResolvePostfix", e);
            }
        }

        public override void Dispose()
        {
            try { harmony?.UnpatchAll(HarmonyId); } catch { }
            harmony = null;
        }
    }
}
