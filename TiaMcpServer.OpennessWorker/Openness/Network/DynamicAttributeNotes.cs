using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// K39 (live V21 2026-11-02, S7-1200 and ET 200SP): every address reported "'Context'/'IsochronousMode'/'InterruptObNumber'
/// is not supported", three messages per address. A dynamic attribute the address does not list in GetAttributeInfos is
/// not read (it stays null), and what was not offered or could not be read is reported once per attribute and call.
/// Shared by list_io_addresses/set_io_address (HardwareModuleService) and the I/O map of read_hardware_config/export_io_map
/// (HardwareIoMapReader, K58: Address.Context and Channel.ChannelAddress/ChannelWidth were read without this check).
/// V21: Siemens.Engineering.HW.Address.GetAttributeInfos, Siemens.Engineering.HW.Channel.GetAttributeInfos.
/// </summary>
internal sealed class DynamicAttributeNotes
{
    private readonly Dictionary<string, int> _notOffered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Count, string First)> _failed = new(StringComparer.Ordinal);
    private int _infosUnreadable;
    private string? _infosProblem;

    private readonly string _objects;

    /// <param name="objects">Plural noun of the objects read ("addresses", "channels"), used in the notes.</param>
    public DynamicAttributeNotes(string objects = "addresses") => _objects = objects;

    /// <summary>The attribute names the object lists, or null (then every attribute is read, as before).</summary>
    public HashSet<string>? Offered(IEngineeringObject engineeringObject)
    {
        try
        {
            return new HashSet<string>(engineeringObject.GetAttributeInfos().Select(info => info.Name), StringComparer.Ordinal);
        }
        catch (EngineeringException exception)
        {
            _infosUnreadable++;
            _infosProblem ??= exception.Message;
            return null;
        }
    }

    public T? Read<T>(HashSet<string>? offered, string name, Func<T?> read)
    {
        if (offered is not null && !offered.Contains(name))
        {
            _notOffered[name] = _notOffered.TryGetValue(name, out var count) ? count + 1 : 1;
            return default;
        }

        try
        {
            return read();
        }
        catch (Exception exception) when (exception is EngineeringException || exception is InvalidCastException
            || exception is FormatException || exception is OverflowException)
        {
            _failed[name] = _failed.TryGetValue(name, out var seen) ? (seen.Count + 1, seen.First) : (1, exception.Message);
            return default;
        }
    }

    public void AddTo(List<string> messages, int total, string? context = null)
    {
        if (_infosUnreadable > 0)
            messages.Add($"GetAttributeInfos() of {_infosUnreadable} of {total} {_objects}{context} could not be read ({_infosProblem}); their dynamic attributes were read directly.");
        foreach (var entry in _notOffered.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            messages.Add($"'{entry.Key}' is not offered by {entry.Value} of {total} {_objects}{context} (GetAttributeInfos); it is reported as null there.");
        foreach (var entry in _failed.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            messages.Add($"Could not read {entry.Key} of {entry.Value.Count} of {total} {_objects}{context} (first: {entry.Value.First}); it is reported as null there.");
    }
}
