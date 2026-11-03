// Offline boundary doubles for the hmi_read log and screen readers. They declare the same members as
// reference-stubs/Siemens.Engineering.WinCCUnified/{Logging,Screens}.cs and add test hooks; no Openness
// assembly is loaded. Keep this file in step with the stubs. HmiButton and HmiGraphicView exist only here:
// the readers never name them, tests use them to prove the CLR short type name is reported.
using Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI.Base;

namespace Siemens.Engineering.HmiUnified
{
    public sealed partial class HmiSoftware
    {
        public HmiLogging.HmiDataLogComposition DataLogs { get; } = new();
        public HmiLogging.HmiAlarmLogComposition AlarmLogs { get; } = new();
        public HmiLogging.HmiAuditTrailComposition AuditTrails { get; } = new();
        public UI.Screens.HmiScreenComposition Screens { get; } = new();
        public UI.ScreenGroup.HmiScreenGroupComposition ScreenGroups { get; } = new();
    }
}

namespace Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon
{
    public enum DeviceNode { None, Off, Default, Local, SDX51, USBX61, USBX62 }
    public enum HmiBackupMode { NoBackup, PrimaryPath }

    public abstract class DurationBase : PropertyBagNamed
    {
        public uint Days { get => Get<uint>(); set => Set(value); }
        public uint Hours { get => Get<uint>(); set => Set(value); }
        public uint Minutes { get => Get<uint>(); set => Set(value); }
        public uint Seconds { get => Get<uint>(); set => Set(value); }
    }

    public sealed class LogDuration : DurationBase { }
    public sealed class SegmentDuration : DurationBase { }

    public sealed class LogSettings : PropertyBagNamed
    {
        public uint LogMaxSize { get => Get<uint>(); set => Set(value); }
        public LogDuration LogTimePeriod { get => Get<LogDuration>(); set => Set(value); }
        public DeviceNode StorageDevice { get => Get<DeviceNode>(); set => Set(value); }
        public string StorageFolder { get => Get<string>(); set => Set(value); }
    }

    public sealed class LogSegment : PropertyBagNamed
    {
        public uint SegmentMaxSize { get => Get<uint>(); set => Set(value); }
        public DateTime SegmentStartTime { get => Get<DateTime>(); set => Set(value); }
        public SegmentDuration SegmentTimePeriod { get => Get<SegmentDuration>(); set => Set(value); }
    }

    public sealed class LogBackup : PropertyBagNamed
    {
        public HmiBackupMode BackupMode { get => Get<HmiBackupMode>(); set => Set(value); }
        public string PrimaryPath { get => Get<string>(); set => Set(value); }
    }

    public abstract class LoggingBase : Common.ValidatableBag
    {
        public LogBackup Backup { get => Get<LogBackup>(); set => Set(value); }
        public LogSegment Segment { get => Get<LogSegment>(); set => Set(value); }
        public LogSettings Settings { get => Get<LogSettings>(); set => Set(value); }
    }
}

namespace Siemens.Engineering.HmiUnified.HmiLogging
{
    public sealed class HmiDataLog : LoggingBase { }
    public sealed class HmiDataLogComposition : Composition<HmiDataLog> { }
    public sealed class HmiAlarmLog : LoggingBase { }
    public sealed class HmiAlarmLogComposition : Composition<HmiAlarmLog> { }
    public sealed class HmiAuditTrail : LoggingBase { }
    public sealed class HmiAuditTrailComposition : Composition<HmiAuditTrail> { }
}

namespace Siemens.Engineering.HmiUnified.UI.Base
{
    public abstract class HmiScreenItemBase : PropertyBagNamed
    {
        public bool Enabled { get => Get<bool>(); set => Set(value); }
        public bool Visible { get => Get<bool>(); set => Set(value); }

        // hmi_write screen doubles (members the worker binds at runtime): Delete, Dynamizations, EventHandlers and the
        // generic attribute API over the CLR properties.
        internal HmiScreenItemBaseComposition? Owner { get; set; }
        public void Delete() => Owner!.Items.Remove(this);
        public Dynamization.DynamizationComposition Dynamizations { get; } = new();
        public Events.EventHandlerComposition EventHandlers { get; } = new();
        public Events.PropertyEventHandlerComposition PropertyEventHandlers { get; } = new();
        public override object GetAttribute(string attributeName) => ReflectedAttributes.Get(this, attributeName);
        public override void SetAttribute(string attributeName, object value) => ReflectedAttributes.Set(this, attributeName, value);
        public override IList<EngineeringAttributeInfo> GetAttributeInfos() => ReflectedAttributes.Infos(this);
    }

    /// <summary>An item with box geometry, like the real concrete types that implement IHmiBoxFeature.</summary>
    public abstract class BoxItem : HmiScreenItemBase, Features.IHmiBoxFeature
    {
        public int Left { get => Get<int>(); set => Set(value); }
        public int Top { get => Get<int>(); set => Set(value); }
        public uint Width { get => Get<uint>(); set => Set(value); }
        public uint Height { get => Get<uint>(); set => Set(value); }

        public BoxItem At(int left, int top, uint width, uint height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            return this;
        }
    }

    public abstract class HmiContainerBase : HmiScreenItemBase
    {
        public string ContainedType { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiScreenItemBaseComposition : Composition<HmiScreenItemBase>
    {
        public T Create<T>(string name) where T : HmiScreenItemBase, new()
        {
            var item = new T { Name = name, Owner = this };
            Items.Add(item);
            return item;
        }
    }

    /// <summary>The generic attribute API of the screen doubles: public CLR properties, writable when they have a public setter.</summary>
    internal static class ReflectedAttributes
    {
        private static System.Reflection.PropertyInfo? Property(object target, string name)
            => target.GetType().GetProperties().FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0);

        public static object Get(object target, string name)
            => (Property(target, name) ?? throw new EngineeringException($"Attribute '{name}' is not modeled.")).GetValue(target)!;

        public static void Set(object target, string name, object value)
        {
            var property = Property(target, name);
            if (property?.SetMethod is not { IsPublic: true }) throw new EngineeringException($"Attribute '{name}' is not writable.");
            property.SetValue(target, value);
        }

        public static IList<EngineeringAttributeInfo> Infos(object target)
            => target.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod is { IsPublic: true })
                .Select(p => new EngineeringAttributeInfo
                {
                    Name = p.Name,
                    AccessMode = p.SetMethod is { IsPublic: true } ? EngineeringAttributeAccessMode.ReadWrite : EngineeringAttributeAccessMode.Read,
                }).ToList();
    }
}

namespace Siemens.Engineering.HmiUnified.UI.Controls
{
    public sealed class HmiFaceplateContainer : HmiContainerBase
    {
        private readonly Parts.HmiFaceplateInterfaceComposition interfaceBindings = new();
        public Parts.HmiFaceplateInterfaceComposition Interface { get { Check(nameof(Interface)); return interfaceBindings; } }
    }
}

namespace Siemens.Engineering.HmiUnified.UI.Parts
{
    public sealed class HmiFaceplateInterface : PropertyBagNamed
    {
        public string PropertyName { get => Get<string>(); set => Set(value); }
        public object Value { get => Get<object>(); set => Set(value); }
    }

    public sealed class HmiFaceplateInterfaceComposition : Composition<HmiFaceplateInterface> { }
}

namespace Siemens.Engineering.HmiUnified.UI.Widgets
{
    public sealed class HmiButton : BoxItem
    {
        public string BackColorName { get => Get<string>() ?? "Gray"; set => Set(value); }
        public HmiTextAlign Align { get => Get<HmiTextAlign>(); set => Set(value); }
    }

    public enum HmiTextAlign { Left, Center, Right }
}

namespace Siemens.Engineering.HmiUnified.UI.Dynamization
{
    /// <summary>Delete() of the dynamization doubles (issue #29 writes): removed from the composition that created it.</summary>
    public interface IOwnedDynamization { System.Collections.IList? Owner { get; set; } }

    public sealed class TagDynamization : HmiTags.PropertyBagNamed, IOwnedDynamization
    {
        public System.Collections.IList? Owner { get; set; }
        public void Delete() => Owner!.Remove(this);
        public string PropertyName { get => Get<string>(); set => Set(value); }
        public string Tag { get => Get<string>(); set => Set(value); }
        public override object GetAttribute(string attributeName) => Base.ReflectedAttributes.Get(this, attributeName);
        public override void SetAttribute(string attributeName, object value) => Base.ReflectedAttributes.Set(this, attributeName, value);
    }

    public sealed class ScriptDynamization : HmiTags.PropertyBagNamed, IOwnedDynamization
    {
        public System.Collections.IList? Owner { get; set; }
        public void Delete() => Owner!.Remove(this);
        public bool Async { get; set; }
        public Script.Trigger Trigger { get; } = new();
        public string PropertyName { get => Get<string>(); set => Set(value); }
        public string ScriptCode { get => Get<string>(); set => Set(value); }
        public string GlobalDefinitionAreaScriptCode { get => Get<string>(); set => Set(value); }
        public override object GetAttribute(string attributeName) => Base.ReflectedAttributes.Get(this, attributeName);
    }

    public sealed class DynamizationComposition : Composition<HmiTags.PropertyBagNamed>
    {
        public T Create<T>(string propertyName) where T : HmiTags.PropertyBagNamed, new()
        {
            var created = new T();
            typeof(T).GetProperty("PropertyName")!.SetValue(created, propertyName);
            if (created is IOwnedDynamization owned) owned.Owner = Items;
            Items.Add(created);
            return created;
        }
    }
}

namespace Siemens.Engineering.HmiUnified.UI.Dynamization.Script
{
    public enum TriggerType { Disabled, T100ms, T1s, Tags, CustomCycle }

    public sealed class Trigger
    {
        public TriggerType Type { get; set; } = TriggerType.T1s;
        public List<string> Tags { get; } = new();
        public int CustomDuration { get; set; }
    }
}

namespace Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing
{
    public enum FlashingCondition { Always, Never, RangeViolation }
    public enum FlashingRate { Slow, Medium, Fast }

    public sealed class FlashingDynamization : HmiTags.PropertyBagNamed, IOwnedDynamization
    {
        public System.Collections.IList? Owner { get; set; }
        public void Delete() => Owner!.Remove(this);
        public string PropertyName { get => Get<string>(); set => Set(value); }
        public System.Drawing.Color Color { get; set; } = System.Drawing.Color.Black;
        public System.Drawing.Color AlternateColor { get; set; } = System.Drawing.Color.White;
        public FlashingCondition FlashingCondition { get; set; }
        public FlashingRate FlashingRate { get; set; } = FlashingRate.Medium;
        public override object GetAttribute(string attributeName) => Base.ReflectedAttributes.Get(this, attributeName);
        public override void SetAttribute(string attributeName, object value) => Base.ReflectedAttributes.Set(this, attributeName, value);
    }
}

namespace Siemens.Engineering.HmiUnified.UI.Enum
{
    public enum HmiButtonEventType { None, Tapped, Up, Down }
}

namespace Siemens.Engineering.HmiUnified.UI.Events
{
    public enum PropertyEventType { Change, QualityCodeChange }

    public sealed class PropertyEventHandler : HmiTags.PropertyBagNamed
    {
        internal PropertyEventHandlerComposition? Owner { get; set; }
        public string PropertyName { get => Get<string>(); set => Set(value); }
        public PropertyEventType EventType { get; set; }
        public HmiScript Script { get; } = new();
        public void Delete() => Owner!.Items.Remove(this);
        public override object GetAttribute(string attributeName) => Base.ReflectedAttributes.Get(this, attributeName);
    }

    public sealed class PropertyEventHandlerComposition : Composition<PropertyEventHandler>
    {
        public PropertyEventHandler Create(string propertyName, PropertyEventType eventType)
        {
            var handler = new PropertyEventHandler { PropertyName = propertyName, EventType = eventType, Owner = this };
            Items.Add(handler);
            return handler;
        }

        public PropertyEventHandler? Find(string propertyName, PropertyEventType eventType)
            => Items.FirstOrDefault(h => h.PropertyName == propertyName && h.EventType == eventType);
    }

    public sealed class HmiScript
    {
        public string ScriptCode { get; set; } = string.Empty;
        public string GlobalDefinitionAreaScriptCode { get; set; } = string.Empty;
        public bool Async { get; set; }
    }

    public sealed class EventHandler : HmiTags.PropertyBagNamed
    {
        internal EventHandlerComposition? Owner { get; set; }
        public void Delete() => Owner!.Items.Remove(this);
        public string EventType { get => Get<string>(); set => Set(value); }
        public HmiScript Script { get; } = new();
        public override object GetAttribute(string attributeName) => Base.ReflectedAttributes.Get(this, attributeName);
    }

    /// <summary>Models a screen item's typed composition, e.g. HmiButtonEventHandlerComposition.Create(HmiButtonEventType).</summary>
    public sealed class EventHandlerComposition : Composition<EventHandler>
    {
        public EventHandler Create(Enum.HmiButtonEventType eventType)
        {
            var handler = new EventHandler { EventType = eventType.ToString(), Owner = this };
            Items.Add(handler);
            return handler;
        }

        public EventHandler? Find(Enum.HmiButtonEventType eventType) => Items.FirstOrDefault(h => h.EventType == eventType.ToString());
    }
}

namespace Siemens.Engineering.HmiUnified.UI.Features
{
    public interface IHmiBoxFeature
    {
        uint Height { get; }
        int Left { get; }
        int Top { get; }
        uint Width { get; }
    }
}

namespace Siemens.Engineering.HmiUnified.UI.Shapes
{
    /// <summary>A shape without box geometry (real circles expose a centre and radius instead).</summary>
    public sealed class HmiCircle : HmiScreenItemBase { }

    public sealed class HmiGraphicView : BoxItem { }
}

namespace Siemens.Engineering.HmiUnified.UI.Screens
{
    public sealed class HmiScreenWindow : HmiScreenItemBase
    {
        public string Screen { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiScreen : Common.ValidatableBag
    {
        private readonly HmiScreenItemBaseComposition screenItems = new();
        public HmiScreenItemBaseComposition ScreenItems { get { Check(nameof(ScreenItems)); return screenItems; } }
        public MultilingualText DisplayName { get => Get<MultilingualText>(); set => Set(value); }
        public ushort ScreenNumber { get => Get<ushort>(); set => Set(value); }
        public uint Width { get => Get<uint>(); set => Set(value); }
        public uint Height { get => Get<uint>(); set => Set(value); }

        internal HmiScreenComposition? Owner { get; set; }
        public void Delete() => Owner!.Items.Remove(this);
        public override object GetAttribute(string attributeName) => Base.ReflectedAttributes.Get(this, attributeName);
        public override void SetAttribute(string attributeName, object value) => Base.ReflectedAttributes.Set(this, attributeName, value);
        public override IList<EngineeringAttributeInfo> GetAttributeInfos() => Base.ReflectedAttributes.Infos(this);
    }

    public sealed class HmiScreenComposition : Composition<HmiScreen>
    {
        public new HmiScreen? Find(string name) => Items.FirstOrDefault(x => x.Name == name);

        public HmiScreen Create(string name)
        {
            var screen = new HmiScreen { Name = name, Owner = this };
            Items.Add(screen);
            return screen;
        }
    }
}

namespace Siemens.Engineering.HmiUnified.UI.ScreenGroup
{
    public sealed class HmiScreenGroup : Common.HmiGroupBase
    {
        public HmiScreenGroupComposition Groups { get; } = new();
        public Screens.HmiScreenComposition Screens { get; } = new();
    }

    public sealed class HmiScreenGroupComposition : Composition<HmiScreenGroup> { }
}
