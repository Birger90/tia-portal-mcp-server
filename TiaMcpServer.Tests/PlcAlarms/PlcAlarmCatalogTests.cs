using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.PlcAlarms;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests.PlcAlarms;

/// <summary>
/// The alarm, text list and supervision operations in plc_read/plc_write: classification in the shared policy and
/// capability catalogs, the per-operation field sets and the shape rules checked before any worker call.
/// </summary>
public sealed class PlcAlarmCatalogTests
{
    private const string Hash = "cells:sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static PlcAlarmCellEditRequest Edit(string cell = "D2", string? expected = "Stopped", string? value = "Halted", string? kind = null)
        => new() { Sheet = "Text lists", Cell = cell, Expected = expected, Value = value, Kind = kind };

    private static PlcOperationRequest Op(string operation, string id = "a") => new() { OperationId = id, Operation = operation };

    [Fact]
    public void Methods_AreClassifiedOnce_InThePolicyCatalog()
    {
        foreach (var method in PlcAlarmMethods.ObserveMethods)
            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(method));
        foreach (var method in PlcAlarmMethods.TemporaryExportMethods)
            Assert.Equal(OperationCapability.TemporaryExport, OperationPolicyCatalog.GetCapability(method));
        foreach (var method in PlcAlarmMethods.Write)
        {
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, method));
            Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
        }

        Assert.All(PlcAlarmMethods.Read, method => Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method)));
        Assert.Equal(PlcAlarmMethods.All.Count, PlcAlarmMethods.All.Distinct().Count());
        Assert.Equal(PlcAlarmMethods.Read.OrderBy(n => n), PlcOperationCatalog.ReadOperationNames.Where(PlcAlarmMethods.IsWorkerMethod).OrderBy(n => n));
        Assert.Equal(PlcAlarmMethods.Write.OrderBy(n => n), PlcOperationCatalog.WriteOperationNames.Where(PlcAlarmMethods.IsWorkerMethod).OrderBy(n => n));
    }

    [Fact]
    public void Methods_AreGatedAsPlcTools_ExceptTheStandaloneAlarmClasses()
    {
        foreach (var method in PlcAlarmMethods.Read)
            Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(method));
        foreach (var method in PlcAlarmMethods.Write.Except(PlcAlarmMethods.StandaloneOnlyOperations))
        {
            Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(method));
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method));
        }

        foreach (var method in PlcAlarmMethods.StandaloneOnlyOperations)
        {
            Assert.Equal(method, ProjectCapabilityCatalog.OperationFor(method));
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method));
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method));
            Assert.Equal(ProjectCapabilityApplicabilities.StandaloneOnly,
                ProjectCapabilityCatalog.Describe(ProjectContainerKinds.LocalSession).Single(item => item.Operation == method).Applicability);
        }
    }

    [Fact]
    public void Reads_ValidateTheirFields()
    {
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { Op(PlcAlarmMethods.ListTextLists) }).IsValid);
        var valid = Op(PlcAlarmMethods.ReadAlarmTextWorkbook);
        valid.ExportOption = "OptionA";
        valid.Cultures = new[] { "en-US", "de-DE" };
        valid.MaxElements = 10;
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { valid }).IsValid);

        var missing = PlcOperationCatalog.ValidateRead(new[] { Op(PlcAlarmMethods.ReadAlarmTextWorkbook) });
        Assert.Contains("missing required field(s): exportOption", missing.Error);
        var foreign = Op(PlcAlarmMethods.ListTextLists);
        foreign.FilePath = @"C:\x.xlsx";
        Assert.Contains("'filePath' is not valid for list_text_lists", PlcOperationCatalog.ValidateRead(new[] { foreign }).Error);
        var bounds = Op(PlcAlarmMethods.ReadTextListWorkbook);
        bounds.MaxElements = 5001;
        bounds.Cultures = new[] { "en-US", "EN-us" };
        var error = PlcOperationCatalog.ValidateRead(new[] { bounds }).Error!;
        Assert.Contains("maxElements must be between 1 and 5000", error);
        Assert.Contains("cultures must not repeat", error);
        Assert.Contains("only together with textListNames", error);
        var narrowed = Op(PlcAlarmMethods.ReadTextListWorkbook);
        narrowed.TextListNames = new[] { "USER_1" };
        narrowed.Cultures = new[] { "de-DE" };
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { narrowed }).IsValid);
        var supervision = Op(PlcAlarmMethods.ReadSupervisionWorkbook);
        supervision.BlockPath = "PLC_1/ProDiag_FB";
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { supervision }).IsValid);
        Assert.Contains("is a write operation", PlcOperationCatalog.ValidateRead(new[] { Op(PlcAlarmMethods.ExportAlarmClasses) }).Error);
    }

    [Fact]
    public void UpdateTextList_NeedsAChange_AndCommentWithCulture()
    {
        var empty = Op(PlcAlarmMethods.UpdateTextList);
        empty.TextListName = "USER_1";
        Assert.Contains("needs newName, listRange or comment", PlcOperationCatalog.ValidateWrite(new[] { empty }).Error);
        var comment = Op(PlcAlarmMethods.UpdateTextList);
        comment.TextListName = "USER_1";
        comment.Comment = "x";
        Assert.Contains("comment and commentCulture go together", PlcOperationCatalog.ValidateWrite(new[] { comment }).Error);
        var range = Op(PlcAlarmMethods.UpdateTextList);
        range.TextListName = "USER_1";
        range.ListRange = "Not a member";
        Assert.Contains("listRange must be an enum member name", PlcOperationCatalog.ValidateWrite(new[] { range }).Error);
        var ok = Op(PlcAlarmMethods.UpdateTextList);
        ok.TextListName = "USER_1";
        ok.NewName = "MOTOR";
        ok.Comment = "";
        ok.CommentCulture = "de-DE";
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { ok }).IsValid);
    }

    [Fact]
    public void FileOperations_RefuseDeviceAndRelativePaths_AndAcceptUncShares()
    {
        var unc = Op(PlcAlarmMethods.ExportTextLists);
        unc.FilePath = @"\\server\share\x.xlsx";
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { unc }).IsValid);
        foreach (var path in new[] { @"\\?\C:\x.xlsx", @"\\.\x.xlsx", "x.xlsx" })
        {
            var export = Op(PlcAlarmMethods.ExportTextLists);
            export.FilePath = path;
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { export }).IsValid, path);
        }

        var classes = Op(PlcAlarmMethods.ExportAlarmClasses);
        classes.FilePath = @"C:\Exports\AlarmClasses.DAT";
        classes.Overwrite = true;
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { classes }).IsValid);
        var withPlc = Op(PlcAlarmMethods.ImportAlarmClasses);
        withPlc.FilePath = @"C:\Exports\AlarmClasses.DAT";
        withPlc.PlcName = "PLC_1";
        Assert.Contains("'plcName' is not valid for import_alarm_classes", PlcOperationCatalog.ValidateWrite(new[] { withPlc }).Error);
        var option = Op(PlcAlarmMethods.ImportTextLists);
        option.FilePath = @"C:\Exports\TextLists.xlsx";
        option.ImportOption = "override";
        Assert.Contains("importOption must be one of None, Override", PlcOperationCatalog.ValidateWrite(new[] { option }).Error);
    }

    [Fact]
    public void CellUpdates_RequireValidEditsAndHash()
    {
        var missing = Op(PlcAlarmMethods.UpdateTextListCells);
        missing.ExpectedWorkbookHash = Hash;
        Assert.Contains("cellEdits is required", PlcOperationCatalog.ValidateWrite(new[] { missing }).Error);

        var bad = Op(PlcAlarmMethods.UpdateTextListCells);
        bad.ExpectedWorkbookHash = "xml:sha256:" + new string('a', 64);
        bad.CellEdits = new[] { Edit(cell: "d2"), Edit(expected: null), Edit(cell: "E2", kind: "boolean"), Edit(cell: "F2", value: "1,5", kind: "number"), Edit(cell: "D2") };
        var error = PlcOperationCatalog.ValidateWrite(new[] { bad }).Error!;
        Assert.Contains("expectedWorkbookHash must be 'cells:sha256:", error);
        Assert.Contains("cellEdits[0].cell 'd2'", error);
        Assert.Contains("cellEdits[1].expected is required", error);
        Assert.Contains("cellEdits[2].kind must be one of string, number", error);
        Assert.Contains("cellEdits[3].value '1,5' is not an invariant number", error);
        Assert.Contains("cellEdits[4] edits Text lists!D2 a second time", error);

        var ok = Op(PlcAlarmMethods.UpdateTextListCells);
        ok.ExpectedWorkbookHash = Hash;
        ok.CellEdits = new[] { Edit(), Edit(cell: "B5", expected: "", value: "3", kind: "number") };
        Assert.Contains("update_text_list_cells needs textListNames", PlcOperationCatalog.ValidateWrite(new[] { ok }).Error);
        ok.TextListNames = new[] { "USER_1" };
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { ok }).IsValid);

        // The other cell updates have no text list names.
        var texts = Op(PlcAlarmMethods.UpdateAlarmTextCells);
        texts.ExportOption = "All";
        texts.ExpectedWorkbookHash = Hash;
        texts.CellEdits = new[] { Edit() };
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { texts }).IsValid);
    }

    /// <summary>L2: startColumn pages one sheet by column; it takes upper-case column letters A to XFD.</summary>
    [Fact]
    public void WorkbookReads_ValidateStartColumn()
    {
        PlcOperationRequest Read(string? sheet, string column)
        {
            var read = Op(PlcAlarmMethods.ReadTextListWorkbook);
            read.Sheet = sheet;
            read.StartColumn = column;
            return read;
        }

        Assert.Contains("startColumn pages one sheet", PlcOperationCatalog.ValidateRead(new[] { Read(null, "B") }).Error);
        foreach (var bad in new[] { "b", "A1", "XFE", "AAAA", "" })
            Assert.Contains("startColumn must be upper-case column letters from A to XFD", PlcOperationCatalog.ValidateRead(new[] { Read("Text lists", bad) }).Error);
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { Read("Text lists", "XFD") }).IsValid);
        Assert.Equal("C", PlcAlarmRequests.From(Read("Text lists", "C")).StartColumn);
        Assert.Equal("A", PlcAlarmRequests.From(Op(PlcAlarmMethods.ReadTextListWorkbook)).StartColumn);
    }

    /// <summary>L2: workbook reads of one plc_read call share the document budget; one read keeps the value budget.</summary>
    [Fact]
    public void WorkbookReads_ShareTheDocumentBudget()
    {
        var item = StructuredOperationBatchPayloadBudget.MaxItemChars - PlcAlarmPolicy.ResultHeadroomChars;
        Assert.Equal(item, PlcAlarmRequests.ResultBudget());
        Assert.Equal(item, PlcAlarmRequests.ResultBudget(0));
        Assert.Equal(item, PlcAlarmRequests.ResultBudget(2));
        foreach (var reads in new[] { 3, 5, 10, 26 })
        {
            var share = PlcAlarmRequests.ResultBudget(reads);
            Assert.True(share < item);
            Assert.True(reads * (share + PlcAlarmPolicy.ResultHeadroomChars) + PlcAlarmRequests.DocumentHeadroomChars <= StructuredOperationBatchPayloadBudget.MaxDocumentChars, reads.ToString());
        }

        // From 27 reads on (defaults) the floor applies; the document budget may then omit whole results with guidance.
        Assert.Equal(PlcAlarmPolicy.MinResultChars, PlcAlarmRequests.ResultBudget(27));
        Assert.Equal(PlcAlarmPolicy.MinResultChars, PlcAlarmRequests.ResultBudget(50));
        Assert.Equal(1234, PlcAlarmRequests.From(Op(PlcAlarmMethods.ReadTextListWorkbook), maxResultChars: 1234).MaxResultChars);

        // A plc_read call counts its three kinds of workbook reads only; other reads and unparsed items do not share.
        var call = new PlcOperationRequest?[]
        {
            Op(PlcAlarmMethods.ReadTextListWorkbook), Op(PlcAlarmMethods.ReadAlarmTextWorkbook), Op(PlcAlarmMethods.ReadSupervisionWorkbook),
            Op(PlcAlarmMethods.ListTextLists), Op("get_block_content"), null,
        };
        Assert.Equal(PlcAlarmRequests.ResultBudget(3), PlcAlarmRequests.CallResultBudget(call));
        Assert.Equal(item, PlcAlarmRequests.CallResultBudget(call.Skip(1).Take(2)));
        Assert.Equal(item, PlcAlarmRequests.CallResultBudget(null));
    }

    [Fact]
    public void CellUpdates_CannotFollowAnotherChangeOfTheSamePlc()
    {
        var rename = Op(PlcAlarmMethods.UpdateTextList, "rename");
        rename.PlcName = "PLC_1";
        rename.TextListName = "USER_1";
        rename.NewName = "MOTOR";
        var cells = Op(PlcAlarmMethods.UpdateTextListCells, "cells");
        cells.PlcName = "PLC_1";
        cells.TextListNames = new[] { "USER_1" };
        cells.ExpectedWorkbookHash = Hash;
        cells.CellEdits = new[] { Edit() };
        var export = Op(PlcAlarmMethods.ExportTextLists, "export");
        export.PlcName = "PLC_1";
        export.FilePath = @"C:\Exports\TextLists.xlsx";
        var other = Op(PlcAlarmMethods.DeleteTextList, "other");
        other.PlcName = "PLC_2";
        other.TextListName = "USER_9";

        Assert.Contains("follows 'update_text_list' (operationId 'rename')", PlcOperationCatalog.ValidateWrite(new[] { rename, cells }).Error);
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { export, other, cells }).IsValid);
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { cells, rename }).IsValid);
        var unknownPlc = Op(PlcAlarmMethods.ImportAlarmClasses, "classes");
        unknownPlc.FilePath = @"C:\Exports\AlarmClasses.dat";
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { unknownPlc, cells }).IsValid);
    }

    [Fact]
    public void Requests_CarryEveryFieldToTheWorker()
    {
        var item = Op(PlcAlarmMethods.UpdateTextListCells);
        item.PlcName = "PLC_1";
        item.TextListNames = new[] { "USER_1" };
        item.Cultures = new[] { "en-US" };
        item.ImportOption = "None";
        item.ExpectedWorkbookHash = Hash;
        item.CellEdits = new[] { Edit(kind: "string") };

        var request = PlcAlarmRequests.From(item);
        Assert.Equal(("PLC_1", "USER_1", "en-US", "None", Hash), (request.PlcName, request.TextListNames!.Single(), request.Cultures!.Single(), request.ImportOption, request.ExpectedWorkbookHash));
        var edit = Assert.Single(request.CellEdits!);
        Assert.Equal(("Text lists", "D2", "Stopped", "Halted", "string"), (edit.Sheet, edit.Cell, edit.Expected, edit.Value, edit.Kind));
        Assert.Equal(PlcAlarmPolicy.DefaultMaxRows, request.MaxRows);

        var probe = PlcAlarmRequests.WorkbookProbe(item);
        Assert.Null(probe.CellEdits);
        Assert.Null(probe.ExpectedWorkbookHash);
        Assert.Equal(1, probe.MaxRows);
        Assert.Equal(("Text lists", "D2"), (probe.Probes!.Single().Sheet, probe.Probes!.Single().Cell));
    }

    [Fact]
    public void WorkbookReads_MatchTheirCellUpdates()
    {
        Assert.Equal(PlcAlarmMethods.ReadTextListWorkbook, PlcAlarmMethods.WorkbookReadFor(PlcAlarmMethods.UpdateTextListCells));
        Assert.Equal(PlcAlarmMethods.ReadAlarmTextWorkbook, PlcAlarmMethods.WorkbookReadFor(PlcAlarmMethods.UpdateAlarmTextCells));
        Assert.Equal(PlcAlarmMethods.ReadSupervisionWorkbook, PlcAlarmMethods.WorkbookReadFor(PlcAlarmMethods.UpdateSupervisionCells));
        foreach (var method in PlcAlarmMethods.CellUpdates)
            Assert.Equal(PlcAlarmMethods.WorkbookKind(method), PlcAlarmMethods.WorkbookKind(PlcAlarmMethods.WorkbookReadFor(method)));
        Assert.Equal(WorkerFailureCategories.ValidationError, "validation_error");
    }

    /// <summary>File keys are scope-free and canonical: case, separators and a trailing separator do not matter.</summary>
    [Fact]
    public void FileKeys_AreScopeFreeAndCanonical()
    {
        Assert.Equal(PlcAlarmVerification.FileKey(@"C:\Exports\TextLists.xlsx"), PlcAlarmVerification.FileKey("c:/exports/textlists.XLSX"));
        Assert.Equal(PlcAlarmVerification.FileKey(@"C:\Exports\ProDiag\"), PlcAlarmVerification.FileKey(@"C:\Exports\ProDiag"));
        Assert.StartsWith("file\n", PlcAlarmVerification.FileKey(@"C:\Exports\ProDiag"), StringComparison.Ordinal);
        Assert.NotEqual(PlcAlarmVerification.FileKey(@"C:\Exports\A.xlsx"), PlcAlarmVerification.FileKey(@"C:\Exports\B.xlsx"));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(PlcAlarmVerification.FileKey("/tmp/exports/a.xlsx"), PlcAlarmVerification.FileKey("/tmp/exports/../exports/A.xlsx"));

        // Two exports to the same file from different PLCs: the later one supersedes the earlier one.
        var first = Op(PlcAlarmMethods.ExportTextLists, "first");
        first.FilePath = @"C:\Exports\TextLists.xlsx";
        var second = Op(PlcAlarmMethods.ExportAlarmTexts, "second");
        second.FilePath = "c:/exports/TEXTLISTS.xlsx";
        PlcWriteEffect Effect(string operation, string plc) => new(operation, new PlcTargetIdentity(PlcWorkingState.TextListWorkbookKind, plc, "Station_" + plc, null, null, null, null, null),
            Array.Empty<PlcFieldChange>(), null, null, null, null);
        var policy = new PlcVerificationPolicy(null!, new Dictionary<string, PlcWriteEffect> { ["first"] = Effect(first.Operation, "PLC_1"), ["second"] = Effect(second.Operation, "PLC_2") });
        Assert.Equal(policy.ObjectKeys(first, asLater: false), policy.ObjectKeys(second, asLater: true));
    }

    /// <summary>
    /// F16, L1: a later block or type change (delete, rename, renumber, move, copy, create, re-import, interface and DB member
    /// edits, the external-source, software-unit and type-group changes) supersedes earlier alarm-text, supervision and
    /// ProDiag items of the same PLC, because the block may own their alarms; a later tag or tag-table (group) change
    /// supersedes supervision and assignment items (a tag can carry AssignedProDiagFB), never
    /// alarm texts (alarms.md documents no tag reference of alarm texts); text list items stay.
    /// </summary>
    [Fact]
    public void LaterBlockChanges_SupersedeEarlierAlarmTextAndSupervisionItems()
    {
        PlcWriteEffect Effect(string operation, string kind, string? name = null, string? blockPath = null)
            => new(operation, new PlcTargetIdentity(kind, "PLC_1", "Station_1", null, null, name, blockPath, null), Array.Empty<PlcFieldChange>(), null, null, null, null);
        var texts = Op(PlcAlarmMethods.UpdateAlarmTextCells, "texts");
        var supervisions = Op(PlcAlarmMethods.UpdateSupervisionCells, "sv");
        supervisions.BlockPath = "PLC_1/ProDiag_FB";
        var assignment = Op(PlcAlarmMethods.SetProDiagAssignment, "assign");
        var textList = Op(PlcAlarmMethods.UpdateTextList, "list");
        textList.TextListName = "USER_1";
        var delete = Op("delete_block", "delete");
        delete.BlockPath = "PLC_1/Blocks/Main";
        var tag = Op("update_tag", "tag");
        var later = new[] { "update_type_content", "copy_block", "create_block", "create_instance_db", "renumber_block", "add_interface_member",
            "update_interface_member", "delete_interface_member", "update_db_member", "rename_block_group", "generate_blocks_from_source",
            "delete_software_unit", "rename_software_unit", "delete_type_group" }.Select(operation => Op(operation, operation)).ToList();
        var tagChanges = new[] { "delete_tag", "move_tag", "delete_tag_table", "move_tag_table", "delete_tag_table_group", "rename_tag_table_group" }
            .Select(operation => Op(operation, operation)).ToList();
        var effects = new Dictionary<string, PlcWriteEffect>
        {
            ["texts"] = Effect(texts.Operation, PlcWorkingState.AlarmTextsKind),
            ["sv"] = Effect(supervisions.Operation, PlcWorkingState.SupervisionsKind, blockPath: "PLC_1/ProDiag_FB"),
            ["assign"] = Effect(assignment.Operation, PlcWorkingState.ProDiagAssignmentKind, name: "/Tags/Valve_1"),
            ["list"] = Effect(textList.Operation, PlcWorkingState.TextListKind, name: "USER_1"),
            ["delete"] = Effect(delete.Operation, "Block", name: "Main", blockPath: "PLC_1/Blocks/Main"),
            ["tag"] = Effect(tag.Operation, "Tag", name: "Valve_1"),
        };
        foreach (var item in later) effects[item.OperationId] = Effect(item.Operation, item.Operation == "update_type_content" ? "Type" : "Block", name: "Main", blockPath: "PLC_1/Blocks/Main");
        foreach (var item in tagChanges) effects[item.OperationId] = Effect(item.Operation, item.Operation.Contains("table", StringComparison.Ordinal) ? "TagTable" : "Tag", name: "Valve_1");
        var policy = new PlcVerificationPolicy(null!, effects);

        bool Supersedes(PlcOperationRequest later, PlcOperationRequest earlier)
            => policy.ObjectKeys(later, asLater: true).Any(k => policy.ObjectKeys(earlier, asLater: false).Any(o => o == k || o.StartsWith(k + "\n", StringComparison.Ordinal)));

        Assert.True(Supersedes(delete, texts));
        Assert.True(Supersedes(delete, supervisions));
        Assert.True(Supersedes(delete, assignment));
        Assert.False(Supersedes(delete, textList));
        Assert.False(Supersedes(tag, texts));
        Assert.True(Supersedes(tag, supervisions));
        Assert.True(Supersedes(tag, assignment));
        Assert.False(Supersedes(tag, textList));
        foreach (var item in later)
        {
            Assert.True(Supersedes(item, texts), item.Operation);
            Assert.True(Supersedes(item, supervisions), item.Operation);
            Assert.True(Supersedes(item, assignment), item.Operation);
            Assert.False(Supersedes(item, textList), item.Operation);
        }

        foreach (var item in tagChanges)
        {
            Assert.False(Supersedes(item, texts), item.Operation);
            Assert.True(Supersedes(item, assignment), item.Operation);
        }
    }
}
