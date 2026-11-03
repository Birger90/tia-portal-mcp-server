using TiaMcpServer.Contracts.PlcOnline;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// The Siemens-free checks behind update_watch_table_content/update_force_table_content (the SimaticML
/// must name exactly the addressed table before an ImportOptions.Override import) and behind
/// update_program (the returned object is interpreted fail-closed, never reported as plain success).
/// </summary>
public sealed class PlcWatchTableImportAndUpdateProgramTests
{
    private const string Export =
        "<Document><Engineering version=\"V21\" /><SW.WatchAndForceTables.PlcWatchTable ID=\"0\"><AttributeList><Name>Watch_1</Name>"
        + "</AttributeList><ObjectList /></SW.WatchAndForceTables.PlcWatchTable></Document>";

    private static string Table(string element, string name) =>
        $"<Document><Engineering version=\"V21\" /><{element} ID=\"0\"><AttributeList><Name>{name}</Name><Comment>edited</Comment></AttributeList></{element}></Document>";

    [Fact]
    public void ImportDocument_NamingExactlyTheAddressedTable_IsAccepted()
        => Assert.Null(PlcWatchTablePolicy.CheckImportDocument(Export, Table("SW.WatchAndForceTables.PlcWatchTable", "Watch_1"), "Watch_1"));

    [Theory]
    [InlineData("<Document><SW.WatchAndForceTables.PlcWatchTable><AttributeList><Name>Other</Name></AttributeList></SW.WatchAndForceTables.PlcWatchTable></Document>", "names table 'Other'")]
    [InlineData("<Document><SW.WatchAndForceTables.PlcWatchTable><AttributeList><Name>watch_1</Name></AttributeList></SW.WatchAndForceTables.PlcWatchTable></Document>", "names table 'watch_1'")]
    [InlineData("<Document><SW.WatchAndForceTables.PlcForceTable><AttributeList><Name>Watch_1</Name></AttributeList></SW.WatchAndForceTables.PlcForceTable></Document>", "describes a 'SW.WatchAndForceTables.PlcForceTable'")]
    [InlineData("<Document><SW.WatchAndForceTables.PlcWatchTable><AttributeList><Name>Watch_1</Name></AttributeList></SW.WatchAndForceTables.PlcWatchTable><SW.WatchAndForceTables.PlcWatchTable><AttributeList><Name>Watch_2</Name></AttributeList></SW.WatchAndForceTables.PlcWatchTable></Document>", "exactly one table")]
    [InlineData("<Document><Unrelated /></Document>", "exactly one table")]
    [InlineData("<Other><SW.WatchAndForceTables.PlcWatchTable><AttributeList><Name>Watch_1</Name></AttributeList></SW.WatchAndForceTables.PlcWatchTable></Other>", "root element is 'Other'")]
    [InlineData("<Document>edited</Document>", "exactly one table")]
    [InlineData("not xml", "not a well-formed XML document")]
    [InlineData("<!DOCTYPE Document [<!ENTITY x \"y\">]><Document>&x;</Document>", "not a well-formed XML document")]
    public void ImportDocument_ThatCouldReplaceAnotherTable_IsRefused(string content, string expected)
    {
        var refusal = PlcWatchTablePolicy.CheckImportDocument(Export, content, "Watch_1");
        Assert.NotNull(refusal);
        Assert.Contains(expected, refusal);
    }

    [Fact]
    public void ImportDocument_WithAnUnrecognisableExport_IsRefusedFailClosed()
    {
        Assert.Contains("cannot be checked", PlcWatchTablePolicy.CheckImportDocument("<Document><Table>Watch_1</Table></Document>",
            Table("SW.WatchAndForceTables.PlcWatchTable", "Watch_1"), "Watch_1"));
        Assert.Contains("could not be parsed", PlcWatchTablePolicy.CheckImportDocument(null, Export, "Watch_1"));
    }

    private sealed class StateResult
    {
        public string? State { get; set; }

        public int ErrorCount { get; set; }

        public object Nested { get; } = new();
    }

    private enum UpdateState { Success, Error, NoErrors }

    private sealed class EnumResult
    {
        public UpdateState State { get; set; }
    }

    private sealed class SuccessFlag
    {
        public bool Succeeded { get; set; }
    }

    private sealed class Opaque
    {
        public string Description => "done";

        public object Payload { get; } = new();
    }

    private sealed class Throwing
    {
        public string State => throw new InvalidOperationException("unreadable");
    }

    [Fact]
    public void UpdateProgram_FailingMembersFailClosed()
    {
        Assert.Equal(PlcUpdateProgramOutcome.Failed, PlcUpdateProgramOutcome.Interpret(new StateResult { State = "Error" }).Outcome);
        Assert.Equal(PlcUpdateProgramOutcome.Failed, PlcUpdateProgramOutcome.Interpret(new StateResult { State = "Success", ErrorCount = 2 }).Outcome);
        Assert.Equal(PlcUpdateProgramOutcome.Failed, PlcUpdateProgramOutcome.Interpret(new EnumResult { State = UpdateState.Error }).Outcome);
        Assert.Equal(PlcUpdateProgramOutcome.Failed, PlcUpdateProgramOutcome.Interpret(new SuccessFlag { Succeeded = false }).Outcome);
        Assert.Contains("ErrorCount is 2", PlcUpdateProgramOutcome.Interpret(new StateResult { State = "Success", ErrorCount = 2 }).Reason);
    }

    [Fact]
    public void UpdateProgram_SucceedsOnlyWhenAMemberSaysSo()
    {
        var success = PlcUpdateProgramOutcome.Interpret(new StateResult { State = "Success" });
        Assert.Equal(PlcUpdateProgramOutcome.Succeeded, success.Outcome);
        Assert.Contains(success.Members, member => member.Name == "State" && member.Value == "Success");
        Assert.DoesNotContain(success.Members, member => member.Name == "Nested");
        Assert.Equal(PlcUpdateProgramOutcome.Succeeded, PlcUpdateProgramOutcome.Interpret(new EnumResult { State = UpdateState.NoErrors }).Outcome);
        Assert.Equal(PlcUpdateProgramOutcome.Succeeded, PlcUpdateProgramOutcome.Interpret(new SuccessFlag { Succeeded = true }).Outcome);
        Assert.Equal(PlcUpdateProgramOutcome.Succeeded, PlcUpdateProgramOutcome.Interpret(new StateResult { State = null, ErrorCount = 0 }).Outcome);
    }

    [Fact]
    public void UpdateProgram_NothingInterpretableIsUninterpreted_NeverPlainSuccess()
    {
        var none = PlcUpdateProgramOutcome.Interpret(null);
        Assert.Equal(PlcUpdateProgramOutcome.Uninterpreted, none.Outcome);
        Assert.Empty(none.Members);
        var opaque = PlcUpdateProgramOutcome.Interpret(new Opaque());
        Assert.Equal(PlcUpdateProgramOutcome.Uninterpreted, opaque.Outcome);
        Assert.Contains(opaque.Members, member => member.Name == "Description");
        Assert.Equal(PlcUpdateProgramOutcome.Uninterpreted, PlcUpdateProgramOutcome.Interpret(new Throwing()).Outcome);
    }
}
