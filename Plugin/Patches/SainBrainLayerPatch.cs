using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SPT.Reflection.Patching;

namespace BlackDiv.Patches;

// WHY BLACK DIVISION NEVER GETS SAIN, established from a dump of BigBrain's own registry
// rather than guesswork (two earlier theories died here, both wrong).
//
// Not a registration problem. The dump showed SAIN's layers already registered for all
// six BD roles on the "PMC" brain - MoreBotsAPI's SAINInterop.AddSAINLayers() had been
// doing its job the whole time - and a live probe showed a SAIN BotComponent attached to
// every BD bot. Both halves were fine.
//
// It is a PRIORITY problem:
//
//   SAIN.Layers.Combat.Solo.CombatSoloLayer    prio 20
//   SAIN.Layers.Combat.Squad.CombatSquadLayer  prio 22
//   vanilla Pmc / AdvAssaultTarget / AssaultHaveEnemy   far above those
//
// and those three vanilla names are exactly what the probe caught holding our bots. SAIN
// is not built to outrank vanilla - it REMOVES the vanilla layers so its own low-priority
// ones become reachable. So the removal is mandatory; an add-only patch cannot work, and
// the previous version of this file (add-only) predictably changed nothing.
//
// MoreBotsAPI asks for that removal too, at TarkovApplication.Init, but SAIN's own
// BigBrainHandler init runs afterwards and rebuilds the exclusions, dropping it. Hence
// re-applying at raid start.
//
// SCOPE IS THE SAFETY STORY. An earlier attempt at the removal passed brains
// ["PMC", "ExUsec"] and stripped the ExUsec brain - the one REAL Rogues run on - leaving
// them on PatrolFollower to trail each other around the ship in a frozen clump. The dump
// has since confirmed BD/Wedge bots report brain 'PMC', so "PMC" alone covers them, and
// Rogues (brain ExUsec, role exUsec) match neither dimension of what is touched here.
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

    // The vanilla layers SAIN expects to be out of the way before its own can run. Same
    // list MoreBotsAPI uses (its commonVanillaLayersToRemove plus BlackDiv's own
    // LayersToRemove); the registry dump caught "Pmc", "AdvAssaultTarget" and
    // "AssaultHaveEnemy" from it actually holding our bots.
    private static readonly List<string> VanillaLayersToExclude = new List<string>
    {
        "Help", "AdvAssaultTarget", "Hit", "Simple Target", "Pmc", "AssaultHaveEnemy",
        "Assault Building", "Enemy Building", "PushAndSup", "Pursuit",
        "Request", "KnightFight", "PmcBear", "PmcUsec", "ExURequest", "StationaryWS",
    };

    [PatchPostfix]
    protected static void PatchPostfix()
    {
        if (!Chainloader.PluginInfos.ContainsKey(SainGuid)) return;

        try
        {
            // withExtract: false - matches what MoreBotsAPI asks for, and keeps our bots
            // from wandering off to an exfil.
            SAIN.BigBrainHandler.BrainAssignment.AddCustomLayersToBrainsAndRoles(Brains, Roles, false);

            // The registration above was never the missing piece - a dump of BigBrain's
            // registry showed SAIN's layers already present for all six BD roles. What
            // holds the bots is priority: SAIN's combat layers sit at 20/22 while the
            // vanilla ones that keep winning (Pmc, AdvAssaultTarget, AssaultHaveEnemy)
            // sit far above them. SAIN is not built to outrank vanilla, it removes
            // vanilla so its own lower-priority layers become reachable - so this half is
            // mandatory, not optional. MoreBotsAPI asks for it too, at
            // TarkovApplication.Init, but SAIN's own BigBrainHandler init runs afterwards
            // and rebuilds the exclusions, dropping it.
            //
            // Scope is the whole safety story here. An earlier attempt passed brains
            // ["PMC", "ExUsec"] and stripped the ExUsec brain - which real Rogues run on -
            // leaving them on PatrolFollower, trailing each other in a frozen clump. The
            // dump has since confirmed BD/Wedge bots report brain 'PMC', so "PMC" alone
            // covers them, and Rogues match neither the brain nor the roles here.
            BrainManager.RemoveLayers(VanillaLayersToExclude, Brains, Roles);

            Plugin.LogSource.LogInfo(
                "[SainBrainFix] SAIN layers added and vanilla combat layers excluded for Black Division/Wedge (brain PMC only)");
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning($"[SainBrainFix] failed (SAIN/BigBrain API changed?): {e.Message}");
        }
    }
}
