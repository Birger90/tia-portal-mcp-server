using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.PlcAlarms;
using Xunit;

namespace TiaMcpServer.Tests.PlcAlarms;

/// <summary>
/// The worker payload rules of the second review round: column paging evidence of workbook reads (L2), the import check
/// outcomes (M3) with the hash of the imported private copy (L8), and the directory export that must list files (L4).
/// </summary>
public sealed class PlcAlarmPayloadContractTests
{
    private const string Hash = "cells:sha256:" + "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static PlcAlarmWorkbookInfo Workbook(string? sheet = null, string startColumn = "A", bool cellsTruncated = false, string? nextStartColumn = null)
        => new()
        {
            Kind = PlcAlarmPolicy.WorkbookKinds.TextLists,
            WorkbookHash = Hash,
            Sheet = sheet,
            StartColumn = startColumn,
            Sheets =
            {
                new PlcAlarmSheetInfo
                {
                    Name = "Text lists", RowCount = 1, ColumnCount = 2, CellCount = 2,
                    Rows = { new PlcAlarmRowInfo { Row = 1, Cells = { new PlcAlarmCellInfo { Cell = "A1", Kind = "string", Value = "x" } }, CellsTruncated = cellsTruncated, NextStartColumn = nextStartColumn } },
                },
            },
        };

    private static PlcAlarmWorkbookInfo Decode(PlcAlarmWorkbookInfo workbook)
        => PlcAlarmPayloadContract.DecodeWorkbook(PlcAlarmMethods.ReadTextListWorkbook, WorkerJson.SerializePayload(workbook));

    [Fact]
    public void WorkbookReads_CarryConsistentColumnPaging()
    {
        Assert.Equal("A", Decode(Workbook()).StartColumn);
        Assert.Equal("C", Decode(Workbook(sheet: "Text lists", startColumn: "C")).StartColumn);
        Assert.Equal("B", Decode(Workbook(cellsTruncated: true, nextStartColumn: "B")).Sheets[0].Rows[0].NextStartColumn);

        // startColumn pages one sheet only, and must be column letters; a cut row names where to read on, an uncut one does not.
        Assert.Throws<JsonException>(() => Decode(Workbook(startColumn: "C")));
        Assert.Throws<JsonException>(() => Decode(Workbook(sheet: "Text lists", startColumn: "c")));
        Assert.Throws<JsonException>(() => Decode(Workbook(sheet: "Text lists", startColumn: "XFE")));
        Assert.Throws<JsonException>(() => Decode(Workbook(cellsTruncated: true)));
        Assert.Throws<JsonException>(() => Decode(Workbook(nextStartColumn: "B")));
        Assert.Throws<JsonException>(() => Decode(Workbook(cellsTruncated: true, nextStartColumn: "1")));
    }

    private static PlcAlarmWriteResultInfo Import(string method, string outcome, string? reason, string operation = PlcAlarmMethods.ImportTextLists, string? sha256 = "ab", long? bytes = 1)
        => new()
        {
            Operation = operation,
            FilePath = @"C:\Exports\TextLists.xlsx",
            Sha256 = sha256,
            Bytes = bytes,
            ImportCheck = new PlcAlarmImportCheckInfo { Method = method, Outcome = outcome, Verified = outcome == PlcAlarmPolicy.ImportOutcomes.Verified, Reason = reason },
        };

    private static PlcAlarmWriteResultInfo DecodeWrite(PlcAlarmWriteResultInfo result)
    {
        var decoded = PlcAlarmPayloadContract.DecodeWriteResult(result.Operation, WorkerJson.SerializePayload(result));
        PlcAlarmPayloadContract.RequireComplete(result.Operation, decoded);
        return decoded;
    }

    [Theory]
    [InlineData("rows", "verified", null)]
    [InlineData("rows", "contradicted", "a row is absent")]
    [InlineData("rows", "inconclusive", "layouts differ")]
    [InlineData("fileHash", "verified", null)]
    [InlineData("fileHash", "inconclusive", "differs byte for byte")]
    [InlineData("none", "notReadBack", "not read back: no documented export.")]
    public void ImportChecks_WithAConsistentOutcome_Decode(string method, string outcome, string? reason)
        => Assert.Equal(outcome, DecodeWrite(Import(method, outcome, reason)).ImportCheck!.Outcome);

    [Theory]
    [InlineData("rows", "unknown", "x")]
    [InlineData("rows", "verified", "a reason")]
    [InlineData("rows", "inconclusive", null)]
    [InlineData("rows", "notReadBack", "x")]
    [InlineData("none", "inconclusive", "x")]
    [InlineData("fileHash", "contradicted", "x")]
    public void ImportChecks_WithAnInconsistentOutcome_AreRejected(string method, string outcome, string? reason)
        => Assert.Throws<JsonException>(() => DecodeWrite(Import(method, outcome, reason)));

    [Fact]
    public void ImportChecks_NeedAFileImport_AndTheHashOfTheImportedCopy()
    {
        Assert.Throws<JsonException>(() => DecodeWrite(Import("rows", "verified", null, operation: PlcAlarmMethods.UpdateTextList)));
        Assert.Throws<JsonException>(() => DecodeWrite(Import("rows", "verified", null, sha256: null)));
        Assert.Throws<JsonException>(() => DecodeWrite(Import("rows", "verified", null, bytes: null)));
    }

    [Fact]
    public void DirectoryExports_ListAtLeastOneFile()
    {
        var files = new List<PlcAlarmExportedFileInfo>();
        var result = new PlcAlarmWriteResultInfo { Operation = PlcAlarmMethods.ExportProDiagInfo, DirectoryPath = @"C:\Exports\ProDiag", Files = files };
        Assert.Throws<JsonException>(() => DecodeWrite(result));

        files.Add(new PlcAlarmExportedFileInfo { Name = "alarms.csv", Bytes = 3, Sha256 = "ab" });
        Assert.Single(DecodeWrite(result).Files!);
    }
}
