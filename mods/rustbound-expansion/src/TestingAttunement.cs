using System.Reflection.Emit;
using HarmonyLib;
using rustboundmagic.src.common.item.tool.spellcasting;
using rustboundmagic.src.system.abstracts;

namespace RustboundExpansion;

internal static class TestingAttunement
{
    // Release builds honor each spell's native school and attunement checks.
    internal const bool Enabled = false;

    internal static string CastingSchool(SpellBase spell) => Enabled && spell is ExpansionSpell
        ? "general" : spell.School;

    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.DeclaredMethod(typeof(ItemToolStaffRM), "OnHeldInteractStart"),
        transpiler: new HarmonyMethod(typeof(TestingAttunement), nameof(StaffChecks)));

    // Replace only the four school reads at the staff's attunement gate. No
    // mutation of shared spell definitions or changes to other cast checks.
    internal static IEnumerable<CodeInstruction> StaffChecks(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(SpellBase), nameof(SpellBase.School));
        var replacement = AccessTools.Method(typeof(TestingAttunement), nameof(CastingSchool));
        int count = 0;
        foreach (var instruction in instructions)
        {
            var output = new CodeInstruction(instruction);
            if (output.Calls(getter))
            {
                output.opcode = OpCodes.Call;
                output.operand = replacement;
                count++;
            }
            else if (output.Calls(replacement)) count++;
            yield return output;
        }
        if (count != 4) throw new InvalidOperationException($"Rustbound staff attunement layout changed: found {count} school reads, expected 4.");
    }
}
