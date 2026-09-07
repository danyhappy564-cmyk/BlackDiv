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
// an earlier version of this file (add-only) predictably changed nothing.
//
// WHY UPSTREAM'S 4.1 SAIN WORK DOES NOT REPLACE THIS. Upstream 1.3.x added
// Server/SAIN/BlackDivSainRegistrations.cs, which registers the six BD roles with
// MoreBotsAPI's SainInteropRegistration. That is the right thing to do and this patch
// leaves it alone, but it does not close the gap:
//
//   - The layer list was never the gap. MoreBotsAPI prepends its own
//     commonVanillaLayersToRemove (Help, AdvAssaultTarget, Hit, Simple Target, Pmc,
//     AssaultHaveEnemy, Assault Building, Enemy Building, PushAndSup, Pursuit) to
//     whatever a registration adds, so Pmc/AdvAssaultTarget/AssaultHaveEnemy are already
//     in the request. VanillaLayersToExclude below is that same 16-name union, kept in
//     sync deliberately.
//   - TIMING is the gap. MoreBotsAPI applies all of it from TarkovInitPatch, a postfix on
//     TarkovApplication.Init. SAIN's own BigBrainHandler init runs afterwards and rebuilds
//     the exclusions, dropping the removal on the floor. Re-applying at raid start
//     (GameWorld.OnGameStarted) is the whole point of this file.
//   - MoreBotsAPI 2.1.1's interop is itself partly disabled - its own 4.1 commit is
//     titled "4.1 update (minus SAIN interop being broken AF)", and inside
//     SAINInterop.CreateCustomBotTypes both BotTypeDefinitions.AddBotType and
//     AddBotTypeToSettings are commented out.
//
// API CHECK FOR 4.1: both calls below still exist. SAIN 4.5.1's own
// BigBrainHandler.ToggleVanillaLayers reaches BrainManager.RemoveLayers(layerNames,
// brainNames, roles) - the same three-argument, role-scoped overload used here - and
// MoreBotsAPI 2.1.1 calls AddCustomLayersToBrainsAndRoles with this signature.
//
// CONFIRMED IN A LIVE RAID (4.0.10) after this fix: blackDivIb and bossWedge both report
// activeLayer 'SAIN : Combat Layer' and 'SAIN : Avoid Threat', Icebreaker's own layers
// (IceCrewRush / IceCrewHold / WedgeRooms) still take their turns alongside, the ExUsec
// brain shows zero exclusions from us, and PersonActiveClass NREs went from ~4000 to 0.
//
// ON SCOPE, CORRECTED. An earlier attempt (SainLayerReassertPatch) passed brains
// ["PMC", "ExUsec"] and left real Rogues trailing each other in a frozen clump on
// PatrolFollower. That was originally written up here as "it stripped the ExUsec brain",
// but that explanation does not survive re-reading the commit: it passed the same six BD
// roles this file does, so the removal was role-scoped and real Rogues (role exUsec)
// should not have matched. It also differed by going through SAIN's
// ToggleVanillaLayersForBrainsAndRoles wrapper, which additionally calls RestoreLayers.
// Which of the two differences broke Rogues was never isolated. What IS established is
// that the configuration below - brain "PMC" only, BrainManager.RemoveLayers directly -
// was verified in a raid with Rogues behaving normally, and the registry dump confirmed
// BD/Wedge bots report brain 'PMC', so "PMC" alone covers them with nothing to gain from
// widening it. Upstream's registration lists both brains, which is fine there: MoreBotsAPI
// applies it per-role, so its ExUsec entry only ever touches BD roles.
internal class SainBrainLayerPatch : ModulePatch
{
    private const string SainGuid = "me.sol.sain";

    // The registry dump has BD/Wedge bots reporting the literal "PMC" brain, so this
    // covers them. See the scope note above for why it is not widened to "ExUsec".
    private static readonly List<string> Brains = new List<string> { "PMC" };

    private static readonly List<WildSpawnType> Roles = new List<int>
    {
        848420, 848421, 848422, 848423, 848424, 848426,
    }.ConvertAll(x => (WildSpawnType)x);

    protected override MethodBase GetTargetMethod()
    {
        return typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted), BindingFlags.Public | BindingFlags.Instance);
    }

    // The vanilla layers SAIN expects to be out of the way before its own can run. This
    // is the exact union MoreBotsAPI builds: its commonVanillaLayersToRemove (first ten)
    // plus the LayersToRemove that Server/SAIN/BlackDivSainRegistrations.cs registers
    // (last six). Keep the two in sync if either side changes. The registry dump caught
    // "Pmc", "AdvAssaultTarget" and "AssaultHaveEnemy" from this list actually holding
    // our bots.
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
            // Brain "PMC" only, and RemoveLayers directly rather than through SAIN's
            // Toggle wrapper: that exact combination is the one verified in a raid with
            // Rogues behaving normally. The scope note at the top of this file explains
            // what the earlier, Rogue-breaking attempt actually differed by.
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
