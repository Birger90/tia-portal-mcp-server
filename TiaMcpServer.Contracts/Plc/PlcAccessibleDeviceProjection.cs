using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TiaMcpServer.Contracts.Library;

namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Siemens-free runtime binding of <c>ConfigurationPcInterface.GetAccessibleDevices()</c>. The V21 reference
/// documents the member and that each returned <c>ConfigurationAccessibleDevice</c> exposes <c>Name</c>,
/// <c>DeviceSeries</c>, <c>Address</c> and <c>MACAddress</c>, but neither the return type nor the member types.
/// So nothing is compiled in: the method is found by exact name with no parameters (exactly one public
/// instance method, <see cref="LibraryRuntimeBinding.Find"/>), the result must be enumerable, every element
/// must be (or derive from) the exact type <see cref="DeviceTypeName"/>, and the four members are read on that
/// type by exact name. A member value is projected only when it is text or an enum (by member name); anything
/// else fails closed, so a value is never rendered by a guessed conversion.
/// </summary>
public static class PlcAccessibleDeviceProjection
{
    public const string MethodName = "GetAccessibleDevices";

    public const string DeviceTypeName = "Siemens.Engineering.Connection.ConfigurationAccessibleDevice";

    public const int DefaultMaxDevices = 256;

    public const int MaxDevices = 1000;

    /// <summary>The documented members in their documented spelling, in the order they are projected.</summary>
    public static IReadOnlyList<string> Members { get; } = Array.AsReadOnly(new[] { "Name", "DeviceSeries", "Address", "MACAddress" });

    /// <summary>The scan's outcome: the projected devices, how many were returned, and the fail-closed reason when binding failed.</summary>
    public sealed class Outcome
    {
        public List<PlcAccessibleDeviceInfo> Devices { get; } = new List<PlcAccessibleDeviceInfo>();

        public int Count { get; set; }

        public bool Truncated { get; set; }

        public string? Error { get; set; }
    }

    /// <summary>Calls the single public parameterless <c>GetAccessibleDevices</c> of <paramref name="pcInterface"/>; a Siemens exception is rethrown unwrapped.</summary>
    /// <exception cref="InvalidOperationException">No single applicable method exists (the message is the fail-closed reason).</exception>
    public static object? Invoke(object pcInterface)
    {
        var method = LibraryRuntimeBinding.Find(pcInterface.GetType(), MethodName, Type.EmptyTypes, out var error)
            ?? throw new InvalidOperationException(error ?? $"This TIA Portal installation exposes no {MethodName}() on '{pcInterface.GetType().FullName}'.");
        try
        {
            return method.Invoke(pcInterface, Array.Empty<object>());
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>
    /// Ends the scan's confirmation scope (it answered every TIA Portal confirmation No, like compare_to_online) and
    /// returns the failure message when any dialog was raised and declined during the scan, or null. A declined dialog
    /// means the scan may be incomplete, so the item fails instead of listing devices. Called after the result was
    /// projected (reading the device members may raise dialogs too), or when the scan failed.
    /// </summary>
    public static string? DeclinedConfirmations(PlcDownloadPortalConfirmationLog? confirmations)
    {
        if (confirmations is null) return null;
        List<PlcDownloadPortalConfirmationInfo> declined = new();
        var observed = 0;
        confirmations.CloseInto((entries, count, _) =>
        {
            declined = entries;
            observed = count;
        });
        if (observed == 0) return null;
        var listed = string.Join("; ", declined.Take(3).Select(entry => entry.Text is null ? entry.Type : $"{entry.Type}: {entry.Text}"));
        return $"TIA Portal raised {observed} confirmation dialog(s) during scan_accessible_devices and each was answered No ({listed}"
            + (observed > 3 ? "; ..." : string.Empty) + "), so the scan result was discarded and no device is listed.";
    }

    /// <summary>
    /// The member name of an enum value: a defined value's name, or for a <c>[Flags]</c> enum the comma-separated
    /// names when every set bit is named; null for an undefined value, including a zero that no member names (a
    /// <c>[Flags]</c> enum without a zero member would otherwise print "0") — never a bare number.
    /// </summary>
    public static string? EnumName(Enum value)
    {
        var type = value.GetType();
        if (Enum.IsDefined(type, value)) return value.ToString();
        if (!type.IsDefined(typeof(FlagsAttribute), inherit: false)) return null;
        var bits = Bits(value);
        if (bits == 0) return null;
        ulong named = 0;
        foreach (var defined in Enum.GetValues(type)) named |= Bits(defined);
        return (bits & ~named) == 0 ? value.ToString() : null;
    }

    private static ulong Bits(object value)
        => Convert.GetTypeCode(value) is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
            ? unchecked((ulong)Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture))
            : Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Projects the returned collection; <see cref="Outcome.Error"/> is set (and nothing is listed) when it cannot be bound.</summary>
    public static Outcome Project(object? returned, int maxDevices)
    {
        var outcome = new Outcome();
        if (returned is null)
        {
            outcome.Error = $"{MethodName}() returned null; no device list can be reported.";
            return outcome;
        }

        if (returned is not IEnumerable devices || returned is string)
        {
            outcome.Error = $"{MethodName}() returned '{returned.GetType().FullName}', which is not a collection.";
            return outcome;
        }

        var projected = new List<PlcAccessibleDeviceInfo>();
        var count = 0;
        foreach (var device in devices)
        {
            if (device is null)
            {
                outcome.Error = $"{MethodName}() returned a null element.";
                return outcome;
            }

            var declaring = DocumentedType(device.GetType());
            if (declaring is null)
            {
                outcome.Error = $"{MethodName}() returned an element of type '{device.GetType().FullName}', which is not {DeviceTypeName}.";
                return outcome;
            }

            var values = new string?[Members.Count];
            for (var index = 0; index < Members.Count; index++)
            {
                var property = LibraryRuntimeBinding.Property(declaring, Members[index], out var propertyError);
                if (property is null)
                {
                    outcome.Error = propertyError;
                    return outcome;
                }

                object? value;
                try
                {
                    value = property.GetValue(device);
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }

                switch (value)
                {
                    case null:
                        values[index] = null;
                        break;
                    case string text:
                        values[index] = text;
                        break;
                    case Enum member when EnumName(member) is { } name:
                        values[index] = name;
                        break;
                    case Enum member:
                        outcome.Error = $"{DeviceTypeName}.{Members[index]} has the value {Convert.ChangeType(member, Enum.GetUnderlyingType(member.GetType()), System.Globalization.CultureInfo.InvariantCulture)}, which no member of '{member.GetType().FullName}' names; it is not reported as a number.";
                        return outcome;
                    default:
                        outcome.Error = $"{DeviceTypeName}.{Members[index]} has the runtime type '{value.GetType().FullName}'; only text and enum values are reported.";
                        return outcome;
                }
            }

            count++;
            if (projected.Count < maxDevices)
                projected.Add(new PlcAccessibleDeviceInfo { Name = values[0], DeviceSeries = values[1], Address = values[2], MacAddress = values[3] });
        }

        outcome.Devices.AddRange(projected);
        outcome.Count = count;
        outcome.Truncated = count > projected.Count;
        return outcome;
    }

    /// <summary>The runtime type itself or the nearest base type whose full name is the documented one.</summary>
    private static Type? DocumentedType(Type runtimeType)
    {
        for (var type = runtimeType; type is not null; type = type.BaseType)
            if (string.Equals(type.FullName, DeviceTypeName, StringComparison.Ordinal)) return type;
        return null;
    }
}
