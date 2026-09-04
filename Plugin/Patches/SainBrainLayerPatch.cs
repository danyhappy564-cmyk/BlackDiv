using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using SPT.Reflection.Patching;

namespace BlackDiv.Patches;

// WHY BLACK DIVISION NEVER GETS SAIN, and what this fixes.
//
// SAIN registers its combat layers per BigBrain brain name, and for the brain literally
// named "PMC" it scopes them to a role list:
//
//   AddCustomLayersToPMCs()      -> brains ["PmcBear", "PmcUsec"], no role filter
//   AddCustomLayersToRaiders()   -> brain  ["PMC"], roles [pmcBot]      <-- role-scoped
//
// Black Division registers with BaseBrain = "PMC" (BigBrain's overlay shows our bots as
// "Bot10 (PMC)") but with roles 848420-848426, which are not pmcBot - so BigBrain's role
// filter drops every SAIN layer before it is ever evaluated, and the vanilla "Pmc" layer
// keeps the bot. A real PMC on the "PmcBear" brain shows "SAIN : Combat Layer" in the
// same raid, which is the other half of the proof.
//
// MoreBotsAPI's SAINInterop.AddSAINLayers() makes exactly the right call for this, but it
// runs once at TarkovApplication.Init and does not survive - SAIN's own BigBrainHandler
// init re-registers the brain layers afterwards. So do it again at raid start.
//
// ADD-ONLY, AND DELIBERATELY SO. An earlier version of this patch also called
// ToggleVanillaLayersForBrainsAndRoles(..., useVanillaLayers: false) and listed "ExUsec"
// among the brains. Both were mistakes and they broke a live raid: "ExUsec" is the brain
// REAL Rogues run on, and the toggle call strips vanilla layers - so the Rogues lost their
// combat layers, fell back to PatrolFollower, and trailed each other around the ship in a
// frozen clump, while SAIN layers force-added to bots with no SAIN BotComponent threw
// ~4000 NullReferenceExceptions per raid inside PersonActiveClass.CheckAlive.
//
// Hence: brain "PMC" ONLY (never ExUsec), and nothing is ever removed or toggled. If the
// added layers still fail to activate, the vanilla layers are untouched and behaviour is
// identical to not having this patch at all - the failure mode is "no improvement", not
// "broken bots".
internal class SainBrainLayerPatch : ModulePatch
{
    private const string SainGuid = "me.sol.sain";

    // BD bots run the literal "PMC" brain. NOT "ExUsec" - that one belongs to the Rogues.
    private static readonly List<string> Brains = new List<string> { "PMC" };

    private static readonly List<WildSpawnType> Roles = new List<int>
    {
        848420, 848421, 848422, 848423, 848424, 848426,
    }.ConvertAll(x => (WildSpawnType)x);

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
            // withExtract: false - matches what MoreBotsAPI asks for, and keeps our bots
            // from wandering off to an exfil.
            SAIN.BigBrainHandler.BrainAssignment.AddCustomLayersToBrainsAndRoles(Brains, Roles, false);
            Plugin.LogSource.LogInfo("[SainBrainFix] SAIN combat layers added for Black Division/Wedge on the PMC brain");
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning($"[SainBrainFix] failed (SAIN API changed?): {e.Message}");
        }
    }
}
