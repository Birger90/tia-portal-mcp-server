using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts.Network;
using static TiaMcpServer.OpennessWorker.Openness.Network.NetworkOpennessRead;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// Reads the structured I/O map for one device item: addresses (I/O type, start, length,
/// dynamic context, controller names) and channels (number, I/O type, type, dynamic bit
/// address and width), each read individually guarded. The dynamic attributes (address Context, channel ChannelAddress and
/// ChannelWidth) are read only when the object lists them in GetAttributeInfos (K58, as list_io_addresses since K39); what is
/// not offered or not readable is reported once per attribute and device item.
/// </summary>
public static class HardwareIoMapReader
{
    public static DeviceItemIoDetailsInfo Read(
        DeviceItem item,
        string itemDescription,
        List<string> messages,
        IoTagIndex? tagIndex)
    {
        var details = new DeviceItemIoDetailsInfo();
        var addressRecords = new List<IoAddressRecord>();

        var addressNotes = new DynamicAttributeNotes("addresses");
        ReadAddresses(item, itemDescription, messages, details, addressRecords, addressNotes);
        addressNotes.AddTo(messages, details.Addresses.Count, $" of device item '{itemDescription}'");
        var channelNotes = new DynamicAttributeNotes("channels");
        ReadChannels(item, itemDescription, messages, tagIndex, addressRecords, details, channelNotes);
        channelNotes.AddTo(messages, details.Channels.Count, $" of device item '{itemDescription}'");

        details.Addresses = details.Addresses
            .OrderBy(address => address.IoType, StringComparer.Ordinal)
            .ThenBy(address => address.StartAddress)
            .ThenBy(address => address.Length)
            .ToList();
        details.Channels = details.Channels
            .OrderBy(channel => channel.Number)
            .ThenBy(channel => channel.Type, StringComparer.Ordinal)
            .ToList();

        return details;
    }

    private static void ReadAddresses(
        DeviceItem item,
        string itemDescription,
        List<string> messages,
        DeviceItemIoDetailsInfo details,
        List<IoAddressRecord> addressRecords,
        DynamicAttributeNotes notes)
    {
        try
        {
            foreach (Address address in item.Addresses)
            {
                try
                {
                    var info = new IoAddressInfo
                    {
                        IoType = ReadOptionalEnumName(
                            () => address.IoType,
                            $"device item '{itemDescription}' address I/O type",
                            messages),
                        StartAddress = ReadOptionalNonNegativeInt(
                            () => address.StartAddress,
                            $"device item '{itemDescription}' address start address",
                            messages),
                        Length = ReadOptionalNonNegativeInt(
                            () => address.Length,
                            $"device item '{itemDescription}' address length",
                            messages),
                        Context = ReadAddressContext(address, notes),
                    };

                    var record = new IoAddressRecord
                    {
                        IoType = info.IoType,
                        StartAddress = info.StartAddress,
                        Length = info.Length,
                    };

                    ReadAddressControllers(address, itemDescription, messages, info, record);

                    details.Addresses.Add(info);
                    addressRecords.Add(record);
                }
                catch (EngineeringException exception)
                {
                    messages.Add(
                        $"Skipped an address while reading device item '{itemDescription}': {exception.Message}");
                }
            }
        }
        catch (EngineeringException exception)
        {
            messages.Add(
                $"Could not enumerate addresses while reading device item '{itemDescription}': {exception.Message}");
        }
    }

    private static void ReadAddressControllers(
        Address address,
        string itemDescription,
        List<string> messages,
        IoAddressInfo info,
        IoAddressRecord record)
    {
        var controllerReadable = true;
        try
        {
            foreach (var controller in address.AddressControllers)
            {
                var controllerName = ReadControllerOwningDeviceName(controller, itemDescription, messages);
                if (controllerName is null)
                {
                    controllerReadable = false;
                    continue;
                }

                info.ControllerNames.Add(controllerName);
            }
        }
        catch (EngineeringException exception)
        {
            controllerReadable = false;
            messages.Add(
                $"Could not read address controllers while reading device item "
                    + $"'{itemDescription}': {exception.Message}");
        }

        info.ControllerNames = info.ControllerNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        record.ControllerNames = info.ControllerNames;
        record.ControllerAssociationReadable = controllerReadable;
    }

    private static void ReadChannels(
        DeviceItem item,
        string itemDescription,
        List<string> messages,
        IoTagIndex? tagIndex,
        IReadOnlyList<IoAddressRecord> addressRecords,
        DeviceItemIoDetailsInfo details,
        DynamicAttributeNotes notes)
    {
        try
        {
            foreach (Channel channel in item.Channels)
            {
                try
                {
                    var offered = notes.Offered((IEngineeringObject)channel);
                    var channelInfo = new IoChannelInfo
                    {
                        Number = ReadOptionalInt(
                            () => channel.Number,
                            $"device item '{itemDescription}' channel number",
                            messages),
                        IoType = ReadOptionalEnumName(
                            () => channel.IoType,
                            $"device item '{itemDescription}' channel I/O type",
                            messages),
                        Type = ReadOptionalEnumName(
                            () => channel.Type,
                            $"device item '{itemDescription}' channel type",
                            messages),
                        ChannelAddressBits = ReadDynamicIntAttribute(
                            (IEngineeringObject)channel,
                            offered,
                            notes,
                            "ChannelAddress",
                            $"device item '{itemDescription}' channel address",
                            messages),
                        ChannelWidthBits = ReadDynamicUIntAttribute(
                            (IEngineeringObject)channel,
                            offered,
                            notes,
                            "ChannelWidth",
                            $"device item '{itemDescription}' channel width",
                            messages),
                    };
                    channelInfo.LogicalAddress = IoLogicalAddressFormatter.FormatLogicalAddress(
                        channelInfo.IoType,
                        channelInfo.ChannelAddressBits,
                        channelInfo.ChannelWidthBits);

                    channelInfo.TagMatches = ReadChannelTagMatches(
                        channelInfo,
                        addressRecords,
                        tagIndex,
                        itemDescription,
                        messages);

                    details.Channels.Add(channelInfo);
                }
                catch (EngineeringException exception)
                {
                    messages.Add(
                        $"Skipped a channel while reading device item '{itemDescription}': {exception.Message}");
                }
            }
        }
        catch (EngineeringException exception)
        {
            messages.Add(
                $"Could not enumerate channels while reading device item '{itemDescription}': {exception.Message}");
        }
    }

    private static List<IoTagMatchInfo> ReadChannelTagMatches(
        IoChannelInfo channelInfo,
        IReadOnlyList<IoAddressRecord> addressRecords,
        IoTagIndex? tagIndex,
        string itemDescription,
        List<string> messages)
    {
        var matches = new List<IoTagMatchInfo>();
        if (tagIndex is null)
        {
            return matches;
        }

        var resolution = IoChannelControllerResolver.Resolve(
            channelInfo.IoType,
            channelInfo.ChannelAddressBits,
            channelInfo.ChannelWidthBits,
            addressRecords,
            itemDescription);

        if (resolution.DiagnosticMessage is not null)
        {
            messages.Add(resolution.DiagnosticMessage);
        }

        if (!resolution.IsTargetMatch(tagIndex.PlcDeviceName))
        {
            return matches;
        }

        var channelArea = IoLogicalAddressFormatter.NormalizeArea(channelInfo.IoType);
        if (channelArea is null
            || channelInfo.ChannelAddressBits is null
            || channelInfo.ChannelWidthBits is null
            || channelInfo.ChannelAddressBits < 0)
        {
            return matches;
        }

        var channelAddress = new IoAbsoluteIoAddress(
            channelArea,
            new IoAbsoluteBitInterval(
                channelInfo.ChannelAddressBits.Value,
                channelInfo.ChannelWidthBits.Value));
        foreach (var candidate in tagIndex.FindMatches(channelAddress))
        {
            matches.Add(new IoTagMatchInfo
            {
                Name = candidate.Name,
                DataType = candidate.DataType,
                LogicalAddress = candidate.LogicalAddress,
                TableName = candidate.TableName,
                FolderPath = candidate.FolderPath,
            });
        }

        return matches;
    }

    private static string? ReadAddressContext(Address address, DynamicAttributeNotes notes)
    {
        var dynamic = (IEngineeringObject)address;
        return notes.Read<string>(notes.Offered(dynamic), "Context", () => dynamic.GetAttribute("Context")?.ToString());
    }

    private static int? ReadDynamicIntAttribute(
        IEngineeringObject engineeringObject,
        HashSet<string>? offered,
        DynamicAttributeNotes notes,
        string attributeName,
        string description,
        List<string> messages)
    {
        var value = notes.Read<object>(offered, attributeName, () => engineeringObject.GetAttribute(attributeName));
        var coerced = DynamicNumericAttribute.CoerceInt32(value);
        if (coerced is null && value is not null)
        {
            messages.Add(
                $"Could not read {description}: attribute '{attributeName}' had an unexpected CLR type or value.");
            return null;
        }

        return coerced;
    }

    private static uint? ReadDynamicUIntAttribute(
        IEngineeringObject engineeringObject,
        HashSet<string>? offered,
        DynamicAttributeNotes notes,
        string attributeName,
        string description,
        List<string> messages)
    {
        var value = notes.Read<object>(offered, attributeName, () => engineeringObject.GetAttribute(attributeName));
        var coerced = DynamicNumericAttribute.CoerceUInt32(value);
        if (coerced is null && value is not null)
        {
            messages.Add(
                $"Could not read {description}: attribute '{attributeName}' had an unexpected CLR type or value.");
            return null;
        }

        return coerced;
    }

    private static string? ReadControllerOwningDeviceName(
        AddressController controller,
        string itemDescription,
        List<string> messages)
    {
        try
        {
            return HardwareConfigReader.FindParentDeviceName(controller.OwnedBy, messages);
        }
        catch (EngineeringException exception)
        {
            messages.Add(
                $"Could not read an address controller while reading device item "
                    + $"'{itemDescription}': {exception.Message}");
            return null;
        }
    }

}
