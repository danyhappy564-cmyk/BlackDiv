using System.Collections.Generic;
using System.Reflection;
using EFT;
using SAIN;
using SPT.Reflection.Patching;

namespace BlackDiv.Patches;

// TEMPORARY DIAGNOSTIC - remove once the "SAIN registers Black Division/Wedge but never
// actually takes over combat" investigation concludes. Two prior attempts already ruled
// out a BigBrain layer-registration timing race: re-asserting
// BigBrainHandler.BrainAssignment.AddCustomLayersToBrainsAndRoles/
// ToggleVanillaLayersForBrainsAndRoles at raid start (SainLayerReassertPatch) had zero
// effect - BigBrain's own debug overlay still showed BD/Wedge stuck on the vanilla "Pmc"
// layer mid-fight, on Icebreaker AND on Lab both (map-agnostic, rules out anything
// Icebreaker-specific).
//
// The next candidate is upstream of BigBrain entirely: SAIN decides per-bot, at spawn,
// whether to attach its own BotComponent at all
// (SAINEnableClass.IsSAINDisabledForBot <- BotSpawnController.AddBot). If that says
// "disabled" for our WildSpawnTypes, no amount of BigBrain layer registration would ever
// matter - the bot never gets a SAIN brain to run those layers on in the first place.
// IsSAINDisabledForBot -> IsBotExcluded -> IsAlwaysEnabled (OR's in
// WildSpawnType.IsPmcBot(), an SPT-common extension method with no available source to
// read - exactly the kind of thing that needs a live value, not another guess) plus
// ShallExludeByWildSpawnType. These three postfixes trace that whole decision for a live
// Black Division/Wedge bot the moment it spawns.
internal static class SainEnableDiag
{
    internal static readonly HashSet<int> BdTypes = new HashSet<int>
    {
        848420, 848421, 848422, 848423, 848424, 848426,
    };

    internal static bool IsBd(WildSpawnType type) => BdTypes.Contains((int)type);
}

internal class SainIsDisabledDiagPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(SAINEnableClass).GetMethod(
            nameof(SAINEnableClass.IsSAINDisabledForBot),
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(BotOwner) },
            null);
    }

    [PatchPostfix]
    private static void PatchPostfix(BotOwner botOwner, ref bool __result)
    {
        var role = botOwner?.Profile?.Info?.Settings?.Role;
        if (role == null || !SainEnableDiag.IsBd(role.Value)) return;
        Plugin.LogSource.LogWarning($"[SainEnableDiag] {role} IsSAINDisabledForBot={__result}");
    }
}

internal class SainAlwaysEnabledDiagPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(SAINEnableClass).GetMethod("IsAlwaysEnabled", BindingFlags.NonPublic | BindingFlags.Static);
    }

    [PatchPostfix]
    private static void PatchPostfix(WildSpawnType wildSpawnType, BotOwner botOwner, ref bool __result)
    {
        if (!SainEnableDiag.IsBd(wildSpawnType)) return;
        Plugin.LogSource.LogWarning($"[SainEnableDiag] IsAlwaysEnabled({wildSpawnType})={__result}");
    }
}

internal class SainExcludeByTypeDiagPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(SAINEnableClass).GetMethod(
            nameof(SAINEnableClass.ShallExludeByWildSpawnType),
            BindingFlags.Public | BindingFlags.Static);
    }

    [PatchPostfix]
    private static void PatchPostfix(WildSpawnType wildSpawnType, BotOwner botOwner, ref bool __result)
    {
        if (!SainEnableDiag.IsBd(wildSpawnType)) return;
        Plugin.LogSource.LogWarning($"[SainEnableDiag] ShallExludeByWildSpawnType({wildSpawnType})={__result}");
    }
}
