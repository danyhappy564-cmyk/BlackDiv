using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BlackDiv.Patches;
using System;
using System.Collections.Generic;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using MoreBotsAPI.Behavior.Layers;
using MoreBotsAPI.Components;

namespace BlackDiv
{
    [BepInDependency("xyz.drakia.bigbrain")]
    [BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.morebotsapi.tacticaltoaster")]
    [BepInPlugin(ClientInfo.GUID, ClientInfo.PluginName, ClientInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;

        // BaseUnityPlugin inherits MonoBehaviour, so you can use base unity functions like Awake() and Update()
        private void Awake()
        {
            // save the Logger to variable so we can use it elsewhere in the project
            LogSource = Logger;

            new TarkovInitPatch().Enable();
            //new BotOwnerActivatePatch().Enable();
            //new BotsControllerInitPatch().Enable();
            new BDNvgPatch().Enable();
            // REGRESSION (2026-09, field-confirmed): this removes vanilla combat layers
            // ("Pmc" included) for BD/Wedge roles to make room for SAIN, but SAIN never
            // actually takes over for them (root cause still under investigation via the
            // diag patches below) - so bots were left with NEITHER working AI and fell
            // back to the native boss-escort "hold near boss" formation logic, which
            // reads as a pack of bots frozen together in one spot for the whole raid.
            // Disabled until the real SAIN-not-taking-over cause is found and fixed;
            // vanilla-only combat (however weaker) is strictly better than stuck bots.
            //new SainLayerReassertPatch().Enable();
            new SainIsDisabledDiagPatch().Enable();
            new SainAlwaysEnabledDiagPatch().Enable();
            new SainExcludeByTypeDiagPatch().Enable();

            var bdEnums = new List<int> { 848420, 848421, 848422, 848423, 848424, 848426 }
                .ConvertAll(x => (WildSpawnType)x);
            
            MonoBehaviourSingleton<HuntManager>.Instance.AddHuntRoles(bdEnums, [WildSpawnType.pmcUSEC, WildSpawnType.pmcBEAR]);
            
            MonoBehaviourSingleton<HuntManager>.Instance.AddHuntSides(bdEnums, new List<EPlayerSide>()
            { 
                EPlayerSide.Usec,
                EPlayerSide.Bear,
            });
            
            var brainList = new List<string>() { "PMC", "ExUsec", "Assault", "PmcUsec", "PmcBear", "PmcUSEC", "PmcBEAR" };
            var typesList = new List<int>() { 848420, 848421, 848422, 848423, 848424, 848426 }.ConvertAll(x => (WildSpawnType)x);

            BrainManager.AddCustomLayer(typeof(HuntTargetLayer), brainList, 10, typesList);
            BrainManager.RemoveLayers(["AdvAssaultTarget"], brainList, typesList);
        }
    }
}
