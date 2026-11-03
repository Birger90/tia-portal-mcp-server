using System.Reflection;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Json;
using Xunit;

namespace TiaMcpServer.Tests.Json;

/// <summary>
/// Register of every Contracts property declared conditional with
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/> — the repository's one convention for "this
/// worker-payload member may legitimately be absent" that
/// <c>CanonicalJson.DeserializeWorkerPayload{T}</c>'s required-member rule exempts. The register
/// only grows deliberately: adding (or removing) a conditional member without updating this test
/// fails it, so the change is always reviewed here.
/// </summary>
public sealed class ConditionalMemberRegisterTests
{
    private static readonly IReadOnlyList<string> Expected = new[]
    {
        "DeviceItemInfo.IoDetails",
        "HardwareConfigInfo.Pagination",
        // New ordinary reads report the root collection count; older/paged reads may omit it.
        "HardwareConfigInfo.RootDeviceCount",
        // Structural traversal evidence appears on new ordinary reads; paged/legacy reads may omit it.
        "HardwareConfigInfo.DiscoveryEvidence",
        // Node selectors carry an owner path when it can be captured; older selectors may omit it.
        "NetworkObjectSelectorInfo.InterfacePath",
        // Relationship identities carry owner evidence on new ordinary reads; older reads may omit it.
        "NetworkNodeIdentityInfo.InterfacePath",
        "NetworkNodeIdentityInfo.InterfaceName",
        // Owner path segments carry a type identifier only when that optional scalar can be read.
        "NetworkInterfacePathSegmentInfo.TypeIdentifier",
        "HardwarePaginationInfo.NextCursor",
        // New ordinary reads report relationship evidence; older/legacy reads may omit it.
        "NodeInfo.ConnectionEvidence",
        "SubnetInfo.ConnectionEvidence",
        "WorkerResponse.BlockImportOutcome",
        // hmi_write: present on a worker failure after a mutation attempt, including portal_lost.
        "WorkerResponse.HmiWriteOutcome",
        // network_write: present on a rejected configure_network_device item; carries the settings applied before the rejection.
        "WorkerResponse.ConfigureNetworkDeviceOutcome",
        // vci_write: present after a VCI mutation attempt, including portal_lost.
        "WorkerResponse.VciWriteOutcome",
        // plc_download: present on a failed download after Download was invoked (configurations seen, abort record).
        "WorkerResponse.PlcDownloadOutcome",
        // plc_download, plc_tia_online, plc_station_upload, compare_to_online: present only on a TLS verification configuration
        // (online legitimation) whose certificate evidence was read; every other configuration and a later one after an abort omit it.
        "PlcDownloadConfigurationInfo.Tls",
        "PlcDownloadAbortInfo.Tls",
        "PlcCallbackConfigurationInfo.Tls",
        "PlcCallbackAbortInfo.Tls",
        // PLCSIM worker: present (true) only after the process observed a cached API type-initializer failure; the host then recycles it.
        "WorkerResponse.WorkerRestartRequired",
        // plc_write: present only on a failed content-import item (update_block_logic).
        "StructuredOperationFailure.BlockImportOutcome",
        // Worker mutation failures: present only when a Siemens exception caused the failure.
        "WorkerResponse.EngineeringError",
        // library_write: present only when the worker could not read back an item's postcondition; the item is then notVerified.
        "LibraryWriteResultInfo.Unverified",
        // library_write: present only on the document and file items (create/export from or to documents, archive, save-as).
        "LibraryWriteResultInfo.Transfer",
        // library_write: present only on the secondary-project items (open, retrieve, close; issue #20).
        "LibraryWriteResultInfo.SecondaryProject",
        // library_write: present only on set_portal_setting (issue #27).
        "LibraryWriteResultInfo.Setting",
        // library_write planning: present only on set_portal_setting, with the names the worker resolved (security review F2).
        "LibraryPlanInfo.Setting",
        // Write item failures: present only when a Siemens exception caused the item failure.
        "StructuredOperationFailure.EngineeringError",
        // Only explicit bind_project inspection actions add this host value; default binding omits it.
        "ProjectBindingResult.Inspection",
        // Local-session owners carry typed context; standalone and unbound values omit it.
        "WorkerSessionIdentity.Context",
        "ProjectStatusInfo.Context",
        "ProjectBindingSnapshot.Context",
        "ProjectBindingInfo.Context",
        // A local-session open/rebind probe carries its source context; standalone probes omit it.
        "ProjectRebindStateInfo.SourceContext",
        // Lifecycle effects include typed source context only for an active local owner.
        "LifecycleEffects.SourceContext",
        // create_project effects only (K16): whether a project is open in the TIA Portal the project would be created in.
        "LifecycleEffects.OpenProject",
        // hmi_read, worker to host only: present when the worker held a snapshot because more rows exist.
        "HmiPage.SnapshotId",
        // hmi_read, host to caller only: present when more rows exist.
        "HmiPage.NextCursor",
        // read_cross_references: present only on a provisional HMI tag selector; path selectors omit it.
        "CrossReferenceSelectorInfo.Hmi",
        // plc_write effects: present only when a plain SimaticML document was diffed against the .xml document of the export bundle.
        "PlcContentDiffEvidence.CurrentDocument",
        // network_write effects: present only on hardware-module items (plug, move, copy, delete, change type, attributes, I/O address, export).
        "NetworkWriteEffect.Hardware",
        // create_block_from_document: present only when the document replaced the main safety block of an F-runtime group and took
        // the temporary F-block route (sdViaXmlForMainSafetyBlock, live 2026-11-02).
        "PlcBlockEditResultInfo.MainSafetyBlockRoute",
        // create_tag/update_tag: present only when the item wrote a comment (K22): the culture written and the text read back.
        "TagMutationResultInfo.CommentCulture",
        "TagMutationResultInfo.Comment",
    };

    [Fact]
    public void ConditionalMembers_AreExactlyTheRegisteredSet()
    {
        // The contracts assembly, plus the host response members that share the convention.
        var actual = typeof(WorkerJson).Assembly.GetTypes()
            .Append(typeof(TiaMcpServer.OperationBatches.StructuredOperationFailure))
            .Append(typeof(TiaMcpServer.Tools.ProjectBindingResult))
            .Append(typeof(TiaMcpServer.Tools.ProjectBindingInfo))
            .Append(typeof(TiaMcpServer.ProjectLifecycle.LifecycleEffects))
            .Append(typeof(TiaMcpServer.Network.NetworkWriteEffect))
            .Append(typeof(TiaMcpServer.Plc.PlcContentDiffEvidence))
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsWhenWritingNullConditional)
                .Select(property => $"{type.Name}.{property.Name}"))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        var expected = Expected.OrderBy(entry => entry, StringComparer.Ordinal).ToList();

        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();
        var unregistered = actual.Except(expected, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0 && unregistered.Count == 0,
            "TiaMcpServer.Contracts' set of [JsonIgnore(Condition = WhenWritingNull)] members no "
                + "longer matches this register. A new conditional member must be added here (as "
                + "\"TypeName.PropertyName\"), with its description stating when the member "
                + "appears; remove an entry only when its member is no longer conditional. "
                + $"Registered but no longer conditional: [{string.Join(", ", missing)}]. "
                + $"Conditional but not registered: [{string.Join(", ", unregistered)}].");
    }

    private static bool IsWhenWritingNullConditional(PropertyInfo property)
        => property.GetCustomAttributes<JsonIgnoreAttribute>(inherit: false)
            .Any(attribute => attribute.Condition == JsonIgnoreCondition.WhenWritingNull);
}
