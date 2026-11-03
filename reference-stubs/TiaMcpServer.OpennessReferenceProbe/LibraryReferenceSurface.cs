using System;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;

namespace TiaMcpServer.OpennessReferenceProbe
{
    // Compile-only signature checks for every Siemens member the library_read/library_write operations bind at
    // compile time. Building this probe against the installed V21 PublicAPI is the mechanical check of the
    // stubs. Members whose return, declaring or collection parameter types the sources do not document
    // (GetGlobalLibraryInfos, Open, OpenWithUpgrade, Retrieve, Create<T>, Save, Close, UpdateCheck,
    // CompareToLibrary, UpdateProject, Release, Discard, SetAsDefault, LibraryTypeVersion.Delete,
    // MasterCopy.Delete, FindInstances, the target compositions' CreateFrom, LibraryTypeFolder.Folders/Types,
    // LibraryType.Versions and the version/info/message/compare-element properties) are bound by exact
    // runtime lookup in the worker; they are deliberately not referenced here.
    internal static class LibraryReferenceSurface
    {
        private static void Libraries(TiaPortal portal, Project project, Guid guid, GlobalLibrary global, UserGlobalLibrary user,
            SystemGlobalLibrary system, CorporateGlobalLibrary corporate, LibraryTypeUserFolder userFolder)
        {
            GlobalLibraryComposition globals = portal.GlobalLibraries;
            ILibrary library = project.ProjectLibrary;
            LibraryTypeSystemFolder typeRoot = library.TypeFolder;
            MasterCopySystemFolder masterCopyRoot = library.MasterCopyFolder;
            LibraryType type = library.FindType(guid);
            LibraryTypeVersion version = library.FindVersion(guid);
            LibraryTypeFolder rootAsFolder = typeRoot;
            LibraryTypeFolder userAsFolder = userFolder;
            ILibrary globalAsLibrary = global;
            GlobalLibrary userAsGlobal = user;
            GlobalLibrary systemAsGlobal = system;
            GlobalLibrary corporateAsGlobal = corporate;
        }

        private static void Types(LibraryType type, LibraryTypeVersion version, CodeBlockLibraryType codeBlockType, PlcTypeLibraryType plcType,
            CodeBlockLibraryTypeVersion codeBlockVersion, PlcTypeLibraryTypeVersion plcTypeVersion)
        {
            Guid typeGuid = type.Guid;
            string name = type.Name;
            Guid versionGuid = version.Guid;
            LibraryTypeVersionState state = version.State;
            Func<LibraryTypeVersion> edit = version.Edit;
            LibraryType codeBlockAsType = codeBlockType;
            LibraryType plcTypeAsType = plcType;
            LibraryTypeVersion codeBlockAsVersion = codeBlockVersion;
            LibraryTypeVersion plcTypeAsVersion = plcTypeVersion;
        }

        private static void MasterCopies(MasterCopyComposition masterCopies, MasterCopy masterCopy, IMasterCopySource source)
        {
            string name = masterCopy.Name;
            Func<IMasterCopySource, MasterCopy> create = masterCopies.Create;
            Func<MasterCopy, MasterCopy> createFrom = masterCopies.CreateFrom;
            Func<string, MasterCopy> find = masterCopies.Find;
        }

        private static void Instances(PlcBlock block, PlcType type)
        {
            // G calls GetService<LibraryTypeInstanceInfo>() on blocks and types; the worker calls it through the
            // IEngineeringServiceProvider interface the stubs implement explicitly.
            LibraryTypeInstanceInfo blockInfo = ((IEngineeringServiceProvider)block).GetService<LibraryTypeInstanceInfo>();
            LibraryTypeInstanceInfo typeInfo = ((IEngineeringServiceProvider)type).GetService<LibraryTypeInstanceInfo>();
        }
    }
}
