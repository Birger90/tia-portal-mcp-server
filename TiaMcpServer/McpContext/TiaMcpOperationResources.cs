using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Plc;

namespace TiaMcpServer.McpContext;

/// <summary>One operation-based tool as the operation reference resources describe it.</summary>
public sealed record TiaMcpOperationTool(
    string Tool,
    bool Writes,
    string Availability,
    string ReferenceDocument,
    Func<IReadOnlyList<string>> OperationNames,
    Func<string, (IReadOnlyList<string> Required, IReadOnlyList<string> Optional)?> Fields);

/// <summary>The index of one tool's operations (tia-mcp://operations/{tool}).</summary>
public sealed record TiaMcpOperationToolIndex(string Tool, string Availability, string ReferenceDocument, string FieldsSource,
    IReadOnlyList<TiaMcpOperationIndexEntry> Operations);

public sealed record TiaMcpOperationIndexEntry(string Operation, string Uri, string? Fields);

/// <summary>
/// One operation (tia-mcp://operations/{tool}/{operation}). For plc_read and plc_write <c>Description</c> is exactly what
/// plc_read describe_operations returns for the operation; for the other tools only the catalog's field names are known
/// to this reference (their rules are in the tool description and the reference document).
/// </summary>
public sealed record TiaMcpOperationEntry(string Tool, string Operation, string Availability, string ReferenceDocument,
    IReadOnlyList<string>? RequiredFields, IReadOnlyList<string>? OptionalFields, PlcOperationDescription? Description, string Note);

/// <summary>The list of operation tools (tia-mcp://operations).</summary>
public sealed record TiaMcpOperationToolList(IReadOnlyList<TiaMcpOperationToolSummary> Tools, string Note);

public sealed record TiaMcpOperationToolSummary(string Tool, string Uri, string Availability, int OperationCount, string ReferenceDocument);

/// <summary>
/// The operation reference behind the resources tia-mcp://operations, tia-mcp://operations/{tool} and
/// tia-mcp://operations/{tool}/{operation}. Reads only the server's own catalogs (no TIA Portal, no binding, every access
/// mode); the PLC entries reuse <see cref="PlcOperationReference"/>, the source of plc_read describe_operations.
/// </summary>
public static class TiaMcpOperationReference
{
    public const string Root = TiaMcpDocuments.Scheme + "operations";
    public const string MimeType = "application/json";
    private const string EveryMode = "every access mode";
    private const string WritableModes = "read-write and full (not offered in read-only)";

    private static (IReadOnlyList<string>, IReadOnlyList<string>)? Plc(string operation)
        => PlcOperationCatalog.TryGetFields(operation, out var required, out var optional, out _) ? (required, optional) : null;

    private static (IReadOnlyList<string>, IReadOnlyList<string>)? Network(string operation)
        => NetworkOperationCatalog.TryGetSpec(operation, out var spec) && spec is not null ? (spec.RequiredFields, spec.OptionalFields) : null;

    private static (IReadOnlyList<string>, IReadOnlyList<string>)? Unknown(string _) => null;

    private static string Doc(string fileName) => TiaMcpDocuments.OperationsDocumentUri("en", fileName);

    /// <summary>The vci_read operations: the worker methods without their vci_ prefix (VciReadCatalog keeps its field table private).</summary>
    internal static IReadOnlyList<string> VciReadOperations => Contracts.Vci.VciMethods.Read.Select(method => method.Substring("vci_".Length)).ToArray();

    /// <summary>Every tool that takes an operations array, in tool-list order.</summary>
    public static IReadOnlyList<TiaMcpOperationTool> Tools { get; } =
    [
        new("plc_read", false, EveryMode, Doc("PLC_OPERATIONS_SUMMARY.md"), () => PlcOperationCatalog.ReadOperationNames, Plc),
        new("plc_write", true, WritableModes, Doc("PLC_OPERATIONS_SUMMARY.md"), () => PlcOperationCatalog.WriteOperationNames, Plc),
        new("network_read", false, EveryMode, Doc("NETWORK_OPERATIONS_SUMMARY.md"), () => NetworkOperationCatalog.ReadOperationNames, Network),
        new("network_write", true, WritableModes, Doc("NETWORK_OPERATIONS_SUMMARY.md"), () => NetworkOperationCatalog.WriteOperationNames, Network),
        new("hmi_read", false, EveryMode, Doc("HMI_OPERATIONS_SUMMARY.md"), () => Hmi.HmiOperationCatalog.OperationNames, Unknown),
        new("hmi_write", true, WritableModes, Doc("HMI_OPERATIONS_SUMMARY.md"), () => Hmi.HmiWriteCatalog.OperationNames, Unknown),
        new("vci_read", false, EveryMode, Doc("VCI_OPERATIONS_SUMMARY.md"), () => VciReadOperations, Unknown),
        new("vci_write", true, WritableModes, Doc("VCI_OPERATIONS_SUMMARY.md"), () => Vci.VciWriteCatalog.OperationNames, Unknown),
        new("library_read", false, EveryMode, Doc("LIBRARY_OPERATIONS_SUMMARY.md"), () => Library.LibraryCatalog.ReadOperationNames, Unknown),
        new("library_write", true, WritableModes, Doc("LIBRARY_OPERATIONS_SUMMARY.md"), () => Library.LibraryCatalog.WriteOperationNames, Unknown),
        new("plcsim_read", false, EveryMode, Doc("PLCSIM_OPERATIONS_SUMMARY.md"), () => PlcSim.PlcSimCatalog.ReadOperationNames.ToArray(), Unknown),
        new("plcsim_write", true, WritableModes, Doc("PLCSIM_OPERATIONS_SUMMARY.md"), () => PlcSim.PlcSimCatalog.WriteOperationNames.ToArray(), Unknown),
        new("plc_online_read", false, EveryMode, Doc("PLC_ONLINE_WEBAPI_SUMMARY.md"), () => PlcWebApi.PlcWebApiCatalog.ReadOperationNames.ToArray(), Unknown),
        new("plc_online_write", true, WritableModes, Doc("PLC_ONLINE_WEBAPI_SUMMARY.md"), () => PlcWebApi.PlcWebApiCatalog.WriteOperationNames.ToArray(), Unknown),
        new(Contracts.Multiuser.ProjectServerMethods.WriteTool, true, WritableModes, Doc("PROJECT_SERVER_WRITE_SUMMARY.md"),
            () => Contracts.Multiuser.ProjectServerMethods.Operations, Unknown),
        new("siemens_docs", false, EveryMode, Doc("SIEMENS_DOCS_SUMMARY.md"), () => SiemensDocs.SiemensDocsCatalogRules.OperationNames.ToArray(), Unknown),
        new(ClaudeInbox.TiaInboxTools.ToolName, false, EveryMode, Doc("CLAUDE_IN_TIA.md"),
            () => [ClaudeInbox.TiaInboxTools.ListRequests, ClaudeInbox.TiaInboxTools.GetRequest, ClaudeInbox.TiaInboxTools.ReplyRequest], Unknown),
    ];

    public static TiaMcpOperationTool? FindTool(string? tool) => Tools.FirstOrDefault(entry => entry.Tool == tool);

    public static string ToolUri(string tool) => Root + "/" + tool;

    public static string OperationUri(string tool, string operation) => Root + "/" + tool + "/" + operation;

    public static TiaMcpOperationToolList ListTools()
        => new(Tools.Select(tool => new TiaMcpOperationToolSummary(tool.Tool, ToolUri(tool.Tool), tool.Availability,
                tool.OperationNames().Count, tool.ReferenceDocument)).ToArray(),
            "Read tia-mcp://operations/{tool} for a tool's operations and tia-mcp://operations/{tool}/{operation} for one operation. "
            + "Single-call tools without an operations array (bind_project, compile_check, plc_download, plc_tia_online, plc_station_upload, plcsim_test, lifecycle tools) are described by their tool descriptions.");

    public static TiaMcpOperationToolIndex Index(TiaMcpOperationTool tool)
        => new(tool.Tool, tool.Availability, tool.ReferenceDocument, FieldsSource(tool),
            tool.OperationNames().Select(name => new TiaMcpOperationIndexEntry(name, OperationUri(tool.Tool, name),
                tool.Fields(name) is { } fields ? PlcOperationReference.IndexFields(fields.Required, fields.Optional) : null)).ToArray());

    /// <summary>One operation, or null when the tool does not offer it.</summary>
    public static TiaMcpOperationEntry? Describe(TiaMcpOperationTool tool, string operation)
    {
        if (!tool.OperationNames().Contains(operation, StringComparer.Ordinal)) return null;
        var fields = tool.Fields(operation);
        var plc = tool.Tool is "plc_read" or "plc_write" ? PlcOperationReference.DescribeOne(operation) : null;
        // A PLC operation name belongs to plc_read or plc_write; describe it under the tool that runs it.
        return new TiaMcpOperationEntry(tool.Tool, operation, tool.Availability, tool.ReferenceDocument, fields?.Required, fields?.Optional, plc,
            FieldsSource(tool));
    }

    private static string FieldsSource(TiaMcpOperationTool tool) => tool.Tool switch
    {
        "plc_read" or "plc_write" => "Fields and rules are those of plc_read describe_operations (the same catalog).",
        "network_read" or "network_write" => "Required and optional fields come from the network operation catalog; rules are in the tool description and the reference document.",
        _ => "This reference lists the operation names of the tool's catalog; fields and rules are in the tool description (tools/list) and the reference document.",
    };

    internal static ReadResourceResult Json<T>(string uri, T value)
        => new() { Contents = [new TextResourceContents { Uri = uri, MimeType = MimeType, Text = CanonicalJson.Serialize(value) }] };
}

/// <summary>tia-mcp://operations: the list of operation tools (a direct resource).</summary>
internal sealed class TiaMcpOperationToolListResource : McpServerResource
{
    private static readonly ResourceTemplate Template = new()
    {
        Name = "operations", Title = "Operation reference: tools", UriTemplate = TiaMcpOperationReference.Root, MimeType = TiaMcpOperationReference.MimeType,
        Description = "Every tool of this server that takes an operations array, with its access-mode availability, operation count and reference document. Built from the server's catalogs; needs no TIA Portal.",
    };

    public override ResourceTemplate ProtocolResourceTemplate => Template;

    public override IReadOnlyList<object> Metadata { get; } = Array.Empty<object>();

    public override bool IsMatch(string uri) => uri == TiaMcpOperationReference.Root;

    public override ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken)
        => ValueTask.FromResult(TiaMcpOperationReference.Json(TiaMcpOperationReference.Root, TiaMcpOperationReference.ListTools()));
}

/// <summary>
/// The resource templates tia-mcp://operations/{tool} and tia-mcp://operations/{tool}/{operation}. Matching is exact: the
/// tool must be one of <see cref="TiaMcpOperationReference.Tools"/>; an unknown operation of a known tool is answered with
/// a resource-not-found protocol error naming the tool's index.
/// </summary>
internal sealed class TiaMcpOperationTemplateResource : McpServerResource
{
    private readonly bool _perOperation;
    private readonly ResourceTemplate _template;

    public TiaMcpOperationTemplateResource(bool perOperation)
    {
        _perOperation = perOperation;
        _template = perOperation
            ? new ResourceTemplate
            {
                Name = "operation", Title = "Operation reference: one operation", UriTemplate = TiaMcpOperationReference.Root + "/{tool}/{operation}",
                MimeType = TiaMcpOperationReference.MimeType,
                Description = "One operation of an operation tool (tool e.g. plc_read, plc_write, network_read, hmi_write, library_read, plcsim_read, plc_online_read, siemens_docs, tia_inbox). "
                    + "For plc_read/plc_write: the same required and optional fields, field rules and reference text as plc_read describe_operations. Needs no TIA Portal.",
            }
            : new ResourceTemplate
            {
                Name = "operation-index", Title = "Operation reference: one tool", UriTemplate = TiaMcpOperationReference.Root + "/{tool}",
                MimeType = TiaMcpOperationReference.MimeType,
                Description = "Every operation of one operation tool with its URI (and its fields where the catalog exposes them). Needs no TIA Portal.",
            };
    }

    public override ResourceTemplate ProtocolResourceTemplate => _template;

    public override IReadOnlyList<object> Metadata { get; } = Array.Empty<object>();

    public override bool IsMatch(string uri) => Parse(uri) is { } parts && (parts.Operation is not null) == _perOperation;

    public override ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken)
    {
        var uri = request.Params?.Uri ?? string.Empty;
        if (Parse(uri) is not { } parts || TiaMcpOperationReference.FindTool(parts.Tool) is not { } tool)
            throw new McpProtocolException($"Unknown resource '{uri}'. Read {TiaMcpOperationReference.Root} for the operation tools.", McpErrorCode.ResourceNotFound);
        if (parts.Operation is null)
            return ValueTask.FromResult(TiaMcpOperationReference.Json(uri, TiaMcpOperationReference.Index(tool)));
        var entry = TiaMcpOperationReference.Describe(tool, parts.Operation)
            ?? throw new McpProtocolException($"'{parts.Operation}' is no {tool.Tool} operation. Read {TiaMcpOperationReference.ToolUri(tool.Tool)} for its operations.",
                McpErrorCode.ResourceNotFound);
        return ValueTask.FromResult(TiaMcpOperationReference.Json(uri, entry));
    }

    /// <summary>Splits tia-mcp://operations/{tool}[/{operation}] with a known tool; anything else is null.</summary>
    internal static (string Tool, string? Operation)? Parse(string uri)
    {
        var prefix = TiaMcpOperationReference.Root + "/";
        if (!uri.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var segments = uri.Substring(prefix.Length).Split('/');
        if (segments.Length is < 1 or > 2 || segments.Any(segment => segment.Length == 0)) return null;
        if (TiaMcpOperationReference.FindTool(segments[0]) is null) return null;
        return (segments[0], segments.Length == 2 ? segments[1] : null);
    }
}
