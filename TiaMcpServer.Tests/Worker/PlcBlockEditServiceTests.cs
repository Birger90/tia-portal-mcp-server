using System.Globalization;
using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// The worker's move_block, copy_block, move_tag_table, move_tag wrapping and update_db_member
/// against the offline object doubles: the ExclusiveAccess transaction, the rollback, the re-import
/// fallback and the recovery folder. One class, so the static recovery roots are never shared.
/// </summary>
public sealed class PlcBlockEditServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tia-mcp-block-edit-tests-" + Guid.NewGuid().ToString("N"));

    public PlcBlockEditServiceTests()
        => PlcRoundTrip.RecoveryRoots = new[] { (Path.Combine(_root, "recovery"), PlcRecoveryStore.LocalAppDataLocation) };

    public void Dispose()
    {
        PlcRoundTrip.RecoveryRoots = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string MotorXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Document>
          <Engineering version="V21" />
          <SW.Blocks.FB ID="0">
            <AttributeList>
              <Interface>
                <Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
                  <Section Name="Static">
                    <Member Name="Count" Datatype="Int" />
                  </Section>
                </Sections>
              </Interface>
              <Name>Motor</Name>
              <Number>5</Number>
              <ProgrammingLanguage>SCL</ProgrammingLanguage>
            </AttributeList>
          </SW.Blocks.FB>
        </Document>
        """;

    private sealed class Fixture
    {
        public SiemensProject Project { get; } = new() { Path = new FileInfo(@"C:\p\p.ap21") };
        public TiaPortal Portal { get; } = new();
        public PlcSoftware Plc { get; } = new() { Name = "PLC_1" };
        public PlcBlockUserGroup Line { get; } = new() { Name = "Line" };
        public FB Motor { get; } = new() { Name = "Motor", Number = 5, ExportDocument = MotorXml };
        public PlcTagTable Table { get; }
        public PlcTagTableGroup LineFolder { get; } = new() { Name = "Line" };
        public GlobalDB Settings { get; } = new() { Name = "Settings", Number = 1 };

        public Fixture()
        {
            var device = new Device { Name = "Device" };
            device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = Plc } });
            Project.Devices.Items.Add(device);
            Plc.BlockGroup.Blocks.Add(Motor);
            Plc.BlockGroup.Blocks.Add(Settings);
            Plc.BlockGroup.Groups.Items.Add(Line);
            Table = Plc.TagTableGroup.TagTables.Create("Motors");
            Table.Tags.Create("Speed", "Int", "%MW10");
            Table.Tags.Create("Run", "Bool", "%M0.0");
            Table.UserConstants.Create("Max");
            Plc.TagTableGroup.Groups.Items.Add(LineFolder);
        }

        public Transaction Transaction => Assert.Single(Portal.Transactions);
    }

    private static PlcBlockEditFailureInfo Evidence(WorkerOperationException exception)
    {
        Assert.NotNull(exception.PlcDownloadOutcome);
        return JsonSerializer.Deserialize<PlcBlockEditFailureInfo>(exception.PlcDownloadOutcome!, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
    }

    private static PlcBlockEditRequestInfo MoveTable() => new() { PlcName = "PLC_1", TableName = "Motors", TargetFolderPath = "/Line" };

    // ---- move_tag_table ---------------------------------------------------------------------

    [Fact]
    public void MoveTagTable_DeletesAndImportsInOneCommittedTransaction()
    {
        var fixture = new Fixture();

        var result = TagTableMoveService.Move(fixture.Portal, fixture.Project, MoveTable());

        Assert.Equal(("Motors", "/", "/Line", 2, 1), (result.TableName, result.SourceFolderPath, result.TargetFolderPath, result.TagCount, result.UserConstantCount));
        Assert.Null(fixture.Plc.TagTableGroup.TagTables.Find("Motors"));
        Assert.NotNull(fixture.LineFolder.TagTables.Find("Motors"));
        Assert.True(fixture.Transaction.Committed);
        Assert.Single(fixture.Portal.ExclusiveAccessCalls);
    }

    [Fact]
    public void MoveTagTable_TargetImportFailureIsRolledBackAndNothingIsReimported()
    {
        var fixture = new Fixture();
        var source = fixture.Plc.TagTableGroup.TagTables;
        fixture.Portal.TransactionRollback = () => { if (!source.Items.Contains(fixture.Table)) source.Items.Add(fixture.Table); };
        fixture.LineFolder.TagTables.ImportFailures.Enqueue(new EngineeringException("The import was refused."));

        var failure = Assert.Throws<WorkerOperationException>(() => TagTableMoveService.Move(fixture.Portal, fixture.Project, MoveTable()));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, failure.FailureCategory);
        Assert.Contains("rolled back", failure.Message, StringComparison.Ordinal);
        var evidence = Evidence(failure);
        Assert.Equal((PlcBlockEditTransactionStates.RolledBack, PlcBlockEditOriginalStates.Unchanged), (evidence.Transaction, evidence.OriginalState));
        Assert.Null(evidence.Recovery);
        Assert.Same(fixture.Table, source.Find("Motors"));
        Assert.Empty(source.ImportedDocuments);
        Assert.False(fixture.Transaction.Committed);
    }

    [Fact]
    public void MoveTagTable_WithoutARestoringRollbackTheExportIsReimportedIntoTheSource()
    {
        var fixture = new Fixture();
        fixture.LineFolder.TagTables.ImportFailures.Enqueue(new EngineeringException("The import was refused."));

        var failure = Assert.Throws<WorkerOperationException>(() => TagTableMoveService.Move(fixture.Portal, fixture.Project, MoveTable()));

        var evidence = Evidence(failure);
        Assert.Equal(PlcBlockEditOriginalStates.RestoredByReimport, evidence.OriginalState);
        var restored = fixture.Plc.TagTableGroup.TagTables.Find("Motors");
        Assert.NotNull(restored);
        Assert.Equal(new[] { "Speed", "Run" }, restored!.Tags.Select(t => t.Name));
        Assert.False(Directory.Exists(Path.Combine(_root, "recovery")));
    }

    [Fact]
    public void MoveTagTable_FailedRestoreKeepsTheExportInARecoveryFolder()
    {
        var fixture = new Fixture();
        fixture.LineFolder.TagTables.ImportFailures.Enqueue(new EngineeringException("The import was refused."));
        fixture.Plc.TagTableGroup.TagTables.ImportFailures.Enqueue(new IOException("The re-import could not read the file."));

        var failure = Assert.Throws<WorkerOperationException>(() => TagTableMoveService.Move(fixture.Portal, fixture.Project, MoveTable()));

        var evidence = Evidence(failure);
        Assert.Equal(PlcBlockEditOriginalStates.Missing, evidence.OriginalState);
        var recovery = Assert.IsType<PlcRecoveryFileInfo>(evidence.Recovery);
        Assert.StartsWith(Path.Combine(_root, "recovery"), recovery.DocumentPath, StringComparison.Ordinal);
        Assert.Equal("Motors\ntag:Speed\ntag:Run\nconst:Max", File.ReadAllText(recovery.DocumentPath));
        Assert.Contains("\"objectName\": \"Motors\"", File.ReadAllText(recovery.MetadataPath!), StringComparison.Ordinal);
        Assert.Contains(recovery.DocumentPath, failure.Message, StringComparison.Ordinal);
        Assert.Null(evidence.RecoveryError);
    }

    [Fact]
    public void MoveTagTable_UnwritableRecoveryRootKeepsTheTemporaryExportInsteadOfDeletingIt()
    {
        var fixture = new Fixture();
        Directory.CreateDirectory(_root);
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "not a directory");
        PlcRoundTrip.RecoveryRoots = new[] { (blocked, PlcRecoveryStore.LocalAppDataLocation) };
        fixture.LineFolder.TagTables.ImportFailures.Enqueue(new EngineeringException("The import was refused."));
        fixture.Plc.TagTableGroup.TagTables.ImportFailures.Enqueue(new EngineeringException("The re-import was refused."));

        var failure = Assert.Throws<WorkerOperationException>(() => TagTableMoveService.Move(fixture.Portal, fixture.Project, MoveTable()));

        var evidence = Evidence(failure);
        var kept = Assert.IsType<PlcRecoveryFileInfo>(evidence.Recovery);
        Assert.Equal(PlcRecoveryStore.TempLocation, kept.Location);
        Assert.True(File.Exists(kept.DocumentPath), "the only copy of the export stays on disk");
        Assert.Contains("localAppData", evidence.RecoveryError, StringComparison.Ordinal);
        Directory.Delete(kept.Directory, recursive: true);
    }

    [Fact]
    public void MoveTagTable_WithASafetyTag_MovesWhereTiaPortalDecides()
    {
        // Not stricter than TIA Portal: a fail-safe tag moves under the safety login rule. The doubles offer no
        // SafetyAdministration, so TIA Portal decides and the move runs.
        var fixture = new Fixture();
        fixture.Table.Tags.Find("Run")!.IsSafety = true;

        var result = TagTableMoveService.Move(fixture.Portal, fixture.Project, MoveTable());

        Assert.Equal(2, result.TagCount);
        Assert.NotNull(fixture.LineFolder.TagTables.Find("Motors"));
        Assert.True(fixture.Transaction.Committed);
    }

    [Fact]
    public void MoveTagTable_BindsTheImportMemberBeforeTheDelete()
    {
        var unbound = Assert.Throws<WorkerOperationException>(() => TagTableMoveService.BindImport(new PlcTagComposition()));
        Assert.Contains("nothing was changed", unbound.Message, StringComparison.Ordinal);

        var source = File.ReadAllText(TiaMcpServer.Tests.TestSupport.RepositoryFiles.Path("TiaMcpServer.OpennessWorker", "Openness", "Plc", "TagTableMoveService.cs"));
        Assert.True(source.IndexOf("BindImport(target.TagTables)", StringComparison.Ordinal) < source.IndexOf("table.Delete()", StringComparison.Ordinal));
        Assert.True(source.IndexOf("BindImport(source.TagTables)", StringComparison.Ordinal) < source.IndexOf("table.Delete()", StringComparison.Ordinal));
    }

    // ---- move_block / copy_block --------------------------------------------------------------

    private static PlcBlockEditRequestInfo MoveMotor() => new() { BlockPath = "PLC_1/Blocks/Motor", TargetGroupPath = "PLC_1/Blocks/Line" };

    [Fact]
    public void MoveBlock_DeletesAndImportsInOneCommittedTransaction()
    {
        var fixture = new Fixture();
        fixture.Line.Blocks.ImportResult.Add(new FB { Name = "Motor", Number = 5 });

        var result = PlcBlockEditService.MoveBlock(fixture.Portal, fixture.Project, MoveMotor());

        Assert.Equal(("PLC_1/Blocks/Motor", "PLC_1/Blocks/Line/Motor"), (result.SourceBlockPath, result.Block.BlockPath));
        Assert.True(fixture.Motor.Deleted);
        Assert.Equal(MotorXml, Assert.Single(fixture.Line.Blocks.ImportedDocuments));
        Assert.True(fixture.Transaction.Committed);
    }

    [Fact]
    public void MoveBlock_ReadBackMismatchRollsBackAndLeavesTheSource()
    {
        var fixture = new Fixture();
        var source = fixture.Plc.BlockGroup.Blocks;
        fixture.Portal.TransactionRollback = () =>
        {
            fixture.Line.Blocks.Items.Clear();
            if (!source.Items.Contains(fixture.Motor)) source.Add(fixture.Motor);
        };
        fixture.Line.Blocks.ImportResult.Add(new FB { Name = "Motor", Number = 99 });

        var failure = Assert.Throws<WorkerOperationException>(() => PlcBlockEditService.MoveBlock(fixture.Portal, fixture.Project, MoveMotor()));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, failure.FailureCategory);
        Assert.Equal(PlcBlockEditOriginalStates.Unchanged, Evidence(failure).OriginalState);
        Assert.Same(fixture.Motor, source.Find("Motor"));
        Assert.Null(fixture.Line.Blocks.Find("Motor"));
        Assert.Empty(source.ImportedDocuments);
    }

    [Fact]
    public void MoveBlock_TargetImportFailureWithoutRestoringRollbackReimportsTheSource()
    {
        var fixture = new Fixture();
        fixture.Line.Blocks.ImportFailures.Enqueue(new EngineeringException("The import was refused."));
        fixture.Plc.BlockGroup.Blocks.ImportResult.Add(new FB { Name = "Motor", Number = 5 });

        var failure = Assert.Throws<WorkerOperationException>(() => PlcBlockEditService.MoveBlock(fixture.Portal, fixture.Project, MoveMotor()));

        var evidence = Evidence(failure);
        Assert.Equal((PlcBlockEditOriginalStates.RestoredByReimport, "block", "Motor"), (evidence.OriginalState, evidence.ObjectKind, evidence.ObjectName));
        Assert.Equal(MotorXml, Assert.Single(fixture.Plc.BlockGroup.Blocks.ImportedDocuments));
        Assert.NotNull(fixture.Plc.BlockGroup.Blocks.Find("Motor"));
    }

    [Fact]
    public void MoveBlock_FailedRestoreWritesTheExportToARecoveryFolder()
    {
        var fixture = new Fixture();
        fixture.Line.Blocks.ImportFailures.Enqueue(new EngineeringException("The import was refused."));
        fixture.Plc.BlockGroup.Blocks.ImportFailures.Enqueue(new ArgumentException("Illegal characters in path."));

        var failure = Assert.Throws<WorkerOperationException>(() => PlcBlockEditService.MoveBlock(fixture.Portal, fixture.Project, MoveMotor()));

        var evidence = Evidence(failure);
        Assert.Equal(PlcBlockEditOriginalStates.Missing, evidence.OriginalState);
        var recovery = Assert.IsType<PlcRecoveryFileInfo>(evidence.Recovery);
        Assert.Equal(MotorXml, File.ReadAllText(recovery.DocumentPath));
        Assert.Contains("ArgumentException", failure.Message, StringComparison.Ordinal);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, failure.FailureCategory);
    }

    [Fact]
    public void MoveBlock_TransactionDisposeFailureReportsAnUnknownOutcome()
    {
        var fixture = new Fixture();
        fixture.Line.Blocks.ImportResult.Add(new FB { Name = "Motor", Number = 5 });
        fixture.Portal.TransactionDisposeFailure = new EngineeringException("Commit failed.");

        var failure = Assert.Throws<WorkerOperationException>(() => PlcBlockEditService.MoveBlock(fixture.Portal, fixture.Project, MoveMotor()));

        var evidence = Evidence(failure);
        Assert.Equal((PlcBlockEditTransactionStates.OutcomeUnknown, PlcBlockEditOriginalStates.MovedToTarget), (evidence.Transaction, evidence.OriginalState));
    }

    [Fact]
    public void MoveBlock_RefusesAKnowHowProtectedBlockBeforeAnyTransaction()
    {
        var fixture = new Fixture();
        fixture.Motor.IsKnowHowProtected = true;

        var failure = Assert.Throws<WorkerOperationException>(() => PlcBlockEditService.MoveBlock(fixture.Portal, fixture.Project, MoveMotor()));

        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, failure.FailureCategory);
        Assert.Empty(fixture.Portal.ExclusiveAccessCalls);
        Assert.False(fixture.Motor.Deleted);
    }

    [Fact]
    public void CopyBlock_SetsTheNumberInsideTheTransaction()
    {
        var fixture = new Fixture();
        var copy = new FB { Name = "Motor_Copy", Number = 5, AutoNumber = true };
        fixture.Line.Blocks.ImportResult.Add(copy);

        var result = PlcBlockEditService.CopyBlock(fixture.Portal, fixture.Project,
            new PlcBlockEditRequestInfo { BlockPath = "PLC_1/Blocks/Motor", NewName = "Motor_Copy", TargetGroupPath = "PLC_1/Blocks/Line", Number = 7 });

        Assert.Equal(("PLC_1/Blocks/Line/Motor_Copy", 7, false), (result.Block.BlockPath, copy.Number, copy.AutoNumber));
        Assert.Contains("<Name>Motor_Copy</Name>", Assert.Single(fixture.Line.Blocks.ImportedDocuments), StringComparison.Ordinal);
        Assert.True(fixture.Transaction.Committed);
        Assert.False(fixture.Motor.Deleted);
    }

    [Fact]
    public void CopyBlock_IgnoredNumberRollsTheCopyBackAndSaysSo()
    {
        var fixture = new Fixture();
        fixture.Line.Blocks.ImportResult.Add(new FB { Name = "Motor_Copy", Number = 5, IgnoreNumberWrites = true });
        fixture.Portal.TransactionRollback = () => fixture.Line.Blocks.Items.Clear();

        var failure = Assert.Throws<WorkerOperationException>(() => PlcBlockEditService.CopyBlock(fixture.Portal, fixture.Project,
            new PlcBlockEditRequestInfo { BlockPath = "PLC_1/Blocks/Motor", NewName = "Motor_Copy", TargetGroupPath = "PLC_1/Blocks/Line", Number = 7 }));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, failure.FailureCategory);
        var evidence = Evidence(failure);
        Assert.Equal((PlcBlockEditOriginalStates.CopyAbsent, "Motor_Copy"), (evidence.OriginalState, evidence.ObjectName));
        Assert.Null(evidence.Recovery);
        Assert.Null(fixture.Line.Blocks.Find("Motor_Copy"));
    }

    // ---- update_db_member -------------------------------------------------------------------

    private static Member DbMember(Fixture fixture, string path, object startValue)
    {
        var member = new Member { Name = path };
        member.Attributes["StartValue"] = startValue;
        fixture.Settings.Interface.Members.ByPath[path] = member;
        return member;
    }

    [Fact]
    public void UpdateDbMember_AnEqualNumberInAnotherSpellingIsNotTheKeptPreviousValue()
    {
        var fixture = new Fixture();
        var member = DbMember(fixture, "Limit", "1.5");
        member.StoredAs["1.50"] = "1.5";

        var result = PlcBlockEditService.UpdateDbMember(fixture.Project, new PlcBlockEditRequestInfo { BlockPath = "PLC_1/Blocks/Settings", MemberPath = "Limit", StartValue = "1.50" });

        var change = Assert.Single(result.Changes);
        Assert.Equal(("1.5", "1.50", "1.5"), (change.Previous, change.Requested, change.Observed));
    }

    [Fact]
    public void UpdateDbMember_AnIgnoredWriteFailsAsPostcondition()
    {
        var fixture = new Fixture();
        DbMember(fixture, "Limit", "1").IgnoredWrites.Add("StartValue");

        var failure = Assert.Throws<WorkerOperationException>(() => PlcBlockEditService.UpdateDbMember(fixture.Project,
            new PlcBlockEditRequestInfo { BlockPath = "PLC_1/Blocks/Settings", MemberPath = "Limit", StartValue = "2" }));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, failure.FailureCategory);
    }

    [Fact]
    public void AttributeTextIsCultureInvariant()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.5", PlcBlockEditService.Text(1.5));
            Assert.Equal("1234567", PlcBlockEditService.Text(1234567));
            Assert.Equal("True", PlcBlockEditService.Text(true));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // ---- move_tag in a transaction ------------------------------------------------------------

    private static PlcTagMoveRequestInfo MoveSpeed() => new()
    {
        PlcName = "PLC_1", TableName = "Motors", Name = "Speed", TargetTableName = "Drives", TemporaryName = "__mcp_move_test",
    };

    [Fact]
    public void MoveTag_RunsTheReCreateInOneCommittedTransaction()
    {
        var fixture = new Fixture();
        fixture.Plc.TagTableGroup.TagTables.Create("Drives");

        var result = PlcRoundTrip.MoveTag(fixture.Portal, fixture.Project, MoveSpeed(), () => TagMutationService.MoveTag(fixture.Project, MoveSpeed()));

        Assert.Equal("Drives", result.TargetTableName);
        Assert.True(fixture.Transaction.Committed);
    }

    [Fact]
    public void MoveTag_FailureStatesTheRollbackAndTheStateAfterIt()
    {
        var fixture = new Fixture();
        fixture.Plc.TagTableGroup.TagTables.Create("Drives");

        var failure = Assert.Throws<WorkerOperationException>(() => PlcRoundTrip.MoveTag(fixture.Portal, fixture.Project, MoveSpeed(),
            () => throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "move_tag: the read-back does not match.", new[] { "step" })));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, failure.FailureCategory);
        Assert.Contains("rolled the transaction back", failure.Message, StringComparison.Ordinal);
        Assert.Contains("nothing changed", failure.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "step" }, failure.Warnings);
        Assert.Null(failure.PlcDownloadOutcome);
        Assert.False(fixture.Transaction.Committed);
    }
}
