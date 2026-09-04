using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;
using UnityEngine;

namespace BlackDiv.Patches;

// READ-ONLY DIAGNOSTIC. Nothing here writes state: it reads BigBrain's registry and asks
// each live bot what it already is. No layer is added, removed or toggled.
//
// The first two dumps settled the diagnosis: BigBrain keys CustomLayers by an
// auto-incrementing id (so duplicate type registrations are kept, not dropped), SAIN's
// layers were already registered for all six BD roles on the "PMC" brain, and every BD
// bot had a SAIN BotComponent attached. What actually held them was priority - SAIN's
// combat layers sit at 20/22 while vanilla Pmc / AdvAssaultTarget / AssaultHaveEnemy sit
// far above, and SAIN's design is to REMOVE those rather than outrank them.
//
// So this now verifies the fix rather than hunting for it:
//   - PMC-brain layer registrations (type, priority, brains, roles)
//   - PMC-brain EXCLUSIONS - the vanilla names excluded, and for which roles. This is the
//     half that has to survive SAIN's own init for anything to change.
//   - each live BD/Wedge bot, reported whenever its active layer changes, so we see what
//     wins in a real fight instead of a single patrol-time sample
internal class SainBrainDumpPatch : ModulePatch
{
    private const BindingFlags Any =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    protected override MethodBase GetTargetMethod()
    {
        return typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted), BindingFlags.Public | BindingFlags.Instance);
    }

    [PatchPostfix]
    protected static void PatchPostfix()
    {
        try
        {
            DumpRegistry();
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning($"[BrainDump] registry dump failed: {e}");
        }

        // BD/Wedge spawn on triggers well after the raid starts, so the per-bot half has
        // to keep looking rather than sampling once here.
        try
        {
            if (UnityEngine.Object.FindObjectOfType<SainBotProbe>() == null)
                new GameObject("BlackDiv_SainBotProbe").AddComponent<SainBotProbe>();
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning($"[BrainDump] probe attach failed: {e.Message}");
        }
    }

    private static void DumpRegistry()
    {
        Type brainManager = FindType("DrakiaXYZ.BigBrain.Brains.BrainManager");
        if (brainManager == null)
        {
            Plugin.LogSource.LogWarning("[BrainDump] BrainManager type not found");
            return;
        }

        object layers = Read(brainManager, null, "CustomLayersReadOnly") ?? Read(brainManager, null, "CustomLayers");
        if (!(layers is IEnumerable entries))
        {
            Plugin.LogSource.LogWarning("[BrainDump] custom layer collection unreadable");
            return;
        }

        int total = 0, shown = 0;
        foreach (object kvp in entries)
        {
            total++;
            object info = Read(kvp.GetType(), kvp, "Value");
            if (info == null) continue;

            Type t = info.GetType();
            string brains = Stringify(Read(t, info, "CustomLayerBrains") ?? Read(t, info, "brainNames"));
            // only the PMC brain matters here - that's the one BD bots run
            if (brains.IndexOf("PMC", StringComparison.OrdinalIgnoreCase) < 0) continue;

            shown++;
            Plugin.LogSource.LogWarning(
                $"[BrainDump] id={Stringify(Read(t, info, "customLayerId"))} "
                + $"type={Stringify(Read(t, info, "customLayerType"))} "
                + $"prio={Stringify(Read(t, info, "customLayerPriority"))} "
                + $"brains={brains} "
                + $"roles={Stringify(Read(t, info, "CustomLayerRoles") ?? Read(t, info, "roles"))}");
        }
        Plugin.LogSource.LogWarning($"[BrainDump] {shown} PMC-brain layer(s) of {total} registered total");

        // The exclusions are the half that decides whether SAIN's low-priority combat
        // layers are ever reachable, so print them too: a vanilla name excluded for our
        // roles is what "the fix took" looks like.
        object excludes = Read(brainManager, null, "ExcludeLayersReadOnly") ?? Read(brainManager, null, "ExcludeLayers");
        if (excludes is IEnumerable exEntries)
        {
            int exShown = 0;
            foreach (object kvp in exEntries)
            {
                object info = Read(kvp.GetType(), kvp, "Value") ?? kvp;
                Type t = info.GetType();
                string brains = Stringify(Read(t, info, "ExcludeLayerBrains") ?? Read(t, info, "brainNames"));
                if (brains.IndexOf("PMC", StringComparison.OrdinalIgnoreCase) < 0) continue;

                exShown++;
                Plugin.LogSource.LogWarning(
                    $"[BrainDump] EXCLUDE name={Stringify(Read(t, info, "excludeLayerName"))} "
                    + $"brains={brains} "
                    + $"roles={Stringify(Read(t, info, "ExcludeLayerRoles") ?? Read(t, info, "roles"))}");
            }
            Plugin.LogSource.LogWarning($"[BrainDump] {exShown} PMC-brain exclusion(s)");
        }
        else
        {
            Plugin.LogSource.LogWarning("[BrainDump] exclusion collection unreadable");
        }
    }

    internal static Type FindType(string fullName)
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = asm.GetType(fullName, false);
            if (t != null) return t;
        }
        return null;
    }

    internal static object Read(Type type, object instance, string name)
    {
        try
        {
            PropertyInfo p = type.GetProperty(name, Any);
            if (p != null && p.CanRead) return p.GetValue(instance);
            FieldInfo f = type.GetField(name, Any);
            if (f != null) return f.GetValue(instance);
            // auto-property backing field, as LayerInfo stores them
            FieldInfo backing = type.GetField($"<{name}>k__BackingField", Any);
            if (backing != null) return backing.GetValue(instance);
        }
        catch { }
        return null;
    }

    internal static string Stringify(object value)
    {
        if (value == null) return "null";
        if (value is string s) return s;

        if (value is IEnumerable items)
        {
            var sb = new StringBuilder("[");
            int n = 0;
            foreach (object item in items)
            {
                if (n++ > 0) sb.Append('|');
                if (n > 40) { sb.Append("..."); break; }
                sb.Append(item?.ToString() ?? "null");
            }
            return sb.Append(']').ToString();
        }

        return value.ToString();
    }
}

// Polls for Black Division / Wedge bots and reports each one once.
internal class SainBotProbe : MonoBehaviour
{
    private static readonly HashSet<int> BdRoles = new HashSet<int>
    {
        848420, 848421, 848422, 848423, 848424, 848426,
    };

    private readonly Dictionary<string, string> _lastLayer = new Dictionary<string, string>();
    private readonly Dictionary<string, int> _reportCount = new Dictionary<string, int>();
    private float _next;

    private void Update()
    {
        if (Time.time < _next) return;
        _next = Time.time + 1.5f;

        try
        {
            var world = Singleton<GameWorld>.Instance;
            if (world?.AllAlivePlayersList == null) return;

            Type sainBotComponent = SainBrainDumpPatch.FindType("SAIN.Components.BotComponent");

            foreach (Player player in world.AllAlivePlayersList)
            {
                if (player == null || !player.AIData.IsAI) continue;

                int role = (int)player.Profile.Info.Settings.Role;
                if (!BdRoles.Contains(role)) continue;

                string id = player.ProfileId;
                BotOwner bot = player.AIData.BotOwner;
                string brain = "?";
                try { brain = bot?.Brain?.BaseBrain?.ShortName() ?? "?"; } catch { }

                string activeLayer = "?";
                try { activeLayer = DrakiaXYZ.BigBrain.Brains.BrainManager.GetActiveLayerName(bot) ?? "(none)"; }
                catch (Exception e) { activeLayer = $"<{e.GetType().Name}>"; }

                string sain = "SAIN-type-not-found";
                if (sainBotComponent != null)
                {
                    try
                    {
                        Component c = bot != null ? bot.gameObject.GetComponent(sainBotComponent) : null;
                        sain = c != null ? "ATTACHED" : "MISSING";
                    }
                    catch (Exception e) { sain = $"<{e.GetType().Name}>"; }
                }

                // only speak up when the picture changes, and cap per bot so a long
                // raid cannot turn this into a log flood
                _lastLayer.TryGetValue(id, out string previous);
                if (previous == activeLayer) continue;
                _lastLayer[id] = activeLayer;

                _reportCount.TryGetValue(id, out int seen);
                if (seen >= 8) continue;
                _reportCount[id] = seen + 1;

                Plugin.LogSource.LogWarning(
                    $"[BotProbe] role={player.Profile.Info.Settings.Role} brain='{brain}' "
                    + $"activeLayer='{activeLayer}' sainComponent={sain}");
            }
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning($"[BotProbe] failed: {e.Message}");
            enabled = false;
        }
    }
}
