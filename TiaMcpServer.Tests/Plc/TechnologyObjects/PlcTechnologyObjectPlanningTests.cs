using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc.TechnologyObjects;

/// <summary>The host planning checks that do not need a worker: the export's existing-file check and the parameter refusal.</summary>
public sealed class PlcTechnologyObjectPlanningTests
{
    private static PlcOperationRequest Export(string path, bool? overwrite = null) => new()
    {
        OperationId = "x", Operation = TechnologyObjectMethods.Export, Name = "Axis_1", FilePath = path, Overwrite = overwrite,
    };

    [Fact]
    public void AnExistingExportFileIsRefusedUnlessOverwriteIsSetThenTheOverwriteGuardFires()
    {
        var probed = new List<string>();
        bool Exists(string path)
        {
            probed.Add(path);
            return path == @"C:\Exports\Existing.xml";
        }

        var (refusal, guard) = PlcTechnologyObjectPlanning.ExportFileCheck(Export("C:/Exports/Existing.xml"), "Axis_1", Exists);
        Assert.Contains(@"'C:\Exports\Existing.xml' already exists; set overwrite:true", refusal);
        Assert.Null(guard);
        (refusal, guard) = PlcTechnologyObjectPlanning.ExportFileCheck(Export(@"C:\Exports\Existing.xml", overwrite: true), "Axis_1", Exists);
        Assert.Null(refusal);
        Assert.Contains(@"replaces the existing file 'C:\Exports\Existing.xml'", guard);
        Assert.Equal(((string?)null, (string?)null), PlcTechnologyObjectPlanning.ExportFileCheck(Export(@"C:\Exports\New.xml"), "Axis_1", Exists));
        // The probe sees the normalized path only; a lexically invalid path is never probed (validation refuses it).
        Assert.Equal(((string?)null, (string?)null), PlcTechnologyObjectPlanning.ExportFileCheck(Export(@"\\?\C:\Exports\Existing.xml"), "Axis_1", Exists));
        Assert.Equal(new[] { @"C:\Exports\Existing.xml", @"C:\Exports\Existing.xml", @"C:\Exports\New.xml" }, probed);
    }

    private static PlcOperationRequest Set(string value) => new()
    {
        OperationId = "p", Operation = TechnologyObjectMethods.SetParameter, Name = "Axis_1", ParameterName = "Axis.Mode", Value = value,
    };

    [Fact]
    public void EnumValuesAreRefusedOnTheHostOnlyAgainstAFullMemberList()
    {
        var full = new TechnologyObjectParameterInfo { Name = "Axis.Mode", ValueKind = "enum", ValueType = "Fake.Mode", Value = "A", EnumMembers = { "A", "B" } };
        Assert.Null(PlcTechnologyObjectPlanning.Refusal(Set("B"), full));
        Assert.Contains("'C' is not a member of Fake.Mode", PlcTechnologyObjectPlanning.Refusal(Set("C"), full));
        Assert.Contains("an exact member name", PlcTechnologyObjectPlanning.Refusal(Set("A, B"), full));

        var truncated = new TechnologyObjectParameterInfo { Name = "Axis.Mode", ValueKind = "enum", ValueType = "Fake.Mode", Value = "A", EnumMembers = { "A", "B" }, EnumMembersTruncated = true };
        Assert.Null(PlcTechnologyObjectPlanning.Refusal(Set("C"), truncated));

        var flags = new TechnologyObjectParameterInfo { Name = "Axis.Mode", ValueKind = "enum", ValueType = "Fake.Sides", Value = "A", EnumMembers = { "A", "B" }, EnumFlags = true };
        Assert.Null(PlcTechnologyObjectPlanning.Refusal(Set("A, B"), flags));
        Assert.Contains("'C'", PlcTechnologyObjectPlanning.Refusal(Set("A, C"), flags));
    }

    private enum Mode { Off, Jog, Auto }

    /// <summary>A value the host refuses reads exactly like the worker's refusal of the same text against the live type.</summary>
    [Fact]
    public void HostRefusalsAreWordedExactlyLikeTheWorkers()
    {
        string Worker(string text, Type type)
        {
            Assert.False(TechnologyObjectValues.TryConvert(text, type, out _, out var error));
            return TechnologyObjectValues.ParameterRefusal("Axis.Mode", "Axis_1", error);
        }

        var single = new TechnologyObjectParameterInfo { Name = "Axis.Mode", ValueKind = "real", ValueType = "System.Single", Value = "1" };
        Assert.Equal(Worker("1,5", typeof(float)), PlcTechnologyObjectPlanning.Refusal(Set("1,5"), single));
        var integer = new TechnologyObjectParameterInfo { Name = "Axis.Mode", ValueKind = "integer", ValueType = "System.UInt16", Value = "1" };
        Assert.Equal(Worker("70000", typeof(ushort)), PlcTechnologyObjectPlanning.Refusal(Set("70000"), integer));
        var mode = new TechnologyObjectParameterInfo
        {
            Name = "Axis.Mode", ValueKind = "enum", ValueType = typeof(Mode).FullName, Value = "Off", EnumMembers = { "Off", "Jog", "Auto" },
        };
        Assert.Equal(Worker("Servo", typeof(Mode)), PlcTechnologyObjectPlanning.Refusal(Set("Servo"), mode));
        Assert.Equal(Worker("Off, Jog", typeof(Mode)), PlcTechnologyObjectPlanning.Refusal(Set("Off, Jog"), mode));
        Assert.StartsWith("Parameter 'Axis.Mode' of 'Axis_1': 'Servo' is not a member of ", PlcTechnologyObjectPlanning.Refusal(Set("Servo"), mode));
        // The planner names the object as it was found (not as the caller cased it).
        Assert.Contains("of 'AXIS_1':", PlcTechnologyObjectPlanning.Refusal(Set("Servo"), mode, "AXIS_1"));
    }

    [Fact]
    public void RealValuesAreCheckedForSyntaxOnlyOnTheHost()
    {
        // Range and exactness are the worker's (one authority, the live type); the host refuses only text that is no number.
        var single = new TechnologyObjectParameterInfo { Name = "Axis.Mode", ValueKind = "real", ValueType = "System.Single", Value = "1" };
        Assert.Null(PlcTechnologyObjectPlanning.Refusal(Set("16777217"), single));
        Assert.Null(PlcTechnologyObjectPlanning.Refusal(Set("1e400"), single));
        Assert.Contains("not a finite invariant number", PlcTechnologyObjectPlanning.Refusal(Set("1,5"), single));
        Assert.Contains("not a finite invariant number", PlcTechnologyObjectPlanning.Refusal(Set("NaN"), single));
        var integer = new TechnologyObjectParameterInfo { Name = "Axis.Mode", ValueKind = "integer", ValueType = "System.UInt16", Value = "1" };
        Assert.Contains("System.UInt16", PlcTechnologyObjectPlanning.Refusal(Set("70000"), integer));
    }
}
