// Offline boundary doubles for the source-linked HardwareModuleService. They model the documented
// members the service calls (HardwareObject plugging, DeviceItem.Delete/ChangeType/Addresses,
// Address.StartAddress and its dynamic attributes, GetAttributeInfos, the bulk SetAttributes with
// AttributeDelegate, PlugLocation, ModuleInformationProvider, Project.HwUtilities) plus test hooks.
// No Openness assembly is loaded.
using System.Collections;

namespace Siemens.Engineering
{
    public enum EngineeringAttributeAccessMode { None = 0, Read = 1, Write = 2, ReadWrite = 3 }

    public sealed class EngineeringAttributeInfo
    {
        public string Name { get; set; } = string.Empty;
        public EngineeringAttributeAccessMode AccessMode { get; set; } = EngineeringAttributeAccessMode.ReadWrite;
        public IEnumerable<Type> SupportedTypes { get; set; } = Array.Empty<Type>();
    }

    public enum AttributeChoiceSelection { Abort, Ignore }

    /// <summary>The documented callback argument of <c>SetAttributes(..., AttributeDelegate)</c>.</summary>
    public sealed class AttributeConfiguration
    {
        private AttributeChoiceSelection selection;
        public string Name { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        /// <summary>Thrown by the CurrentSelection setter (models an installation that cannot take the answer).</summary>
        public Exception? SelectionFailure { get; set; }
        public AttributeChoiceSelection CurrentSelection
        {
            get => selection;
            set
            {
                if (SelectionFailure is not null) throw SelectionFailure;
                selection = value;
            }
        }
    }

    public delegate void AttributeDelegate(AttributeConfiguration configuration);

    public sealed partial class Project
    {
        /// <summary>Read by name through reflection, as the worker does (its declaring type is undocumented).</summary>
        public List<object> HwUtilities { get; } = new();
    }
}

namespace Siemens.Engineering.HW
{
    public enum AddressIoType { Input, Output }

    /// <summary>Common base of Device and DeviceItem: plugging, attributes and the bulk attribute write.</summary>
    public abstract class HardwareObject : NamedObject
    {
        protected HardwareObject() => DeviceItems.Parent = this;

        public DeviceItemComposition DeviceItems { get; } = new();

        // ---- attributes
        public Dictionary<string, object> Attributes { get; } = new(StringComparer.Ordinal);
        public List<EngineeringAttributeInfo> AttributeInfos { get; } = new();
        /// <summary>Returns a rejection message for a value TIA Portal refuses, or null to accept it.</summary>
        public Func<string, object, string?>? RejectAttribute { get; set; }
        /// <summary>Accepted but not stored (models a write Portal silently ignores).</summary>
        public HashSet<string> SilentlyIgnored { get; } = new(StringComparer.Ordinal);
        /// <summary>What TIA Portal continues with when the callback leaves CurrentSelection unanswered.</summary>
        public AttributeChoiceSelection DefaultSelection { get; set; } = AttributeChoiceSelection.Ignore;
        public int BulkCalls { get; private set; }

        public override IList<EngineeringAttributeInfo> GetAttributeInfos() => AttributeInfos;

        public override object GetAttribute(string attributeName)
            => Attributes.TryGetValue(attributeName, out var value) ? value : base.GetAttribute(attributeName);

        public override void SetAttribute(string attributeName, object value)
        {
            if (RejectAttribute?.Invoke(attributeName, value) is { } message) throw new EngineeringTargetInvocationException(message);
            if (!SilentlyIgnored.Contains(attributeName)) Attributes[attributeName] = value;
        }

        public void SetAttributes(IEnumerable<KeyValuePair<string, object>> attributes, AttributeDelegate callback)
        {
            BulkCalls++;
            foreach (var pair in attributes)
            {
                if (RejectAttribute?.Invoke(pair.Key, pair.Value) is { } message)
                {
                    var configuration = new AttributeConfiguration { Name = pair.Key, Message = message };
                    configuration.CurrentSelection = DefaultSelection;
                    configuration.SelectionFailure = SelectionFailureForCallback;
                    callback(configuration);
                    if (configuration.CurrentSelection == AttributeChoiceSelection.Abort)
                        throw new EngineeringTargetInvocationException($"SetAttributes aborted at '{pair.Key}': {message}");
                    continue;
                }

                if (!SilentlyIgnored.Contains(pair.Key)) Attributes[pair.Key] = pair.Value;
            }
        }

        /// <summary>Applied to each configuration handed to the callback.</summary>
        public Exception? SelectionFailureForCallback { get; set; }

        // ---- plugging
        public Func<string, string, int, bool>? CanPlugNewRule { get; set; }
        /// <summary>Replaces what PlugNew/PlugMove/PlugCopy return (after the default placement), e.g. null or another item.</summary>
        public Func<DeviceItem, DeviceItem?>? PlacedResult { get; set; }
        /// <summary>Position TIA Portal actually uses (default: the requested one).</summary>
        public Func<int, int>? ActualPosition { get; set; }
        public Exception? PlugFailure { get; set; }
        public List<Extensions.PlugLocation> PlugLocations { get; } = new();

        public bool CanPlugNew(string typeIdentifier, string name, int positionNumber)
            => CanPlugNewRule?.Invoke(typeIdentifier, name, positionNumber) ?? DeviceItems.Items.All(item => item.PositionNumber != positionNumber);

        public bool CanPlugMove(DeviceItem deviceItem, int positionNumber) => DeviceItems.Items.All(item => item.PositionNumber != positionNumber);

        public bool CanPlugCopy(DeviceItem deviceItem, int positionNumber) => DeviceItems.Items.All(item => item.PositionNumber != positionNumber);

        public DeviceItem PlugNew(string typeIdentifier, string name, int positionNumber)
        {
            if (PlugFailure is not null) throw PlugFailure;
            var item = new DeviceItem { Name = name.Length == 0 ? typeIdentifier : name, TypeIdentifier = typeIdentifier, PositionNumber = Position(positionNumber) };
            return Place(item);
        }

        public DeviceItem PlugMove(DeviceItem deviceItem, int positionNumber)
        {
            if (PlugFailure is not null) throw PlugFailure;
            if (deviceItem.Parent is HardwareObject owner) owner.DeviceItems.Items.Remove(deviceItem);
            deviceItem.PositionNumber = Position(positionNumber);
            return Place(deviceItem);
        }

        public DeviceItem PlugCopy(DeviceItem deviceItem, int positionNumber)
        {
            if (PlugFailure is not null) throw PlugFailure;
            var copy = new DeviceItem { Name = deviceItem.Name + "_1", TypeIdentifier = deviceItem.TypeIdentifier, PositionNumber = Position(positionNumber) };
            return Place(copy);
        }

        public IList<Extensions.PlugLocation> GetPlugLocations() => PlugLocations;

        private int Position(int requested) => ActualPosition?.Invoke(requested) ?? requested;

        private DeviceItem Place(DeviceItem item)
        {
            item.Parent = this;
            DeviceItems.Items.Add(item);
            return PlacedResult is null ? item : PlacedResult(item)!;
        }
    }

    public sealed partial class DeviceItem
    {
        public AddressComposition Addresses { get; } = new();
        public Exception? DeleteFailure { get; set; }
        /// <summary>Deleting leaves the item in place (models a delete Portal does not perform).</summary>
        public bool IgnoreDelete { get; set; }

        public void Delete()
        {
            if (DeleteFailure is not null) throw DeleteFailure;
            if (!IgnoreDelete && Parent is HardwareObject owner) owner.DeviceItems.Items.Remove(this);
        }

        /// <summary>Documented <c>ChangeType(System.String)</c>; the service invokes it by reflection.</summary>
        public void ChangeType(string typeIdentifier)
        {
            if (ChangeTypeFailure is not null) throw ChangeTypeFailure;
            if (!IgnoreChangeType) TypeIdentifier = typeIdentifier;
        }

        public Exception? ChangeTypeFailure { get; set; }
        public bool IgnoreChangeType { get; set; }
    }

    public sealed class AddressComposition : IEnumerable<Address>
    {
        public List<Address> Items { get; } = new();
        public IEnumerator<Address> GetEnumerator() => Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class Address : NamedObject
    {
        private int startAddress;
        public AddressIoType IoType { get; set; }
        public int Length { get; set; }
        public Dictionary<string, object> Attributes { get; } = new(StringComparer.Ordinal);
        public Exception? StartAddressFailure { get; set; }
        /// <summary>Runs after a StartAddress write (models coupled ranges or Portal reordering the composition).</summary>
        public Action<Address>? StartAddressChanged { get; set; }

        public int StartAddress
        {
            get => startAddress;
            set
            {
                if (StartAddressFailure is not null) throw StartAddressFailure;
                startAddress = value;
                StartAddressChanged?.Invoke(this);
            }
        }

        /// <summary>What GetAttributeInfos lists: the modeled attributes, unless a test sets the list itself.</summary>
        public List<EngineeringAttributeInfo>? AttributeInfos { get; set; }
        /// <summary>Counts every GetAttribute call (an attribute that is not offered must not be read).</summary>
        public List<string> AttributeReads { get; } = new();

        public override IList<EngineeringAttributeInfo> GetAttributeInfos()
            => AttributeInfos ?? Attributes.Keys.Select(name => new EngineeringAttributeInfo { Name = name }).ToList();

        public override object GetAttribute(string attributeName)
        {
            AttributeReads.Add(attributeName);
            return Attributes.TryGetValue(attributeName, out var value) ? value : throw new EngineeringNotSupportedException($"'{attributeName}' is not supported.");
        }

        public override void SetAttribute(string attributeName, object value) => Attributes[attributeName] = value;
    }
}

namespace Siemens.Engineering.HW.Extensions
{
    public class PlugLocation
    {
        public int PositionNumber { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}

namespace Siemens.Engineering.HW.Utilities
{
    public sealed class ModuleInformationProvider
    {
        public Dictionary<string, string[]> ContainerTypes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string[]> ModuleTypes { get; } = new(StringComparer.Ordinal);
        public string[] FindContainerTypes(string typeIdentifier) => ContainerTypes.TryGetValue(typeIdentifier, out var types) ? types : Array.Empty<string>();
        public string[] FindModuleTypes(string partialTypeIdentifier) => ModuleTypes.TryGetValue(partialTypeIdentifier, out var types) ? types : Array.Empty<string>();
    }
}
