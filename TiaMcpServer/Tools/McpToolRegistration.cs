using ModelContextProtocol.Protocol;
using Microsoft.Extensions.DependencyInjection;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Tools;

/// <summary>The public surface follows the same capability presets as host and worker dispatch.</summary>
public static class McpToolRegistration
{
    private const string ValidationError = WorkerFailureCategories.ValidationError;

    /// <summary>
    /// The one registration table: every tool type, its canonical argument-shape rejection, and whether nested
    /// schemas are closed. <c>closeNestedSchemas</c> also closes the output schema, so it matches each tool on main.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, Func<IMcpServerBuilder, IMcpServerBuilder>> ProductionTools =
        new Dictionary<Type, Func<IMcpServerBuilder, IMcpServerBuilder>>
        {
            [typeof(ProjectReadTools)] = builder => builder.WithClosedTools(typeof(ProjectReadTools),
                (_, tool, message) => tool == "browse_project_tree"
                    ? ProjectTreeArgumentValidatingTool.ValidationFailure(ValidationError, message)
                    : Standalone<ProjectStatusInfo>(tool, message),
                closeNestedSchemas: true, transformSchemaNode: ProjectReadToolRegistration.AlignSelectorSchemaNullability,
                decorate: (tool, strict) => tool == "browse_project_tree" ? new ProjectTreeArgumentValidatingTool(strict) : strict),
            [typeof(ProjectBindingTools)] = builder => builder.WithClosedTools(typeof(ProjectBindingTools),
                (_, tool, message) => Standalone<ProjectBindingResult>(tool, message),
                closeNestedSchemas: true, transformSchemaNode: ProjectBindingToolRegistration.AllowNullForceRebind,
                decorate: (_, strict) => new BindProjectArgumentValidatingTool(strict)),
            [typeof(NetworkReadTools)] = builder => builder.WithClosedTools(typeof(NetworkReadTools),
                (_, _, message) => NetworkReadTools.Error(ValidationError, message), closeNestedSchemas: false,
                transformSchemaNode: NetworkWriteToolRegistration.ShortenRepeatedSelectors),
            [typeof(PlcReadTools)] = builder => builder.WithClosedTools(typeof(PlcReadTools),
                (_, _, message) => PlcReadTools.Error(ValidationError, message), closeNestedSchemas: false,
                transformSchemaNode: PlcItemSchema.ShortenFieldDescriptions),
            [typeof(Hmi.HmiReadTools)] = builder => builder.WithClosedTools(typeof(Hmi.HmiReadTools),
                (_, _, message) => Hmi.HmiReadTools.Error(ValidationError, message), closeNestedSchemas: false),
            [typeof(CrossReferences.CrossReferenceReadTools)] = builder => builder.WithClosedTools(
                typeof(CrossReferences.CrossReferenceReadTools),
                (_, tool, message) => Standalone<CrossReferenceReport>(tool, message), closeNestedSchemas: false),
            [typeof(Vci.VciReadTools)] = builder => builder.WithClosedTools(typeof(Vci.VciReadTools),
                (_, _, message) => Vci.VciReadTools.Error(ValidationError, message), closeNestedSchemas: true),
            [typeof(Library.LibraryReadTools)] = builder => builder.WithClosedTools(typeof(Library.LibraryReadTools),
                (_, _, message) => Library.LibraryReadTools.Error(ValidationError, message), closeNestedSchemas: true,
                transformSchemaNode: Library.LibraryItemSchema.KeepSegmentDepth),
            [typeof(ClaudeInbox.TiaInboxTools)] = builder => builder.WithClosedTools(typeof(ClaudeInbox.TiaInboxTools),
                (_, _, message) => ClaudeInbox.TiaInboxTools.Error(ValidationError, message), closeNestedSchemas: true),
            [typeof(ProjectEngineeringTools)] = builder => builder.WithClosedTools(typeof(ProjectEngineeringTools),
                (_, tool, message) => Standalone<CompileCheckReport>(tool, message), closeNestedSchemas: false),
            [typeof(NetworkWriteTools)] = builder => builder.WithClosedTools(typeof(NetworkWriteTools),
                (services, _, message) => WriteRejection(new NetworkWriteDomain(Client(services)), message),
                closeNestedSchemas: true, transformSchemaNode: NetworkWriteToolRegistration.TransformWrite),
            [typeof(PlcWriteTools)] = builder => builder.WithClosedTools(typeof(PlcWriteTools),
                (services, _, message) => WriteRejection(new PlcWriteDomain(Client(services)), message),
                closeNestedSchemas: true, transformSchemaNode: PlcItemSchema.TransformWrite,
                retiredItemMembers: PlcWriteToolRegistration.RetiredItemMembers),
            [typeof(Hmi.HmiWriteTools)] = builder => builder.WithClosedTools(typeof(Hmi.HmiWriteTools),
                (services, _, message) => WriteRejection(new Hmi.HmiWriteDomain(Client(services)), message),
                closeNestedSchemas: true),
            [typeof(Vci.VciWriteTools)] = builder => builder.WithClosedTools(typeof(Vci.VciWriteTools),
                (services, _, message) => WriteRejection(new Vci.VciWriteDomain(Client(services),
                    services.GetRequiredService<Vci.VciBreadcrumbLocation>()), message),
                closeNestedSchemas: true),
            [typeof(Library.LibraryWriteTools)] = builder => builder.WithClosedTools(typeof(Library.LibraryWriteTools),
                (services, _, message) => WriteRejection(new Library.LibraryWriteDomain(Client(services)), message),
                closeNestedSchemas: true, transformSchemaNode: Library.LibraryItemSchema.KeepSegmentDepth),
            [typeof(PlcSim.PlcSimReadTools)] = builder => builder.WithClosedTools(typeof(PlcSim.PlcSimReadTools),
                (_, _, message) => PlcSim.PlcSimReadTools.Error(ValidationError, message), closeNestedSchemas: true),
            [typeof(PlcSim.PlcSimWriteTools)] = builder => builder.WithClosedTools(typeof(PlcSim.PlcSimWriteTools),
                (_, _, message) => PlcSim.PlcSimWriteTools.Error(ValidationError, message), closeNestedSchemas: true),
            [typeof(PlcSim.PlcSimTestTools)] = builder => builder.WithClosedTools(typeof(PlcSim.PlcSimTestTools),
                (_, _, message) => PlcSim.PlcSimTestTools.Error(ValidationError, message), closeNestedSchemas: true),
            [typeof(ProjectWriteTools)] = builder => builder.WithClosedTools(typeof(ProjectWriteTools),
                (services, tool, message) => WriteRejection(new LifecycleWriteDomain(Client(services), new LifecycleWriteItem(tool)), message),
                closeNestedSchemas: false),
            [typeof(ProjectServers.ProjectServerWriteTools)] = builder => builder.WithClosedTools(typeof(ProjectServers.ProjectServerWriteTools),
                (services, _, message) => WriteRejection(new ProjectServers.ProjectServerWriteDomain(Client(services)), message),
                closeNestedSchemas: true),
            [typeof(PlcDownload.PlcDownloadTools)] = builder => builder.WithClosedTools(typeof(PlcDownload.PlcDownloadTools),
                (services, _, message) => WriteRejection(new PlcDownload.PlcDownloadWriteDomain(Client(services)), message),
                closeNestedSchemas: true, transformSchemaNode: PlcDownload.PlcDownloadToolRegistration.TransformSchema),
            [typeof(SiemensDocs.SiemensDocsTools)] = builder => builder.WithClosedTools(typeof(SiemensDocs.SiemensDocsTools),
                (_, _, message) => SiemensDocs.SiemensDocsTools.Error(ValidationError, message), closeNestedSchemas: true),
            [typeof(PlcWebApi.PlcWebApiReadTools)] = builder => builder.WithClosedTools(typeof(PlcWebApi.PlcWebApiReadTools),
                (_, _, message) => PlcWebApi.PlcWebApiReadTools.Error(ValidationError, message), closeNestedSchemas: true),
            [typeof(PlcWebApi.PlcWebApiWriteTools)] = builder => builder.WithClosedTools(typeof(PlcWebApi.PlcWebApiWriteTools),
                (services, _, message) => WriteRejection(new PlcWebApi.PlcWebApiWriteDomain(services.GetRequiredService<PlcWebApi.PlcWebApiService>()), message),
                closeNestedSchemas: true),
            [typeof(PlcOnline.PlcOnlineConnectionTools)] = builder => builder.WithClosedTools(typeof(PlcOnline.PlcOnlineConnectionTools),
                (services, _, message) => WriteRejection(new PlcOnline.PlcOnlineConnectionWriteDomain(Client(services)), message),
                closeNestedSchemas: true, transformSchemaNode: PlcOnline.PlcOnlineConnectionToolRegistration.TransformSchema),
            [typeof(PlcOnline.PlcStationUploadTools)] = builder => builder.WithClosedTools(typeof(PlcOnline.PlcStationUploadTools),
                (services, _, message) => WriteRejection(new PlcOnline.PlcStationUploadWriteDomain(Client(services)), message),
                closeNestedSchemas: true, transformSchemaNode: PlcOnline.PlcOnlineConnectionToolRegistration.TransformSchema),
        };

    public static IMcpServerBuilder WithAccessModeTools(this IMcpServerBuilder builder, McpAccessMode mode)
    {
        _ = McpAccessModeNames.ToName(mode); // Reject an invalid configuration before registration.
        foreach (var type in new[] { typeof(ProjectReadTools), typeof(ProjectBindingTools), typeof(NetworkReadTools), typeof(PlcReadTools),
                     typeof(Hmi.HmiReadTools), typeof(CrossReferences.CrossReferenceReadTools), typeof(Vci.VciReadTools), typeof(PlcSim.PlcSimReadTools),
                     typeof(PlcWebApi.PlcWebApiReadTools), typeof(Library.LibraryReadTools), typeof(ClaudeInbox.TiaInboxTools), typeof(SiemensDocs.SiemensDocsTools) })
            builder.WithProductionTools(type);
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.Compile))
            builder.WithProductionTools(typeof(ProjectEngineeringTools));
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.ProjectMutation))
            foreach (var type in new[] { typeof(NetworkWriteTools), typeof(PlcWriteTools), typeof(Hmi.HmiWriteTools), typeof(Vci.VciWriteTools),
                         typeof(PlcOnline.PlcStationUploadTools), typeof(Library.LibraryWriteTools) })
                builder.WithProductionTools(type);
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.ProjectLifecycle))
        {
            builder.WithProductionTools(typeof(ProjectWriteTools));
            builder.WithProductionTools(typeof(ProjectServers.ProjectServerWriteTools));
        }
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.OnlineControl))
            foreach (var type in new[] { typeof(PlcDownload.PlcDownloadTools), typeof(PlcWebApi.PlcWebApiWriteTools), typeof(PlcOnline.PlcOnlineConnectionTools) })
                builder.WithProductionTools(type);
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.SimulationControl))
            foreach (var type in new[] { typeof(PlcSim.PlcSimWriteTools), typeof(PlcSim.PlcSimTestTools) })
                builder.WithProductionTools(type);
        return builder;
    }

    /// <summary>Registers one production tool type with closed arguments and its canonical rejection.</summary>
    public static IMcpServerBuilder WithProductionTools(this IMcpServerBuilder builder, Type toolType)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ProductionTools.TryGetValue(toolType, out var register)
            ? register(builder)
            : throw new ArgumentOutOfRangeException(nameof(toolType), toolType, "Not a production tool type.");
    }

    private static OpennessWorkerClient Client(IServiceProvider services) => services.GetRequiredService<OpennessWorkerClient>();

    private static CallToolResult Standalone<TPayload>(string tool, string message) where TPayload : class
        => StructuredStandaloneResult.Create<TPayload>(tool, null, [], new StructuredOperationFailure(ValidationError, message));

    /// <summary>The document WriteRun.Finish returns for an error report, without its audit append.</summary>
    private static CallToolResult WriteRejection<TItem, TEffect, TVerification, TResponse>(
        IWriteDomain<TItem, TEffect, TVerification, TResponse> domain, string message) where TItem : IOperationBatchItem
        => StructuredToolResult.CreateCanonical(CanonicalJson.Serialize(domain.Compose(new WriteReport<TEffect, TVerification>(
            WritePhases.Error, false, new WriteToolError(ValidationError, message), [], [], [], null, default))), isError: true);
}

/// <summary>The one production guard catalog: lifecycle, Network, PLC, HMI, VCI, PLC-download, PLC-online, library and Project Server guard definitions.</summary>
public static class WriteGuardRegistration
{
    public static WriteGuardCatalog ProductionCatalog()
        => new(LifecycleWriteDomain.GuardDefinitions.Concat(NetworkGuardDefinitions.Definitions).Concat(PlcGuardDefinitions.Definitions)
            .Concat(Hmi.HmiGuardDefinitions.Definitions).Concat(Vci.VciGuardDefinitions.Definitions)
            .Concat(PlcDownload.PlcDownloadGuardDefinitions.Definitions).Concat(PlcOnline.PlcOnlineGuardDefinitions.Definitions)
            .Concat(Library.LibraryGuardDefinitions.Definitions)
            .Concat(ProjectServers.ProjectServerGuardDefinitions.Definitions));
}
