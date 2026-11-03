using TiaMcpServer.Contracts.PlcAlarms;
using Xunit;

namespace TiaMcpServer.Tests.PlcAlarms;

/// <summary>Result objects are read by the documented member names on the runtime object; missing members fail closed as unreadable.</summary>
public sealed class PlcAlarmResultReaderTests
{
    public enum FakeState
    {
        Success,
        Warning,
        Error,
    }

    public sealed class XlsxResult
    {
        public FakeState State { get; init; }

        public FileInfo LogFilePath { get; init; } = new("/tmp/log.txt");
    }

    public sealed class ClassMessage
    {
        public string Message { get; init; } = string.Empty;

        public FakeState State { get; init; }
    }

    public sealed class ClassResult
    {
        public FakeState State { get; init; }

        public int ErrorCount { get; init; }

        public int WarningCount { get; init; }

        public IEnumerable<ClassMessage> Messages { get; init; } = Array.Empty<ClassMessage>();
    }

    public sealed class Throwing
    {
        public FakeState State => throw new InvalidOperationException("getter failed");
    }

    public class BaseResult
    {
        public int State => 1;
    }

    /// <summary>Hides the base State with another type: GetProperty("State") would throw AmbiguousMatchException.</summary>
    public sealed class HidingResult : BaseResult
    {
        public new FakeState State { get; init; }
    }

    public sealed class Stateless
    {
        public string LogFilePath => "x";
    }

    [Fact]
    public void Bind_RunsBeforeTheCall_OnTheDeclaredType_AndFailsClosedWithoutState()
    {
        var binding = PlcAlarmResultReader.Bind(typeof(XlsxResult), withCounts: false);
        Assert.True(binding.IsUsable);
        Assert.Null(PlcAlarmResultReader.Refusal(binding, "Provider.Export(FileInfo)"));
        Assert.Equal("Success", PlcAlarmResultReader.Read(new XlsxResult { State = FakeState.Success }, binding).State);

        var stateless = PlcAlarmResultReader.Bind(typeof(Stateless), withCounts: false);
        Assert.False(stateless.IsUsable);
        Assert.Contains("nothing was called", PlcAlarmResultReader.Refusal(stateless, "Provider.Export(FileInfo)"));
        Assert.Contains("not the declared", Assert.Single(PlcAlarmResultReader.Read(new Stateless(), binding).Unreadable));
    }

    [Fact]
    public void Bind_PicksTheMostDerivedDeclaration_InsteadOfAnAmbiguousMatch()
    {
        Assert.Throws<System.Reflection.AmbiguousMatchException>(() => typeof(HidingResult).GetProperty("State"));

        var binding = PlcAlarmResultReader.Bind(typeof(HidingResult), withCounts: false);

        Assert.Equal("Warning", PlcAlarmResultReader.Read(new HidingResult { State = FakeState.Warning }, binding).State);
        Assert.Equal(typeof(HidingResult), PlcAlarmResultReader.Property(typeof(HidingResult), "State")!.DeclaringType);
    }

    [Fact]
    public void XlsxResults_ReadStateAndLogFile()
    {
        var outcome = PlcAlarmResultReader.Read(new XlsxResult { State = FakeState.Warning }, withCounts: false);

        Assert.Equal("Warning", outcome.State);
        Assert.Equal(new FileInfo("/tmp/log.txt").FullName, outcome.LogFilePath);
        Assert.Empty(outcome.Unreadable);
        Assert.Null(outcome.ErrorCount);
    }

    [Fact]
    public void CountedResults_ReadCountsAndMessages()
    {
        var outcome = PlcAlarmResultReader.Read(new ClassResult
        {
            State = FakeState.Error,
            ErrorCount = 2,
            WarningCount = 1,
            Messages = new[] { new ClassMessage { Message = "Class 'Errors' exists", State = FakeState.Error } },
        }, withCounts: true);

        Assert.Equal(("Error", 2, 1), (outcome.State, outcome.ErrorCount, outcome.WarningCount));
        Assert.Equal(new[] { "Error: Class 'Errors' exists" }, outcome.Messages);
    }

    [Fact]
    public void MissingMembers_AreUnreadable_AndNullResultsToo()
    {
        var outcome = PlcAlarmResultReader.Read(new object(), withCounts: true);
        Assert.Null(outcome.State);
        Assert.Contains("Object.State", outcome.Unreadable);
        Assert.Contains("Object.ErrorCount", outcome.Unreadable);
        Assert.Contains("the call returned no result object", PlcAlarmResultReader.Read(null, withCounts: false).Unreadable);
        Assert.Equal(PlcAlarmPolicy.StateVerdict.Unknown, PlcAlarmPolicy.InterpretState(outcome.State, warningIsSuccess: true));
    }

    [Fact]
    public void GetterExceptions_AreRethrownUnwrapped()
        => Assert.Equal("getter failed", Assert.Throws<InvalidOperationException>(() => PlcAlarmResultReader.Read(new Throwing(), withCounts: false)).Message);

    [Fact]
    public void Messages_AreBounded()
    {
        var outcome = PlcAlarmResultReader.Read(new ClassResult
        {
            Messages = Enumerable.Range(0, PlcAlarmResultReader.MaxMessages + 5).Select(i => new ClassMessage { Message = $"m{i}" }).ToArray(),
        }, withCounts: true);

        Assert.Equal(PlcAlarmResultReader.MaxMessages + 1, outcome.Messages!.Count);
        Assert.Contains("omitted", outcome.Messages[^1]);
    }
}
