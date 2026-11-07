using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc.Settings;

/// <summary>
/// Package G: resolves the object paths of <see cref="PlcObjectPaths"/> to live engineering objects with the existing locators
/// (PLC software, blocks, PLC data types, technology objects) and exact-name searches (tag tables, devices, device items, channels).
/// Nothing found is target_not_found, more than one is target_ambiguous.
/// </summary>
internal static class EngineeringObjectResolver
{
    public static object Resolve(ProjectBase project, string path)
    {
        var (kind, rest, error) = PlcObjectPaths.Parse(path);
        if (error is not null) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error);
        var parts = rest!.Split('/');
        switch (kind)
        {
            case "project":
                return project;
            case "plc":
                return PlcSoftwareLocator.FindUnique(project, rest).Software;
            case "block":
            {
                BlockAddress address;
                try
                {
                    address = BlockAddress.Parse(rest);
                }
                catch (ArgumentException exception)
                {
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError, exception.Message);
                }

                return BlockLocator.Locate(PlcSoftwareLocator.FindUnique(project, address.PlcName).Software, address).Block;
            }
            case "type":
            {
                PlcTypeAddress address;
                try
                {
                    address = PlcTypeAddress.Parse(rest);
                }
                catch (ArgumentException exception)
                {
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError, exception.Message);
                }

                return PlcTypeTargetResolver.ResolveForExport(project, address).Type
                    ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC data type '{rest}' was found.");
            }
            case "tagTable":
            {
                var software = PlcSoftwareLocator.FindUnique(project, parts[0]).Software;
                var matches = new List<PlcTagTable>();
                Collect(software.TagTableGroup, parts[1], matches);
                return Single(matches, $"tag table '{parts[1]}' in PLC '{software.Name}'");
            }
            case "device":
                return Device(project, rest);
            case "deviceItem":
                return Item(Device(project, parts[0]), parts[1]);
            case "technologyObject":
                return TechnologyObjects.TechnologyObjectService.Locate(project, parts[0], parts[1]).Object;
            case "channel":
            {
                var item = Item(Device(project, parts[0]), parts[1]);
                var number = int.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
                var channels = item.Channels.Cast<Channel>()
                    .Where(c => c.Type.ToString() == parts[2] && c.IoType.ToString() == parts[3] && c.Number == number).ToList();
                return Single(channels, $"channel {parts[2]}/{parts[3]}/{number} of device item '{parts[1]}'");
            }
            default:
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"objectPath kind '{kind}' is unknown.");
        }
    }

    public static string Describe(object? value)
    {
        if (value is null) return "(null)";
        string? name;
        try
        {
            name = value switch
            {
                Device device => device.Name,
                DeviceItem item => item.Name,
                Channel channel => $"{channel.Type}/{channel.IoType}/{channel.Number}",
                _ => PlcSettingsContext.DefaultName(value),
            };
        }
        catch (EngineeringException)
        {
            name = null;
        }

        return $"{value.GetType().Name} '{name ?? "(unnamed)"}'";
    }

    private static void Collect(PlcTagTableGroup group, string name, List<PlcTagTable> matches)
    {
        foreach (PlcTagTable table in group.TagTables)
            if (string.Equals(table.Name, name, StringComparison.Ordinal)) matches.Add(table);
        foreach (PlcTagTableUserGroup child in group.Groups)
            Collect(child, name, matches);
    }

    private static Device Device(ProjectBase project, string name)
        => Single(Project.ProjectDeviceEnumerator.Enumerate(project).Where(d => string.Equals(d.Name, name, StringComparison.Ordinal)).ToList(), $"device '{name}'");

    private static DeviceItem Item(Device device, string name)
    {
        var matches = new List<DeviceItem>();
        void Walk(DeviceItemComposition items)
        {
            foreach (DeviceItem item in items)
            {
                if (string.Equals(item.Name, name, StringComparison.Ordinal)) matches.Add(item);
                Walk(item.DeviceItems);
            }
        }

        Walk(device.DeviceItems);
        return Single(matches, $"device item '{name}' of device '{device.Name}'");
    }

    private static T Single<T>(IReadOnlyList<T> matches, string what)
        => matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No {what} was found. Nothing was changed."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"The {what} is ambiguous ({matches.Count} matches). Nothing was changed."),
        };
}
