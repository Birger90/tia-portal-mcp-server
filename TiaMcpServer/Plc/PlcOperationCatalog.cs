using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// Whitelists the dedicated PLC read and write operations and validates a batch before any worker
/// call. Pure and Siemens-free.
/// </summary>
public static partial class PlcOperationCatalog
{
    private sealed record Spec(IReadOnlyList<string> Required, IReadOnlyList<string> Optional, bool IsWrite);

    private static readonly IReadOnlyList<string> None = Array.Empty<string>();

    private static Spec Read(string[] required, string[] optional) => new(required, optional, false);

    private static Spec Write(string[] required, string[] optional) => new(required, optional, true);

    private static readonly IReadOnlyDictionary<string, Spec> Specs = WithSafetySpecs(WithOnlineFingerprintSpecs(WithProjectLanguageSpecs(WithBlockProtectionSpecs(WithSettingsSpecs(WithDeviceDataSpecs(WithOpcUaSpecs(WithLoadableSpecs(WithChangeDetectionSpecs(WithCompilationSettingsSpecs(WithSimaticSdSpecs(WithEditingSpecs(WithSourceFolderSpecs(WithSecuritySpecs(WithTechnologyObjectSpecs(WithAlarmSpecs(WithHelpSpecs(new Dictionary<string, Spec>(StringComparer.Ordinal)
    {
        ["get_block_content"] = Read(new[] { "blockPath" }, new[] { "format", "withDependencies" }),
        ["get_type_content"] = Read(new[] { "typePath" }, new[] { "format", "withDependencies" }),
        ["list_tag_tables"] = Read(Array.Empty<string>(), new[] { "plcName", "tableName", "folderPath" }),
        ["list_download_targets"] = Read(Array.Empty<string>(), new[] { "plcName" }),
        [PlcTagMoveMethods.ReadAddressOccupancy] = Read(Array.Empty<string>(), new[] { "plcName", "areas", "includeHardware" }),
        [PlcTagMoveMethods.FindFreeAddress] = Read(new[] { "area", "dataType" }, new[] { "plcName", "startByte", "endByte", "requireHardware", "alignBytes" }),
        [PlcOnlineMethods.CompareToOnline] = Read(Array.Empty<string>(), new[] { "plcName", "maxDepth", "maxElements", "differencesOnly", "trustedCertificates", "passwords", "onlineUser" }),
        [PlcOnlineMethods.CompareSoftware] = Read(new[] { "compareTarget" }, new[] { "plcName", "targetName", "targetProject", "maxDepth", "maxElements", "differencesOnly" }),
        [PlcOnlineMethods.CompareHardware] = Read(new[] { "compareTarget", "targetName" }, new[] { "plcName", "hardwareScope", "maxDepth", "maxElements", "differencesOnly" }),
        [PlcOnlineMethods.CompareHardwareToOnline] = Read(Array.Empty<string>(), new[] { "plcName", "hardwareScope", "maxDepth", "maxElements", "differencesOnly", "trustedCertificates", "passwords", "onlineUser" }),
        [PlcOnlineMethods.GetOnlineState] = Read(Array.Empty<string>(), new[] { "plcName" }),
        [PlcOnlineMethods.ListStationUploadInterfaces] = Read(Array.Empty<string>(), Array.Empty<string>()),
        [PlcOnlineMethods.ListWatchAndForceTables] = Read(Array.Empty<string>(), new[] { "plcName" }),
        [PlcOnlineMethods.ReadWatchTable] = Read(new[] { "tableName" }, new[] { "plcName", "folderPath", "maxElements" }),
        [PlcOnlineMethods.ReadForceTable] = Read(new[] { "tableName" }, new[] { "plcName", "folderPath", "maxElements" }),
        [PlcOnlineMethods.GetWatchTableContent] = Read(new[] { "tableName" }, new[] { "plcName", "folderPath" }),
        [PlcOnlineMethods.GetForceTableContent] = Read(new[] { "tableName" }, new[] { "plcName", "folderPath" }),
        ["get_block_interface"] = Read(new[] { "blockPath" }, Array.Empty<string>()),
        ["get_type_interface"] = Read(new[] { "typePath" }, Array.Empty<string>()),
        ["get_db_values"] = Read(new[] { "blockPath" }, new[] { "memberPath", "withAttributes" }),
        [PlcAnalysisMethods.GetCallTree] = Read(new[] { "blockPath" }, new[] { "maxDepth", "maxElements", "timeoutSeconds" }),
        [PlcAnalysisMethods.FindCallPaths] = Read(new[] { "blockPath" }, new[] { "maxDepth", "maxElements", "timeoutSeconds" }),
        [PlcAnalysisMethods.FindUnusedBlocks] = Read(Array.Empty<string>(), new[] { "plcName", "maxElements", "timeoutSeconds" }),
        [PlcAnalysisMethods.FindOrphanedInstanceDbs] = Read(Array.Empty<string>(), new[] { "plcName", "maxElements", "timeoutSeconds" }),
        [PlcAnalysisMethods.AnalyzeChangeImpact] = Read(Array.Empty<string>(),
            new[] { "blockPath", "typePath", "tableName", "name", "plcName", "folderPath", "maxDepth", "maxElements", "timeoutSeconds" }),

        // Write field sets are the batch catalog's, with yamlContent/sourceContent renamed to content and
        // the two content updates also requiring the hash of the document they were based on.
        ["update_block_logic"] = Write(new[] { "blockPath", "content", "expectedContentHash" }, new[] { "format", "swImportOptions", "documentImportOption" }),
        ["update_type_content"] = Write(new[] { "typePath", "content", "expectedContentHash" }, new[] { "format", "swImportOptions" }),
        ["create_tag_table"] = Write(new[] { "tableName" }, new[] { "plcName", "folderPath" }),
        ["delete_tag_table"] = Write(new[] { "tableName" }, new[] { "plcName", "folderPath" }),
        ["create_tag"] = Write(new[] { "tableName", "name", "dataType" }, new[] { "plcName", "folderPath", "logicalAddress", "comment", "commentCulture" }),
        ["update_tag"] = Write(
            new[] { "tableName", "name" },
            new[] { "plcName", "folderPath", "newName", "dataType", "logicalAddress", "externalAccessible", "externalVisible", "externalWritable", "comment", "commentCulture" }),
        ["delete_tag"] = Write(new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
        [PlcTagMoveMethods.MoveTag] = Write(new[] { "tableName", "name", "targetTableName" }, new[] { "plcName", "folderPath", "targetFolderPath" }),
        ["create_user_constant"] = Write(new[] { "tableName", "name", "dataType", "value" }, new[] { "plcName", "folderPath" }),
        ["update_user_constant"] = Write(new[] { "tableName", "name" }, new[] { "plcName", "folderPath", "dataType", "value" }),
        ["delete_user_constant"] = Write(new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
        ["create_block"] = Write(new[] { "blockPath", "blockType" }, new[] { "language", "obEventClass" }),
        ["delete_block"] = Write(new[] { "blockPath" }, Array.Empty<string>()),
        ["create_block_group"] = Write(new[] { "blockPath" }, Array.Empty<string>()),
        ["delete_block_group"] = Write(new[] { "blockPath" }, Array.Empty<string>()),
        [PlcOnlineMethods.CreateWatchTable] = Write(new[] { "tableName" }, new[] { "plcName", "folderPath" }),
        [PlcOnlineMethods.DeleteWatchTable] = Write(new[] { "tableName" }, new[] { "plcName", "folderPath" }),
        [PlcOnlineMethods.CreateWatchTableGroup] = Write(new[] { "folderPath" }, new[] { "plcName" }),
        [PlcOnlineMethods.DeleteWatchTableGroup] = Write(new[] { "folderPath" }, new[] { "plcName" }),
        [PlcOnlineMethods.UpdateWatchTableContent] = Write(new[] { "tableName", "content", "expectedContentHash" }, new[] { "plcName", "folderPath" }),
        [PlcOnlineMethods.UpdateForceTableContent] = Write(new[] { "tableName", "content", "expectedContentHash" }, new[] { "plcName", "folderPath" }),
        [PlcOnlineMethods.UpdateProgram] = Write(Array.Empty<string>(), new[] { "plcName" }),

        // Granular block editing. Interface edits address a block or a PLC data type (exactly one).
        ["add_interface_member"] = Write(new[] { "memberPath", "dataType" },
            new[] { "blockPath", "typePath", "section", "startValue", "comment", "commentCulture", "insertBefore", "expectedContentHash" }),
        ["update_interface_member"] = Write(new[] { "memberPath" },
            new[] { "blockPath", "typePath", "section", "newName", "dataType", "startValue", "clearStartValue", "comment", "commentCulture", "expectedContentHash" }),
        ["delete_interface_member"] = Write(new[] { "memberPath" }, new[] { "blockPath", "typePath", "section", "expectedContentHash" }),
        ["update_db_member"] = Write(new[] { "blockPath", "memberPath" },
            new[] { "startValue", "externalAccessible", "externalVisible", "externalWritable", "retain", "setPoint" }),
        ["create_instance_db"] = Write(new[] { "blockPath", "instanceOfName" }, new[] { "number" }),
        ["rename_block"] = Write(new[] { "blockPath", "newName" }, Array.Empty<string>()),
        ["renumber_block"] = Write(new[] { "blockPath" }, new[] { "number", "autoNumber" }),
        ["copy_block"] = Write(new[] { "blockPath", "newName" }, new[] { "targetGroupPath", "number" }),
        ["move_block"] = Write(new[] { "blockPath", "targetGroupPath" }, Array.Empty<string>()),
        ["move_tag_table"] = Write(new[] { "tableName", "targetFolderPath" }, new[] { "plcName", "folderPath" }),
    })))))))))))))))));

    /// <summary>Wire name, getter and "is a nonblank value required when listed as required" for every string field.</summary>
    private static readonly (string Name, Func<PlcOperationRequest, string?> Get)[] StringFields = WithSecurityStringFields(WithTechnologyObjectStringFields(new (string Name, Func<PlcOperationRequest, string?> Get)[]
    {
        ("blockPath", op => op.BlockPath),
        ("typePath", op => op.TypePath),
        ("format", op => op.Format),
        ("plcName", op => op.PlcName),
        ("tableName", op => op.TableName),
        ("folderPath", op => op.FolderPath),
        ("content", op => op.Content),
        ("expectedContentHash", op => op.ExpectedContentHash),
        ("name", op => op.Name),
        ("newName", op => op.NewName),
        ("dataType", op => op.DataType),
        ("logicalAddress", op => op.LogicalAddress),
        ("value", op => op.Value),
        ("blockType", op => op.BlockType),
        ("language", op => op.Language),
        ("obEventClass", op => op.ObEventClass),
        ("section", op => op.Section),
        ("memberPath", op => op.MemberPath),
        ("startValue", op => op.StartValue),
        ("comment", op => op.Comment),
        ("commentCulture", op => op.CommentCulture),
        ("insertBefore", op => op.InsertBefore),
        ("targetGroupPath", op => op.TargetGroupPath),
        ("targetFolderPath", op => op.TargetFolderPath),
        ("instanceOfName", op => op.InstanceOfName),
        ("targetTableName", op => op.TargetTableName),
        ("area", op => op.Area),
        ("compareTarget", op => op.CompareTarget),
        ("targetName", op => op.TargetName),
        ("hardwareScope", op => op.HardwareScope),
        ("textListName", op => op.TextListName),
        ("listRange", op => op.ListRange),
        ("exportOption", op => op.ExportOption),
        ("importOption", op => op.ImportOption),
        // filePath, directoryPath (and overwrite below) are shared by the source/folder, alarm and technology object file operations.
        ("filePath", op => op.FilePath),
        ("expectedWorkbookHash", op => op.ExpectedWorkbookHash),
        ("sheet", op => op.Sheet),
        ("startColumn", op => op.StartColumn),
        ("portalConfirmations", op => op.PortalConfirmations),
        ("proDiagFb", op => op.ProDiagFb),
        ("directoryPath", op => op.DirectoryPath),
    }.Concat(SourceFolderStringFields()).Concat(SafetyStringFields()).Concat(EditingStringFields()).Concat(SimaticSdStringFields()).Concat(LoadableStringFields()).Concat(ProjectLanguageStringFields()).Concat(SettingsStringFields()).Concat(OpcUaStringFields()).Concat(OnlineFingerprintStringFields()).ToArray()))
        // The source/folder, alarm and technology object fields share filePath and directoryPath: one entry per wire name.
        .DistinctBy(field => field.Name).ToArray();

    private static readonly (string Name, Func<PlcOperationRequest, bool> IsSet)[] OperationFields =
        StringFields
            .Select(field => (field.Name, IsSet: new Func<PlcOperationRequest, bool>(op => field.Get(op) is not null)))
            .Concat(new (string, Func<PlcOperationRequest, bool>)[]
            {
                ("withDependencies", op => op.WithDependencies is not null),
                ("externalAccessible", op => op.ExternalAccessible is not null),
                ("externalVisible", op => op.ExternalVisible is not null),
                ("externalWritable", op => op.ExternalWritable is not null),
                ("clearStartValue", op => op.ClearStartValue is not null),
                ("number", op => op.Number is not null),
                ("autoNumber", op => op.AutoNumber is not null),
                ("retain", op => op.Retain is not null),
                ("setPoint", op => op.SetPoint is not null),
                ("withAttributes", op => op.WithAttributes is not null),
                ("areas", op => op.Areas is not null),
                ("includeHardware", op => op.IncludeHardware is not null),
                ("startByte", op => op.StartByte is not null),
                ("endByte", op => op.EndByte is not null),
                ("requireHardware", op => op.RequireHardware is not null),
                ("alignBytes", op => op.AlignBytes is not null),
                ("maxDepth", op => op.MaxDepth is not null),
                ("maxElements", op => op.MaxElements is not null),
                ("differencesOnly", op => op.DifferencesOnly is not null),
                ("timeoutSeconds", op => op.TimeoutSeconds is not null),
                ("textListNames", op => op.TextListNames is not null),
                ("cultures", op => op.Cultures is not null),
                ("trustedCertificates", op => op.TrustedCertificates is not null),
                ("overwrite", op => op.Overwrite is not null),
                ("cellEdits", op => op.CellEdits is not null),
                ("startRow", op => op.StartRow is not null),
            })
            .Concat(SourceFolderOtherFields())
            .Concat(TechnologyObjectOtherFields())
            .Concat(SecurityOtherFields())
            .Concat(EditingOtherFields())
            .Concat(SimaticSdOtherFields())
            .Concat(CompilationSettingsOtherFields())
            .Concat(LoadableOtherFields())
            .Concat(ProjectLanguageOtherFields())
            .Concat(SettingsOtherFields())
            .Concat(OpcUaOtherFields())
            .Concat(DeviceDataOtherFields())
            .Concat(HelpOtherFields())
            .Concat(ImportOptionOtherFields())
            .Concat(OnlineLoginOtherFields())
            // The source/folder, alarm and technology object fields share overwrite, offset and limit: one entry per wire name.
            .DistinctBy(field => field.Item1)
            .ToArray();

    private static readonly IReadOnlySet<string> GroupPathOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "create_block",
        "create_block_group",
        "delete_block_group",
        "create_instance_db",
    };

    public static IReadOnlyList<string> ReadOperationNames { get; } =
        Specs.Where(entry => !entry.Value.IsWrite).Select(entry => entry.Key).ToArray();

    public static IReadOnlyList<string> WriteOperationNames { get; } =
        Specs.Where(entry => entry.Value.IsWrite).Select(entry => entry.Key).ToArray();

    /// <summary>Exposes write fields to catalog and field-forwarding tests so new fields cannot escape their coverage.</summary>
    public static bool TryGetWriteFields(
        string operation,
        out IReadOnlyList<string> required,
        out IReadOnlyList<string> optional)
    {
        if (Specs.TryGetValue(operation, out var spec) && spec.IsWrite)
        {
            required = spec.Required;
            optional = spec.Optional;
            return true;
        }

        required = optional = None;
        return false;
    }

    public static OperationValidation ValidateRead(IReadOnlyList<PlcOperationRequest>? operations)
        => Validate(operations, write: false);

    public static OperationValidation ValidateWrite(IReadOnlyList<PlcOperationRequest>? operations)
        => Validate(operations, write: true);

    public static IReadOnlyList<string> ValidateAccessMode(
        IReadOnlyList<PlcOperationRequest> operations,
        McpAccessMode mode)
    {
        var errors = new List<string>();
        foreach (var operation in operations)
        {
            if (operation is null || string.IsNullOrWhiteSpace(operation.Operation))
            {
                continue;
            }

            if (!OperationPolicyCatalog.IsAllowed(mode, operation.Operation))
            {
                errors.Add(
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}') is not permitted in {McpAccessModeNames.ToName(mode)} mode.");
            }
        }

        return errors;
    }

    private static OperationValidation Validate(IReadOnlyList<PlcOperationRequest>? operations, bool write)
    {
        if (OperationBatchRules.CheckCount(operations?.Count ?? 0) is { } countError)
        {
            return OperationValidation.Invalid(WorkerFailureCategories.ValidationError, countError);
        }

        var errors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var envelopeValid = new List<PlcOperationRequest>();
        foreach (var operation in operations!)
        {
            if (OperationBatchRules.CheckItem(operation, seenIds) is { } itemError)
            {
                errors.Add(itemError);
                continue;
            }

            envelopeValid.Add(operation);
            ValidateItem(operation, errors, write);
        }

        if (write)
        {
            ValidateWriteCall(envelopeValid, errors);
        }

        return errors.Count == 0
            ? OperationValidation.Valid
            : OperationValidation.Invalid(WorkerFailureCategories.ValidationError, string.Join("\n", errors));
    }

    private static void ValidateItem(PlcOperationRequest operation, List<string> errors, bool write)
    {
        if (string.IsNullOrWhiteSpace(operation.Operation))
        {
            errors.Add($"Operation name is required for operationId '{operation.OperationId}'.");
            return;
        }

        if (!Specs.TryGetValue(operation.Operation, out var spec))
        {
            errors.Add(
                $"Unknown operation '{operation.Operation}' for operationId '{operation.OperationId}'. "
                + $"Valid {(write ? "write" : "read")} operations: "
                + $"{string.Join(", ", write ? WriteOperationNames : ReadOperationNames)}.");
            return;
        }

        if (spec.IsWrite != write)
        {
            errors.Add(write
                ? $"'{operation.Operation}' is a read operation; use plc_read."
                : $"'{operation.Operation}' is a write operation; use plc_write.");
            return;
        }

        var prefix = $"Operation '{operation.Operation}' (operationId '{operation.OperationId}')";
        foreach (var (name, isSet) in OperationFields)
        {
            if (isSet(operation) && !spec.Required.Contains(name) && !spec.Optional.Contains(name))
            {
                var valid = spec.Optional.Count > 0 ? string.Join(", ", spec.Optional) : "(none)";
                errors.Add($"{prefix}: '{name}' is not valid for {operation.Operation}. Valid optional fields: {valid}.");
            }
        }

        var missing = spec.Required.Where(field => !IsPresent(operation, field)).ToArray();
        if (missing.Length > 0)
        {
            errors.Add($"{prefix} is missing required field(s): {string.Join(", ", missing)}.");
        }

        foreach (var (name, value) in new[]
                 {
                     ("plcName", operation.PlcName), ("tableName", operation.TableName), ("folderPath", operation.FolderPath),
                     ("targetTableName", operation.TargetTableName), ("targetFolderPath", operation.TargetFolderPath),
                 })
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{prefix}: '{name}' must be nonblank when supplied.");
            }
        }

        ValidateOnlineItem(operation, prefix, errors);

        var formatIsValid = true;
        if (operation.Format is not null)
        {
            try
            {
                _ = PlcFormatNames.Normalize(operation.Operation, operation.Format);
            }
            catch (ArgumentException exception)
            {
                formatIsValid = false;
                errors.Add($"{prefix}: {exception.Message}");
            }
        }

        if (write)
        {
            ValidateWriteItem(operation, prefix, formatIsValid, errors);
        }
        else if (operation.Operation == "get_db_values" && operation.MemberPath is not null)
        {
            PlcBlockEditValidation.MemberPath(operation.MemberPath, allowIndices: true, prefix, errors);
        }

        PlcAddressSpace.Validate(operation, prefix, errors);
        ValidateTechnologyObjectItem(operation, prefix, errors);
        PlcAnalysis.PlcAnalysisPolicy.Validate(operation, prefix, errors);
        ValidateSourceFolderItem(operation, prefix, errors);
        ValidateAlarmItem(operation, prefix, errors);
        ValidateSecurityItem(operation, prefix, errors);
        ValidateSafetyItem(operation, prefix, errors);
        ValidateEditingItem(operation, prefix, errors);
        ValidateSimaticSdItem(operation, prefix, errors);
        ValidateCompilationSettingsItem(operation, prefix, errors);
        ValidateChangeDetectionItem(operation, prefix, errors);
        ValidateLoadableItem(operation, prefix, errors);
        ValidateBlockProtectionItem(operation, prefix, errors);
        ValidateProjectLanguageItem(operation, prefix, errors);
        ValidateSettingsItem(operation, prefix, errors);
        ValidateOpcUaItem(operation, prefix, errors);
        ValidateDeviceDataItem(operation, prefix, errors);
        ValidateHelpItem(operation, prefix, errors);
        ValidateImportOptionsItem(operation, prefix, errors);
        ValidateOnlineLoginItem(operation, prefix, errors);
        ValidateOnlineFingerprintItem(operation, prefix, errors);
    }

    /// <summary>
    /// The normalized <c>trustedCertificates</c> of a compare_to_online item (validated by <see cref="ValidateRead"/>; an
    /// invalid list trusts nothing).
    /// </summary>
    public static IReadOnlyList<string> TrustedCertificates(PlcOperationRequest operation)
        => PlcTlsTrustPolicy.NormalizeList(operation.TrustedCertificates, out var normalized) is null ? normalized : Array.Empty<string>();

    /// <summary>The compare caps and targets, the table entry cap and the watch-table group paths.</summary>
    private static void ValidateOnlineItem(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        switch (operation.Operation)
        {
            case PlcOnlineMethods.CompareHardware:
            case PlcOnlineMethods.CompareHardwareToOnline:
                if (PlcOnlinePolicy.ValidateHardwareCompare(operation.CompareTarget, operation.TargetName, operation.HardwareScope,
                        operation.MaxDepth ?? PlcOnlinePolicy.DefaultCompareDepth, operation.MaxElements ?? PlcOnlinePolicy.DefaultCompareElements,
                        online: operation.Operation == PlcOnlineMethods.CompareHardwareToOnline) is { } hardware)
                    errors.Add($"{prefix}: {hardware}");
                if (PlcTlsTrustPolicy.NormalizeList(operation.TrustedCertificates, out _) is { } hardwareTrusted)
                    errors.Add($"{prefix}: {hardwareTrusted}");
                break;
            case PlcOnlineMethods.CompareToOnline:
            case PlcOnlineMethods.CompareSoftware:
                if (PlcOnlinePolicy.ValidateCompare(operation.CompareTarget, operation.TargetName,
                        operation.MaxDepth ?? PlcOnlinePolicy.DefaultCompareDepth, operation.MaxElements ?? PlcOnlinePolicy.DefaultCompareElements,
                        online: operation.Operation == PlcOnlineMethods.CompareToOnline) is { } compare)
                    errors.Add($"{prefix}: {compare}");
                if (PlcTlsTrustPolicy.NormalizeList(operation.TrustedCertificates, out _) is { } trusted)
                    errors.Add($"{prefix}: {trusted}");
                break;
            case PlcOnlineMethods.ReadWatchTable:
            case PlcOnlineMethods.ReadForceTable:
                if (operation.MaxElements is < 1 or > PlcWatchTablePolicy.MaxEntries)
                    errors.Add($"{prefix}: maxElements must be between 1 and {PlcWatchTablePolicy.MaxEntries}.");
                break;
            case PlcOnlineMethods.CreateWatchTableGroup:
            case PlcOnlineMethods.DeleteWatchTableGroup:
                if (operation.FolderPath is not null && PlcWatchTablePolicy.Segments(operation.FolderPath).Count == 0)
                    errors.Add($"{prefix}: folderPath must name a user group, for example /Line/Cell; the system group '/' cannot be created or deleted.");
                break;
        }
    }

    private static void ValidateWriteItem(PlcOperationRequest operation, string prefix, bool formatIsValid, List<string> errors)
    {
        if (GroupPathOperations.Contains(operation.Operation) && !string.IsNullOrWhiteSpace(operation.BlockPath))
        {
            ValidateGroupPath(operation.BlockPath, prefix, errors);
        }

        if (operation.Operation == "create_block")
        {
            ValidateObCreation(operation, prefix, errors);
        }

        PlcBlockEditValidation.Validate(operation, prefix, errors);

        if (!string.IsNullOrWhiteSpace(operation.ExpectedContentHash) && formatIsValid)
        {
            ValidateHash(operation, prefix, errors);
        }
    }

    private static void ValidateObCreation(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        var isOb = string.Equals(operation.BlockType, "OB", StringComparison.OrdinalIgnoreCase);
        if (operation.ObEventClass is not null)
        {
            if (!isOb)
            {
                errors.Add($"{prefix}: obEventClass applies only to blockType OB.");
            }
            else if (!ObEventClasses.TryGet(operation.ObEventClass, out _))
            {
                errors.Add($"{prefix}: obEventClass '{operation.ObEventClass}' is not valid. Valid values: {ObEventClasses.NamesForMessage()}.");
            }
        }

        if (isOb && string.Equals(operation.Language, "GRAPH", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{prefix}: language GRAPH is not supported for blockType OB.");
        }
    }

    private static void ValidateGroupPath(string blockPath, string prefix, List<string> errors)
    {
        try
        {
            if (BlockAddress.Parse(blockPath).PlcName is null)
            {
                errors.Add(
                    $"{prefix}: blockPath '{blockPath}' is a single name. Use 'PLC/Name' (the PLC root group) "
                    + "or the deterministic 'PLC/Blocks/.../Name'.");
            }
        }
        catch (ArgumentException exception)
        {
            errors.Add($"{prefix}: {exception.Message}");
        }
    }

    private static void ValidateHash(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        var hash = operation.ExpectedContentHash!;
        if (!ContentHashRules.TryParse(hash, out var hashFormat, out _))
        {
            errors.Add(
                $"{prefix}: expectedContentHash '{hash}' is malformed. Expected '<format>:sha256:<64 lower-case hex>' "
                + $"with format one of: {string.Join(", ", SourceFormatNames.HashFormats)}.");
            return;
        }

        var writeFormat = PlcFormatNames.Normalize(operation.Operation, operation.Format);
        if (!string.Equals(hashFormat, writeFormat, StringComparison.Ordinal))
        {
            errors.Add(
                $"{prefix}: expectedContentHash was taken from the '{hashFormat}' format but the write uses the "
                + $"'{writeFormat}' format. Read the object in the write format and use that hash.");
        }
    }

    private static void ValidateWriteCall(IReadOnlyList<PlcOperationRequest> operations, List<string> errors)
    {
        ValidateAlarmCall(operations, errors);
        var createdBlocks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (operation is null || string.IsNullOrWhiteSpace(operation.BlockPath))
            {
                continue;
            }

            if (operation.Operation == "create_block")
            {
                createdBlocks.Add(BlockKey(operation.BlockPath));
            }
            else if (operation.Operation == "update_block_logic" && createdBlocks.Contains(BlockKey(operation.BlockPath)))
            {
                errors.Add(
                    $"Operation 'update_block_logic' (operationId '{operation.OperationId}'): block '{operation.BlockPath}' "
                    + "is created earlier in this call, so no content hash can exist for it. Read it after the call.");
            }
        }
    }

    /// <summary>Spelling-independent identity of a block path ('PLC/Name' equals 'PLC/Blocks/Name').</summary>
    private static string BlockKey(string blockPath)
    {
        try
        {
            var address = BlockAddress.Parse(blockPath);
            return string.Join(
                "/",
                new[] { address.PlcName ?? string.Empty, address.UnitName ?? string.Empty }
                    .Concat(address.FolderPath)
                    .Append(address.BlockName)).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return blockPath.Trim().ToLowerInvariant();
        }
    }

    private static bool IsPresent(PlcOperationRequest operation, string field)
        => StringFields.Any(entry => entry.Name == field && !string.IsNullOrWhiteSpace(entry.Get(operation)));
}
