using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>Dispatches validated PLC read and write operations to the worker client.</summary>
public static class PlcWorkerInvoker
{
    /// <param name="workbookResultChars">The row budget of an alarm workbook read (<see cref="PlcAlarms.PlcAlarmRequests.ResultBudget"/>).</param>
    public static Task<WorkerCallResult> InvokeReadAsync(
        OpennessWorkerClient client,
        PlcOperationRequest operation,
        CancellationToken cancellationToken = default,
        int? workbookResultChars = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return operation.Operation switch
            {
                "get_block_content" => client.GetBlockContentAsync(
                    operation.BlockPath!,
                    format: PlcFormatNames.Normalize(operation.Operation, operation.Format),
                    withDependencies: operation.WithDependencies),
                "get_type_content" => client.GetTypeContentAsync(
                    operation.TypePath!,
                    format: PlcFormatNames.Normalize(operation.Operation, operation.Format),
                    withDependencies: operation.WithDependencies),
                "list_tag_tables" => client.ListTagTablesAsync(
                    operation.PlcName, tableName: operation.TableName, folderPath: operation.FolderPath),
                "list_download_targets" => client.ListDownloadTargetsAsync(operation.PlcName),
                PlcBlockEditMethods.GetBlockInterface or PlcBlockEditMethods.GetTypeInterface or PlcBlockEditMethods.GetDbValues or PlcBlockEditMethods.GetDbSnapshot
                    => client.PlcBlockEditAsync(operation.Operation, EditRequest(operation)),
                var compare when PlcOnlineMethods.IsCompare(compare) => client.ComparePlcAsync(operation.Operation, new PlcOnlineRequestInfo
                {
                    PlcName = operation.PlcName,
                    CompareTarget = operation.CompareTarget,
                    TargetName = operation.TargetName,
                    TargetProject = operation.TargetProject,
                    MaxDepth = operation.MaxDepth ?? PlcOnlinePolicy.DefaultCompareDepth,
                    MaxElements = operation.MaxElements ?? PlcOnlinePolicy.DefaultCompareElements,
                    DifferencesOnly = operation.DifferencesOnly ?? true,
                    HardwareScope = PlcOnlineMethods.IsHardwareCompare(operation.Operation) ? operation.HardwareScope ?? PlcOnlinePolicy.HardwareScopes.Device : null,
                    TrustedCertificates = PlcOperationCatalog.TrustedCertificates(operation).ToList(),
                    // The online compares answer online password prompts from the item's sources and the server password map.
                    Passwords = PlcOnlineMethods.IsOnlineCompare(operation.Operation)
                        ? PlcOnline.PlcOnlineConnectionTools.Passwords(operation.Passwords, null, client.BindingSnapshot.ProjectPath, operation.PlcName).ToList()
                        : new List<PlcCallbackPasswordInfo>(),
                }),
                PlcOnlineMethods.GetOnlineState => client.GetOnlineStateAsync(operation.PlcName),
                PlcOnlineMethods.ListStationUploadInterfaces => client.ListStationUploadInterfacesAsync(),
                PlcOnlineMethods.ListWatchAndForceTables => client.ListWatchAndForceTablesAsync(operation.PlcName),
                PlcOnlineMethods.ReadWatchTable or PlcOnlineMethods.ReadForceTable => client.ReadWatchOrForceTableAsync(operation.Operation,
                    operation.PlcName, operation.TableName!, operation.FolderPath, operation.MaxElements ?? PlcWatchTablePolicy.DefaultMaxEntries),
                PlcOnlineMethods.GetWatchTableContent or PlcOnlineMethods.GetForceTableContent => client.GetWatchOrForceTableContentAsync(
                    operation.Operation, operation.PlcName, operation.TableName!, operation.FolderPath),
                var method when PlcSourceFolderOperations.IsWorkerRead(method) => PlcSourceFolderOperations.InvokeReadAsync(client, operation),
                var method when PlcAlarmMethods.IsReadMethod(method) => client.PlcAlarmAsync(method, PlcAlarms.PlcAlarmRequests.From(operation, maxResultChars: workbookResultChars)),
                var method when TechnologyObjectMethods.IsRead(method) => client.TechnologyObjectAsync(method, PlcTechnologyObjectRequests.ToRequest(operation)),
                var method when PlcSecurityMethods.IsRead(method) => client.PlcSecurityAsync(method, PlcSecurityPayloadContract.ToRequest(operation)),
                var method when Contracts.Plc.Safety.PlcSafetyMethods.IsRead(method) => client.PlcSafetyAsync(method, PlcSafetyContract.ToRequest(operation)),
                var method when Contracts.Project.ProjectLanguageMethods.IsRead(method) => client.ProjectLanguagesAsync(method, PlcProjectLanguages.ToRequest(operation)),
                var method when Contracts.Plc.BlockProtection.PlcBlockProtectionMethods.IsRead(method)
                    => client.PlcBlockProtectionAsync(method, PlcBlockProtection.ToRequest(operation)),
                var method when Contracts.Plc.ChangeDetection.PlcChangeDetectionMethods.IsRead(method)
                    => client.PlcChangeDetectionAsync(method, PlcChangeDetectionPayloadContract.ToRequest(operation)),
                var method when Contracts.Plc.Settings.PlcSettingsCatalog.IsRead(method) => client.PlcSettingsAsync(method, PlcSettings.ToRequest(operation)),
                var method when Contracts.Plc.OpcUa.PlcOpcUaMethods.IsRead(method) => client.PlcOpcUaAsync(method, PlcOpcUa.ToRequest(operation)),
                _ => Task.FromResult(WorkerCallResult.Fail(
                    WorkerFailureCategories.ValidationError,
                    $"Unsupported PLC read operation '{operation.Operation}'.")),
            };
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, exception.Message));
        }
    }

    /// <summary>Executes one validated write operation against the worker.</summary>
    public static Task<WorkerCallResult> InvokeWriteAsync(
        OpennessWorkerClient client,
        PlcOperationRequest op,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return op.Operation switch
        {
            "update_block_logic" => InvokeUpdateBlockLogic(client, op),
            "update_type_content" => InvokeUpdateTypeContent(client, op),
            "create_tag_table" => client.CreateTagTableAsync(op.PlcName, op.TableName!, op.FolderPath),
            "delete_tag_table" => client.DeleteTagTableAsync(op.PlcName, op.TableName!, op.FolderPath),
            "create_tag" => client.CreateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.LogicalAddress, op.Comment, op.CommentCulture),
            "update_tag" => client.UpdateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.NewName, op.DataType, op.LogicalAddress, op.ExternalAccessible, op.ExternalVisible, op.ExternalWritable,
                op.Comment, op.CommentCulture),
            "delete_tag" => client.DeleteTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!),
            PlcTagMoveMethods.MoveTag => client.MoveTagAsync(new PlcTagMoveRequestInfo
            {
                PlcName = op.PlcName,
                TableName = op.TableName!,
                FolderPath = op.FolderPath,
                Name = op.Name!,
                TargetTableName = op.TargetTableName!,
                TargetFolderPath = op.TargetFolderPath,
                // A fresh name per attempt keeps the temporary tag unique and names it in any failure.
                TemporaryName = PlcTagMoveMethods.TemporaryNamePrefix + Guid.NewGuid().ToString("N").Substring(0, 12),
            }),
            "create_user_constant" => client.CreateUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.Value!),
            "update_user_constant" => client.UpdateUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType, op.Value),
            "delete_user_constant" => client.DeleteUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!),
            "create_block" => client.CreateBlockAsync(op.BlockPath!, op.BlockType!, op.Language, op.ObEventClass),
            "delete_block" => client.DeleteBlockAsync(op.BlockPath!),
            "create_block_group" => client.CreateBlockGroupAsync(op.BlockPath!),
            "delete_block_group" => client.DeleteBlockGroupAsync(op.BlockPath!),
            var method when PlcBlockEditMethods.Write.Contains(method, StringComparer.Ordinal)
                => client.PlcBlockEditAsync(method, EditRequest(op)),
            PlcOnlineMethods.CreateWatchTable or PlcOnlineMethods.DeleteWatchTable or PlcOnlineMethods.CreateWatchTableGroup
                or PlcOnlineMethods.DeleteWatchTableGroup or PlcOnlineMethods.UpdateProgram
                => client.WatchTableWriteAsync(op.Operation, op.PlcName, op.TableName, op.FolderPath),
            PlcOnlineMethods.UpdateWatchTableContent or PlcOnlineMethods.UpdateForceTableContent
                => client.WatchTableWriteAsync(op.Operation, op.PlcName, op.TableName, op.FolderPath, op.Content, op.ExpectedContentHash),
            var method when PlcSourceFolderMethods.IsWrite(method) => PlcSourceFolderOperations.InvokeWriteAsync(client, op, cancellationToken),
            var method when PlcAlarmMethods.IsWriteMethod(method) => client.PlcAlarmAsync(method, PlcAlarms.PlcAlarmRequests.From(op)),
            var method when TechnologyObjectMethods.IsWrite(method) => client.TechnologyObjectAsync(method, PlcTechnologyObjectRequests.ToRequest(op)),
            var method when PlcSecurityMethods.IsWrite(method) => client.PlcSecurityAsync(method, PlcSecurityPayloadContract.ToRequest(op)),
            var method when Contracts.Plc.Safety.PlcSafetyMethods.IsWrite(method) => client.PlcSafetyAsync(method, PlcSafetyContract.ToRequest(op)),
            Contracts.Project.ProjectCompilationSettingsMethods.Set => client.ProjectCompilationSettingsAsync(PlcCompilationSettings.ToRequest(op)),
            Contracts.Plc.Loadable.PlcLoadableMethods.GenerateLoadable => client.GenerateLoadableAsync(PlcLoadable.ToRequest(op)),
            var method when Contracts.Plc.BlockProtection.PlcBlockProtectionMethods.IsWrite(method) => client.PlcBlockProtectionAsync(method, PlcBlockProtection.ToRequest(op)),
            var method when Contracts.Project.ProjectLanguageMethods.IsWrite(method) => client.ProjectLanguagesAsync(method, PlcProjectLanguages.ToRequest(op)),
            var method when Contracts.Plc.Settings.PlcSettingsCatalog.IsWrite(method) => client.PlcSettingsAsync(method, PlcSettings.ToRequest(op)),
            var method when Contracts.Plc.OpcUa.PlcOpcUaMethods.IsWrite(method) => client.PlcOpcUaAsync(method, PlcOpcUa.ToRequest(op)),
            var method when Contracts.Plc.DeviceData.PlcDeviceDataMethods.IsWorkerMethod(method) => client.PlcDeviceDataAsync(method, PlcDeviceData.ToRequest(op)),
            _ => Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported PLC write operation '{op.Operation}'.")),
        };
    }

    /// <summary>The worker parameters of one granular block operation; only the method's fields are read.</summary>
    internal static PlcBlockEditRequestInfo EditRequest(PlcOperationRequest op) => new()
    {
        BlockPath = op.BlockPath,
        TypePath = op.TypePath,
        MemberPath = op.MemberPath,
        NewName = op.NewName,
        Number = op.Number,
        AutoNumber = op.AutoNumber,
        InstanceOfName = op.InstanceOfName,
        TargetGroupPath = op.TargetGroupPath,
        PlcName = op.PlcName,
        TableName = op.TableName,
        FolderPath = op.FolderPath,
        TargetFolderPath = op.TargetFolderPath,
        StartValue = op.StartValue,
        ExternalAccessible = op.ExternalAccessible,
        ExternalVisible = op.ExternalVisible,
        ExternalWritable = op.ExternalWritable,
        Retain = op.Retain,
        SetPoint = op.SetPoint,
        WithAttributes = op.WithAttributes,
        MaxElements = op.Operation is PlcBlockEditMethods.GetDbSnapshot or PlcBlockEditMethods.CreateDbSnapshot ? op.MaxElements : null,
        Renames = op.Operation is PlcBlockEditMethods.RenameTags or PlcBlockEditMethods.RenameObjects ? op.Renames : null,
        // A fresh stamp per attempt keeps the temporary names unique and names them in any failure.
        Replacements = op.Operation == PlcBlockEditMethods.ReplaceTagComments ? op.Replacements : null,
        Culture = op.Operation == PlcBlockEditMethods.ReplaceTagComments ? op.Culture : null,
        FilePath = PlcBlockEditMethods.CallerFileWrites.Contains(op.Operation, StringComparer.Ordinal) ? op.FilePath : null,
        Overwrite = PlcBlockEditMethods.CallerFileWrites.Contains(op.Operation, StringComparer.Ordinal) || op.Operation == Contracts.Plc.SimaticSd.SimaticSdMethods.CreateBlockFromDocument ? op.Overwrite : null,
        Content = op.Operation == Contracts.Plc.SimaticSd.SimaticSdMethods.CreateBlockFromDocument ? op.Content : null,
        TemporaryStamp = op.Operation is PlcBlockEditMethods.RenameTags or PlcBlockEditMethods.RenameObjects ? Guid.NewGuid().ToString("N").Substring(0, 8) : null,
    };

    private static Task<WorkerCallResult> InvokeUpdateBlockLogic(OpennessWorkerClient client, PlcOperationRequest op)
    {
        if (!PlcFormatNames.TryNormalize(op.Operation, op.Format, out var format, out var error))
        {
            // Rejected before any worker call: the import never started, and the caller gets typed evidence of that.
            return Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, error!) with
            {
                DispatchState = WorkerDispatchState.NotSent,
                BlockImportOutcome = BlockImportOutcomeSynthesizer.Synthesize(
                    WorkerDispatchState.NotSent,
                    normalizedFormatOrNull: null),
            });
        }

        return client.UpdateBlockLogicAsync(op.BlockPath!, op.Content!, format: format, expectedContentHash: op.ExpectedContentHash);
    }

    private static Task<WorkerCallResult> InvokeUpdateTypeContent(OpennessWorkerClient client, PlcOperationRequest op)
        => PlcFormatNames.TryNormalize(op.Operation, op.Format, out var format, out var error)
            ? client.UpdateTypeContentAsync(op.TypePath!, op.Content!, format: format, expectedContentHash: op.ExpectedContentHash)
            : Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, error!));
}
