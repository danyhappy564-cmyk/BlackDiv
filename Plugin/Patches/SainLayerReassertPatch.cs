using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using SPT.Reflection.Patching;

namespace BlackDiv.Patches;

// MoreBotsAPI's SAINInterop.AddSAINLayers() is supposed to give SAIN's own combat
// layers to our "PMC"/"ExUsec" brains for the Black Division roles - same idea as its
// CreateCustomBotTypes() call, which is confirmed working (Black Division shows up
// fine, fully editable, in SAIN's own F6 bot settings menu). But it only runs once,
// at TarkovApplication.Init (long before any raid, at game boot), and a live field
// check (BigBrain's own debug overlay, 2026-09) showed BD/Wedge bots sitting on the
// literal vanilla "Pmc" layer mid-fight (Node: shootFromPlace/dogFight/holdPosition -
// vanilla EFT bot AI state names, not SAIN's) despite that call including "Pmc" in the
// vanilla-layers-to-remove list. Whatever's racing (SAIN's own PMC-brain layer setup
// vs. MoreBotsAPI's one-shot boot-time hook, BigBrain layer-list caching, etc.) isn't
// provable without BigBrain's source - so instead of guessing further, just redo the
// exact same registration again at actual raid start, when SAIN's own setup is
// unquestionably long finished. Safe to repeat every raid: these are list-based
// BigBrain calls (add/remove-by-name), not the Dictionary.Add SAIN uses for bot type
// settings - no duplicate-key crash risk like a naive redo of CreateCustomBotTypes()
// would have.
internal class SainLayerReassertPatch : ModulePatch
{
    private const string SainGuid = "me.sol.sain";

    private static readonly List<string> Brains = new List<string> { "PMC", "ExUsec" };

    private static readonly List<WildSpawnType> Roles = new List<int>
    {
        848420, 848421, 848422, 848423, 848424, 848426,
    }.ConvertAll(x => (WildSpawnType)x);

    // same list MoreBotsAPI's SAINInterop.AddSAINLayers() uses: its own
    // commonVanillaLayersToRemove plus BlackDiv's CustomTypesPatch LayersToRemove.
    private static readonly List<string> VanillaLayersToRemove = new List<string>
    {
        "Help", "AdvAssaultTarget", "Hit", "Simple Target", "Pmc", "AssaultHaveEnemy",
        "Assault Building", "Enemy Building", "PushAndSup", "Pursuit",
        "Request", "KnightFight", "PmcBear", "PmcUsec", "ExURequest", "StationaryWS",
    };

    protected override MethodBase GetTargetMethod()
    {
        return typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted), BindingFlags.Public | BindingFlags.Instance);
    }

    [PatchPostfix]
    protected static void PatchPostfix()
    {
        if (!Chainloader.PluginInfos.ContainsKey(SainGuid)) return;

        try
        {
            SAIN.BigBrainHandler.BrainAssignment.AddCustomLayersToBrainsAndRoles(Brains, Roles, false);
            SAIN.BigBrainHandler.BrainAssignment.ToggleVanillaLayersForBrainsAndRoles(Brains, Roles, VanillaLayersToRemove, false);
            Plugin.LogSource.LogInfo("[SainLayerFix] re-asserted SAIN combat layers for Black Division/Wedge at raid start");
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning($"[SainLayerFix] failed: {e.Message}");
        }
    }
}
