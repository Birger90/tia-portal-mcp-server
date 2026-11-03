// Stand-ins shaped like the S7-PLCSIM Advanced API, used only to test the reflection binding.
// They are looked up by the same full-name lookup the worker uses against the real assembly,
// with the namespace mapped. They encode no claim about the real API's members.
#pragma warning disable CA1822, CA1050, IDE1006

namespace TiaMcpServer.Tests.PlcSim.FakeApiFull
{
    // Errors negative, warnings positive (the native convention in the Siemens developer reference).
    public enum ERuntimeErrorCode { OK = 0, Timeout = -2, DoesNotExist = -5, NotRunning = -9, AlreadyRunning = 2, LicenseExpiresSoon = 7 }
    public enum EOperatingState { Off = 0, Booting = 1, Stop = 2, Run = 3 }
    public enum EOperatingMode { Default = 0, SingleStep = 1 }
    public enum ECommunicationInterface { None = 0, Softbus = 1, TCPIP = 2 }
    public enum ECPUType { Unspecified = 0, CPU1500_Unspecified = 1 }
    public enum ETagListDetails { None = 0, IO = 1, IOMCTDB = 15 }
    public enum EArea { Input = 1, Output = 2, Marker = 3, DataBlock = 4 }

    public struct SInstanceInfo
    {
        public int ID;
        public string Name;
    }

    public struct STagInfo
    {
        public string Name;
        public EArea Area;
        public uint Offset;
        public byte Bit;
        public Nested Detail;
    }

    public struct Nested
    {
        public int Level;
        public Deeper Deeper;
    }

    public struct Deeper
    {
        public int Value;
        public DeepestEver Deepest;
    }

    public struct DeepestEver
    {
        public int Unreached;
    }

    public class SimulationRuntimeException : Exception
    {
        public SimulationRuntimeException(ERuntimeErrorCode code, string message) : base(message) => RuntimeErrorCode = code;
        public ERuntimeErrorCode RuntimeErrorCode { get; }
    }

    public class SimulationInitializationException : Exception
    {
        public SimulationInitializationException(string message) : base(message) { }
    }

    public interface IInstance : IDisposable
    {
        int ID { get; }
        string Name { get; }
        EOperatingState OperatingState { get; }
        EOperatingMode OperatingMode { get; set; }
        double ScaleFactor { get; set; }
        ECommunicationInterface CommunicationInterface { get; }
        ECPUType CPUType { get; set; }
        STagInfo[] TagInfos { get; }
        ERuntimeErrorCode PowerOn();
        ERuntimeErrorCode PowerOn(uint in_TimeOut);
        void PowerOff();
        void Run(uint in_TimeOut = 60000);
        void Stop();
        ERuntimeErrorCode MemoryReset(uint in_TimeOut);
        void UpdateTagList(ETagListDetails in_TagListDetails = ETagListDetails.IOMCTDB, bool in_IsHMIVisibleOnly = true);
        void GetTagListStatus(ref ETagListDetails out_TagListDetails, ref bool out_IsHMIVisibleOnly);
        bool ReadBool(string in_Tag);
        sbyte ReadInt8(string in_Tag);
        short ReadInt16(string in_Tag);
        int ReadInt32(string in_Tag);
        long ReadInt64(string in_Tag);
        byte ReadUInt8(string in_Tag);
        ushort ReadUInt16(string in_Tag);
        uint ReadUInt32(string in_Tag);
        ulong ReadUInt64(string in_Tag);
        float ReadFloat(string in_Tag);
        double ReadDouble(string in_Tag);
        string ReadString(string in_Tag);
        void WriteBool(string in_Tag, bool in_Value);
        void WriteInt8(string in_Tag, sbyte in_Value);
        void WriteInt16(string in_Tag, short in_Value);
        void WriteInt32(string in_Tag, int in_Value);
        ERuntimeErrorCode WriteInt64(string in_Tag, long in_Value);
        void WriteFloat(string in_Tag, float in_Value);
        bool WriteDouble(string in_Tag, double in_Value);
    }

    /// <summary>Per-test state behind the static manager (AsyncLocal so parallel tests do not interfere).</summary>
    public sealed class FakeApiState
    {
        private static readonly AsyncLocal<FakeApiState?> CurrentState = new();

        public static FakeApiState Current
        {
            get => CurrentState.Value ?? throw new InvalidOperationException("No fake API state.");
            set => CurrentState.Value = value;
        }

        public bool IsInitialized { get; set; } = true;
        public bool ManagerThrows { get; set; }
        /// <summary>The manager's members fail like a failed static initializer (TypeInitializationException).</summary>
        public bool ManagerTypeInitializationFails { get; set; }
        public ERuntimeErrorCode MemoryResetResult { get; set; } = ERuntimeErrorCode.OK;
        public ERuntimeErrorCode WriteInt64Result { get; set; } = ERuntimeErrorCode.OK;
        public bool WriteDoubleResult { get; set; } = true;
        public Dictionary<string, FakeInstance> Instances { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Calls { get; } = new();
    }

    public static class SimulationRuntimeManager
    {
        public static bool IsInitialized => FakeApiState.Current.ManagerThrows
            ? throw new SimulationInitializationException("Runtime Manager is not running.")
            : FailIfUninitialized(FakeApiState.Current.IsInitialized);

        public static bool IsRuntimeManagerAvailable => FailIfUninitialized(true);

        public static SInstanceInfo[] RegisteredInstanceInfo
            => FailIfUninitialized(FakeApiState.Current.Instances.Values.Select(instance => new SInstanceInfo { ID = instance.ID, Name = instance.Name }).ToArray());

        private static T FailIfUninitialized<T>(T value) => FakeApiState.Current.ManagerTypeInitializationFails
            ? throw new TypeInitializationException(typeof(SimulationRuntimeManager).FullName, new SimulationInitializationException("Runtime Manager is not running."))
            : value;

        public static IInstance CreateInterface(string in_InstanceName)
            => FakeApiState.Current.Instances.TryGetValue(in_InstanceName, out var instance)
                ? instance
                : throw new SimulationRuntimeException(ERuntimeErrorCode.DoesNotExist, $"Instance '{in_InstanceName}' does not exist.");

        public static IInstance CreateInterface(int in_InstanceID)
            => FakeApiState.Current.Instances.Values.First(instance => instance.ID == in_InstanceID);
    }

    public sealed class FakeInstance : IInstance
    {
        public int ID { get; set; } = 7;
        public string Name { get; set; } = "Line1";
        public EOperatingState OperatingState { get; set; } = EOperatingState.Off;
        public EOperatingMode OperatingMode { get; set; }
        public double ScaleFactor { get; set; } = 1;
        public ECommunicationInterface CommunicationInterface => ECommunicationInterface.Softbus;
        public ECPUType CPUType { get; set; } = ECPUType.CPU1500_Unspecified;
        public STagInfo[] TagInfos { get; set; } = Array.Empty<STagInfo>();
        public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);
        public List<string> Calls => FakeApiState.Current.Calls;
        public int Disposals { get; private set; }

        public ERuntimeErrorCode PowerOn() { Calls.Add("PowerOn()"); OperatingState = EOperatingState.Stop; return ERuntimeErrorCode.OK; }
        public ERuntimeErrorCode PowerOn(uint in_TimeOut) { Calls.Add($"PowerOn({in_TimeOut})"); OperatingState = EOperatingState.Stop; return ERuntimeErrorCode.OK; }
        public void PowerOff() { Calls.Add("PowerOff()"); OperatingState = EOperatingState.Off; }
        public void Run(uint in_TimeOut = 60000) { Calls.Add($"Run({in_TimeOut})"); OperatingState = EOperatingState.Run; }
        public void Stop() { Calls.Add("Stop()"); throw new SimulationRuntimeException(ERuntimeErrorCode.NotRunning, "The instance is not running."); }
        public ERuntimeErrorCode MemoryReset(uint in_TimeOut) { Calls.Add($"MemoryReset({in_TimeOut})"); return FakeApiState.Current.MemoryResetResult; }
        public void UpdateTagList(ETagListDetails in_TagListDetails = ETagListDetails.IOMCTDB, bool in_IsHMIVisibleOnly = true)
            => Calls.Add($"UpdateTagList({in_TagListDetails}, {in_IsHMIVisibleOnly})");
        public void GetTagListStatus(ref ETagListDetails out_TagListDetails, ref bool out_IsHMIVisibleOnly)
        {
            out_TagListDetails = ETagListDetails.IO;
            out_IsHMIVisibleOnly = true;
        }

        public bool ReadBool(string in_Tag) => (bool)Get(in_Tag);
        public sbyte ReadInt8(string in_Tag) => (sbyte)Get(in_Tag);
        public short ReadInt16(string in_Tag) => (short)Get(in_Tag);
        public int ReadInt32(string in_Tag) => (int)Get(in_Tag);
        public long ReadInt64(string in_Tag) => (long)Get(in_Tag);
        public byte ReadUInt8(string in_Tag) => (byte)Get(in_Tag);
        public ushort ReadUInt16(string in_Tag) => (ushort)Get(in_Tag);
        public uint ReadUInt32(string in_Tag) => (uint)Get(in_Tag);
        public ulong ReadUInt64(string in_Tag) => (ulong)Get(in_Tag);
        public float ReadFloat(string in_Tag) => (float)Get(in_Tag);
        public double ReadDouble(string in_Tag) => (double)Get(in_Tag);
        public string ReadString(string in_Tag) => (string)Get(in_Tag);
        public void WriteBool(string in_Tag, bool in_Value) => Set(in_Tag, in_Value);
        public void WriteInt8(string in_Tag, sbyte in_Value) => Set(in_Tag, in_Value);
        public void WriteInt16(string in_Tag, short in_Value) => Set(in_Tag, in_Value);
        public void WriteInt32(string in_Tag, int in_Value) => Set(in_Tag, in_Value);
        public ERuntimeErrorCode WriteInt64(string in_Tag, long in_Value) { Set(in_Tag, in_Value); return FakeApiState.Current.WriteInt64Result; }
        public void WriteFloat(string in_Tag, float in_Value) => Set(in_Tag, in_Value);
        public bool WriteDouble(string in_Tag, double in_Value) { Set(in_Tag, in_Value); return FakeApiState.Current.WriteDoubleResult; }
        public void Dispose() { Disposals++; Calls.Add("Dispose()"); }

        private object Get(string tag)
            => tag == "TypeInitPoisoned"
                ? throw new TypeInitializationException("Siemens.Simatic.Simulation.Runtime.Native", new SimulationInitializationException("Runtime Manager is not running."))
                : Values.TryGetValue(tag, out var value) ? value : throw new SimulationRuntimeException(ERuntimeErrorCode.DoesNotExist, $"Tag '{tag}' does not exist.");

        private void Set(string tag, object value)
        {
            Calls.Add($"Write {tag}={value}");
            if (!Values.ContainsKey(tag))
                throw new SimulationRuntimeException(ERuntimeErrorCode.DoesNotExist, $"Tag '{tag}' does not exist.");
            Values[tag] = value;
        }
    }
}

namespace TiaMcpServer.Tests.PlcSim.FakeApiSparse
{
    // A deliberately different shape: no PowerOn(UInt32), an ambiguous all-optional Stop (neither overload is UInt32), a
    // ReadInt16 with the wrong return type, a non-string tag name, float ScaleFactor, no
    // Dispose, no error-code member on the exception, and no WriteInt8/MemoryReset at all.
    public enum ERuntimeErrorCode { OK = 0 }
    public enum EOperatingState { Off = 0 }

    public struct STagInfo
    {
        public int Name;
    }

    public class SimulationRuntimeException : Exception
    {
        public SimulationRuntimeException(string message) : base(message) { }
        public int Code => 3;
    }

    public interface IInstance
    {
        string Name { get; }
        EOperatingState OperatingState { get; }
        float ScaleFactor { get; set; }
        STagInfo[] TagInfos { get; }
        void PowerOn(int in_TimeOut = 1000);
        void Stop(int in_A = 1);
        void Stop(long in_B = 2);
        int ReadInt16(string in_Tag);
        bool ReadBool(string in_Tag);
        void WriteBool(string in_Tag, bool in_Value);
    }

    public static class SimulationRuntimeManager
    {
        public static IInstance CreateInterface(string in_InstanceName)
            => in_InstanceName == "boom" ? throw new SimulationRuntimeException("boom") : new FakeInstance(in_InstanceName.ToUpperInvariant());
    }

    public sealed class FakeInstance : IInstance
    {
        public FakeInstance(string name) => Name = name;
        public string Name { get; }
        public EOperatingState OperatingState => EOperatingState.Off;
        public float ScaleFactor { get; set; } = 1;
        public STagInfo[] TagInfos => new[] { new STagInfo { Name = 5 } };
        public void PowerOn(int in_TimeOut = 1000) { }
        public void Stop(int in_A = 1) { }
        public void Stop(long in_B = 2) { }
        public int ReadInt16(string in_Tag) => 1;
        public bool ReadBool(string in_Tag) => true;
        public void WriteBool(string in_Tag, bool in_Value) { }
    }
}

namespace TiaMcpServer.Tests.PlcSim.FakeApiNoOk
{
    // An error-code enum without a member named OK: zero is assumed to be success (with a warning), the sign decides the rest.
    public enum ERuntimeErrorCode { Success = 0, Refused = -1, Notice = 1 }

    public interface IInstance
    {
        string Name { get; }
        ERuntimeErrorCode PowerOn(uint in_TimeOut);
    }

    public static class SimulationRuntimeManager
    {
        public static ERuntimeErrorCode NextPowerOn { get; set; } = ERuntimeErrorCode.Success;

        public static IInstance CreateInterface(string in_InstanceName) => new FakeInstance(in_InstanceName);
    }

    public sealed class FakeInstance : IInstance
    {
        public FakeInstance(string name) => Name = name;
        public string Name { get; }
        public ERuntimeErrorCode PowerOn(uint in_TimeOut) => SimulationRuntimeManager.NextPowerOn;
    }
}

namespace TiaMcpServer.Tests.PlcSim.FakeApiOkNonZero
{
    // OK is not zero: its value, not zero, means success.
    public enum ERuntimeErrorCode { Unknown = 0, OK = 3, Bad = -1, Hint = 5 }

    public interface IInstance
    {
        string Name { get; }
        ERuntimeErrorCode PowerOn(uint in_TimeOut);
    }

    public static class SimulationRuntimeManager
    {
        public static ERuntimeErrorCode NextPowerOn { get; set; } = ERuntimeErrorCode.OK;

        public static IInstance CreateInterface(string in_InstanceName) => new FakeInstance(in_InstanceName);
    }

    public sealed class FakeInstance : IInstance
    {
        public FakeInstance(string name) => Name = name;
        public string Name { get; }
        public ERuntimeErrorCode PowerOn(uint in_TimeOut) => SimulationRuntimeManager.NextPowerOn;
    }
}

namespace TiaMcpServer.Tests.PlcSim.FakeApiUnsigned
{
    // An unsigned error-code enum: no sign separates errors from warnings.
    public enum ERuntimeErrorCode : uint { OK = 0, Busy = 5 }

    public interface IInstance
    {
        string Name { get; }
        ERuntimeErrorCode PowerOn(uint in_TimeOut);
    }

    public static class SimulationRuntimeManager
    {
        public static IInstance CreateInterface(string in_InstanceName) => new FakeInstance(in_InstanceName);
    }

    public sealed class FakeInstance : IInstance
    {
        public FakeInstance(string name) => Name = name;
        public string Name { get; }
        public ERuntimeErrorCode PowerOn(uint in_TimeOut) => ERuntimeErrorCode.Busy;
    }
}
