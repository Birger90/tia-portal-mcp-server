using System.Text.RegularExpressions;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using Xunit;
using static TiaMcpServer.Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy;
using Evidence = TiaMcpServer.Contracts.Plc.TechnologyObjects.TechnologyObjectConnectionEvidence;

namespace TiaMcpServer.Tests.Plc.TechnologyObjects;

/// <summary>The Siemens-free connect evidence, endpoint selection, overload, import-return and caller-file rules the worker uses.</summary>
public sealed class TechnologyObjectEvidenceTests
{
    private static TechnologyObjectRequestInfo Connect(string connectionInterface, string kind, Action<TechnologyObjectRequestInfo>? configure = null)
    {
        var request = new TechnologyObjectRequestInfo { ConnectionInterface = connectionInterface, ConnectionKind = kind };
        configure?.Invoke(request);
        return request;
    }

    public static TheoryData<string, string, string[]> ExpectedProperties => new()
    {
        { Interfaces.Actor, ConnectionKinds.Address, new[] { "InputAddress", "OutputAddress", "ConnectOption" } },
        { Interfaces.Torque, ConnectionKinds.Address, new[] { "InputAddress", "OutputAddress", "ConnectOption" } },
        { Interfaces.MeasuringInput, ConnectionKinds.Address, new[] { "InputAddress" } },
        { Interfaces.OutputCam, ConnectionKinds.Address, new[] { "OutputAddress" } },
        { Interfaces.Sensor, ConnectionKinds.Module, new[] { "InputOutputModule" } },
        { Interfaces.MeasuringInput, ConnectionKinds.Module, new[] { "InputModule", "ChannelIndex" } },
        { Interfaces.Ident, ConnectionKinds.Module, new[] { "ConnectedIdentDevice" } },
        { Interfaces.Actor, ConnectionKinds.Modules, new[] { "InputModule", "OutputModule" } },
        { Interfaces.OutputCam, ConnectionKinds.Channel, new[] { "Channel" } },
        { Interfaces.Actor, ConnectionKinds.Tag, new[] { "OutputTag" } },
        { Interfaces.Torque, ConnectionKinds.DbMember, new[] { "PathToDBMember" } },
        { Interfaces.Torque, ConnectionKinds.Tag, Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(ExpectedProperties))]
    public void EveryConnectNamesTheDocumentedTargetProperty(string connectionInterface, string kind, string[] properties)
    {
        var expectations = Evidence.Expectations(Connect(connectionInterface, kind));
        Assert.Equal(properties, expectations.Select(e => e.Property));
        Assert.All(expectations, e => Assert.Contains(e.Property, PropertiesFor(connectionInterface)));
    }

    [Fact]
    public void TheConnectOptionPassedIsReadBackWhereTheInterfaceDocumentsIt()
    {
        var modules = Evidence.Expectations(Connect(Interfaces.Torque, ConnectionKinds.Modules, r => r.ConnectOption = "Telegram"));
        Assert.Equal(new[] { "InputModule", "OutputModule", "ConnectOption" }, modules.Select(e => e.Property));
        Assert.Equal("ConnectOption=Telegram", modules[2].Requested);
        Assert.Equal("ConnectOption=Default", Evidence.Expectations(Connect(Interfaces.Actor, ConnectionKinds.Address))[2].Requested);
        var option = modules[2];
        Assert.True(Evidence.Matches(option, Endpoint(Interfaces.Torque, true, Property("ConnectOption", "enum", "Telegram"))));
        Assert.False(Evidence.Matches(option, Endpoint(Interfaces.Torque, true, Property("ConnectOption", "enum", "Default"))));
    }

    private static TechnologyObjectConnectionInterfaceInfo Endpoint(string connectionInterface, bool? connected, params TechnologyObjectConnectionPropertyInfo[] properties)
    {
        var endpoint = new TechnologyObjectConnectionInterfaceInfo { Interface = connectionInterface, Provider = "P", IsConnected = connected };
        endpoint.Properties.AddRange(properties);
        return endpoint;
    }

    private static TechnologyObjectConnectionPropertyInfo Property(string name, string kind, string? value, params string[] modulePath)
        => new() { Name = name, Kind = kind, Value = value, ModulePath = modulePath.Length == 0 ? null : modulePath.ToList() };

    [Fact]
    public void RenderedPropertiesMatchTheRequestedTarget()
    {
        var address = Evidence.Expectations(Connect(Interfaces.Actor, ConnectionKinds.Address, r => (r.InputAddress, r.OutputAddress) = (2048, 2056)));
        var after = Endpoint(Interfaces.Actor, true, Property("InputAddress", "integer", "2048"), Property("OutputAddress", "integer", "2048"));
        var observed = Evidence.Observe(address, after);
        Assert.Equal(new bool?[] { true, false, null }, observed.Select(o => o.Match));
        Assert.Equal("OutputAddress=2048", observed[1].Shown);
        Assert.Equal("OutputAddress=2056", observed[1].Expectation.Requested);

        var module = Evidence.Expectations(Connect(Interfaces.Sensor, ConnectionKinds.Module, r => r.ModulePath = new() { "Drive_1", "Module_1" })).Single();
        Assert.True(Evidence.Matches(module, Endpoint(Interfaces.Sensor, true, Property("InputOutputModule", "deviceItem", "Module_1", "Drive_1", "Module_1"))));
        Assert.False(Evidence.Matches(module, Endpoint(Interfaces.Sensor, true, Property("InputOutputModule", "deviceItem", "Module_1", "Drive_2", "Module_1"))));
        Assert.False(Evidence.Matches(module, Endpoint(Interfaces.Sensor, true, Property("InputOutputModule", "null", null))));
        Assert.Null(Evidence.Matches(module, Endpoint(Interfaces.Sensor, true)));

        var channel = Evidence.Expectations(Connect(Interfaces.Actor, ConnectionKinds.Channel, r =>
        {
            r.ModulePath = new() { "Drive_1", "Module_1" };
            r.ChannelNumber = 3;
            r.ChannelIoType = "Output";
        })).Single();
        Assert.True(Evidence.Matches(channel, Endpoint(Interfaces.Actor, true, Property("Channel", "channel", "Analog Output 3", "Drive_1", "Module_1"))));
        Assert.False(Evidence.Matches(channel, Endpoint(Interfaces.Actor, true, Property("Channel", "channel", "Analog Input 3", "Drive_1", "Module_1"))));
        Assert.False(Evidence.Matches(channel, Endpoint(Interfaces.Actor, true, Property("Channel", "channel", "Analog Output 4", "Drive_1", "Module_1"))));

        var tag = Evidence.Expectations(Connect(Interfaces.OutputCam, ConnectionKinds.Tag, r => (r.TableName, r.TagName) = ("T", "Cam_Out"))).Single();
        Assert.True(Evidence.Matches(tag, Endpoint(Interfaces.OutputCam, true, Property("OutputTag", "plcTag", "cam_out"))));
        var member = Evidence.Expectations(Connect(Interfaces.Actor, ConnectionKinds.DbMember, r => r.DbMemberPath = "\"Drive\".Telegram")).Single();
        Assert.False(Evidence.Matches(member, Endpoint(Interfaces.Actor, true, Property("PathToDBMember", "string", "\"drive\".Telegram"))));
    }

    [Fact]
    public void IdentStateIsWhetherConnectedIdentDeviceReadsADevice()
    {
        Assert.False(Evidence.Connected(Endpoint(Interfaces.Ident, null, Property("ConnectedIdentDevice", "null", null))));
        Assert.True(Evidence.Connected(Endpoint(Interfaces.Ident, null, Property("ConnectedIdentDevice", "deviceItem", "Head_1", "Reader_1", "Head_1"))));
        Assert.Null(Evidence.Connected(Endpoint(Interfaces.Ident, null)));
        Assert.True(Evidence.Connected(Endpoint(Interfaces.Actor, true)));
    }

    private static Evidence.Observation Seen(string property, bool? match)
        => new(new(property, Evidence.TargetKinds.Number, null, 8, null, null, null, null, null), match, property + (match is null ? " unreadable" : "=0"));

    [Fact]
    public void ConnectIsEvidencedOnlyByTheRequestedTargetOrAFalseToTrueChange()
    {
        var warnings = new List<string>();
        Assert.Null(Evidence.ConnectFailure(Interfaces.Actor, "C", "Axis_1", true, true, new[] { Seen("InputAddress", true) }, warnings));
        Assert.Contains("IsConnected=false", Evidence.ConnectFailure(Interfaces.Actor, "C", "Axis_1", false, false, new[] { Seen("InputAddress", true) }, warnings));
        Assert.Contains("instead of InputAddress=8", Evidence.ConnectFailure(Interfaces.Actor, "C", "Axis_1", false, true, new[] { Seen("InputAddress", false) }, warnings));
        Assert.Empty(warnings);

        // An unreadable target property: accepted with a warning only after a false-to-true change.
        Assert.Null(Evidence.ConnectFailure(Interfaces.Actor, "C", "Axis_1", false, true, new[] { Seen("InputAddress", null) }, warnings));
        Assert.Contains("not verified", Assert.Single(warnings));
        Assert.Contains("already connected", Evidence.ConnectFailure(Interfaces.Actor, "C", "Axis_1", true, true, new[] { Seen("InputAddress", null) }, warnings));
        Assert.Contains("unknown state", Evidence.ConnectFailure(Interfaces.Actor, "C", "Axis_1", null, true, new[] { Seen("InputAddress", null) }, warnings));
        Assert.Contains("ident has no other evidence", Evidence.ConnectFailure(Interfaces.Ident, "C", "Ident_1", false, true, new[] { Seen("ConnectedIdentDevice", null) }, warnings));
        Assert.Contains("ConnectedIdentDevice", Evidence.ConnectFailure(Interfaces.Ident, "C", "Ident_1", false, false, Array.Empty<Evidence.Observation>(), warnings));
    }

    [Fact]
    public void EndpointSelectionIsExactAndFailsBeforeAnyCall()
    {
        var offered = new List<(string Interface, string Provider, int? Index, string Target)>
        {
            (Interfaces.Actor, "Axis", null, "actor"), (Interfaces.Sensor, "Axis", 0, "s0"), (Interfaces.Sensor, "Axis", 1, "s1"),
        };
        Assert.Equal("actor", SelectEndpoint(offered, Interfaces.Actor, null).Endpoint);
        Assert.Equal("s1", SelectEndpoint(offered, Interfaces.Sensor, 1).Endpoint);
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, SelectEndpoint(offered, Interfaces.Sensor, null).Category);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, SelectEndpoint(offered, Interfaces.Sensor, 2).Category);
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, SelectEndpoint(offered, Interfaces.Torque, null).Category);
        Assert.Equal(WorkerFailureCategories.ValidationError, SelectEndpoint(offered, "encoder", null).Category);
    }

    [Fact]
    public void OverloadsFollowTheDocumentedSignatures()
    {
        Assert.Equal(("Int32, Int32, ConnectOption", 3), Shape(Overload(Interfaces.Actor, ConnectionKinds.Address, connectOption: false)));
        Assert.Equal(("Int32", 1), Shape(Overload(Interfaces.MeasuringInput, ConnectionKinds.Address, connectOption: false)));
        Assert.Equal(new[] { ConnectArguments.OutputAddress }, Overload(Interfaces.OutputCam, ConnectionKinds.Address, false)!.Value.Arguments);
        Assert.Equal(("DeviceItem, Int32", 2), Shape(Overload(Interfaces.MeasuringInput, ConnectionKinds.Module, false)));
        Assert.Equal(("DeviceItem, DeviceItem", 2), Shape(Overload(Interfaces.Torque, ConnectionKinds.Modules, false)));
        Assert.Equal(("DeviceItem, DeviceItem, ConnectOption", 3), Shape(Overload(Interfaces.Torque, ConnectionKinds.Modules, true)));
        Assert.Equal(("String", 1), Shape(Overload(Interfaces.Actor, ConnectionKinds.DbMember, false)));
        Assert.Null(Overload(Interfaces.Torque, ConnectionKinds.Channel, false));
        Assert.Null(Overload(Interfaces.Ident, ConnectionKinds.Address, false));
        static (string, int) Shape((IReadOnlyList<string> Arguments, string Signature)? overload) => (overload!.Value.Signature, overload.Value.Arguments.Count);
    }

    [Fact]
    public void ImportReturnNamesAreUsedOnlyWhenEveryElementIsATechnologyObject()
    {
        static string? Name(object item) => item is string text && text.StartsWith("TO:", StringComparison.Ordinal) ? text[3..] : null;
        Assert.Null(ReturnedNames(null, Name));
        Assert.Null(ReturnedNames("TO:Axis_1", Name));
        Assert.Null(ReturnedNames(42, Name));
        Assert.Null(ReturnedNames(new object[] { "TO:Axis_1", "other" }, Name));
        Assert.Equal(new[] { "Axis_1", "Axis_2" }, ReturnedNames(new object[] { "TO:Axis_1", "TO:Axis_2" }, Name));
        Assert.Empty(ReturnedNames(Array.Empty<object>(), Name)!);
    }

    [Fact]
    public void CallerFilesResolveOnlyToTheRequestedPath()
    {
        Assert.Equal((@"C:\Exports\Axis_1.xml", (string?)null), ResolveLocalFile("C:/Exports/Axis_1.xml", NormalizeLocalFilePath));
        Assert.Contains("resolves to", ResolveLocalFile(@"C:\PROGRA~1\Axis_1.xml", _ => @"C:\Program Files\Axis_1.xml").Refusal);
        Assert.Contains("not a valid path", ResolveLocalFile(@"C:\Exports\Axis_1.xml", _ => throw new PathTooLongException("too long")).Refusal);
        Assert.Null(ResolveLocalFile(@"\\server\share\Axis_1.xml", p => p).Refusal);
        Assert.Contains("normalization", ResolveLocalFile(@"C:\Exports\Axis_1.xml", _ => @"\\?\C:\Exports\Axis_1.xml").Refusal);
    }

    [Fact]
    public void NetworkDrivesAndReparsePointsAreWarningsNeverRefusals()
    {
        Assert.Empty(LocalFileWarnings(@"C:\Exports\Axis_1.xml", _ => false, _ => false));
        Assert.Empty(LocalFileWarnings(@"C:\Exports\Axis_1.xml", _ => null, _ => null));
        Assert.Contains(@"network drive (N:\)", Assert.Single(LocalFileWarnings(@"N:\Exports\Axis_1.xml", root => root == @"N:\", _ => false)));
        var reparse = Assert.Single(LocalFileWarnings(@"C:\Link\Sub\Axis_1.xml", _ => false, directory => directory == @"C:\Link"));
        Assert.Contains(@"'C:\Link' is a reparse point", reparse);
        Assert.Equal(3, LocalFileWarnings(@"N:\Link\Axis_1.xml", _ => true, _ => true).Count);
        Assert.Contains("is itself a reparse point", Assert.Single(LocalFileWarnings(@"C:\Exports\Axis_1.xml", _ => false, path => path == @"C:\Exports\Axis_1.xml")));
    }

    [Fact]
    public void ExportsArePlacedThroughAStagedFileAndHashChecked()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-to-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "Axis_1.xml");
            var first = System.Text.Encoding.UTF8.GetBytes("<Document>1</Document>");
            Assert.Null(TechnologyObjectFiles.Place(first, target, overwrite: false, "The export", new List<string>()));
            Assert.Equal(first, File.ReadAllBytes(target));

            // An existing file without overwrite: refused, the target is untouched and no staging file is left.
            var second = System.Text.Encoding.UTF8.GetBytes("<Document>2</Document>");
            var refused = TechnologyObjectFiles.Place(second, target, overwrite: false, "The export", new List<string>());
            Assert.Equal(WorkerFailureCategories.ValidationError, refused!.Category);
            Assert.Equal(first, File.ReadAllBytes(target));

            Assert.Null(TechnologyObjectFiles.Place(second, target, overwrite: true, "The export", new List<string>()));
            Assert.Equal(second, File.ReadAllBytes(target));
            Assert.Equal(new[] { "Axis_1.xml" }, Directory.GetFiles(directory).Select(Path.GetFileName));

            // A missing directory fails without leaving anything behind.
            var missing = TechnologyObjectFiles.Place(first, Path.Combine(directory, "none", "x.xml"), overwrite: false, "The export", new List<string>());
            Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, missing!.Category);
            Assert.EndsWith("was not changed; no staged file is left; no backup was made.", missing.Message);
            Assert.StartsWith(".x.xml.", Path.GetFileName(TechnologyObjectFiles.SiblingPath(Path.Combine(directory, "x.xml"), "tmp")));
            Assert.Equal(64, TechnologyObjectFiles.Sha256(first).Length);
            Assert.Equal(TechnologyObjectFiles.Sha256(first).ToLowerInvariant(), TechnologyObjectFiles.Sha256(first));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>File operations that fail on request (the defaults otherwise), for the failure paths of Place.</summary>
    private static TechnologyObjectFiles.Operations Failing(Action<string, string, string>? replace = null, Func<string, string, bool>? move = null, Func<string, bool>? delete = null)
        => new()
        {
            Replace = replace ?? TechnologyObjectFiles.Operations.Default.Replace,
            Move = (source, destination) =>
            {
                if (move?.Invoke(source, destination) == true) throw new IOException($"moving {Path.GetFileName(source)} to {Path.GetFileName(destination)} was refused");
                File.Move(source, destination);
            },
            Delete = path =>
            {
                if (delete?.Invoke(path) == true) throw new IOException($"deleting {Path.GetFileName(path)} was refused");
                File.Delete(path);
            },
        };

    private static bool Staged(string path) => path.EndsWith(".tmp", StringComparison.Ordinal);

    private static bool Backup(string path) => path.EndsWith(".bak", StringComparison.Ordinal);

    /// <summary>
    /// A failure is recoverable from its message alone: it names the target, every staged (.tmp, the new content) and
    /// backup (.bak, the previous content) file that is left with its path, and says so when there is none.
    /// </summary>
    private static void AssertRecoverable(TechnologyObjectFiles.Failure? failure, string target, byte[] previous, byte[] next)
    {
        Assert.NotNull(failure);
        var message = failure!.Message;
        Assert.Contains($"'{target}'", message);
        var left = Directory.GetFiles(Path.GetDirectoryName(target)!).Where(path => path != target).ToList();
        Assert.All(left, path => Assert.True(Staged(path) || Backup(path), path));
        foreach (var path in left)
        {
            Assert.Contains($"'{path}'", message);
            Assert.Equal(Staged(path) ? next : previous, File.ReadAllBytes(path));
        }

        if (!left.Any(Staged)) Assert.Matches("no staged file is left|the staged file was removed|the staged file is gone", message);
        if (!left.Any(Backup)) Assert.Matches("no backup was made|no backup is left", message);
        Assert.Contains(File.Exists(target) ? "'" + target + "' was" : "'" + target + "' is missing", message);
    }

    private static void InDirectory(Action<string, byte[], byte[]> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-to-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            test(Path.Combine(directory, "Axis_1.xml"), System.Text.Encoding.UTF8.GetBytes("previous"), System.Text.Encoding.UTF8.GetBytes("next"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AFailedReplaceFallsBackToRenamingAsideAndSucceedsWhenTheMovesWork()
    {
        InDirectory((target, previous, next) =>
        {
            // ReplaceFile is not supported on the volume (nothing changed): the fallback renames aside and moves in.
            File.WriteAllBytes(target, previous);
            Assert.Null(TechnologyObjectFiles.Place(next, target, overwrite: true, "The export", new List<string>(), Failing(replace: (_, _, _) => throw new IOException("not supported"))));
            Assert.Equal(next, File.ReadAllBytes(target));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target)!));

            // ReplaceFile renamed the target to the backup and then failed (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2): the previous file is
            // restored first, then the fallback places the new one; the backup goes only after the hash matched.
            File.WriteAllBytes(target, previous);
            Assert.Null(TechnologyObjectFiles.Place(next, target, overwrite: true, "The export", new List<string>(), Failing(replace: (_, destination, backup) =>
            {
                File.Move(destination, backup);
                throw new IOException("unable to move replacement");
            })));
            Assert.Equal(next, File.ReadAllBytes(target));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target)!));

            // The target was removed outside the call while ReplaceFile failed: the new content is placed as requested.
            File.WriteAllBytes(target, previous);
            Assert.Null(TechnologyObjectFiles.Place(next, target, overwrite: true, "The export", new List<string>(), Failing(replace: (_, destination, _) =>
            {
                File.Delete(destination);
                throw new IOException("target vanished");
            })));
            Assert.Equal(next, File.ReadAllBytes(target));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target)!));
        });
    }

    public static TheoryData<string> ReplaceFailures => new()
    {
        "aside fails", "move in fails, restored", "move in fails, not restored", "replace renamed, not restored", "staged file removed", "target removed, move in fails",
    };

    /// <summary>Every failure path of an overwrite names what became of the target, the staged file and the backup, and the files are where it says.</summary>
    [Theory]
    [MemberData(nameof(ReplaceFailures))]
    public void EveryFailedReplaceNamesTheTargetTheStagedFileAndTheBackup(string path)
    {
        InDirectory((target, previous, next) =>
        {
            File.WriteAllBytes(target, previous);
            var unsupported = new IOException("ReplaceFile is not supported");
            var operations = path switch
            {
                "aside fails" => Failing(replace: (_, _, _) => throw unsupported, move: (source, _) => source == target),
                "move in fails, restored" => Failing(replace: (_, _, _) => throw unsupported, move: (source, _) => Staged(source)),
                "move in fails, not restored" => Failing(replace: (_, _, _) => throw unsupported, move: (source, _) => Staged(source) || Backup(source)),
                "replace renamed, not restored" => Failing(replace: (_, destination, backup) =>
                {
                    File.Move(destination, backup);
                    throw new IOException("unable to move replacement");
                }, move: (source, _) => Backup(source)),
                "staged file removed" => Failing(replace: (source, _, _) =>
                {
                    File.Delete(source);
                    throw unsupported;
                }),
                _ => Failing(replace: (_, destination, _) =>
                {
                    File.Delete(destination);
                    throw unsupported;
                }, move: (source, _) => Staged(source)),
            };

            var failure = TechnologyObjectFiles.Place(next, target, overwrite: true, "The export", new List<string>(), operations);
            Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, failure!.Category);
            Assert.StartsWith($"The export could not replace '{target}': ", failure.Message);
            AssertRecoverable(failure, target, previous, next);
            switch (path)
            {
                case "aside fails":
                case "staged file removed":
                    Assert.Equal(previous, File.ReadAllBytes(target));
                    Assert.Contains("was not changed", failure.Message);
                    break;
                case "move in fails, restored":
                    Assert.Equal(previous, File.ReadAllBytes(target));
                    Assert.Contains("was restored to the previous file", failure.Message);
                    break;
                default:
                    Assert.False(File.Exists(target));
                    break;
            }
        });
    }

    [Fact]
    public void FailuresOfANewFileAndOfTheReadBackNameEveryFileToo()
    {
        InDirectory((target, previous, next) =>
        {
            // The move of a new file fails: nothing was written, the staged file was removed.
            var refused = TechnologyObjectFiles.Place(next, target, overwrite: false, "The export", new List<string>(), Failing(move: (_, _) => true));
            Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, refused!.Category);
            Assert.Contains("Nothing was written", refused.Message);
            Assert.Contains("the staged file was removed", refused.Message);
            Assert.Contains("no backup was made", refused.Message);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!));

            // ... and the staged file cannot be removed either: it is named.
            var left = TechnologyObjectFiles.Place(next, target, overwrite: false, "The export", new List<string>(), Failing(move: (_, _) => true, delete: Staged));
            Assert.Contains("the staged file is left at '", left!.Message);
            Assert.Equal(Directory.GetFiles(Path.GetDirectoryName(target)!).Single(), Regex.Match(left.Message, "left at '([^']+)'").Groups[1].Value);
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(target)!)) File.Delete(file);

            // The placed file cannot be read back: the target is named as missing, no staged file and no backup exist.
            var unread = TechnologyObjectFiles.Place(next, target, overwrite: false, "The export", new List<string>(), new TechnologyObjectFiles.Operations
            {
                Move = (source, destination) =>
                {
                    File.Move(source, destination);
                    File.Delete(destination);
                },
            });
            Assert.Equal(WorkerFailureCategories.PostconditionFailed, unread!.Category);
            Assert.Contains("cannot be read back", unread.Message);
            AssertRecoverable(unread, target, previous, next);
            Assert.Contains("no backup was made (there was no previous file)", unread.Message);

            // Another process changed the replaced file: the backup with the previous content is kept and named.
            File.WriteAllBytes(target, previous);
            var changed = TechnologyObjectFiles.Place(next, target, overwrite: true, "The export", new List<string>(), Failing(replace: (source, destination, backup) =>
            {
                File.Replace(source, destination, backup, ignoreMetadataErrors: true);
                File.WriteAllText(destination, "other");
            }));
            Assert.Equal(WorkerFailureCategories.PostconditionFailed, changed!.Category);
            Assert.Contains("holds that other content; no staged file is left; the previous file is kept at '", changed.Message);
            var backup = Directory.GetFiles(Path.GetDirectoryName(target)!).Single(Backup);
            Assert.Contains($"'{backup}'", changed.Message);
            Assert.Equal(previous, File.ReadAllBytes(backup));
        });
    }

    [Fact]
    public void ABackupThatCannotBeDeletedAfterSuccessIsAWarningNamingIt()
    {
        InDirectory((target, previous, next) =>
        {
            File.WriteAllBytes(target, previous);
            var warnings = new List<string>();
            Assert.Null(TechnologyObjectFiles.Place(next, target, overwrite: true, "The export", warnings, Failing(delete: Backup)));
            Assert.Equal(next, File.ReadAllBytes(target));
            var backup = Directory.GetFiles(Path.GetDirectoryName(target)!).Single(Backup);
            Assert.Equal(previous, File.ReadAllBytes(backup));
            Assert.Contains($"The previous file was kept at '{backup}'", Assert.Single(warnings));
        });
    }

    [Fact]
    public void StructuralWriteChecksAreSharedRules()
    {
        Assert.Null(TechnologyObjectWriteChecks.ExistingObject("Axis_9", null, "PLC_1"));
        Assert.Equal(WorkerFailureCategories.ValidationError, TechnologyObjectWriteChecks.ExistingObject("Axis_1", "/Axes", "PLC_1")!.Category);
        Assert.Null(TechnologyObjectWriteChecks.Created("Axis_9", "TO_PositioningAxis", "10.0", null, "/", 1));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, TechnologyObjectWriteChecks.Created("Axis_9", "TO_PositioningAxis", "10.0", null, "/", 0)!.Category);
        Assert.Null(TechnologyObjectWriteChecks.CreatedGroup("/", "G", 1, 2, 3));
        Assert.NotNull(TechnologyObjectWriteChecks.CreatedGroup("/", "G", 1, 3, 3));
        Assert.NotNull(TechnologyObjectWriteChecks.DeletedGroup("/G", "/", stillListed: true));
        Assert.NotNull(TechnologyObjectWriteChecks.Deleted("Axis_1", "/", stillListed: true));

        var warnings = new List<string>();
        var (imported, failure) = TechnologyObjectWriteChecks.Imported(new[] { "Axis_3" }, new[] { "Axis_1" }, new[] { "Axis_1", "Axis_3" }, _ => true, false, "f", "/", "L", warnings);
        Assert.Null(failure);
        Assert.Equal(new[] { "Axis_3" }, imported);
        Assert.NotNull(TechnologyObjectWriteChecks.Imported(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), _ => true, false, "f", "/", "L", warnings).Failure);
        Assert.NotNull(TechnologyObjectWriteChecks.Imported(new[] { "Axis_3" }, Array.Empty<string>(), Array.Empty<string>(), _ => false, false, "f", "/", "L", warnings).Failure);
        Assert.Empty(warnings);
        Assert.NotNull(TechnologyObjectWriteChecks.Imported(null, new[] { "Axis_1" }, new[] { "AXIS_1" }, _ => true, false, "f", "/", null, warnings).Failure);
        var replaced = TechnologyObjectWriteChecks.Imported(null, new[] { "Axis_1" }, new[] { "Axis_1" }, _ => true, true, "f", "/", null, warnings);
        Assert.Null(replaced.Failure);
        Assert.Empty(replaced.Imported);
        Assert.Contains("not a list of technology objects", Assert.Single(warnings));
    }
}
