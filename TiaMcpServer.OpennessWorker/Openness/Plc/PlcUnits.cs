using System.Collections;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.SW.Units;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// Audit T4/K87 (not live-tested): every software unit of a PLC, the standard units (<c>PlcUnitSystemGroup.Units</c>) and the
/// safety units (<c>PlcUnitSystemGroup.SafetyUnits</c>, V21 members.txt: "Composition of Safety Units"). The safety units are
/// read by reflection (the reference stubs do not declare them) and used through <c>PlcUnitBase</c>, which declares
/// <c>BlockGroup</c>, <c>TagTableGroup</c>, <c>TypeGroup</c>, <c>ExternalSourceGroup</c> and <c>Name</c>; V21 lists no own
/// members on <c>PlcSafetyUnit</c>, so it is expected to derive from <c>PlcUnitBase</c> like <c>PlcUnit</c>. A safety unit that
/// is not a <c>PlcUnitBase</c> at runtime is reported through <c>note</c> and skipped. Safety units appear under the same
/// path form as software units (<c>&lt;PLC&gt;/Units/&lt;name&gt;</c>), as <c>list_safety_blocks</c> already shows them.
/// </summary>
internal static class PlcUnits
{
    public const string SafetyUnitsProperty = "SafetyUnits";

    public const string UnitsProperty = "Units";

    /// <summary>Standard units first (<c>Units</c>, read as before: its exceptions propagate), then safety units.</summary>
    public static IEnumerable<PlcUnitBase> All(object group, Action<string>? note = null)
    {
        foreach (var unit in Standard(group)) yield return unit;
        foreach (var unit in Safety(group, note)) yield return unit;
    }

    private static IEnumerable<PlcUnitBase> Standard(object group)
    {
        var property = group.GetType().GetProperty(UnitsProperty, BindingFlags.Public | BindingFlags.Instance);
        object? units;
        try
        {
            units = property?.GetValue(group, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        if (units is not IEnumerable items) yield break;
        foreach (var item in items)
            if (item is PlcUnitBase unit) yield return unit;
    }

    /// <summary>
    /// The safety units; empty when the installation's group has no <c>SafetyUnits</c> property. A non-fatal exception while
    /// reading them is reported through <paramref name="note"/> (the standard units stay usable); a lost Portal propagates.
    /// </summary>
    public static IReadOnlyList<PlcUnitBase> Safety(object group, Action<string>? note = null)
    {
        var property = group.GetType().GetProperty(SafetyUnitsProperty, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || property.GetIndexParameters().Length != 0) return Array.Empty<PlcUnitBase>();
        var units = new List<PlcUnitBase>();
        try
        {
            if (property.GetValue(group, null) is not IEnumerable items) return units;
            foreach (var item in items)
            {
                if (item is PlcUnitBase unit) units.Add(unit);
                else if (item is not null) note?.Invoke($"A safety unit of type '{item.GetType().FullName}' is not a PlcUnitBase and was skipped.");
            }
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            var inner = exception is TargetInvocationException { InnerException: { } wrapped } ? wrapped : exception;
            note?.Invoke($"PlcUnitSystemGroup.SafetyUnits could not be read ({inner.GetType().Name}: {inner.Message}); safety units are not included.");
        }

        return units;
    }

    private static bool IsFatal(Exception exception)
    {
        var inner = exception is TargetInvocationException { InnerException: { } wrapped } ? wrapped : exception;
        return inner is NonRecoverableException or EngineeringObjectDisposedException;
    }
}
