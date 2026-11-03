using System.Collections.Generic;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Extensions;
using Siemens.Engineering.HW.Utilities;

namespace TiaMcpServer.OpennessReferenceProbe
{
    // Compile-only signature checks for every Siemens member the hardware-module operations bind at
    // compile time. Building this probe against the installed V21 PublicAPI is the mechanical check of
    // the stubs. DeviceItem.ChangeType, SetAttributes(..., AttributeDelegate), Project.HwUtilities and
    // the PlugLocation properties are bound by reflection at runtime because their return types,
    // declaring types or property types are not documented; they are deliberately not referenced here.
    internal static class HardwareModulesReferenceSurface
    {
        private static void Plugging(HardwareObject container, DeviceItem item, string typeIdentifier, string name, int position)
        {
            bool canNew = container.CanPlugNew(typeIdentifier, name, position);
            DeviceItem plugged = container.PlugNew(typeIdentifier, name, position);
            bool canMove = container.CanPlugMove(item, position);
            DeviceItem moved = container.PlugMove(item, position);
            bool canCopy = container.CanPlugCopy(item, position);
            DeviceItem copied = container.PlugCopy(item, position);
            IList<PlugLocation> free = container.GetPlugLocations();
            item.Delete();
        }

        private static void Addresses(Address address)
        {
            int start = address.StartAddress;
            address.StartAddress = start;
        }

        private static void ModuleInformation(ModuleInformationProvider provider, string typeIdentifier)
        {
            string[] containers = provider.FindContainerTypes(typeIdentifier);
            string[] modules = provider.FindModuleTypes(typeIdentifier);
        }

        // move_tag copies the multilingual comment: PlcTag.Comment (tags-types.md) and the
        // MultilingualText model (language-settings.md §5: Items, Find(Language), Language, Text).
        private static void TagComment(Siemens.Engineering.SW.Tags.PlcTag tag, Siemens.Engineering.Language language)
        {
            Siemens.Engineering.MultilingualText comment = tag.Comment;
            foreach (Siemens.Engineering.MultilingualTextItem item in comment.Items)
            {
                Siemens.Engineering.Language itemLanguage = item.Language;
                string text = item.Text;
                item.Text = text;
            }

            Siemens.Engineering.MultilingualTextItem found = comment.Items.Find(language);
        }
    }
}
