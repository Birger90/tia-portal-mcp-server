using System.Globalization;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Plc;

/// <summary>One occupied interval of the I, Q or M area: a declared tag or a hardware Address.</summary>
public sealed record PlcAddressOccupant(
    string Kind,
    string Area,
    int StartByte,
    int StartBit,
    int BitLength,
    string Address,
    string? TagName,
    string? TableName,
    string? FolderPath,
    string? DataType,
    string? DeviceName,
    string? ItemPath,
    string? IoType);

/// <summary>Two declared tags whose intervals overlap (TIA Portal allows it; it is usually a mistake).</summary>
public sealed record PlcAddressOverlap(string Area, string FirstTag, string FirstAddress, string SecondTag, string SecondAddress);

/// <summary>A tag whose logical address is not an absolute I/Q/M bit, byte, word or double-word address.</summary>
public sealed record PlcUnparsedTag(string TagName, string TableName, string FolderPath, string LogicalAddress, string DataType);

/// <summary>
/// A tag at an absolute I/Q/M address whose data type has no documented length here (PLC data types,
/// arrays, strings, structures, and elementary types whose length was not evidenced): only its start
/// is known, so it occupies at least the spelled width and possibly more.
/// </summary>
public sealed record PlcUnsizedTag(string TagName, string TableName, string FolderPath, string LogicalAddress, string DataType,
    string Area, int StartByte, int StartBit, int SpelledBits);

/// <summary>A tag whose address spelling width differs from its data type's documented length; the larger width is used.</summary>
public sealed record PlcAddressWidthMismatch(string TagName, string LogicalAddress, string DataType, int AddressBits, int DataTypeBits);

public sealed record PlcAddressArea(string Area, IReadOnlyList<PlcAddressOccupant> Entries, int UsedBytes);

/// <summary>Result of read_address_occupancy: the assignment list of one PLC.</summary>
public sealed record PlcAddressOccupancyResult(
    string PlcName,
    string DeviceName,
    bool IsComplete,
    bool HardwareIncluded,
    IReadOnlyList<PlcAddressArea> Areas,
    IReadOnlyList<PlcAddressOverlap> Overlaps,
    IReadOnlyList<PlcAddressOccupant> TagsOutsideHardware,
    IReadOnlyList<PlcUnparsedTag> UnparsedTags,
    IReadOnlyList<PlcUnsizedTag> UnsizedTags,
    IReadOnlyList<PlcAddressWidthMismatch> WidthMismatches,
    IReadOnlyList<string> Messages);

/// <summary>Result of find_free_address.</summary>
public sealed record PlcFreeAddressResult(
    string PlcName,
    string Area,
    string DataType,
    int BitLength,
    bool Found,
    string? Address,
    int? StartByte,
    int? StartBit,
    int SearchedFromByte,
    int SearchedToByte,
    int AlignBytes,
    bool RequireHardware,
    bool IsComplete,
    IReadOnlyList<PlcUnsizedTag> UnsizedTags,
    IReadOnlyList<string> Messages);

/// <summary>
/// Pure, Siemens-free I/Q/M address arithmetic for the assignment list and the free-address search.
/// Addresses are parsed from the documented <c>%&lt;area&gt;&lt;byte&gt;[.&lt;bit&gt;]</c> form with the
/// B/W/D width letters (international I/Q/M; the German E/A mnemonics map to I/Q). Anything else
/// (symbolic, DB, peripheral <c>:P</c>) is reported as unparsed, never guessed. A tag's width is its
/// data type's documented length, cross-checked against the address spelling (the larger is used);
/// a data type without a documented length leaves the tag unsized.
/// </summary>
public static class PlcAddressSpace
{
    public static IReadOnlyList<string> AreaNames { get; } = new[] { "I", "Q", "M" };

    public const int DefaultEndByte = 65535;

    /// <summary>The highest byte number the free-address search accepts for <c>startByte</c>/<c>endByte</c>.</summary>
    public const int MaxByte = 65535;

    private static readonly Regex AddressPattern = new(@"^%(?<area>[IQMEA])(?<width>[BWD]?)(?<byte>\d+)(?:\.(?<bit>\d+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Data type lengths in bits as stated by the TIA Portal V21 information system ("Data types": the
    /// BOOL, BYTE, WORD, DWORD, LWORD, SINT, USINT, INT, UINT, DINT, UDINT, LINT, ULINT, REAL, LREAL,
    /// CHAR, WCHAR, TOD, LTOD, DT, LDT and DTL pages). TIME, LTIME, S5TIME and DATE are absent because
    /// their length could not be confirmed from those pages; such tags are unsized. Matched
    /// case-insensitively, including the long names the pages give (TIME_OF_DAY, DATE_AND_TIME, ...).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> DocumentedDataTypeBits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Bool"] = 1,
        ["Byte"] = 8, ["Char"] = 8, ["SInt"] = 8, ["USInt"] = 8,
        ["Word"] = 16, ["WChar"] = 16, ["Int"] = 16, ["UInt"] = 16,
        ["DWord"] = 32, ["DInt"] = 32, ["UDInt"] = 32, ["Real"] = 32, ["TOD"] = 32, ["Time_Of_Day"] = 32,
        ["LWord"] = 64, ["LInt"] = 64, ["ULInt"] = 64, ["LReal"] = 64, ["LTOD"] = 64, ["LTime_Of_Day"] = 64,
        ["DT"] = 64, ["Date_And_Time"] = 64, ["LDT"] = 64, ["Date_And_LTime"] = 64,
        ["DTL"] = 96,
    };

    /// <summary>Types find_free_address can place: a documented length with a 1/8/16/32-bit address spelling.</summary>
    public static IReadOnlyCollection<string> SupportedDataTypes { get; } = DocumentedDataTypeBits
        .Where(entry => entry.Value is 1 or 8 or 16 or 32).Select(entry => entry.Key).ToArray();

    /// <summary>The documented length of a data type in bits, when known.</summary>
    public static bool TryGetDocumentedBits(string? dataType, out int bits)
        => DocumentedDataTypeBits.TryGetValue(dataType?.Trim() ?? string.Empty, out bits);

    /// <summary>The width find_free_address places for a requested data type (1, 8, 16 or 32 bits).</summary>
    public static bool TryGetBits(string? dataType, out int bits)
        => TryGetDocumentedBits(dataType, out bits) && bits is 1 or 8 or 16 or 32;

    /// <summary>Parses an absolute I/Q/M address into area, start bit and the bit length its spelling denotes.</summary>
    public static bool TryParse(string? text, out string area, out long startBit, out int bitLength)
    {
        area = string.Empty;
        startBit = 0;
        bitLength = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = AddressPattern.Match(text!.Trim().ToUpperInvariant());
        if (!match.Success) return false;
        if (!long.TryParse(match.Groups["byte"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var byteNumber) || byteNumber > int.MaxValue / 8)
            return false;
        var width = match.Groups["width"].Value;
        var hasBit = match.Groups["bit"].Success;
        if (width.Length > 0 == hasBit) return false;
        area = match.Groups["area"].Value switch { "E" => "I", "A" => "Q", var value => value };
        if (hasBit)
        {
            if (!int.TryParse(match.Groups["bit"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bit) || bit > 7) return false;
            startBit = byteNumber * 8 + bit;
            bitLength = 1;
            return true;
        }

        startBit = byteNumber * 8;
        bitLength = width switch { "B" => 8, "W" => 16, _ => 32 };
        return true;
    }

    /// <summary>The canonical international spelling of an interval, or null for a width without one.</summary>
    public static string? Format(string area, long startBit, int bitLength) => bitLength switch
    {
        1 => $"%{area}{(startBit / 8).ToString(CultureInfo.InvariantCulture)}.{(startBit % 8).ToString(CultureInfo.InvariantCulture)}",
        8 when startBit % 8 == 0 => $"%{area}B{(startBit / 8).ToString(CultureInfo.InvariantCulture)}",
        16 when startBit % 8 == 0 => $"%{area}W{(startBit / 8).ToString(CultureInfo.InvariantCulture)}",
        32 when startBit % 8 == 0 => $"%{area}D{(startBit / 8).ToString(CultureInfo.InvariantCulture)}",
        _ => null,
    };

    /// <summary>Validation of the address-space fields of plc_read and plc_write items (runs for every item).</summary>
    internal static void Validate(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        if (operation.Area is { } area && !AreaNames.Contains(area, StringComparer.Ordinal))
            errors.Add($"{prefix}: 'area' must be one of: {string.Join(", ", AreaNames)}.");
        if (operation.Areas is { } areas)
        {
            if (areas.Count == 0) errors.Add($"{prefix}: 'areas' must not be empty when supplied.");
            if (areas.Any(value => value is null || !AreaNames.Contains(value, StringComparer.Ordinal)))
                errors.Add($"{prefix}: 'areas' may contain only: {string.Join(", ", AreaNames)}.");
            if (areas.Distinct(StringComparer.Ordinal).Count() != areas.Count) errors.Add($"{prefix}: 'areas' must not contain duplicates.");
        }

        if (operation.StartByte is < 0 or > MaxByte) errors.Add($"{prefix}: 'startByte' must be between 0 and {MaxByte}.");
        if (operation.EndByte is < 0 or > MaxByte) errors.Add($"{prefix}: 'endByte' must be between 0 and {MaxByte}.");
        if (operation.EndByte is { } end && end < (operation.StartByte ?? 0))
            errors.Add($"{prefix}: 'endByte' must not be below 'startByte'.");
        if (operation.AlignBytes is { } align && align is not (1 or 2 or 4 or 8))
            errors.Add($"{prefix}: 'alignBytes' must be 1, 2, 4 or 8.");
        if (operation.Operation == PlcTagMoveMethods.FindFreeAddress)
        {
            if (operation.DataType is { } dataType && !string.IsNullOrWhiteSpace(dataType) && !TryGetBits(dataType, out _))
                errors.Add($"{prefix}: dataType '{dataType}' has no documented length with a 1/8/16/32-bit address spelling. Supported: {string.Join(", ", SupportedDataTypes)}.");
            if (operation.RequireHardware == true && operation.Area == "M")
                errors.Add($"{prefix}: 'requireHardware' applies only to areas I and Q; the M area has no hardware addresses.");
        }
    }

    /// <summary>The PLC's declared tags, split into sized occupants, unsized tags, unparsed tags and width mismatches.</summary>
    public sealed record TagSet(
        List<PlcAddressOccupant> Sized,
        List<PlcUnsizedTag> Unsized,
        List<PlcUnparsedTag> Unparsed,
        List<PlcAddressWidthMismatch> WidthMismatches);

    /// <summary>Tag occupants of one PLC plus the tags whose extent is unknown or whose address is not absolute I/Q/M.</summary>
    public static TagSet Tags(PlcTagInventoryPlcInfo plc, CancellationToken cancellationToken = default)
    {
        var set = new TagSet(new List<PlcAddressOccupant>(), new List<PlcUnsizedTag>(), new List<PlcUnparsedTag>(), new List<PlcAddressWidthMismatch>());
        foreach (var table in plc.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var tag in table.Tags)
            {
                var dataType = tag.DataType ?? string.Empty;
                if (!TryParse(tag.LogicalAddress, out var area, out var startBit, out var spelledBits))
                {
                    set.Unparsed.Add(new PlcUnparsedTag(tag.Name, table.Name, table.FolderPath, tag.LogicalAddress, dataType));
                    continue;
                }

                if (!TryGetDocumentedBits(dataType, out var typeBits))
                {
                    set.Unsized.Add(new PlcUnsizedTag(tag.Name, table.Name, table.FolderPath, tag.LogicalAddress, dataType,
                        area, (int)(startBit / 8), (int)(startBit % 8), spelledBits));
                    continue;
                }

                if (typeBits != spelledBits)
                    set.WidthMismatches.Add(new PlcAddressWidthMismatch(tag.Name, tag.LogicalAddress, dataType, spelledBits, typeBits));
                var bits = Math.Max(typeBits, spelledBits);
                set.Sized.Add(new PlcAddressOccupant("tag", area, (int)(startBit / 8), (int)(startBit % 8), bits,
                    Format(area, startBit, bits) ?? tag.LogicalAddress, tag.Name, table.Name, table.FolderPath, dataType, null, null, null));
            }
        }

        return set;
    }

    /// <summary>Hardware Address intervals owned by the PLC's device (controller association), from an I/O-detail read.</summary>
    public static List<PlcAddressOccupant> Hardware(HardwareConfigInfo config, string? plcDeviceName, List<string> messages)
    {
        var result = new List<PlcAddressOccupant>();
        if (string.IsNullOrWhiteSpace(plcDeviceName))
        {
            messages.Add("The PLC's device name is unknown, so no hardware address can be attributed to it.");
            return result;
        }

        foreach (var device in config.Devices)
        {
            foreach (var item in device.Items) Walk(device.Name ?? string.Empty, item, new List<string>());
        }

        return result;

        void Walk(string deviceName, DeviceItemInfo item, List<string> path)
        {
            path.Add(item.Name ?? string.Empty);
            foreach (var address in item.IoDetails?.Addresses ?? new List<IoAddressInfo>())
            {
                var area = IoLogicalAddressFormatter.NormalizeArea(address.IoType);
                if (area is null || address.StartAddress is not { } start || address.Length is not { } length || length <= 0) continue;
                if (!address.ControllerNames.Contains(plcDeviceName, StringComparer.Ordinal)) continue;
                var bits = length * 8;
                result.Add(new PlcAddressOccupant("hardware", area, start, 0, bits, Format(area, (long)start * 8, bits) ?? $"%{area}B{start}..{start + length - 1}",
                    null, null, null, null, deviceName, string.Join("/", path), address.IoType));
            }

            foreach (var child in item.Items) Walk(deviceName, child, path);
            path.RemoveAt(path.Count - 1);
        }
    }

    public static PlcAddressOccupancyResult Occupancy(PlcTagInventoryPlcInfo plc, bool inventoryComplete, IReadOnlyList<string> areas,
        List<PlcAddressOccupant>? hardware, List<string> messages, CancellationToken cancellationToken = default)
    {
        var tags = Tags(plc, cancellationToken);
        var hardwareList = hardware ?? new List<PlcAddressOccupant>();
        var resultAreas = new List<PlcAddressArea>();
        var overlaps = new List<PlcAddressOverlap>();
        var outside = new List<PlcAddressOccupant>();
        foreach (var area in areas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var areaTags = tags.Sized.Where(tag => tag.Area == area).ToList();
            var areaHardware = hardwareList.Where(entry => entry.Area == area).ToList();
            var entries = areaTags.Concat(areaHardware)
                .OrderBy(Start).ThenBy(entry => entry.Kind == "hardware" ? 0 : 1)
                .ThenBy(entry => entry.TagName, StringComparer.Ordinal).ToList();
            var usedBits = Merge(entries.Select(entry => (Start: Start(entry) / 8 * 8, End: (End(entry) + 7) / 8 * 8)), cancellationToken);
            resultAreas.Add(new PlcAddressArea(area, entries, (int)(usedBits.Sum(range => range.End - range.Start) / 8)));

            var ordered = areaTags.OrderBy(Start).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var j = i + 1; j < ordered.Count && Start(ordered[j]) < End(ordered[i]); j++)
                    overlaps.Add(new PlcAddressOverlap(area, ordered[i].TagName!, ordered[i].Address, ordered[j].TagName!, ordered[j].Address));
            }

            if (hardware is not null && area != "M")
            {
                var containment = new RangeContainment(areaHardware.Select(entry => (Start(entry), End(entry))));
                outside.AddRange(areaTags.Where(tag => !containment.Contains(Start(tag), End(tag))));
            }
        }

        var unsized = tags.Unsized.Where(tag => areas.Contains(tag.Area, StringComparer.Ordinal)).ToList();
        if (unsized.Count > 0)
            messages.Add($"{unsized.Count} tag(s) at absolute addresses have a data type without a documented length (listed in unsizedTags); "
                + "their extent is unknown, so entries, usedBytes and overlaps omit them and the list is not complete.");
        var mismatches = tags.WidthMismatches.Where(mismatch => TryParse(mismatch.LogicalAddress, out var area, out _, out _) && areas.Contains(area, StringComparer.Ordinal)).ToList();
        if (mismatches.Count > 0)
            messages.Add($"{mismatches.Count} tag(s) have an address spelling whose width differs from the data type's documented length (listed in widthMismatches); the larger width is used.");
        if (!inventoryComplete)
            messages.Add("The tag inventory is incomplete; unread tags are missing from this list.");

        return new PlcAddressOccupancyResult(plc.PlcName, plc.DeviceName ?? string.Empty, inventoryComplete && unsized.Count == 0, hardware is not null,
            resultAreas, overlaps, outside, tags.Unparsed, unsized, mismatches, messages);
    }

    public static PlcFreeAddressResult FindFree(PlcTagInventoryPlcInfo plc, bool inventoryComplete, string area, string dataType,
        int startByte, int endByte, int alignBytes, bool requireHardware, List<PlcAddressOccupant>? hardware, List<string> messages,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetBits(dataType, out var bits)) throw new ArgumentException($"dataType '{dataType}' has no supported width.");
        if (startByte is < 0 or > MaxByte || endByte is < 0 or > MaxByte || endByte < startByte)
            throw new ArgumentOutOfRangeException(nameof(endByte), $"The byte range must lie within 0..{MaxByte} with startByte <= endByte.");
        if (alignBytes is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(alignBytes));

        var tags = Tags(plc, cancellationToken);
        var unsized = tags.Unsized.Where(tag => tag.Area == area).ToList();
        // An unsized tag occupies at least its spelled width; its true extent is unknown.
        var occupied = Merge(tags.Sized.Where(tag => tag.Area == area).Select(tag => (Start(tag), End(tag)))
            .Concat(unsized.Select(tag => ((long)tag.StartByte * 8 + tag.StartBit, (long)tag.StartByte * 8 + tag.StartBit + tag.SpelledBits))), cancellationToken);
        if (!inventoryComplete)
            messages.Add("The tag inventory is incomplete; an unread tag may already use the returned address. Inspect list_tag_tables before using it.");
        foreach (var tag in unsized)
            messages.Add($"Tag '{tag.TagName}' at {tag.LogicalAddress} has data type '{tag.DataType}' without a documented length; "
                + "addresses after its start may lie inside it.");

        var step = bits == 1 ? 1L : alignBytes * 8L;
        var from = (long)startByte * 8;
        var last = (long)endByte * 8 + 8;
        IEnumerable<(long Start, long End)> windows = requireHardware
            ? (hardware ?? new List<PlcAddressOccupant>()).Where(entry => entry.Area == area)
                .Select(entry => (Start: Math.Max(Start(entry), from), End: Math.Min(End(entry), last)))
                .Where(window => window.Start < window.End).OrderBy(window => window.Start).ToArray()
            : new[] { (Start: from, End: last) };

        long? best = null;
        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (best is { } found && window.Start >= found) break;
            var candidate = FirstFree(occupied, window.Start, window.End, bits, step, cancellationToken);
            if (candidate is { } value && (best is null || value < best)) best = value;
        }

        var complete = inventoryComplete && unsized.Count == 0;
        if (best is { } first)
            return new PlcFreeAddressResult(plc.PlcName, area, dataType, bits, true, Format(area, first, bits), (int)(first / 8), (int)(first % 8),
                startByte, endByte, alignBytes, requireHardware, complete, unsized, messages);

        messages.Add($"No free {bits}-bit address in %{area} bytes {startByte}..{endByte}" + (requireHardware ? " inside the PLC's hardware address ranges." : "."));
        return new PlcFreeAddressResult(plc.PlcName, area, dataType, bits, false, null, null, null, startByte, endByte, alignBytes, requireHardware,
            complete, unsized, messages);
    }

    /// <summary>
    /// The lowest aligned start in [<paramref name="low"/>, <paramref name="high"/>) whose interval of
    /// <paramref name="bits"/> overlaps no merged occupied interval. Each step jumps past one occupied
    /// interval, so the sweep is linear in the number of intervals, not in the byte range.
    /// </summary>
    private static long? FirstFree(IReadOnlyList<(long Start, long End)> occupied, long low, long high, int bits, long step, CancellationToken cancellationToken)
    {
        var candidate = Align(low, step);
        var index = FirstEndingAfter(occupied, candidate);
        while (candidate + bits <= high)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (index < occupied.Count && occupied[index].End <= candidate) index++;
            if (index >= occupied.Count || occupied[index].Start >= candidate + bits) return candidate;
            candidate = Align(occupied[index].End, step);
        }

        return null;
    }

    private static long Align(long value, long step) => (value + step - 1) / step * step;

    /// <summary>Index of the first merged interval whose end lies after <paramref name="position"/> (binary search).</summary>
    private static int FirstEndingAfter(IReadOnlyList<(long Start, long End)> occupied, long position)
    {
        int low = 0, high = occupied.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (occupied[middle].End <= position) low = middle + 1;
            else high = middle;
        }

        return low;
    }

    /// <summary>Sorts and merges half-open intervals into disjoint ascending intervals.</summary>
    private static List<(long Start, long End)> Merge(IEnumerable<(long Start, long End)> intervals, CancellationToken cancellationToken)
    {
        var merged = new List<(long Start, long End)>();
        foreach (var interval in intervals.Where(range => range.End > range.Start).OrderBy(range => range.Start))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, interval.End));
            else
                merged.Add(interval);
        }

        return merged;
    }

    /// <summary>Answers "does one interval contain [start, end)?" in O(log n) per query (prefix maximum of ends by start).</summary>
    private sealed class RangeContainment
    {
        private readonly long[] _starts;
        private readonly long[] _maxEnds;

        public RangeContainment(IEnumerable<(long Start, long End)> ranges)
        {
            var sorted = ranges.OrderBy(range => range.Start).ToArray();
            _starts = sorted.Select(range => range.Start).ToArray();
            _maxEnds = new long[sorted.Length];
            for (var i = 0; i < sorted.Length; i++) _maxEnds[i] = i == 0 ? sorted[i].End : Math.Max(_maxEnds[i - 1], sorted[i].End);
        }

        public bool Contains(long start, long end)
        {
            int low = 0, high = _starts.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (_starts[middle] <= start) low = middle + 1;
                else high = middle;
            }

            return low > 0 && _maxEnds[low - 1] >= end;
        }
    }

    private static long Start(PlcAddressOccupant entry) => (long)entry.StartByte * 8 + entry.StartBit;

    private static long End(PlcAddressOccupant entry) => Start(entry) + entry.BitLength;
}
