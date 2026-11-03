using TiaMcpServer.Contracts.Library;
using Xunit;

namespace TiaMcpServer.Tests.Library;

/// <summary>The Siemens-free selector, path and name rules of the library tools.</summary>
public sealed class LibraryPolicyTests
{
    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("/", new string[0])]
    [InlineData("/Motors", new[] { "Motors" })]
    [InlineData("Motors/Legacy", new[] { "Motors", "Legacy" })]
    [InlineData("/Motors/Legacy/", new[] { "Motors", "Legacy" })]
    public void FolderPaths_Parse(string? path, string[] expected)
    {
        Assert.Equal(expected, LibraryPolicy.ParseFolderPath(path, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("/A//B")]
    [InlineData("/ A")]
    [InlineData("/A\tB")]
    public void FolderPaths_RejectEmptyPaddedOrControlSegments(string path)
    {
        Assert.Null(LibraryPolicy.ParseFolderPath(path, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TypePaths_SplitFolderAndName()
    {
        var parsed = LibraryPolicy.ParseTypePath("/Motors/PumpControl", out _)!.Value;
        Assert.Equal(new[] { "Motors" }, parsed.Folder);
        Assert.Equal("PumpControl", parsed.Name);
        Assert.Equal("PumpControl", LibraryPolicy.ParseTypePath("PumpControl", out _)!.Value.Name);
        Assert.Null(LibraryPolicy.ParseTypePath("/", out var error));
        Assert.Contains("names a type", error);
        Assert.Equal("/Motors/PumpControl", LibraryPolicy.FormatPath(new[] { "Motors", "PumpControl" }));
        Assert.Equal("/", LibraryPolicy.FormatPath(Array.Empty<string>()));
    }

    [Theory]
    [InlineData(@"C:\Libraries\Plant.al21", true)]
    [InlineData(@"C:/Libraries/Plant.al21", true)]
    [InlineData(@"\\server\share\Plant.al21", true)]
    [InlineData(@"Plant.al21", false)]
    [InlineData(@"\Libraries\Plant.al21", false)]
    [InlineData(@"\\?\C:\Plant.al21", false)]
    [InlineData(@"C:\Libraries\..\Plant.al21", false)]
    [InlineData(@"C:\Libraries\Plant.al21:stream", false)]
    [InlineData(@" C:\Plant.al21", false)]
    public void FilePaths_AreAbsoluteWindowsPaths(string path, bool valid)
        => Assert.Equal(valid, LibraryPolicy.FilePathError(path) is null);

    [Theory]
    [InlineData(@"\\.\COM1")]
    [InlineData(@"\\./COM1")]
    [InlineData(@"\\?\C:\Plant.al21")]
    [InlineData(@"\\?/UNC/server/share/Plant.al21")]
    [InlineData(@"//./COM1")]
    [InlineData(@"//?/C:/Plant.al21")]
    [InlineData(@"\/./COM1")]
    [InlineData(@"\\.")]
    [InlineData(@"\\?")]
    public void FilePaths_RefuseDeviceAndExtendedPaths_WithEitherSeparator(string path)
    {
        // Consistent with the hardware-module and Web API path rules: \\?\ and \\.\ prefixes are refused in their forward-slash forms too.
        Assert.True(LibraryPolicy.IsDevicePath(path));
        Assert.Contains("device or extended-length path", LibraryPolicy.FilePathError(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"\\server\share\Plant.al21")]
    [InlineData(@"\\.server\share\Plant.al21")]
    [InlineData(@"C:\Libraries\Plant.al21")]
    public void FilePaths_KeepUncSharesAndDrivePaths(string path)
    {
        Assert.False(LibraryPolicy.IsDevicePath(path));
        Assert.Null(LibraryPolicy.FilePathError(path));
    }

    [Theory]
    [InlineData(@"..\..\Temp\Evil", "is not allowed")]
    [InlineData(@"Sub\Lib", "'\\' is not allowed")]
    [InlineData("Sub/Lib", "must not contain '/'")]
    [InlineData("C:Lib", "':' is not allowed")]
    [InlineData("Lib:stream", "':' is not allowed")]
    [InlineData("..", "must not contain '..'")]
    [InlineData("Lib..v2", "must not contain '..'")]
    [InlineData("Lib.", "must not end with '.'")]
    [InlineData("Lib*", "'*' is not allowed")]
    [InlineData("Lib?", "'?' is not allowed")]
    [InlineData("Lib<1>", "'<' is not allowed")]
    [InlineData("Lib|1", "'|' is not allowed")]
    [InlineData("Lib\"1\"", "'\"' is not allowed")]
    [InlineData("Lib\u0001", "control characters")]
    [InlineData("CON", "reserved Windows device name 'CON'")]
    [InlineData("nul.lib", "reserved Windows device name 'nul'")]
    [InlineData("COM1", "reserved Windows device name 'COM1'")]
    [InlineData(" Lib", "whitespace")]
    public void LibraryFileNames_FollowWindowsFileNameRules(string name, string fragment)
        => Assert.Contains(fragment, LibraryPolicy.LibraryFileNameError(name), StringComparison.Ordinal);

    [Theory]
    [InlineData("Plant")]
    [InlineData("Plant Library 2.0")]
    [InlineData("Größe_温度")]
    [InlineData("CONVEYOR")]
    [InlineData("COM10")]
    public void LibraryFileNames_AcceptOrdinaryNames(string name)
        => Assert.Null(LibraryPolicy.LibraryFileNameError(name));

    [Fact]
    public void WindowsInvalidFileNameChars_AreAFixedWindowsSet_IndependentOfTheHostOs()
    {
        Assert.Equal(41, LibraryPolicy.WindowsInvalidFileNameChars.Count);
        foreach (var character in new[] { '"', '<', '>', '|', ':', '*', '?', '\\', '/', '\0', '\u001F' })
            Assert.Contains(character, LibraryPolicy.WindowsInvalidFileNameChars);
        // Linux reports only '\0' and '/': the set must not depend on it.
        Assert.Superset(new HashSet<char>(Path.GetInvalidFileNameChars()), new HashSet<char>(LibraryPolicy.WindowsInvalidFileNameChars));
        // Object names (types, folders, master copies, newName) are not file names and keep the plain name rules.
        Assert.Null(LibraryPolicy.NameError("Motor:Control"));
    }

    [Theory]
    [InlineData(@"C:\A\Plant.zal21", true)]
    [InlineData(@"C:\A\Plant.ZAL19", true)]
    [InlineData(@"C:\A\Plant.al21", false)]
    [InlineData(@"C:\A.zal\Plant.al21", false)]
    public void Archives_AreRecognizedByExtension(string path, bool archive)
        => Assert.Equal(archive, LibraryPolicy.IsArchive(path));

    [Fact]
    public void Selectors_CompareProjectExactly_AndPathsCaseInsensitively()
    {
        Assert.True(LibraryPolicy.IsProject("project"));
        Assert.False(LibraryPolicy.IsProject("Project"));
        Assert.True(LibraryPolicy.SamePath(@"C:\Libraries\Plant\Plant.al21", @"c:/libraries/plant/PLANT.al21"));
        Assert.True(LibraryPolicy.SamePath(@"C:\Libraries\", @"C:\Libraries"));
        Assert.False(LibraryPolicy.SamePath(@"C:\Libraries\Plant.al21", null));
    }

    [Theory]
    [InlineData("2.0", true)]
    [InlineData("2.0.0", true)]
    [InlineData("1.2.3.4", true)]
    [InlineData("2", false)]
    [InlineData("2.0.0.0.0", false)]
    [InlineData(" 2.0", false)]
    [InlineData("v2.0", false)]
    public void VersionNumbers_AreSystemVersionTexts(string text, bool valid)
        => Assert.Equal(valid, LibraryPolicy.IsVersionNumber(text));

    [Theory]
    [InlineData("2.0.0", "2.0.0", true)]
    [InlineData("2.0.0", "2.0", true)]
    [InlineData("2.0", "2.0.0.0", true)]
    [InlineData("2.0.1", "2.0", false)]
    [InlineData("2.1.0", "2.0.0", false)]
    [InlineData("V2.0", "V2.0", true)]
    [InlineData("V2.0", "2.0", false)]
    [InlineData(null, "2.0", false)]
    public void VersionNumbers_ReadBackCompareAsVersionsWithMissingComponentsZero(string? actual, string requested, bool same)
        => Assert.Equal(same, LibraryPolicy.SameVersionNumber(actual, requested));

    [Fact]
    public void Names_PagesAndTypeNames()
    {
        Assert.Null(LibraryPolicy.NameError("Valve_1"));
        Assert.NotNull(LibraryPolicy.NameError(" Valve"));
        Assert.NotNull(LibraryPolicy.NameError(new string('x', LibraryPolicy.MaxNameLength + 1)));
        Assert.Equal(new[] { 2, 3 }, LibraryPolicy.Page(new[] { 1, 2, 3, 4 }, 1, 2));
        Assert.Empty(LibraryPolicy.Page(new[] { 1 }, 5, 2));
        Assert.Equal("FB", LibraryPolicy.ShortTypeName("Siemens.Engineering.SW.Blocks.FB"));
        Assert.Equal("Device", LibraryPolicy.ShortTypeName("Device"));
    }

    [Fact]
    public void TargetKinds_FollowTheDocumentedCreateFromOverloads()
    {
        Assert.True(LibraryPolicy.TargetKinds.TakesMasterCopyMode(LibraryPolicy.TargetKinds.TechnologyObjects));
        Assert.True(LibraryPolicy.TargetKinds.TakesMasterCopyMode(LibraryPolicy.TargetKinds.SoftwareUnits));
        Assert.False(LibraryPolicy.TargetKinds.HasFolder(LibraryPolicy.TargetKinds.SoftwareUnits));
        Assert.False(LibraryPolicy.TargetKinds.TakesMasterCopyMode(LibraryPolicy.TargetKinds.Devices));
        Assert.False(LibraryPolicy.TargetKinds.TakesMasterCopyMode(LibraryPolicy.TargetKinds.DeviceItem));
        Assert.Equal(new[] { "blockGroup", "typeGroup" }, LibraryPolicy.TargetKinds.TypeVersionTargets);
        Assert.Equal(new[] { "ReadOnly", "ReadWrite" }, LibraryPolicy.OpenModes);
        Assert.Equal(new[] { "ThrowIfExists", "Rename", "Replace" }, LibraryPolicy.MasterCopyModes);
    }

    [Theory]
    [InlineData(@"C:\Libraries\NUL")]
    [InlineData(@"C:\Libraries\nul.al21")]
    [InlineData(@"C:\CON\Plant.al21")]
    [InlineData(@"C:\Libraries\PRN.txt")]
    [InlineData(@"C:\Libraries\AUX")]
    [InlineData(@"C:\Libraries\COM1.al21")]
    [InlineData(@"C:\Libraries\com9")]
    [InlineData(@"C:\Libraries\LPT1.zal21")]
    [InlineData(@"\\server\share\LPT9.al21")]
    public void FilePaths_RefuseReservedDeviceNames_WithAnyExtension(string path)
        => Assert.Contains("reserved Windows device name", LibraryPolicy.FilePathError(path), StringComparison.Ordinal);

    [Theory]
    [InlineData(@"C:\Libraries\Nullable.al21")]
    [InlineData(@"C:\Libraries\Console\Plant.al21")]
    [InlineData(@"C:\Libraries\COM10.al21")]
    public void FilePaths_KeepNamesThatOnlyStartLikeADevice(string path)
        => Assert.Null(LibraryPolicy.FilePathError(path));

    [Theory]
    [InlineData("General", "AutoAcceptCertificates")]
    [InlineData("Online", "AllowLegacyCommunication")]
    [InlineData("General", "TlsVersion")]
    [InlineData("Communication", "Option")]
    [InlineData("General", "EncryptionMode")]
    [InlineData("General", "TrustedHosts")]
    [InlineData("General", "OpennessAccess")]
    public void SecurityWords_CoverTrustAcceptAllowLegacyCertificateTlsEncryptionCommunication(string folder, string name)
        => Assert.True(LibraryPolicy.IsSecurityRelevantSetting(folder, name));

    [Fact]
    public void ResolvedClassification_FailsSafe_AndAMismatchIsRefused()
    {
        Assert.True(LibraryPolicy.IsSecurityRelevantResolved("General", "ShowStartView", anyNameUnreadable: true));
        Assert.True(LibraryPolicy.IsSecurityRelevantResolved("General", null, anyNameUnreadable: false));
        Assert.False(LibraryPolicy.IsSecurityRelevantResolved("General", "ShowStartView", anyNameUnreadable: false));
        Assert.Null(LibraryPolicy.PortalSettingClassificationMismatch(false, false, "x"));
        Assert.Null(LibraryPolicy.PortalSettingClassificationMismatch(true, true, "x"));
        Assert.Contains("Nothing was changed", LibraryPolicy.PortalSettingClassificationMismatch(false, true, "x"));
        Assert.Contains("Nothing was changed", LibraryPolicy.PortalSettingClassificationMismatch(true, false, "x"));
        Assert.Contains("unclassified", LibraryPolicy.PortalSettingClassificationMismatch(null, false, "x"));
    }

    [Fact]
    public void WriteDomain_ClassifiesByTheResolvedNames_NotTheCallerText()
    {
        var item = new TiaMcpServer.Library.LibraryOperationRequest { OperationId = "s", Operation = LibraryMethods.SetPortalSetting, SettingsFolderPath = "Display", SettingName = "Opt", SettingValue = "true" };
        var resolved = new LibraryPlanInfo { Operation = item.Operation, Target = "t", Setting = new PortalSettingInfo { FolderPath = "General/Accept", Name = "AutoAcceptCertificates" } };
        Assert.True(TiaMcpServer.Library.LibraryWriteDomain.IsSecurityRelevant(resolved));
        Assert.Contains(TiaMcpServer.Library.LibraryWriteDomain.Guards(item, resolved), g => g.Id == TiaMcpServer.Library.LibraryGuardDefinitions.PortalSecuritySetting);
        var plainItem = new TiaMcpServer.Library.LibraryOperationRequest { OperationId = "s", Operation = LibraryMethods.SetPortalSetting, SettingsFolderPath = "General/Security", SettingName = "CertificateCheck", SettingValue = "true" };
        var plain = new LibraryPlanInfo { Operation = item.Operation, Target = "t", Setting = new PortalSettingInfo { FolderPath = "General", Name = "ShowStartView" } };
        Assert.False(TiaMcpServer.Library.LibraryWriteDomain.IsSecurityRelevant(plain));
        Assert.DoesNotContain(TiaMcpServer.Library.LibraryWriteDomain.Guards(plainItem, plain), g => g.Id == TiaMcpServer.Library.LibraryGuardDefinitions.PortalSecuritySetting);
        // Without the resolved setting the item is security-relevant (fail safe).
        Assert.True(TiaMcpServer.Library.LibraryWriteDomain.IsSecurityRelevant(new LibraryPlanInfo { Operation = item.Operation, Target = "t" }));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void BindingCandidates_AreOnlyProjectsThatReadPrimary(bool? isPrimary, bool candidate)
        // Security review U3: an unreadable IsPrimary is treated like a secondary project for binding.
        => Assert.Equal(candidate, LibraryPolicy.IsBindingCandidate(isPrimary));
}
