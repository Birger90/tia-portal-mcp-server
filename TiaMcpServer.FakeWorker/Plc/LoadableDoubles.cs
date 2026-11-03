// Stand-ins with the members Siemens.Engineering.Step7.xml (V21) documents for the loadable generation:
// LoadableProvider.GenerateLoadable(FileInfo, IEnumerable<PlcBlock>, TargetOption) and the IEnumerable<PlcUnit> overload, and
// TargetOption { None, Plc, PlcSim } (None "throws an exception"). The return type is not documented; these return nothing.
// The written content is invented test data. Member shapes only; no evidence of Siemens behavior.
namespace Siemens.Engineering.SW.Blocks
{
    public sealed class PlcBlock(string name)
    {
        public string Name { get; } = name;
    }
}

namespace Siemens.Engineering.SW.Units
{
    public sealed class PlcUnit(string name)
    {
        public string Name { get; } = name;
    }
}

namespace Siemens.Engineering.SW.Loader
{
    public enum TargetOption { None, Plc, PlcSim }

    public sealed class LoadableProvider
    {
        public void GenerateLoadable(System.IO.FileInfo path, System.Collections.Generic.IEnumerable<Siemens.Engineering.SW.Blocks.PlcBlock> blocks, TargetOption targetOption)
            => Write(path, targetOption, System.Linq.Enumerable.Select(blocks, block => block.Name));

        public void GenerateLoadable(System.IO.FileInfo path, System.Collections.Generic.IEnumerable<Siemens.Engineering.SW.Units.PlcUnit> units, TargetOption targetOption)
            => Write(path, targetOption, System.Linq.Enumerable.Select(units, unit => unit.Name));

        private static void Write(System.IO.FileInfo path, TargetOption targetOption, System.Collections.Generic.IEnumerable<string> names)
        {
            if (targetOption == TargetOption.None) throw new System.InvalidOperationException("TargetOption.None");
            System.IO.File.WriteAllText(path.FullName, $"FAKE LOADABLE {targetOption}: {string.Join(",", names)}\n");
        }
    }
}
