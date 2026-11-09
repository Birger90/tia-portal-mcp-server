using System.Text;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer.McpContext;

/// <summary>One prompt argument.</summary>
public sealed record TiaMcpPromptArgument(string Name, string Description, bool Required);

/// <summary>
/// One prompt: name, description, arguments, the capabilities the tools it names need (the prompt is listed only in access
/// modes that allow all of them) and the text builder. Values reach the builder validated and trimmed.
/// </summary>
public sealed record TiaMcpPromptDefinition(
    string Name,
    string Title,
    string Description,
    IReadOnlyList<TiaMcpPromptArgument> Arguments,
    IReadOnlyList<OperationCapability> RequiredCapabilities,
    Func<IReadOnlyDictionary<string, string>, string> Build);

/// <summary>
/// The workflow prompts (prompts/list, prompts/get). Each returns one user message in English that tells the model to
/// answer in the user's language and restates the server rules the workflow touches: passwords only as password sources,
/// TLS trust only for fingerprints the user checked, downloads and online changes only as the access mode confirms them,
/// the safety acceptance never replaced. Prompts only instruct; they call nothing and read nothing.
/// </summary>
public static class TiaMcpPrompts
{
    /// <summary>Longest accepted argument value.</summary>
    public const int MaxArgumentChars = 400;

    private const string Language = "Answer in the language the user writes in (for example German or English); keep TIA Portal terms as TIA Portal shows them.";

    private const string Rules =
        "Server rules that always apply: never ask for, type or pass a password value; passwords are given only as password sources "
        + "(an environment variable NAME, { \"credential\": \"TiaMcp/...\" } or the server password map). TLS certificates are trusted only through "
        + "trustedCertificates with SHA-256 fingerprints the user compared and approved; never trust a certificate on your own. "
        + "The access mode decides what may change; do not try to work around a refused or declined call. "
        + "The safety acceptance of a fail-safe program stays with a qualified person; nothing you do replaces it. "
        + "If a field of an operation is unclear, read plc_read describe_operations or the resource tia-mcp://operations/{tool}/{operation} first.";

    private static readonly TiaMcpPromptArgument BlockPath = new("blockPath",
        "PLC block path as plc_read get_block_content takes it, e.g. PLC_1/Blocks/Folder/Main.", true);

    private static readonly TiaMcpPromptArgument PlcNameOptional = new("plcName",
        "PLC software or device name; optional when the project has exactly one PLC.", false);

    public static IReadOnlyList<TiaMcpPromptDefinition> All { get; } =
    [
        new("explain_block", "Explain a PLC block",
            "Read one PLC block and explain what it does, network by network, without changing anything.",
            [BlockPath], [],
            args => $$"""
                Explain the PLC block {{Q(args["blockPath"])}} of the bound TIA Portal project.
                1. Check get_project_status; if no project is bound, ask the user which one and use bind_project.
                2. Read the block with plc_read get_block_content (format source for SCL/STL, else xml or sd) and, for its networks, list_networks; read get_block_interface for the interface.
                3. Use read_cross_references and plc_read get_call_tree where callers or callees matter.
                4. Explain purpose, interface (inputs, outputs, in/outs, statics, temps), every network, called blocks and used tags, and anything unclear or suspicious.
                Change nothing; this is read-only.
                """),
        new("review_block", "Review a PLC block",
            "Review one PLC block for correctness, robustness, readability and safety-relevant issues; read-only.",
            [BlockPath], [],
            args => $$"""
                Review the PLC block {{Q(args["blockPath"])}} of the bound TIA Portal project.
                1. Read it with plc_read get_block_content and get_block_interface; read its callers and callees (read_cross_references, get_call_tree).
                2. Check: logic errors, unhandled states, edge detection, timer and counter use, uninitialised or overlapping data, magic numbers, missing comments, naming, unused members, and whether it is a fail-safe block (failSafe in the result).
                3. Report findings ordered by severity with the network or line, and suggest fixes as text.
                Do not write anything; changes need an explicit request (see the prompt revise_block_safely). A review is no safety acceptance.
                """),
        new("revise_block_safely", "Revise a PLC block safely",
            "Change one PLC block towards a goal with the guarded write path: read with hash, plan, dry run, write with expectedContentHash, compile check, report.",
            [BlockPath, new("goal", "What the change should achieve, in the user's words.", true)],
            [OperationCapability.ProjectMutation, OperationCapability.Compile],
            args => $$"""
                Change the PLC block {{Q(args["blockPath"])}} so that: {{Q(args["goal"])}}.
                Follow these steps in order and stop to ask the user when something is unclear:
                1. Read the block with plc_read get_block_content in the format you will write (source, xml or sd) and keep content and contentHash. Read get_block_interface and read_cross_references for side effects.
                2. Plan the change and show the user the plan (what changes, which networks/members, risks, what callers are affected). If the block is fail-safe, say that the change needs a new safety acceptance by a qualified person.
                3. Run plc_write with dryRun:true (update_block_logic, or a granular edit such as insert_network, replace_network, update_interface_member) with expectedContentHash = the contentHash from step 1. Show effects and guards.
                4. Only after that, run the same plc_write without dryRun. A state_changed failure means the block changed meanwhile: read again, never force it.
                5. Run compile_check with blockPath (or the PLC) and report the compiler messages; fix errors only with a new read and the same steps.
                6. Report what changed, the verification entry and the compile result.
                Never save the project (save_project) or download it (plc_download) unless the user explicitly asks for it in this conversation.
                """),
        new("document_project", "Document the project",
            "Read the project structure, tag tables and hardware and write a Markdown documentation of it; read-only.",
            [PlcNameOptional], [],
            args => $$"""
                Write a Markdown documentation of the bound TIA Portal project{{(args.TryGetValue("plcName", out var plc) ? ", limited to the PLC " + Q(plc) : "")}}.
                1. get_project_status, then browse_project_tree for devices, PLCs, block folders, PLC data types and HMIs.
                2. plc_read list_tag_tables (with plcName where given) for tag tables and tags; plc_read get_call_tree for the program structure.
                3. network_read read_hardware_config (per device; page with cursor) for hardware, modules and addresses, and list_network_objects for subnets and IO systems.
                4. Write the result as one Markdown document: overview, hardware and networks, program structure (OBs, FBs, FCs, DBs, call tree), tag tables, open points. Mark what you could not read.
                Present the document in your answer (or save it as a file only with the user's tool and consent); this server writes no documentation file and nothing is changed in the project.
                """),
        new("check_hardware", "Check a device's hardware configuration",
            "Read one device's hardware configuration and report modules, addresses, network settings and inconsistencies; read-only.",
            [new("deviceName", "Device name as browse_project_tree or network_read read_hardware_config lists it.", true)], [],
            args => $$"""
                Check the hardware configuration of the device {{Q(args["deviceName"])}} in the bound TIA Portal project.
                1. network_read read_hardware_config with deviceName (includeIoDetails where useful; page with cursor) and list_network_objects / inspect_network_object for its interfaces, subnets and IO system.
                2. For a CPU: plc_read get_plc_security and get_cpu_settings, list_download_targets for the download path, read_address_occupancy for address use.
                3. Report modules and slots, I/O addresses (gaps, overlaps), IP addresses, PROFINET names, subnet and IO-system assignment, protection settings, and anything inconsistent.
                Change nothing. A hardware compile (compile_check scope hardware) changes no configuration, but run it only if the user agrees.
                """),
        new("safety_overview", "Fail-safe overview",
            "Read the safety administration and F-blocks of a PLC and summarise them; read-only, no acceptance.",
            [new("plcName", "Name of the fail-safe PLC (F-CPU).", true)], [],
            args => $$"""
                Give an overview of the fail-safe program of the PLC {{Q(args["plcName"])}} in the bound TIA Portal project, read-only.
                1. plc_read get_safety_administration (plcName): safety mode, password and login state, F-runtime groups with main safety blocks and cycle settings, signatures.
                2. plc_read list_safety_blocks (plcName): F-blocks with their signatures and state; get_safety_validation for the validation assistant's state where available.
                3. Summarise structure, what is consistent and what is not (for example a runtime group without a main safety block, changed signatures).
                Do not log on, change passwords or write anything. State clearly that this overview is no safety validation: the validation and acceptance of a safety program stay with a qualified person.
                """),
        new("download_to_plcsim", "Download to PLCSIM",
            "Download the bound project's PLC to an S7-PLCSIM Advanced instance with the guarded path: targets, dry run, user-approved certificate fingerprints, download, simulation state.",
            [new("plcName", "PLC software or device name to download.", true),
             new("instance", "Exact, case-sensitive S7-PLCSIM Advanced instance name.", true)],
            [OperationCapability.OnlineControl, OperationCapability.SimulationControl],
            args => $$"""
                Download the PLC {{Q(args["plcName"])}} of the bound TIA Portal project to the S7-PLCSIM Advanced instance {{Q(args["instance"])}}.
                1. plcsim_read get_instance (instance) to see that the instance exists and its operating state; power it on with plcsim_write power_on only if the user wants that.
                2. plc_read list_download_targets (plcName): choose the connection mode and the PLCSIM PC interface { name, number } and the target interface exactly as listed. Never use a non-PLCSIM interface: that would be a real controller.
                3. plc_download with dryRun:true and that path; show the user the resolved path, effects and guards.
                4. Run the download only after the user agreed; in read-write mode the server asks the user once to confirm the call. A configuration without an answer aborts with its decisionKey: ask the user which answer to give and retry with decisions.
                5. If the download aborts with abort.tls (certificate_not_trusted), show the fingerprints, subjects and validity, and let the user compare them with the device's certificate (for example in TIA Portal, "Zertifikate anzeigen"). Retry with trustedCertificates only with the fingerprints the user approved; never trust a certificate on your own.
                6. Passwords only as password sources. Afterwards read plcsim_read get_instance for the operating state and report; completion of the download is not verified by TIA Portal V21.
                """),
        new("handle_tia_inbox", "Handle Claude-in-TIA requests",
            "Process the pending requests the user sent from TIA Portal (Claude in TIA): list, read, work, reply.",
            [], [],
            _ => """
                Process the pending "Claude in TIA" requests.
                1. tia_inbox list_requests; take the requests oldest first.
                2. For each: tia_inbox get_request (id). Its addresses are hints built from names: bind_project to its projectPath if needed, then confirm each address with browse_project_tree, plc_read, network_read or library_read before using it.
                3. Do what the request asks with the other tools, within the access mode; for changes follow the guarded write path (read with contentHash, dryRun, write with expectedContentHash, compile_check). Never save or download unless the request and the user explicitly ask for it.
                4. Finish every request with tia_inbox reply_request (id, text in the user's language, status done or failed), one reply per request.
                """),
    ];

    public static TiaMcpPromptDefinition? Find(string name) => All.FirstOrDefault(prompt => prompt.Name == name);

    /// <summary>The prompts whose tools the access mode offers.</summary>
    public static IReadOnlyList<TiaMcpPromptDefinition> ForMode(McpAccessMode mode)
        => All.Where(prompt => prompt.RequiredCapabilities.All(capability => OperationPolicyCatalog.IsCapabilityAllowed(mode, capability))).ToArray();

    /// <summary>
    /// Validates the arguments of prompts/get: no unknown names, every required one present, every value a string of 1 to
    /// <see cref="MaxArgumentChars"/> characters without control characters. Returns the trimmed values or an error message.
    /// </summary>
    public static (IReadOnlyDictionary<string, string>? Values, string? Error) Validate(TiaMcpPromptDefinition prompt, IDictionary<string, JsonElement>? arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var known = prompt.Arguments.Select(argument => argument.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, element) in arguments ?? new Dictionary<string, JsonElement>())
        {
            if (!known.Contains(name))
                return (null, $"Prompt '{prompt.Name}' has no argument '{name}'. Arguments: {(known.Count == 0 ? "none" : string.Join(", ", prompt.Arguments.Select(a => a.Name)))}.");
            if (element.ValueKind == JsonValueKind.Null) continue;
            if (element.ValueKind != JsonValueKind.String) return (null, $"Argument '{name}' must be a string.");
            var value = element.GetString()!.Trim();
            if (value.Length == 0) continue;
            if (value.Length > MaxArgumentChars) return (null, $"Argument '{name}' is longer than {MaxArgumentChars} characters.");
            if (value.Any(char.IsControl)) return (null, $"Argument '{name}' must not contain control characters or line breaks.");
            values[name] = value;
        }

        var missing = prompt.Arguments.Where(argument => argument.Required && !values.ContainsKey(argument.Name)).Select(argument => argument.Name).ToArray();
        return missing.Length > 0
            ? (null, $"Prompt '{prompt.Name}' requires {string.Join(", ", missing)}.")
            : (values, null);
    }

    /// <summary>The full prompt text: the workflow, then the language and server rules.</summary>
    public static string Render(TiaMcpPromptDefinition prompt, IReadOnlyDictionary<string, string> values)
        => new StringBuilder(prompt.Build(values).Trim()).Append("\n\n").Append(Language).Append('\n').Append(Rules).ToString();

    /// <summary>Quotes a user value so it reads as data, not as an instruction.</summary>
    private static string Q(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

/// <summary>One <see cref="TiaMcpPromptDefinition"/> as an MCP prompt.</summary>
internal sealed class TiaMcpPrompt : McpServerPrompt
{
    private readonly TiaMcpPromptDefinition _definition;

    public TiaMcpPrompt(TiaMcpPromptDefinition definition)
    {
        _definition = definition;
        ProtocolPrompt = new Prompt
        {
            Name = definition.Name,
            Title = definition.Title,
            Description = definition.Description,
            Arguments = definition.Arguments.Select(argument => new PromptArgument
            {
                Name = argument.Name, Description = argument.Description, Required = argument.Required,
            }).ToList(),
        };
    }

    public override Prompt ProtocolPrompt { get; }

    public override IReadOnlyList<object> Metadata { get; } = Array.Empty<object>();

    public override ValueTask<GetPromptResult> GetAsync(RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken)
    {
        var (values, error) = TiaMcpPrompts.Validate(_definition, request.Params?.Arguments);
        if (error is not null) throw new McpProtocolException(error, McpErrorCode.InvalidParams);
        return ValueTask.FromResult(new GetPromptResult
        {
            Description = _definition.Description,
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = TiaMcpPrompts.Render(_definition, values!) } }],
        });
    }
}

/// <summary>Registers the resources (every mode) and the prompts the access mode supports.</summary>
public static class McpContextRegistration
{
    public static IMcpServerBuilder WithTiaMcpResourcesAndPrompts(this IMcpServerBuilder builder, McpAccessMode mode)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithResources(Resources());
        builder.WithPrompts(TiaMcpPrompts.ForMode(mode).Select(definition => (McpServerPrompt)new TiaMcpPrompt(definition)).ToArray());
        return builder;
    }

    internal static IReadOnlyList<McpServerResource> Resources()
        => TiaMcpDocuments.All.Select(document => (McpServerResource)new TiaMcpDocumentResource(document))
            .Append(new TiaMcpOperationToolListResource())
            .Append(new TiaMcpOperationTemplateResource(perOperation: false))
            .Append(new TiaMcpOperationTemplateResource(perOperation: true))
            .ToArray();
}
