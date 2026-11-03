using System.Text;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Network;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>The review fixes of the source/folder operations: file-name rules, file locations, writes, keys and CSV leads.</summary>
public sealed class PlcSourceFolderHardeningTests
{
    // ---------------------------------------------------------------- P1: Windows names in every segment

    [Theory]
    [InlineData(@"C:\out\CON.csv")]
    [InlineData(@"C:\out\con")]
    [InlineData(@"C:\AUX\tags.csv")]
    [InlineData(@"C:\out\NUL .txt")]
    [InlineData(@"C:\out\COM0.csv")]
    [InlineData(@"C:\out\lpt9")]
    [InlineData("C:\\out\\COM\u00B9.csv")]
    [InlineData("C:\\out\\LPT\u00B3")]
    [InlineData(@"C:\out\CONIN$")]
    [InlineData(@"C:\out\conout$.txt")]
    [InlineData(@"C:\out\tags.")]
    [InlineData(@"C:\out\tags ")]
    [InlineData(@"C:\out\ .\x.csv")]
    [InlineData(@"C:\out\..\x.csv")]
    [InlineData(@"C:\out\.\x.csv")]
    [InlineData("C:/out/PRN/x.csv")]
    public void PathsWithSegmentsWindowsCannotKeepAreRefused(string path)
        => Assert.Contains("cannot be written on Windows", PlcFileTargets.PathShapeError("filePath", path));

    [Theory]
    [InlineData(@"C:\out\CONSOLE.csv")]
    [InlineData(@"C:\out\COM10.csv")]
    [InlineData(@"C:\out\LPT.csv")]
    [InlineData(@"C:\out\.hidden")]
    [InlineData(@"C:\out\a.b.c")]
    public void OrdinaryNamesPass(string path) => Assert.Null(PlcFileTargets.PathShapeError("filePath", path));

    [Theory]
    [InlineData("COM0", "_COM0")]
    [InlineData("CONIN$", "_CONIN$")]
    [InlineData("LPT\u00B2.xml", "_LPT\u00B2.xml")]
    [InlineData("nul .xml", "_nul .xml")]
    [InlineData("..", "__")]
    [InlineData("COM10", "COM10")]
    public void SafeSegmentsUseTheSameDeviceNameRule(string segment, string expected)
    {
        Assert.Equal(expected, PlcFileTargets.SafeFileSegment(segment).Name);
        Assert.Equal(PlcFileTargets.IsReservedDeviceName(segment), expected.StartsWith('_') && segment != "..");
    }

    // ---------------------------------------------------------------- locations (owner decision: mapped drives allowed)

    [Fact]
    public void MappedNetworkDrivesAndLinksToSharesWarn()
    {
        var mapped = PlcFileTargets.ClassifyLocation(@"Z:\exports", DriveType.Network, @"\\?\UNC\server\share\exports", reparseAncestor: false);
        Assert.Null(mapped.Refusal);
        Assert.Contains(@"'Z:\exports' is on a mapped network drive (\\server\share\exports)", mapped.Warning);

        var junction = PlcFileTargets.ClassifyLocation(@"C:\exports", DriveType.Fixed, @"\\?\UNC\server\share\exports", reparseAncestor: true);
        // Owner decision 2026-11-01: writes to UNC shares are allowed, so a link to one only warns.
        Assert.Null(junction.Refusal);
        Assert.Contains(@"network share '\\server\share\exports'", junction.Warning);

        var local = PlcFileTargets.ClassifyLocation(@"C:\exports", DriveType.Fixed, @"\\?\D:\real\exports", reparseAncestor: true);
        Assert.Null(local.Refusal);
        Assert.Contains(@"resolves through a junction or symbolic link to 'D:\real\exports'", local.Warning);

        Assert.Contains("could not be resolved", PlcFileTargets.ClassifyLocation("/data/x", null, null, reparseAncestor: true).Warning);
        var plain = PlcFileTargets.ClassifyLocation(@"C:\exports", DriveType.Fixed, @"\\?\C:\exports", reparseAncestor: false);
        Assert.Equal((null, null), (plain.Refusal, plain.Warning));
    }

    [Fact]
    public void AnExistingLocalDirectoryHasNoLocationFinding()
    {
        var directory = Directory.CreateTempSubdirectory("plc-location-").FullName;
        try
        {
            var location = PlcFileTargets.InspectDirectory(directory);
            Assert.Null(location.Refusal);
            Assert.Equal(Path.GetFullPath(directory), location.NormalizedPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---------------------------------------------------------------- writes

    [Fact]
    public void ReplaceFallsBackOnlyForUnsupportedReplaceErrors()
    {
        Assert.True(PlcFileTargets.ReplaceUnsupported(new PlatformNotSupportedException()));
        Assert.True(PlcFileTargets.ReplaceUnsupported(new IOException("not supported", unchecked((int)0x80070032))));
        Assert.True(PlcFileTargets.ReplaceUnsupported(new IOException("unable to move replacement", unchecked((int)0x80070498))));
        Assert.False(PlcFileTargets.ReplaceUnsupported(new IOException("sharing violation", unchecked((int)0x80070020))));
        Assert.False(PlcFileTargets.ReplaceUnsupported(new UnauthorizedAccessException()));
    }

    private static (string Directory, string Target, string Temporary) Swap(string previous, string next)
    {
        var directory = Directory.CreateTempSubdirectory("plc-commit-").FullName;
        var target = Path.Combine(directory, "tags.csv");
        File.WriteAllText(target, previous);
        var temporary = PlcFileTargets.TemporaryPathFor(target);
        File.WriteAllText(temporary, next);
        return (directory, target, temporary);
    }

    private static IOException Win32(int code) => new($"error {code}", unchecked((int)0x80070000) | code);

    [Fact]
    public void AReplaceRefusedWithError1176FallsBackAndKeepsTheBackupUntilVerified()
    {
        var (directory, target, temporary) = Swap("old", "new");
        try
        {
            var commit = PlcFileTargets.Commit(temporary, target, overwrite: true, (_, _, _) => throw Win32(1176), File.Move);
            Assert.True(commit.Replaced);
            Assert.Equal("new", File.ReadAllText(target));
            Assert.Equal("old", File.ReadAllText(commit.Backup!));
            Assert.False(File.Exists(temporary));
            PlcFileTargets.VerifyCommitted(target, PlcFileTargets.Sha256Hex(Encoding.UTF8.GetBytes("new")), commit);
            Assert.False(File.Exists(commit.Backup));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AReplaceThatRenamedTheTargetAsideWith1177IsRestoredBeforeTheFallback()
    {
        var (directory, target, temporary) = Swap("old", "new");
        try
        {
            var commit = PlcFileTargets.Commit(temporary, target, overwrite: true, (_, destination, backup) =>
            {
                File.Move(destination, backup);
                throw Win32(1177);
            }, File.Move);
            Assert.Equal("new", File.ReadAllText(target));
            Assert.Equal("old", File.ReadAllText(commit.Backup!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AFailedFallbackRestoresThePreviousFileAndKeepsTheNewContent()
    {
        var (directory, target, temporary) = Swap("old", "new");
        try
        {
            var failure = Assert.Throws<PlcFileCommitException>(() => PlcFileTargets.Commit(temporary, target, overwrite: true,
                (_, _, _) => throw new PlatformNotSupportedException("no ReplaceFile"), (_, _) => throw Win32(5)));
            Assert.Equal("old", File.ReadAllText(target));
            Assert.Equal("new", File.ReadAllText(temporary));
            Assert.Contains($"'{target}' was restored and the new content is kept at '{temporary}'", failure.Message);
            Assert.IsAssignableFrom<IOException>(failure);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AFallbackThatCannotRestoreNamesBothCopiesAndDeletesNothing()
    {
        var (directory, target, temporary) = Swap("old", "new");
        try
        {
            // The move-in fails after something else created the target, so the previous file cannot be moved back.
            var failure = Assert.Throws<PlcFileCommitException>(() => PlcFileTargets.Commit(temporary, target, overwrite: true,
                (_, _, _) => throw Win32(50), (_, destination) =>
                {
                    File.WriteAllText(destination, "other");
                    throw Win32(80);
                }));
            Assert.Contains("the previous file could not be restored", failure.Message);
            var backup = Directory.GetFiles(directory, ".tags.csv.*.bak").Single();
            Assert.Equal("old", File.ReadAllText(backup));
            Assert.Equal("new", File.ReadAllText(temporary));
            Assert.Contains(backup, failure.Message);
            Assert.Contains(temporary, failure.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AFailedReadBackSaysTheFileIsInPlace()
    {
        var directory = Directory.CreateTempSubdirectory("plc-verify-").FullName;
        try
        {
            var path = Path.Combine(directory, "a.csv");
            File.WriteAllText(path, "x");
            var replaced = Assert.Throws<PlcFileNotVerifiedException>(() => PlcFileTargets.VerifyCommitted(path, new string('0', 64), replaced: true));
            Assert.Contains("was written but not verified", replaced.Message);
            Assert.Contains($"The file at '{path}' is in place and replaced the previous file, which no longer exists", replaced.Message);
            var backup = Path.Combine(directory, "old.bak");
            File.WriteAllText(backup, "previous");
            var kept = Assert.Throws<PlcFileNotVerifiedException>(() => PlcFileTargets.VerifyCommitted(path, new string('0', 64), new PlcCommit(true, backup)));
            Assert.Contains($"it replaced the previous file, which is kept at '{backup}'", kept.Message);
            Assert.True(File.Exists(backup));
            var created = Assert.Throws<PlcFileNotVerifiedException>(() => PlcFileTargets.VerifyCommitted(path, new string('0', 64), replaced: false));
            Assert.EndsWith("is in place.", created.Message);
            Assert.Equal(PlcFileTargets.Sha256Hex(Encoding.UTF8.GetBytes("x")),
                PlcFileTargets.VerifyCommitted(path, PlcFileTargets.Sha256Hex(Encoding.UTF8.GetBytes("x")), replaced: false));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheExportBlocksResultStaysWithinTheItemBudgetAtTheObjectCap()
    {
        var segment = new string('x', 120);
        var result = new PlcBlockExportResultInfo
        {
            DirectoryPath = @"C:\exports", Format = "xml", PlcName = "PLC_1", DeviceName = "S1", ObjectCount = PlcOperationCatalog.MaxExportObjects,
            Files = Enumerable.Range(0, PlcOperationCatalog.MaxExportObjects).Select(i => new PlcExportedFileInfo
            {
                ObjectPath = $"PLC_1/Blocks/{segment}/{segment}{i}", Kind = "block", RelativePath = $"Blocks/{segment}/{segment}{i}.xml", Bytes = i, Sha256 = new string('a', 64),
            }).ToList(),
            Skipped = Enumerable.Range(0, PlcOperationCatalog.MaxExportObjects).Select(i => new PlcExportSkipInfo
            {
                ObjectPath = $"PLC_1/Blocks/{segment}/{segment}{i}", Kind = "block", Reason = new string('r', 2000), FailureCategory = "worker_operation_failed",
            }).ToList(),
            Messages = Enumerable.Range(0, 100).Select(i => new string('m', 300)).ToList(),
        };
        result.FileCount = result.Files.Count;
        PlcFileExports.Bound(result);
        var size = TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(result).Length;
        Assert.True(size < 45_000, $"{size} characters");
        Assert.True(result.FilesTruncated);
        Assert.True(result.SkippedTruncated);
        Assert.All(result.Skipped, skip => Assert.EndsWith("... (full text in the manifest)", skip.Reason));
        Assert.Equal(PlcFileExports.MaxListedMessages + 1, result.Messages.Count);
    }

    // ---------------------------------------------------------------- P4: CSV formula leads

    [Theory]
    [InlineData(" =SUM(A1)", true)]
    [InlineData("\u00A0+1", true)]
    [InlineData("\n-1", true)]
    [InlineData("  \t@x", true)]
    [InlineData("\uFF1DSUM(A1)", true)]
    [InlineData("\uFF0B1", true)]
    [InlineData("\uFF0D1", true)]
    [InlineData("\uFF20x", true)]
    [InlineData("=x", true)]
    [InlineData("  text", false)]
    [InlineData("a=b", false)]
    [InlineData("   ", false)]
    public void FormulaLeadsAreFoundAfterLeadingWhitespace(string value, bool neutralized)
    {
        var count = 0;
        var result = HardwareIoMapCsv.Neutralize(value, rawCells: false, ref count);
        Assert.Equal(neutralized ? "'" + value : value, result);
        Assert.Equal(neutralized ? 1 : 0, count);
        Assert.Equal(value, HardwareIoMapCsv.Neutralize(value, rawCells: true, ref count));
    }

    // ---------------------------------------------------------------- accessible devices

    private enum Series { S71500 = 1, Et200Sp = 2 }

    [Flags]
    private enum Capabilities { None = 0, Read = 1, Write = 2 }

    [Flags]
    private enum Bits { One = 1, Two = 2 }

    [Fact]
    public void EnumValuesAreReportedOnlyByTheirNames()
    {
        Assert.Equal("S71500", PlcAccessibleDeviceProjection.EnumName(Series.S71500));
        Assert.Null(PlcAccessibleDeviceProjection.EnumName((Series)7));
        Assert.Equal("Read, Write", PlcAccessibleDeviceProjection.EnumName(Capabilities.Read | Capabilities.Write));
        Assert.Null(PlcAccessibleDeviceProjection.EnumName((Capabilities)8));
        Assert.Equal("None", PlcAccessibleDeviceProjection.EnumName(Capabilities.None));
        // A [Flags] zero that no member names would print "0": it fails closed like any undefined value.
        Assert.Null(PlcAccessibleDeviceProjection.EnumName((Bits)0));
    }

    private sealed class CountingScope : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    [Fact]
    public void ADeclinedDialogFailsTheScanAfterTheScopeIsClosed()
    {
        Assert.Null(PlcAccessibleDeviceProjection.DeclinedConfirmations(null));
        var quiet = new PlcDownloadPortalConfirmationLog();
        var quietScope = new CountingScope();
        quiet.AttachScope(quietScope);
        Assert.Null(PlcAccessibleDeviceProjection.DeclinedConfirmations(quiet));
        Assert.Equal(1, quietScope.Disposed);

        var log = new PlcDownloadPortalConfirmationLog();
        var scope = new CountingScope();
        log.AttachScope(scope);
        log.Record("Siemens.Engineering.ConfirmationEventArgs", "Go online?", PlcDownloadPolicy.PortalConfirmationAnswers.No);
        var message = PlcAccessibleDeviceProjection.DeclinedConfirmations(log);
        Assert.Equal(1, scope.Disposed);
        Assert.Contains("raised 1 confirmation dialog(s) during scan_accessible_devices", message);
        Assert.Contains("Go online?", message);
        Assert.Contains("no device is listed", message);
    }
}

/// <summary>P6: verification keys — a leaf never prefixes a same-named container; folders split into segments.</summary>
public sealed class PlcSourceFolderKeyTests
{
    private readonly Dictionary<string, PlcWriteEffect> _effects = new(StringComparer.Ordinal);
    private int _next;

    private PlcOperationRequest Item(string operation, PlcTargetIdentity target, Action<PlcOperationRequest>? set = null)
    {
        var item = new PlcOperationRequest { OperationId = "i" + _next++, Operation = operation };
        set?.Invoke(item);
        _effects[item.OperationId] = new PlcWriteEffect(operation, target, Array.Empty<PlcFieldChange>(), null, null, null, null);
        return item;
    }

    private bool Supersedes(PlcOperationRequest later, PlcOperationRequest earlier)
    {
        var policy = new PlcVerificationPolicy(null!, _effects);
        var own = policy.ObjectKeys(earlier, asLater: false);
        return policy.ObjectKeys(later, asLater: true).Any(k => own.Any(o => o == k || o.StartsWith(k + "\n", StringComparison.Ordinal)));
    }

    private static PlcTargetIdentity Type(string path) => new("Type", "PLC_1", "S1", null, null, path.Split('/')[^1], null, path);

    private static PlcTargetIdentity Block(string path) => new(PlcNameRules.Block, "PLC_1", "S1", null, null, path.Split('/')[^1], path, null);

    [Fact]
    public void ATypeNamedLikeAGroupDoesNotSupersedeTheGroupsTypes()
    {
        var inGroup = Item("update_type_content", Type("PLC_1/Types/Motion/UDT_B"));
        var sameNameLeaf = Item("update_type_content", Type("PLC_1/Types/Motion"));
        Assert.False(Supersedes(sameNameLeaf, inGroup));
        var rename = Item(PlcSourceFolderMethods.RenameTypeGroup, new(PlcSourceFolderKinds.TypeGroup, "PLC_1", "S1", null, null, "Motion", null, "PLC_1/Types/Motion"),
            o => { o.TypePath = "PLC_1/Types/Motion"; o.NewName = "Axes"; });
        Assert.True(Supersedes(rename, inGroup));
        Assert.False(Supersedes(rename, sameNameLeaf));
        Assert.True(Supersedes(Item("update_type_content", Type("PLC_1/Types/Motion/UDT_B")), inGroup));
    }

    [Fact]
    public void ABlockNamedLikeAGroupDoesNotSupersedeTheGroupsBlocks()
    {
        var inGroup = Item("update_block_logic", Block("PLC_1/Blocks/Line/FC_1"), o => o.BlockPath = "PLC_1/Blocks/Line/FC_1");
        var sameNameLeaf = Item("update_block_logic", Block("PLC_1/Blocks/Line"), o => o.BlockPath = "PLC_1/Blocks/Line");
        Assert.False(Supersedes(sameNameLeaf, inGroup));
        var groupDelete = Item("delete_block_group", new(PlcNameRules.BlockGroup, "PLC_1", "S1", null, null, "Line", "PLC_1/Blocks/Line", null), o => o.BlockPath = "PLC_1/Blocks/Line");
        Assert.True(Supersedes(groupDelete, inGroup));
        var member = Item("update_interface_member", Block("PLC_1/Blocks/Line/FC_1"), o => { o.BlockPath = "PLC_1/Blocks/Line/FC_1"; o.MemberPath = "a"; });
        Assert.True(Supersedes(Item("update_block_logic", Block("PLC_1/Blocks/Line/FC_1"), o => o.BlockPath = "PLC_1/Blocks/Line/FC_1"), member));
    }

    [Fact]
    public void FolderRenamesCoverNestedTablesAndGroups()
    {
        var nestedTag = Item("create_tag", new(PlcNameRules.Tag, "PLC_1", "S1", "/A/B", "T", "Tag", null, null));
        var rootTableA = Item("create_tag", new(PlcNameRules.Tag, "PLC_1", "S1", "/", "A", "Tag", null, null));
        var rename = Item(PlcSourceFolderMethods.RenameTagTableGroup, new(PlcSourceFolderKinds.TagTableGroup, "PLC_1", "S1", "/A", null, "A", null, null),
            o => { o.FolderPath = "/A"; o.NewName = "X"; });
        Assert.True(Supersedes(rename, nestedTag));
        Assert.False(Supersedes(rename, rootTableA));

        var nestedWatch = Item("update_watch_table_content", new(PlcWorkingState.WatchTableKind, "PLC_1", "S1", "/Ops/Sub", "W", "W", null, null));
        var watchRename = Item(PlcSourceFolderMethods.RenameWatchTableGroup, new(PlcWorkingState.WatchTableGroupKind, "PLC_1", "S1", "/Ops", null, null, null, null),
            o => { o.FolderPath = "/Ops"; o.NewName = "Service"; });
        Assert.True(Supersedes(watchRename, nestedWatch));
    }

    [Fact]
    public void GenerationSupersedesEarlierBlockAndTypeItemsOfItsPlc()
    {
        var shortBlock = Item("update_block_logic", Block("PLC_1/Main"), o => o.BlockPath = "PLC_1/Main");
        var type = Item("update_type_content", Type("PLC_1/Types/UDT_A"));
        var otherPlc = Item("update_type_content", new("Type", "PLC_2", "S2", null, null, "UDT_A", null, "PLC_2/Types/UDT_A"));
        var generate = Item(PlcSourceFolderMethods.GenerateBlocksFromSource, new(PlcSourceFolderKinds.ExternalSource, "PLC_1", "S1", null, null, "Main.scl", null, null),
            o => o.SourceName = "Main.scl");
        Assert.True(Supersedes(generate, shortBlock));
        Assert.True(Supersedes(generate, type));
        Assert.False(Supersedes(generate, otherPlc));
        // As an earlier item it owns only its own item key, so later block items do not supersede it.
        Assert.False(Supersedes(Item("update_block_logic", Block("PLC_1/Main"), o => o.BlockPath = "PLC_1/Main"), generate));
    }

    [Fact]
    public void GenerationLeavesContainersAndSourceImportsAloneAndCoversShortTypePaths()
    {
        var shortType = Item("update_type_content", new("Type", "PLC_1", "S1", null, null, "UDT_A", null, "UDT_A"));
        var typeGroup = Item(PlcSourceFolderMethods.CreateTypeGroup, new(PlcSourceFolderKinds.TypeGroup, "PLC_1", "S1", null, null, "Motion", null, "PLC_1/Types/Motion"),
            o => o.TypePath = "PLC_1/Types/Motion");
        var blockGroup = Item("create_block_group", new(PlcNameRules.BlockGroup, "PLC_1", "S1", null, null, "Line", "PLC_1/Blocks/Line", null), o => o.BlockPath = "PLC_1/Blocks/Line");
        var unit = Item(PlcSourceFolderMethods.CreateSoftwareUnit, new(PlcSourceFolderKinds.SoftwareUnit, "PLC_1", "S1", "Units/U", null, "U", null, null), o => o.UnitName = "U");
        var import = Item(PlcSourceFolderMethods.ImportExternalSource, new(PlcSourceFolderKinds.ExternalSource, "PLC_1", "S1", null, null, "Main.scl", null, null),
            o => o.FilePath = @"C:\src\Main.scl");
        var generate = Item(PlcSourceFolderMethods.GenerateBlocksFromSource, new(PlcSourceFolderKinds.ExternalSource, "PLC_1", "S1", null, null, "Main.scl", null, null),
            o => o.SourceName = "Main.scl");
        Assert.True(Supersedes(generate, shortType));
        Assert.False(Supersedes(generate, typeGroup));
        Assert.False(Supersedes(generate, blockGroup));
        Assert.False(Supersedes(generate, unit));
        Assert.False(Supersedes(generate, import));

        // Container operations still supersede each other and what is inside them.
        var deleteParent = Item("delete_block_group", new(PlcNameRules.BlockGroup, "PLC_1", "S1", null, null, "Blocks", "PLC_1/Blocks", null), o => o.BlockPath = "PLC_1/Blocks");
        Assert.True(Supersedes(deleteParent, blockGroup));
        var deleteUnit = Item(PlcSourceFolderMethods.DeleteSoftwareUnit, new(PlcSourceFolderKinds.SoftwareUnit, "PLC_1", "S1", "Units/U", null, "U", null, null), o => o.UnitName = "U");
        Assert.True(Supersedes(deleteUnit, unit));
        var unitGroup = Item("create_block_group", new(PlcNameRules.BlockGroup, "PLC_1", "S1", null, null, "G", "PLC_1/Units/U/Blocks/G", null), o => o.BlockPath = "PLC_1/Units/U/Blocks/G");
        Assert.True(Supersedes(deleteUnit, unitGroup));
        var renameType = Item(PlcSourceFolderMethods.RenameTypeGroup, new(PlcSourceFolderKinds.TypeGroup, "PLC_1", "S1", null, null, "Motion", null, "PLC_1/Types/Motion"),
            o => { o.TypePath = "PLC_1/Types/Motion"; o.NewName = "Axes"; });
        Assert.True(Supersedes(renameType, typeGroup));
    }

    [Fact]
    public void ARenameSaysWhereToReReadInsteadOfClaimingVerification()
    {
        var block = Item("update_block_logic", Block("PLC_1/Blocks/Line/FC_1"), o => o.BlockPath = "PLC_1/Blocks/Line/FC_1");
        var rename = Item(PlcSourceFolderMethods.RenameBlockGroup, new(PlcNameRules.BlockGroup, "PLC_1", "S1", null, null, "Line", "PLC_1/Blocks/Line", null),
            o => { o.BlockPath = "PLC_1/Blocks/Line"; o.NewName = "Line2"; });
        var evidence = new PlcVerificationPolicy(null!, _effects).SupersessionEvidence(block, rename);
        Assert.Contains("not re-verified; re-read it at 'PLC_1/Blocks/Line2/FC_1' with plc_read", evidence);
        Assert.DoesNotContain("verified there", evidence);
        var delete = Item(PlcSourceFolderMethods.DeleteSoftwareUnit, new(PlcSourceFolderKinds.SoftwareUnit, "PLC_1", "S1", "Units/U", null, "U", null, null), o => o.UnitName = "U");
        Assert.Contains("nothing is left to verify", new PlcVerificationPolicy(null!, _effects).SupersessionEvidence(block, delete));
        Assert.Null(new PlcVerificationPolicy(null!, _effects).SupersessionEvidence(block, Item("update_block_logic", Block("PLC_1/Blocks/Line/FC_1"))));
    }
}
