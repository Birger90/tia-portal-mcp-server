using Siemens.Engineering;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>K20: a failed export of an object that reads IsConsistent=false names compile_check; the exception is unchanged.</summary>
public sealed class ExportConsistencyEvidenceTests
{
    private sealed class FakeBlock(bool consistent)
    {
        public bool IsConsistent { get; } = consistent;
    }

    private static string Fail() => throw new EngineeringTargetInvocationException("Inconsistent blocks and PLC data types (UDT) cannot be exported.");

    [Fact]
    public void AFailedExportOfAnInconsistentBlock_AppendsTheCompileHintToTheFailure()
    {
        ExportConsistencyEvidence.BeginRequest();
        var thrown = Assert.Throws<EngineeringTargetInvocationException>(() =>
            ExportConsistencyEvidence.Observe(new FakeBlock(false), () => PlcExportConsistency.BlockHint("PLC_1/Blocks/FC_1"), Fail));
        Assert.StartsWith("Inconsistent blocks", thrown.Message);

        var response = new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.WorkerOperationFailed, Error = "TIA Portal operation failed: x." };
        ExportConsistencyEvidence.Annotate(response);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, response.FailureCategory);
        Assert.StartsWith("TIA Portal operation failed: x. " + PlcExportConsistency.Marker, response.Error);
        Assert.Contains("compile_check with blockPath 'PLC_1/Blocks/FC_1'", response.Error);
        Assert.True(PlcExportConsistency.Carries(response.Error));

        // The hint belongs to that request only.
        var next = new WorkerResponse { Success = false, Error = "other" };
        ExportConsistencyEvidence.Annotate(next);
        Assert.Equal("other", next.Error);
    }

    [Fact]
    public void AConsistentOrUnreadableObject_GetsNoHint()
    {
        ExportConsistencyEvidence.BeginRequest();
        Assert.Throws<EngineeringTargetInvocationException>(() => ExportConsistencyEvidence.Observe(new FakeBlock(true), () => "hint", Fail));
        Assert.Throws<EngineeringTargetInvocationException>(() => ExportConsistencyEvidence.Observe(new object(), () => "hint", Fail));
        var response = new WorkerResponse { Success = false, Error = "failed" };
        ExportConsistencyEvidence.Annotate(response);
        Assert.Equal("failed", response.Error);
    }

    [Fact]
    public void AHandledExportFailureInASucceededRequest_BecomesAWarning()
    {
        ExportConsistencyEvidence.BeginRequest();
        try
        {
            ExportConsistencyEvidence.Observe(new FakeBlock(false), () => PlcExportConsistency.TechnologyObjectHint("PLC_1", "Axis_1"), Fail);
        }
        catch (EngineeringTargetInvocationException)
        {
            // handled by the caller, as a re-export check does
        }

        var response = new WorkerResponse { Success = true, Payload = "{}" };
        ExportConsistencyEvidence.Annotate(response);
        var warning = Assert.Single(response.Warnings!);
        Assert.Contains("technology object 'Axis_1'", warning);
        Assert.Contains("compile_check with plcName 'PLC_1'", warning);
        Assert.Equal("ok", ExportConsistencyEvidence.Observe(new FakeBlock(false), () => "unused", () => "ok"));
    }
}
