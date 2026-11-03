using System.Reflection;
using Siemens.Engineering;
using TiaMcpServer.Json;
using TiaMcpServer.OpennessWorker.Openness.Block;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class CompileReportProjectionTests
{
    [Fact]
    public void BlockOutcomeProjectionCopiesAndBoundsEveryComponentWithoutMutatingInput()
    {
        var report = new TiaMcpServer.Contracts.Block.CompileCheckReport
        {
            Scope = new string('s', 400),
            BlockPath = "C:\\private\\" + new string('p', 400),
            OverallState = "Error",
            TotalErrorCount = 25,
            TotalWarningCount = 9
        };
        for (var plcIndex = 0; plcIndex < 10; plcIndex++)
        {
            var plc = new TiaMcpServer.Contracts.Plc.PlcCompileInfo
            {
                PlcName = "PLC-" + plcIndex + "-" + new string('n', 300),
                DeviceName = "Device-" + new string('d', 300),
                State = "Error",
                ErrorCount = plcIndex == 0 ? 25 : 0,
                WarningCount = plcIndex == 0 ? 9 : 0
            };
            for (var messageIndex = 0; messageIndex < 5; messageIndex++)
            {
                plc.Messages.Add(new TiaMcpServer.Contracts.Block.CompileMessageInfo
                {
                    Description = "SECRET_CONTENT-" + new string('x', 300),
                    Path = "C:\\private\\Fixture.ap21\\" + new string('y', 300),
                    Severity = "Error"
                });
            }
            plc.DiagnosticNotes.Add("\\\\server\\share\\private " + new string('z', 300));
            report.Plcs.Add(plc);
        }

        var projected = BlockImportOutcomeProjection.Project(new TiaMcpServer.Contracts.Block.BlockImportOutcomeInfo
        {
            ImportStage = "completed",
            ImportResultState = "success",
            TargetMutationCommitted = true,
            CompileStage = "failed",
            CompileReport = report,
            FinalReadStage = "succeeded",
            TargetPresent = true,
            ContentRelation = "unknown",
            TemporarySourceState = "not_applicable"
        });

        Assert.NotSame(report, projected.CompileReport);
        Assert.Equal(10, report.Plcs.Count);
        Assert.True(projected.CompileDetailsOmitted);
        Assert.InRange(projected.CompileReport!.Plcs.Count, 1, 8);
        Assert.InRange(projected.CompileReport.BlockPath!.Length, 1, 256);
        Assert.InRange(projected.CompileReport.Plcs.Sum(plc => plc.Messages.Count), 0, 20);
        Assert.InRange(projected.CompileReport.Plcs.Sum(plc => plc.DiagnosticNotes.Count), 0, 8);
        Assert.Equal(25, projected.CompileReport.TotalErrorCount);
        Assert.Equal(9, projected.CompileReport.TotalWarningCount);
        Assert.All(projected.CompileReport.Plcs, plc =>
        {
            Assert.InRange(plc.PlcName.Length, 0, 256);
            if (plc.DeviceName is not null) Assert.InRange(plc.DeviceName.Length, 0, 256);
            Assert.All(plc.Messages, message =>
            {
                Assert.InRange(message.Description.Length, 0, 256);
                Assert.InRange(message.Path.Length, 0, 256);
                // TIA Portal's own texts are kept (shortened to the field bound), never replaced by a fixed text.
                Assert.StartsWith("SECRET_CONTENT-", message.Description, StringComparison.Ordinal);
            });
        });
        Assert.InRange(projected.CompileReport.Plcs.Sum(plc => plc.Messages.Count), 1, 20);
        Assert.True(TiaMcpServer.Worker.BlockImportOutcomeValidator.Validate(projected, "xml"));
    }

    [Fact]
    public void BlockOutcomeProjectionBoundsEscapeHeavySerializedDocument()
    {
        var report = new TiaMcpServer.Contracts.Block.CompileCheckReport
        {
            Scope = new string('"', 256),
            BlockPath = new string('\\', 256),
            OverallState = "Warning",
            TotalWarningCount = 20
        };
        for (var plcIndex = 0; plcIndex < 8; plcIndex++)
        {
            var plc = new TiaMcpServer.Contracts.Plc.PlcCompileInfo
            {
                PlcName = new string('"', 256),
                DeviceName = new string('\\', 256),
                State = "Warning",
                WarningCount = plcIndex == 0 ? 20 : 0
            };
            plc.Messages.Add(new TiaMcpServer.Contracts.Block.CompileMessageInfo
            {
                Description = new string('\u0001', 256),
                Path = new string('"', 256),
                Severity = "Warning"
            });
            plc.DiagnosticNotes.Add(new string('\\', 256));
            report.Plcs.Add(plc);
        }

        var projected = BlockImportOutcomeProjection.Project(new TiaMcpServer.Contracts.Block.BlockImportOutcomeInfo
        {
            ImportStage = "completed",
            ImportResultState = "success",
            TargetMutationCommitted = true,
            CompileStage = "succeeded",
            CompileReport = report,
            FinalReadStage = "succeeded",
            TargetPresent = true,
            ContentRelation = "unknown",
            TemporarySourceState = "not_applicable"
        });

        var json = System.Text.Json.JsonSerializer.Serialize(projected, TiaJson.Presentation);
        Assert.InRange(json.Length, 1, 12000);
        Assert.True(TiaMcpServer.Worker.BlockImportOutcomeValidator.Validate(projected, "xml"));
    }

    [Fact]
    public void BlockOutcomeProjectionUsesTheHostNullInclusiveBudgetNearTheLimit()
    {
        var maximumHostLength = 0;
        for (var severityLength = 1; severityLength <= 256; severityLength++)
        {
            var report = new TiaMcpServer.Contracts.Block.CompileCheckReport
            {
                Scope = new string('"', 256),
                BlockPath = null,
                OverallState = "Warning",
                TotalWarningCount = 20
            };
            for (var plcIndex = 0; plcIndex < 8; plcIndex++)
            {
                var plc = new TiaMcpServer.Contracts.Plc.PlcCompileInfo
                {
                    PlcName = new string('"', 256),
                    DeviceName = null,
                    State = "Warning",
                    WarningCount = plcIndex == 0 ? 20 : 0
                };
                for (var messageIndex = 0; messageIndex < (plcIndex < 4 ? 3 : 2); messageIndex++)
                {
                    plc.Messages.Add(new TiaMcpServer.Contracts.Block.CompileMessageInfo
                    {
                        Description = string.Empty,
                        Path = string.Empty,
                        Severity = new string('"', severityLength)
                    });
                }
                report.Plcs.Add(plc);
            }

            var projected = BlockImportOutcomeProjection.Project(new TiaMcpServer.Contracts.Block.BlockImportOutcomeInfo
            {
                ImportStage = "completed",
                ImportResultState = "success",
                TargetMutationCommitted = true,
                CompileStage = "succeeded",
                CompileReport = report,
                FinalReadStage = "unavailable",
                TargetPresent = null,
                ContentRelation = "unknown",
                TemporarySourceState = "not_applicable"
            });

            var hostLength = System.Text.Json.JsonSerializer.Serialize(projected, TiaJson.Presentation).Length;
            maximumHostLength = Math.Max(maximumHostLength, hostLength);
            Assert.InRange(hostLength, 1, 12_000);
            Assert.True(TiaMcpServer.Worker.BlockImportOutcomeValidator.Validate(projected, "xml"));
        }

        Assert.InRange(maximumHostLength, 11_500, 12_000);
    }

    [Fact]
    public void BlockOutcomeProjectionKeepsShortCompilerTextsPathsAndNotes()
    {
        // K04 (live 2026-11-02): four short information messages came back as "Compiler diagnostic omitted.".
        var report = new TiaMcpServer.Contracts.Block.CompileCheckReport
        {
            Scope = "block",
            BlockPath = "PLC_1/Blocks/Main",
            OverallState = "Warning",
            TotalWarningCount = 1,
            Plcs =
            {
                new TiaMcpServer.Contracts.Plc.PlcCompileInfo
                {
                    PlcName = "PLC_1",
                    DeviceName = "Device_1",
                    State = "Warning",
                    WarningCount = 1,
                    Messages =
                    {
                        new TiaMcpServer.Contracts.Block.CompileMessageInfo { Description = "Block was compiled.", Path = "PLC_1/Program blocks/Main", Severity = "Information" },
                        new TiaMcpServer.Contracts.Block.CompileMessageInfo { Description = "Implicit conversion.", Path = "PLC_1/Program blocks/Main", Severity = "Warning" },
                        new TiaMcpServer.Contracts.Block.CompileMessageInfo { Description = " \u0001Padded\t", Path = "PLC_1", Severity = "Information" },
                    },
                    DiagnosticNotes = { "worker note" }
                }
            }
        };

        var projected = BlockImportOutcomeProjection.Project(Completed(report, "succeeded"));

        var projectedReport = Assert.IsType<TiaMcpServer.Contracts.Block.CompileCheckReport>(projected.CompileReport);
        Assert.False(projected.CompileDetailsOmitted);
        Assert.Equal("PLC_1/Blocks/Main", projectedReport.BlockPath);
        var plc = Assert.Single(projectedReport.Plcs);
        Assert.Equal(new[] { "Block was compiled.", "Implicit conversion.", "Padded" }, plc.Messages.Select(m => m.Description));
        Assert.Equal(new[] { "PLC_1/Program blocks/Main", "PLC_1/Program blocks/Main", "PLC_1" }, plc.Messages.Select(m => m.Path));
        Assert.Equal(new[] { "Information", "Warning", "Information" }, plc.Messages.Select(m => m.Severity));
        Assert.Equal("worker note", Assert.Single(plc.DiagnosticNotes));
        Assert.True(TiaMcpServer.Worker.BlockImportOutcomeValidator.Validate(projected, "xml"));
    }

    [Fact]
    public void BlockOutcomeProjectionKeepsErrorsAndWarningsBeforeInformationWhenTheBudgetIsShort()
    {
        var plc = new TiaMcpServer.Contracts.Plc.PlcCompileInfo { PlcName = "PLC_1", State = "Error", ErrorCount = 2, WarningCount = 1 };
        for (var index = 0; index < 30; index++)
            plc.Messages.Add(new TiaMcpServer.Contracts.Block.CompileMessageInfo { Description = $"info {index:D2} " + new string('i', 200), Path = "PLC_1", Severity = "Information" });
        plc.Messages.Insert(25, new TiaMcpServer.Contracts.Block.CompileMessageInfo { Description = "late error", Path = "PLC_1/Blocks/B", Severity = "Error" });
        plc.Messages.Insert(10, new TiaMcpServer.Contracts.Block.CompileMessageInfo { Description = "a warning", Path = "PLC_1/Blocks/A", Severity = "Warning" });
        plc.Messages.Add(new TiaMcpServer.Contracts.Block.CompileMessageInfo { Description = "last error", Path = "PLC_1/Blocks/C", Severity = "Error" });
        var report = new TiaMcpServer.Contracts.Block.CompileCheckReport
        {
            Scope = "block", BlockPath = "PLC_1/Blocks/Main", OverallState = "Error", TotalErrorCount = 2, TotalWarningCount = 1, Plcs = { plc }
        };

        var projected = BlockImportOutcomeProjection.Project(Completed(report, "failed"));

        Assert.True(projected.CompileDetailsOmitted);
        var messages = Assert.Single(projected.CompileReport!.Plcs).Messages;
        Assert.InRange(messages.Count, 3, 20);
        // Errors and the warning survive; information fills the rest in report order, and report order is kept.
        Assert.Equal(new[] { "a warning", "late error", "last error" }, messages.Where(m => m.Severity != "Information").Select(m => m.Description));
        Assert.Equal(messages.OrderBy(m => plc.Messages.FindIndex(o => o.Description == m.Description)).Select(m => m.Description), messages.Select(m => m.Description));
        Assert.Equal(2, projected.CompileReport.TotalErrorCount);
        Assert.True(TiaMcpServer.Worker.BlockImportOutcomeValidator.Validate(projected, "xml"));
    }

    private static TiaMcpServer.Contracts.Block.BlockImportOutcomeInfo Completed(TiaMcpServer.Contracts.Block.CompileCheckReport report, string stage) => new()
    {
        ImportStage = "completed",
        ImportResultState = "success",
        TargetMutationCommitted = true,
        CompileStage = stage,
        CompileReport = report,
        FinalReadStage = "succeeded",
        TargetPresent = true,
        ContentRelation = "unknown",
        TemporarySourceState = "not_applicable"
    };

    [Fact]
    public void Flatten_PreservesTopLevelFields()
    {
        // Catches field loss at the extracted projection seam independently of Siemens access.
        var projection = Flatten(new[] { new Message("Missing tag", "PLC/Blocks/Main", "Error") });

        var row = Assert.Single(projection.Messages);
        Assert.Equal("Missing tag", row.Description);
        Assert.Equal("PLC/Blocks/Main", row.Path);
        Assert.Equal("Error", row.Severity);
        Assert.False(projection.WasTruncated);
    }

    [Fact]
    public void Flatten_DropsBlankContainersAndCarriesTheirPathsIntoNestedMessages()
    {
        var root = new Message("", "PLC", "Information");
        var blocks = new Message(" ", "Blocks", "Error");
        var error = new Message("Missing tag", "Main", "Error");
        error.Children.Add(new Message("Conversion", "", "Warning"));
        blocks.Children.Add(error);
        root.Children.Add(blocks);

        var projection = Flatten(new[] { root });

        Assert.Equal(new[] { "Missing tag", "Conversion" }, projection.Messages.Select(m => m.Description));
        Assert.Equal(new[] { "Error", "Warning" }, projection.Messages.Select(m => m.Severity));
        Assert.Equal(new[] { "PLC/Blocks/Main", "PLC/Blocks/Main" }, projection.Messages.Select(m => m.Path));
        Assert.False(projection.WasTruncated);
    }

    [Fact]
    public void Flatten_LiveHardwareCompileShapeYieldsOnlyTheRealErrorsWithTheirContainerChain()
    {
        // Shape observed live 2026-11-02 (hardware compile of PLC_F1): container rows with "" / " " descriptions
        // and the container's severity, then the real error texts with an empty path.
        var rail = new Message(" ", "Profilschiene_0", "Error");
        foreach (var text in new[] { "Access level needs a password.", "Confidential data protection needs a password.", "Certificate unusable." })
            rail.Children.Add(new Message(text, "", "Error"));
        var hardware = new Message("", "Hardware configuration", "Error");
        hardware.Children.Add(rail);
        var root = new Message("", "PLC_F1", "Error");
        root.Children.Add(hardware);

        var projection = Flatten(new[] { root });

        Assert.Equal(3, projection.Messages.Count);
        Assert.All(projection.Messages, m =>
        {
            Assert.Equal("PLC_F1/Hardware configuration/Profilschiene_0", m.Path);
            Assert.Equal("Error", m.Severity);
        });
    }

    [Theory]
    [InlineData("", "PLC_1", "PLC_1")]
    [InlineData("PLC_1", "", "PLC_1")]
    [InlineData("PLC_1", "  ", "PLC_1")]
    [InlineData("PLC_DP/Blocks/Main", "PLC_DP/Blocks/Helper", "PLC_DP/Blocks/Helper")]
    [InlineData("PLC_1", "Main", "PLC_1/Main")]
    [InlineData("PLC_1/Main", "Main", "PLC_1/Main")]
    [InlineData("PLC_1/Blocks", "PLC_1/Blocks/Main", "PLC_1/Blocks/Main")]
    [InlineData("PLC_1/Blocks", "PLC_1/Blocks", "PLC_1/Blocks")]
    [InlineData("PLC_F1/Hardware configuration", "PLC_F1", "PLC_F1/Hardware configuration/PLC_F1")]
    public void CompileMessagePaths_ExtendTheContainerChain(string context, string own, string expected)
        => Assert.Equal(expected, TiaMcpServer.Contracts.Block.CompileMessagePaths.Extend(context, own));

    [Theory]
    [InlineData(16, 16, false)]
    [InlineData(17, 16, true)]
    public void Flatten_BoundsDepth(int depth, int expectedRows, bool truncated)
    {
        var root = new Message("1", "", "Information");
        var current = root;
        for (var level = 2; level <= depth; level++)
        {
            var child = new Message(level.ToString(), "", "Information");
            current.Children.Add(child);
            current = child;
        }

        var projection = Flatten(new[] { root });

        Assert.Equal(expectedRows, projection.Messages.Count);
        Assert.Equal("16", projection.Messages.Last().Description);
        Assert.Equal(truncated, projection.WasTruncated);
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(201, true)]
    public void Flatten_BoundsWideMessageTrees(int count, bool truncated)
    {
        var projection = Flatten(Enumerable.Range(0, count).Select(i => new Message(i.ToString(), "", "Information")));

        Assert.Equal(200, projection.Messages.Count);
        Assert.Equal("199", projection.Messages.Last().Description);
        Assert.Equal(truncated, projection.WasTruncated);
    }

    [Theory]
    [InlineData(1024, false)]
    [InlineData(1025, true)]
    public void Flatten_BoundsEachField(int length, bool truncated)
    {
        var projection = Flatten(new[] { new Message(new string('d', length), new string('p', length), "Error") });

        var row = Assert.Single(projection.Messages);
        Assert.Equal(new string('d', 1024), row.Description);
        Assert.Equal(new string('p', 1024), row.Path);
        Assert.Equal(truncated, projection.WasTruncated);
    }

    [Fact]
    public void Flatten_SharesCharacterBudgetAcrossPlcs()
    {
        var budget = new CompileReportProjection.Budget();
        var roots = Enumerable.Range(0, 20).Select(_ => new Message(new string('d', 1000), "", "Error"));

        var first = Flatten(roots, budget);
        var second = Flatten(roots, budget);

        Assert.Equal(20, first.Messages.Count);
        Assert.Equal(12, second.Messages.Count);
        Assert.False(first.WasTruncated);
        Assert.True(second.WasTruncated);
        Assert.Equal(32000, first.Messages.Concat(second.Messages).Sum(m => m.Description.Length + m.Path.Length));
    }

    [Fact]
    public void Flatten_SharesMessageBudgetAcrossPlcs()
    {
        var budget = new CompileReportProjection.Budget();
        var roots = Enumerable.Range(0, 150).Select(_ => new Message("d", "p", "Error"));

        var first = Flatten(roots, budget);
        var second = Flatten(roots, budget);

        Assert.Equal(150, first.Messages.Count);
        Assert.Equal(50, second.Messages.Count);
        Assert.False(first.WasTruncated);
        Assert.True(second.WasTruncated);
    }

    [Fact]
    public void Flatten_SanitizesFailedPropertyReadAndRetainsChildren()
    {
        var parent = new Message("parent", "", "Information");
        parent.Children.Add(new Message("child", "child-path", "Error"));

        var projection = CompileReportProjection.Flatten(new[] { parent }, m => m.Description,
            m => m == parent ? throw new InvalidOperationException("private-path-secret") : m.Path,
            m => m.Severity, m => m.Children, new CompileReportProjection.Budget());

        Assert.True(projection.WasTruncated);
        Assert.Equal(new[] { "parent", "child" }, projection.Messages.Select(m => m.Description));
        Assert.Equal(new[] { "", "child-path" }, projection.Messages.Select(m => m.Path));
    }

    [Fact]
    public void Flatten_TruncationDoesNotSplitSurrogatePairs()
    {
        var projection = Flatten(new[] { new Message(new string('d', 1023) + "\U0001F600", "", "Error") });

        var row = Assert.Single(projection.Messages);
        Assert.Equal(new string('d', 1023), row.Description);
        Assert.True(projection.WasTruncated);
    }

    [Fact]
    public void Flatten_StopsAfterTheBudgetWithoutEnumeratingTheWholeSource()
    {
        var readCount = 0;
        IEnumerable<Message> Messages()
        {
            while (readCount < 1000)
            {
                readCount++;
                yield return new Message("d", "p", "Information");
            }
        }

        var projection = Flatten(Messages());

        Assert.Equal(200, projection.Messages.Count);
        Assert.Equal(201, readCount);
        Assert.True(projection.WasTruncated);
    }

    [Fact]
    public void Flatten_PreservesRowsWhenEnumerationFails()
    {
        IEnumerable<Message> Messages()
        {
            yield return new Message("retained", "path", "Error");
            throw new InvalidOperationException("private-project-path");
        }

        var projection = Flatten(Messages());

        Assert.Equal("retained", Assert.Single(projection.Messages).Description);
        Assert.True(projection.WasTruncated);
    }

    [Fact]
    public void Flatten_EmptyPlcAfterBudgetExhaustionIsNotMarkedTruncated()
    {
        var budget = new CompileReportProjection.Budget();
        Flatten(Enumerable.Range(0, 200).Select(_ => new Message("d", "p", "Information")), budget);

        var projection = Flatten(Array.Empty<Message>(), budget);

        Assert.Empty(projection.Messages);
        Assert.False(projection.WasTruncated);
    }

    public static IEnumerable<object[]> NestedFailureStages()
    {
        foreach (var stage in new[] { "description", "path", "severity", "children", "move-next" })
        foreach (var wrapped in new[] { false, true })
            yield return new object[] { stage, wrapped };
    }

    public static IEnumerable<object[]> NestedInfrastructureFailures()
    {
        foreach (var stage in NestedFailureStages())
        foreach (var kind in new[] { "session", "io", "cancel", "format", "derived-invalid-operation" })
            yield return new[] { stage[0], stage[1], kind };
    }

    [Theory]
    [MemberData(nameof(NestedFailureStages))]
    public void Flatten_NestedExpectedFailureRetainsBoundedSanitizedDiagnostics(string stage, bool wrapped)
    {
        var failure = Wrap(new InvalidOperationException("private-project-path-secret"), wrapped);

        var projection = ProjectWithNestedFailure(stage, failure);

        Assert.True(projection.WasTruncated);
        Assert.Equal("parent", projection.Messages[0].Description);
        Assert.Equal(stage == "children" ? 2 : 3, projection.Messages.Count);
        var child = projection.Messages[1];
        Assert.Equal(stage == "description" ? "" : "child", child.Description);
        Assert.Equal(stage == "path" ? "parent-path" : "parent-path/child-path", child.Path);
        Assert.Equal(stage == "severity" ? "Information" : "Error", child.Severity);
        if (stage != "children")
            Assert.Equal("grandchild", projection.Messages[2].Description);
        var json = System.Text.Json.JsonSerializer.Serialize(projection.Messages);
        Assert.DoesNotContain("private-project-path-secret", json);
        Assert.True(json.Length < 60000);
    }

    [Theory]
    [MemberData(nameof(NestedInfrastructureFailures))]
    public void Flatten_NestedInfrastructureFailurePropagates(string stage, bool wrapped, string kind)
    {
        Exception failure = kind switch
        {
            "session" => new NonRecoverableException("private-session-path"),
            "io" => new IOException("private-io-path"),
            "cancel" => new OperationCanceledException("private-cancel-path"),
            "format" => new FormatException("private-format-path"),
            "derived-invalid-operation" => new UnexpectedInvalidOperationException(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var propagated = Assert.ThrowsAny<Exception>(() => ProjectWithNestedFailure(stage, Wrap(failure, wrapped)));

        // The generic seam may preserve the reflection wrapper or unwrap it; neither may
        // turn the underlying infrastructure/interruption failure into partial success.
        if (propagated is TargetInvocationException reflectionFailure)
            propagated = reflectionFailure.InnerException!;
        Assert.Same(failure, propagated);
    }

    private static Exception Wrap(Exception failure, bool wrapped) =>
        wrapped ? new TargetInvocationException(failure) : failure;

    private static CompileReportProjection.Projection ProjectWithNestedFailure(string stage, Exception failure)
    {
        var parent = new Message("parent", "parent-path", "Information");
        var child = new Message("child", "child-path", "Error");
        child.Children.Add(new Message("grandchild", "grandchild-path", "Warning"));
        parent.Children.Add(child);

        IEnumerable<Message> ChildrenThenFailure()
        {
            foreach (var message in child.Children)
                yield return message;
            // This is thrown by MoveNext only after real nested diagnostics were retained.
            throw failure;
        }

        return CompileReportProjection.Flatten(new[] { parent },
            m => ReferenceEquals(m, child) && stage == "description" ? throw failure : m.Description,
            m => ReferenceEquals(m, child) && stage == "path" ? throw failure : m.Path,
            m => ReferenceEquals(m, child) && stage == "severity" ? throw failure : m.Severity,
            m => ReferenceEquals(m, child)
                ? stage == "children" ? throw failure : stage == "move-next" ? ChildrenThenFailure() : m.Children
                : m.Children,
            new CompileReportProjection.Budget());
    }

    private sealed class UnexpectedInvalidOperationException : InvalidOperationException { }

    private static CompileReportProjection.Projection Flatten(IEnumerable<Message> roots,
        CompileReportProjection.Budget? budget = null) =>
        CompileReportProjection.Flatten(roots, m => m.Description, m => m.Path,
            m => m.Severity, m => m.Children, budget ?? new CompileReportProjection.Budget());

    private sealed record Message(string Description, string Path, string Severity)
    {
        public List<Message> Children { get; } = new();
    }
}
