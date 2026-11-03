using System.Reflection;
using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness.Vci;

/// <summary>
/// The <c>ProgrammingLanguage</c> of a VCI leaf that is a PLC block, read by name so that any leaf type (blocks, tag tables,
/// types, technology objects) can be asked; null when the leaf has none or it cannot be read (then only the system-folder
/// rule of <see cref="Contracts.Vci.VciSafetyExport"/> applies).
/// </summary>
internal static class VciLeafLanguage
{
    public static string? Read(object leaf)
    {
        var property = leaf.GetType().GetProperty("ProgrammingLanguage", BindingFlags.Public | BindingFlags.Instance);
        if (property is null) return null;
        try
        {
            return property.GetValue(leaf)?.ToString();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is EngineeringException)
        {
            return null;
        }
    }
}
