using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// I/O addresses of a device item. When the selected item has no <c>Addresses</c> of its own, the addresses of its
/// sub-items are collected depth-first, each with the relative sub-item path it came from (live 2026-11-02: the S7-1500
/// DI 32 module's addresses sit on its sub-item <c>DI_32/DI_32</c>, whose <c>TypeIdentifier</c> is null, so that sub-item
/// cannot be selected itself; K06). <c>set_io_address</c> uses the same collection.
/// </summary>
internal static partial class HardwareModuleService
{
    /// <summary>Depth limit of the sub-item walk; deeper levels are reported in the messages, never silently dropped.</summary>
    private const int MaxSubItemDepth = 16;

    public static HardwareIoAddressListInfo ListIoAddresses(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var target = Resolve(project, request.Target, "target");
        var item = RequireItem(target, "list_io_addresses");
        var result = new HardwareIoAddressListInfo { Target = target.Identity };
        result.Addresses.AddRange(CollectAddresses(item, result.Messages).Select(entry => entry.Info));
        return result;
    }

    public static HardwareIoAddressWriteResultInfo SetIoAddress(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var target = Resolve(project, request.Target, "target");
        var item = RequireItem(target, HardwareModuleMethods.SetIoAddress);
        var selector = request.Address ?? throw Validation("Address is required.");
        var changes = request.AddressChanges ?? throw Validation("AddressChanges is required.");
        var result = new HardwareIoAddressWriteResultInfo { Target = target.Identity };
        var before = CollectAddresses(item, result.Messages);
        var matches = before.Where(entry => string.Equals(entry.Info.IoType, selector.IoType, StringComparison.Ordinal)
            && entry.Info.StartAddress == selector.StartAddress
            && (selector.Length is null || entry.Info.Length == selector.Length)).ToArray();
        if (matches.Length == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"set_io_address: {Describe(target.Identity)} has no {selector.IoType} address starting at {selector.StartAddress}"
                + (selector.Length is { } length ? $" with length {length}" : string.Empty) + ". Read the current addresses with network_read list_io_addresses.");
        if (matches.Length > 1)
            throw Validation($"set_io_address: {matches.Length} {selector.IoType} addresses start at {selector.StartAddress} on {Describe(target.Identity)}: "
                + string.Join("; ", matches.Select(entry => Describe(entry.Info))) + ". Add length to select one; nothing was changed.");
        var address = matches[0].Address;
        result.Before = before.Select(entry => entry.Info).ToList();
        try
        {
            if (changes.StartAddress is { } start) address.StartAddress = start;
            var dynamic = (IEngineeringObject)address;
            if (changes.ProcessImage is { } processImage) dynamic.SetAttribute("ProcessImage", processImage);
            if (changes.IsochronousMode is { } isochronous) dynamic.SetAttribute("IsochronousMode", isochronous);
            if (changes.InterruptObNumber is { } interruptOb) dynamic.SetAttribute("InterruptObNumber", interruptOb);
        }
        catch (EngineeringException exception)
        {
            var partial = new List<string>();
            var afterFailure = CollectAddresses(item, partial).Select(entry => entry.Info).ToList();
            throw new WorkerOperationException(WorkerExecution.Classify(exception, mutates: true),
                $"set_io_address: {exception.Message} Earlier changes of this item may already be applied (order: StartAddress, ProcessImage, IsochronousMode, InterruptObNumber).",
                afterFailure.Select(Describe).Concat(partial).ToList(), engineeringError: WorkerExecution.Describe(exception));
        }

        result.After = CollectAddresses(item, result.Messages).Select(entry => entry.Info).ToList();
        result.ChangedIndexes = result.After.Where(after => result.Before.FirstOrDefault(b => b.Index == after.Index) is not { } previous
            || !SameAddress(previous, after)).Select(after => after.Index).ToList();
        // Identify the written address by what it must now be (owner sub-item, IoType and the requested or unchanged start,
        // plus the original length), never by its position in the Addresses composition, which TIA Portal may reorder.
        var original = matches[0].Info;
        var expectedStart = changes.StartAddress ?? original.StartAddress;
        var candidates = result.After.Where(entry => SameSubItem(entry.SubItemPath, original.SubItemPath)
            && string.Equals(entry.IoType, original.IoType, StringComparison.Ordinal)
            && entry.StartAddress == expectedStart && (original.Length is null || entry.Length == original.Length)).ToArray();
        var mismatches = new List<string>();
        if (candidates.Length == 0)
            mismatches.Add($"no {original.IoType} address of length {Text(original.Length)} starts at {Text(expectedStart)}{Owner(original)} after the write");
        else if (candidates.Length > 1)
            mismatches.Add($"{candidates.Length} {original.IoType} addresses start at {Text(expectedStart)}{Owner(original)} after the write, so the written one cannot be identified");
        else
        {
            var written = candidates[0];
            if (changes.StartAddress is { } start && written.StartAddress != start) mismatches.Add($"StartAddress reads {Text(written.StartAddress)}, expected {start}");
            if (changes.ProcessImage is { } image && written.ProcessImage != image) mismatches.Add($"ProcessImage reads {Text(written.ProcessImage)}, expected {image}");
            if (changes.IsochronousMode is { } iso && written.IsochronousMode != iso) mismatches.Add($"IsochronousMode reads {Text(written.IsochronousMode)}, expected {iso}");
            if (changes.InterruptObNumber is { } ob && written.InterruptObNumber != ob) mismatches.Add($"InterruptObNumber reads {Text(written.InterruptObNumber)}, expected {ob}");
        }

        if (mismatches.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"set_io_address: {string.Join("; ", mismatches)}.", result.After.Select(Describe).ToList());
        var writtenIndex = candidates[0].Index;
        var coupled = result.ChangedIndexes.Where(index => index != writtenIndex && index != original.Index).ToArray();
        if (coupled.Length > 0)
            result.Messages.Add($"TIA Portal also changed address index(es) {string.Join(", ", coupled)} of this item or its sub-items (coupled input/output ranges).");
        return result;
    }

    /// <summary>
    /// The item's own addresses; when it has none, its sub-items' addresses depth-first (each sub-item's own before its
    /// children's), each with its relative sub-item path. Listing indexes run across the whole collection.
    /// </summary>
    private static List<(Address Address, HardwareIoAddressInfo Info)> CollectAddresses(DeviceItem item, List<string> messages)
    {
        var result = new List<(Address Address, HardwareIoAddressInfo Info)>();
        var notes = new DynamicAttributeNotes();
        ReadAddresses(item, new List<HardwareSubItemSegmentInfo>(), result, messages, notes);
        if (result.Count == 0)
        {
            CollectSubItemAddresses(item, new List<HardwareSubItemSegmentInfo>(), new List<DeviceItem> { item }, result, messages, notes);
            if (result.Count > 0)
                messages.Add("The selected item has no addresses of its own; the addresses of its sub-items are listed with the subItemPath they belong to.");
        }

        notes.AddTo(messages, result.Count);
        return result;
    }

    private static void CollectSubItemAddresses(DeviceItem parent, List<HardwareSubItemSegmentInfo> parentPath, List<DeviceItem> ancestors,
        List<(Address Address, HardwareIoAddressInfo Info)> result, List<string> messages, DynamicAttributeNotes notes)
    {
        if (parentPath.Count >= MaxSubItemDepth)
        {
            messages.Add($"Sub-items deeper than {MaxSubItemDepth} levels below the selected item were not read.");
            return;
        }

        var index = 0;
        foreach (DeviceItem child in parent.DeviceItems)
        {
            var position = index++;
            if (ancestors.Any(seen => ReferenceEquals(seen, child)))
            {
                messages.Add("The sub-item hierarchy contains a cycle; it was not followed further.");
                continue;
            }

            var path = parentPath.Concat(new[] { SubItemSegment(child, position, messages) }).ToList();
            ReadAddresses(child, path, result, messages, notes);
            ancestors.Add(child);
            CollectSubItemAddresses(child, path, ancestors, result, messages, notes);
            ancestors.RemoveAt(ancestors.Count - 1);
        }
    }

    private static HardwareSubItemSegmentInfo SubItemSegment(DeviceItem item, int index, List<string> messages) => new()
    {
        Index = index,
        Name = Read(() => item.Name, $"sub-item {index} name", messages),
        PositionNumber = Read<int?>(() => item.PositionNumber, $"sub-item {index} position number", messages),
        // A null TypeIdentifier is what TIA Portal reports for such sub-items; it is evidence, not a read failure.
        TypeIdentifier = Read(() => item.TypeIdentifier, $"sub-item {index} type identifier", messages),
    };

    private static void ReadAddresses(DeviceItem item, List<HardwareSubItemSegmentInfo> subItemPath,
        List<(Address Address, HardwareIoAddressInfo Info)> result, List<string> messages, DynamicAttributeNotes notes)
    {
        var owner = subItemPath.Count == 0 ? string.Empty : $" of sub-item '{string.Join("/", subItemPath.Select(segment => segment.Name ?? "?"))}'";
        foreach (Address address in item.Addresses)
        {
            var index = result.Count;
            var label = $"address {index}{owner}";
            var info = new HardwareIoAddressInfo { Index = index, SubItemPath = subItemPath.Select(Clone).ToList() };
            info.IoType = Read(() => address.IoType.ToString(), $"{label} IoType", messages);
            info.StartAddress = Read<int?>(() => address.StartAddress, $"{label} StartAddress", messages);
            info.Length = Read<int?>(() => address.Length, $"{label} Length", messages);
            var dynamic = (IEngineeringObject)address;
            var offered = notes.Offered(dynamic);
            info.Context = notes.Read<string>(offered, "Context", () => dynamic.GetAttribute("Context")?.ToString());
            info.ProcessImage = notes.Read<int?>(offered, "ProcessImage", () => dynamic.GetAttribute("ProcessImage") is { } value ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) : (int?)null);
            info.IsochronousMode = notes.Read<bool?>(offered, "IsochronousMode", () => dynamic.GetAttribute("IsochronousMode") as bool?);
            info.InterruptObNumber = notes.Read<long?>(offered, "InterruptObNumber", () => dynamic.GetAttribute("InterruptObNumber") is { } value ? Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) : (long?)null);
            result.Add((address, info));
        }
    }

    private static HardwareSubItemSegmentInfo Clone(HardwareSubItemSegmentInfo segment) => new()
    {
        Index = segment.Index, Name = segment.Name, PositionNumber = segment.PositionNumber, TypeIdentifier = segment.TypeIdentifier,
    };

    private static bool SameSubItem(List<HardwareSubItemSegmentInfo> left, List<HardwareSubItemSegmentInfo> right)
        => left.Count == right.Count && left.Zip(right, (a, b) => a.Index == b.Index && a.Name == b.Name && a.PositionNumber == b.PositionNumber
            && a.TypeIdentifier == b.TypeIdentifier).All(same => same);

    private static T? Read<T>(Func<T?> read, string description, List<string> messages)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is EngineeringException || exception is InvalidCastException
            || exception is FormatException || exception is OverflowException)
        {
            messages.Add($"Could not read {description}: {exception.Message}");
            return default;
        }
    }

    private static bool SameAddress(HardwareIoAddressInfo left, HardwareIoAddressInfo right)
        => SameSubItem(left.SubItemPath, right.SubItemPath) && left.IoType == right.IoType && left.StartAddress == right.StartAddress
            && left.Length == right.Length && left.ProcessImage == right.ProcessImage && left.IsochronousMode == right.IsochronousMode
            && left.InterruptObNumber == right.InterruptObNumber;

    private static string Owner(HardwareIoAddressInfo address)
        => address.SubItemPath.Count == 0 ? string.Empty : $" on sub-item '{string.Join("/", address.SubItemPath.Select(segment => segment.Name ?? "?"))}'";

    private static string Describe(HardwareIoAddressInfo address)
        => $"address {address.Index}{Owner(address)}: {address.IoType ?? "?"} start {Text(address.StartAddress)} length {Text(address.Length)} "
            + $"processImage {Text(address.ProcessImage)} isochronousMode {Text(address.IsochronousMode)} interruptObNumber {Text(address.InterruptObNumber)}";

    private static string Text<T>(T? value) where T : struct => value?.ToString() ?? "null";
}
