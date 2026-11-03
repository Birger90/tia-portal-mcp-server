using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.ChangeDetection;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

// Stand-ins with exactly the members Siemens.Engineering.Step7.xml (V21) documents: PlcChecksumProvider.Software/TextLists,
// FingerprintProvider.GetFingerprints() returning IList<Fingerprint>, Fingerprint.Id/Value, and the FingerprintId field names.
// Their value types are not documented; the stand-ins use strings and the enum. Member shapes only; no evidence of Siemens behavior.
namespace Siemens.Engineering.SW
{
    public enum FingerprintId { Code, Comments, Interface, LibraryType, Texts, Alarms, Supervisions, TechnologyObject, Events, TextualInterface, Properties, ProgramCode }

    public sealed class PlcChecksumProvider
    {
        public string Software { get; set; } = string.Empty;

        public string TextLists { get; set; } = string.Empty;
    }

    public sealed class Fingerprint
    {
        public FingerprintId Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }

    public sealed class FingerprintProvider
    {
        public IList<Fingerprint> Fingerprints { get; } = new List<Fingerprint>();

        public IList<Fingerprint> GetFingerprints() => Fingerprints;
    }
}

namespace TiaMcpServer.FakeWorker.Plc
{
    /// <summary>
    /// <c>plc-change-detection</c>: PLC_1 (Station_1) lists a PlcChecksumProvider (Software "9F3C01AB", TextLists empty, as for a
    /// PLC changed since its last compile); PLC_2 (Station_2) lists none. Block PLC_1/Main lists a FingerprintProvider with
    /// two fingerprints; the PLC data type PLC_1/Types/UDT_1 lists none (get_fingerprints fails unsupported_capability). Runs the
    /// worker's own Siemens-free <see cref="PlcChangeDetectionRun"/>.
    /// </summary>
    sealed class PlcChangeDetectionScenario
    {
        private readonly Siemens.Engineering.SW.PlcChecksumProvider _checksums = new() { Software = "9F3C01AB", TextLists = string.Empty };
        private readonly Siemens.Engineering.SW.FingerprintProvider _fingerprints = new();

        public PlcChangeDetectionScenario()
        {
            _fingerprints.Fingerprints.Add(new Siemens.Engineering.SW.Fingerprint { Id = Siemens.Engineering.SW.FingerprintId.Code, Value = "7d1e0a" });
            _fingerprints.Fingerprints.Add(new Siemens.Engineering.SW.Fingerprint { Id = Siemens.Engineering.SW.FingerprintId.Interface, Value = "c0ffee" });
        }

        public string Handle(string line)
        {
            var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
            try
            {
                if (request.Method == "list_tag_tables")
                    return Ok(WorkerJson.SerializePayload(new PlcTagInventoryInfo
                    {
                        Plcs =
                        {
                            new PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = "Station_1" },
                            new PlcTagInventoryPlcInfo { PlcName = "PLC_2", DeviceName = "Station_2" },
                        },
                    }));
                var info = request.PlcChangeDetection ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"{request.Method} requires the change-detection request.");
                if (request.Method == PlcChangeDetectionMethods.GetPlcChecksums)
                {
                    var result = new PlcChecksumReadInfo();
                    foreach (var (plc, device, provider) in new (string, string, object?)[] { ("PLC_1", "Station_1", _checksums), ("PLC_2", "Station_2", null) })
                    {
                        if (info.PlcName is not null && !Same(info.PlcName, plc) && !Same(info.PlcName, device)) continue;
                        var offered = provider is null ? new List<Type>() : new List<Type> { provider.GetType() };
                        result.Plcs.Add(PlcChangeDetectionRun.ReadChecksums(plc, device, offered, _ => provider, _ => false));
                    }

                    if (result.Plcs.Count == 0)
                        throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{info.PlcName}' was found in the project.");
                    return Ok(WorkerJson.SerializePayload(result));
                }

                if (request.Method != PlcChangeDetectionMethods.GetFingerprints)
                    throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"plc-change-detection scenario does not script '{request.Method}'.");
                if (info.BlockPath is not null)
                {
                    if (!Same(info.BlockPath, "PLC_1/Main"))
                        throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"Block '{info.BlockPath}' was not found.");
                    return Ok(WorkerJson.SerializePayload(PlcChangeDetectionRun.ReadFingerprints(PlcChangeDetectionNames.ObjectKindBlock, info.BlockPath, "PLC_1",
                        new List<Type> { _fingerprints.GetType() }, _ => _fingerprints, _ => false)));
                }

                if (!Same(info.TypePath ?? string.Empty, "PLC_1/Types/UDT_1"))
                    throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"PLC data type '{info.TypePath}' was not found.");
                return Ok(WorkerJson.SerializePayload(PlcChangeDetectionRun.ReadFingerprints(PlcChangeDetectionNames.ObjectKindType, info.TypePath!, "PLC_1",
                    new List<Type>(), _ => null, _ => false)));
            }
            catch (RuntimeBindingException ex)
            {
                return JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = ex.Category, Error = ex.Message }, WorkerJson.Envelope);
            }
        }

        private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static string Ok(string payload)
            => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload }, WorkerJson.Envelope);
    }
}
