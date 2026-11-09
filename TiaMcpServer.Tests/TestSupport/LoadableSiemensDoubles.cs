// Stand-ins of the documented loadable generation members (Siemens.Engineering.Step7.xml, V21), named exactly as
// PlcLoadableRun binds them (PlcBlock and PlcUnit are the existing test doubles). The return type is undocumented; these
// return nothing. Shapes only.
namespace Siemens.Engineering.SW.Loader
{
    public enum TargetOption { None, Plc, PlcSim }

    public sealed class LoadableProvider
    {
        public void GenerateLoadable(System.IO.FileInfo path, System.Collections.Generic.IEnumerable<Siemens.Engineering.SW.Blocks.PlcBlock> blocks, TargetOption targetOption)
            => System.IO.File.WriteAllText(path.FullName, "blocks " + targetOption);

        public void GenerateLoadable(System.IO.FileInfo path, System.Collections.Generic.IEnumerable<Siemens.Engineering.SW.Units.PlcUnit> units, TargetOption targetOption)
            => System.IO.File.WriteAllText(path.FullName, "units " + targetOption);
    }
}
