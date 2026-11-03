using System.Reflection;
using Xunit;
using Session = TiaMcpServer.OpennessWorker.Openness.Project.TiaPortalSession;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// The worker creates its shared <c>TiaPortalSession</c> in a static field initializer, before
/// <c>AssemblyResolver.Register</c> runs. A field of a Siemens value type (an enum such as
/// <c>ConfirmationResult</c>) makes loading the session type load Siemens.Engineering, which is not resolvable yet:
/// build 1205113 crashed at worker startup this way (live 2026-11-02). Reference-type fields are not loaded with the type.
/// </summary>
public sealed class TiaPortalSessionTypeLoadTests
{
    [Fact]
    public void Session_DeclaresNoFieldOfASiemensValueType()
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var offenders = typeof(Session).GetFields(all)
            .Where(field => IsSiemensValueType(field.FieldType))
            .Select(field => $"{field.Name}: {field.FieldType.FullName}")
            .ToList();
        Assert.Empty(offenders);
    }

    private static bool IsSiemensValueType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsValueType && (underlying.Namespace ?? string.Empty).StartsWith("Siemens", StringComparison.Ordinal);
    }
}
