using System;
using System.Collections;
using System.Reflection;
using System.Text;
using EFT;
using SPT.Reflection.Patching;

namespace BlackDiv.Patches;

// READ-ONLY DIAGNOSTIC. Dumps what BigBrain actually has registered once the raid has
// started, so we can stop guessing at its internals.
//
// Where we are: SainBrainLayerPatch successfully calls SAIN's
// AddCustomLayersToBrainsAndRoles(["PMC"], BD roles) at raid start - it logs success and
// throws nothing - yet BD/Wedge bots still show the vanilla "Pmc" layer in BigBrain's
// overlay. SAIN already registered those same layer TYPES on the "PMC" brain scoped to
// role pmcBot (its raider path), and BigBrain keeps its registrations in a Dictionary, so
// the likely story is that our second registration of an already-registered layer type is
// being dropped rather than merged. This prints the actual registry instead of theorising:
// for every registered custom layer we log its type, priority, and whatever brain/role
// collections it carries, all via reflection so nothing depends on BigBrain's exact
// member shapes.
//
// Nothing here writes any state - it only reads BrainManager's public accessors and
// reflects over the returned objects.
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
            Type brainManager = AccessToolsTypeByName("DrakiaXYZ.BigBrain.Brains.BrainManager");
            if (brainManager == null)
            {
                Plugin.LogSource.LogWarning("[BrainDump] BrainManager type not found");
                return;
            }

            object layers = ReadMember(brainManager, null, "CustomLayersReadOnly")
                            ?? ReadMember(brainManager, null, "CustomLayers");

            // static members may hang off the singleton instead of the type
            if (layers == null)
            {
                object instance = ReadMember(brainManager, null, "Instance");
                if (instance != null)
                {
                    layers = ReadMember(brainManager, instance, "CustomLayersReadOnly")
                             ?? ReadMember(brainManager, instance, "CustomLayers");
                }
            }

            if (!(layers is IEnumerable enumerable))
            {
                Plugin.LogSource.LogWarning($"[BrainDump] custom layer collection unreadable (got: {layers?.GetType().FullName ?? "null"})");
                return;
            }

            int count = 0;
            foreach (object entry in enumerable)
            {
                count++;
                Plugin.LogSource.LogWarning($"[BrainDump] {Describe(entry)}");
            }
            Plugin.LogSource.LogWarning($"[BrainDump] {count} registered custom layer entr(ies) total");
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning($"[BrainDump] failed: {e}");
        }
    }

    private static Type AccessToolsTypeByName(string fullName)
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = asm.GetType(fullName, false);
            if (t != null) return t;
        }
        return null;
    }

    private static object ReadMember(Type type, object instance, string name)
    {
        try
        {
            PropertyInfo prop = type.GetProperty(name, Any);
            if (prop != null && prop.CanRead) return prop.GetValue(instance);
            FieldInfo field = type.GetField(name, Any);
            if (field != null) return field.GetValue(instance);
        }
        catch { }
        return null;
    }

    // A registry entry is likely a KeyValuePair or a wrapper object; print whatever
    // readable members it exposes rather than assuming a shape.
    private static string Describe(object entry)
    {
        if (entry == null) return "(null)";

        var sb = new StringBuilder();
        Type t = entry.GetType();
        sb.Append(t.Name).Append(" { ");

        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            sb.Append(p.Name).Append('=').Append(Stringify(SafeGet(() => p.GetValue(entry)))).Append(", ");
        }
        foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            sb.Append(f.Name).Append('=').Append(Stringify(SafeGet(() => f.GetValue(entry)))).Append(", ");
        }

        return sb.Append('}').ToString();
    }

    private static object SafeGet(Func<object> get)
    {
        try { return get(); }
        catch (Exception e) { return $"<{e.GetType().Name}>"; }
    }

    // Collections print as their contents - the brain-name and role lists are the whole
    // point of this dump, and "System.Collections.Generic.List`1[...]" tells us nothing.
    private static string Stringify(object value)
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
