using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.Connection;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

namespace TiaMcpServer.OpennessReferenceProbe
{
    // Compile-only signature checks for every Siemens member the external-source, group-rename and
    // accessible-device operations bind at compile time. Building this probe against the installed V21
    // PublicAPI is the mechanical check of the stubs. GenerateBlockOption.KeepOnError (no documented value),
    // ConfigurationPcInterface.GetAccessibleDevices() and the ConfigurationAccessibleDevice members (return
    // and member types undocumented) are bound at runtime and deliberately not referenced here; the group
    // renames use the generic IEngineeringObject attribute API. PlcTypeUserGroupComposition.Create(String) is
    // bound at runtime too (tags-types.md lists it without a return type).
    internal static class SourceFolderReferenceSurface
    {
        private static void ExternalSources(PlcSoftware software, PlcBlockUserGroup blockGroup, PlcTypeUserGroup typeGroup, string name, string path)
        {
            PlcExternalSourceSystemGroup group = software.ExternalSourceGroup;
            PlcExternalSourceComposition sources = group.ExternalSources;
            PlcExternalSource created = sources.CreateFromFile(name, path);
            PlcExternalSource found = sources.Find(name);
            string sourceName = found.Name;
            foreach (PlcExternalSource source in sources) { string each = source.Name; }
            IList<IEngineeringObject> root = found.GenerateBlocksFromSource(GenerateBlockOption.None);
            IList<IEngineeringObject> intoBlocks = found.GenerateBlocksFromSource(blockGroup, GenerateBlockOption.None);
            IList<IEngineeringObject> intoTypes = found.GenerateBlocksFromSource(typeGroup, GenerateBlockOption.None);
            found.Delete();
        }

        private static void GenerateSource(PlcExternalSourceSystemGroup group, PlcBlock block, PlcType type, FileInfo file)
        {
            group.GenerateSource(new List<IGenerateSource> { block, type }, file, GenerateOptions.None);
            group.GenerateSource(new List<IGenerateSource> { block }, file, GenerateOptions.WithDependencies);
        }

        private static void Rename(IEngineeringObject group, string newName)
        {
            IList<EngineeringAttributeInfo> infos = group.GetAttributeInfos();
            EngineeringAttributeAccessMode mode = infos[0].AccessMode;
            group.SetAttribute("Name", newName);
            object name = group.GetAttribute("Name");
        }

        private static void Containers(PlcSoftware software, PlcTypeUserGroup typeGroup, PlcUnitComposition units, string name)
        {
            PlcTagTableUserGroup tagGroup = software.TagTableGroup.Groups.Create(name);
            PlcTagTableUserGroup nested = tagGroup.Groups.Create(name);
            PlcTagTableUserGroup foundTagGroup = software.TagTableGroup.Groups.Find(name);
            foundTagGroup.Delete();
            PlcTypeUserGroup foundTypeGroup = typeGroup.Groups.Find(name);
            foundTypeGroup.Delete();
            PlcUnit unit = units.Create(name);
            PlcUnit foundUnit = units.Find(name);
            foundUnit.Delete();
        }

        private static void Scan(ConfigurationMode mode)
        {
            foreach (ConfigurationPcInterface pcInterface in mode.PcInterfaces)
            {
                string name = pcInterface.Name;
                int number = pcInterface.Number;
            }
        }
    }
}
