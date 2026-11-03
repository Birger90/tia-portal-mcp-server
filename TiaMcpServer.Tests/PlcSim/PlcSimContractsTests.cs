using System.Text.Json;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Contracts.Project;
using Xunit;

namespace TiaMcpServer.Tests.PlcSim;

public sealed class PlcSimContractsTests
{
    private static JsonElement Json(string text) => PlcSimValueCodec.Parse(text);

    [Theory]
    [InlineData("bool", "true", typeof(bool))]
    [InlineData("int8", "-128", typeof(sbyte))]
    [InlineData("int16", "32767", typeof(short))]
    [InlineData("int32", "-2147483648", typeof(int))]
    [InlineData("int64", "9223372036854775807", typeof(long))]
    [InlineData("uint8", "255", typeof(byte))]
    [InlineData("uint16", "65535", typeof(ushort))]
    [InlineData("uint32", "4294967295", typeof(uint))]
    [InlineData("uint64", "18446744073709551615", typeof(ulong))]
    [InlineData("float", "0.1", typeof(float))]
    [InlineData("double", "-1.5e3", typeof(double))]
    [InlineData("string", "\"abc\"", typeof(string))]
    public void Codec_ParsesExactClrTypes(string dataType, string json, Type expected)
    {
        Assert.True(PlcSimValueCodec.TryParse(dataType, Json(json), out var typed, out var error), error);
        Assert.IsType(expected, typed);
        Assert.Equal(expected, PlcSimDataTypes.ClrType(dataType));
    }

    [Theory]
    [InlineData("bool", "1")]
    [InlineData("bool", "\"true\"")]
    [InlineData("int8", "128")]
    [InlineData("int16", "1.5")]
    [InlineData("int16", "\"5\"")]
    [InlineData("int32", "2147483648")]
    [InlineData("float", "1e39")]
    [InlineData("float", "\"NaN\"")]
    [InlineData("double", "true")]
    [InlineData("string", "5")]
    [InlineData("int64", "null")]
    [InlineData("uint8", "-1")]
    [InlineData("uint8", "256")]
    [InlineData("uint32", "4294967296")]
    [InlineData("uint64", "-1")]
    [InlineData("uint64", "18446744073709551616")]
    public void Codec_RejectsCoercion(string dataType, string json)
    {
        Assert.False(PlcSimValueCodec.TryParse(dataType, Json(json), out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData(float.MaxValue)]
    [InlineData(-float.MaxValue)]
    [InlineData(float.Epsilon)]
    [InlineData(-float.Epsilon)]
    [InlineData(1e-40f)]
    [InlineData(1.17549435E-38f)]
    [InlineData(0f)]
    public void Codec_FloatRoundTripsItsOwnOutputAtTheRangeEdges(float value)
    {
        var json = PlcSimValueCodec.ToJson(value);
        Assert.True(PlcSimValueCodec.TryParse("float", json, out var typed, out var error), error);
        Assert.Equal(value, Assert.IsType<float>(typed));
        // The same holds for an expect step, which parses its value with the same codec.
        Assert.True(PlcSimValueCodec.TryParse("float", Json(json.GetRawText()), out _, out _));
    }

    [Theory]
    [InlineData("3.5e38", "single-precision range")]
    [InlineData("-3.5e38", "single-precision range")]
    [InlineData("1e-50", "underflow")]
    [InlineData("-1e-46", "underflow")]
    public void Codec_FloatRejectsOverflowAndUnderflowInsteadOfCoercing(string json, string expected)
    {
        Assert.False(PlcSimValueCodec.TryParse("float", Json(json), out var typed, out var error));
        Assert.Null(typed);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void Codec_FloatKeepsNegativeZero()
    {
        Assert.True(PlcSimValueCodec.TryParse("float", Json("-0.0"), out var typed, out _));
        Assert.True(float.IsNegative((float)typed!));
    }

    [Fact]
    public void Codec_RejectsMissingValue()
        => Assert.False(PlcSimValueCodec.TryParse("bool", null, out _, out _));

    [Fact]
    public void Codec_RendersShortestRoundTripAndNonFiniteAsStrings()
    {
        Assert.Equal("0.1", PlcSimValueCodec.ToJson(0.1f).GetRawText());
        Assert.Equal("21.5", PlcSimValueCodec.ToJson(21.5).GetRawText());
        Assert.Equal("\"NaN\"", PlcSimValueCodec.ToJson(float.NaN).GetRawText());
        Assert.Equal("\"-Infinity\"", PlcSimValueCodec.ToJson(double.NegativeInfinity).GetRawText());
        Assert.Equal("-5", PlcSimValueCodec.ToJson((sbyte)-5).GetRawText());
        Assert.Equal("true", PlcSimValueCodec.ToJson(true).GetRawText());
        Assert.Equal("a\"b", PlcSimValueCodec.ToJson("a\"b").GetString());
    }

    [Fact]
    public void Codec_FromApiValueAcceptsOnlyFittingIntegers()
    {
        Assert.True(PlcSimValueCodec.TryFromApiValue("int16", (short)5, out var exact, out _));
        Assert.IsType<short>(exact);
        Assert.True(PlcSimValueCodec.TryFromApiValue("int16", 5, out var widened, out _));
        Assert.IsType<short>(widened);
        Assert.False(PlcSimValueCodec.TryFromApiValue("int8", 300, out _, out _));
        Assert.False(PlcSimValueCodec.TryFromApiValue("float", 1.0, out _, out _));
        Assert.False(PlcSimValueCodec.TryFromApiValue("bool", "true", out _, out _));
        Assert.False(PlcSimValueCodec.TryFromApiValue("string", null, out _, out _));
    }

    [Fact]
    public void StringIsReadOnly()
    {
        Assert.Contains("string", PlcSimDataTypes.All);
        Assert.DoesNotContain("string", PlcSimDataTypes.Writable);
        Assert.Null(PlcSimDataTypes.WriteMethod("string"));
        Assert.Equal("ReadString", PlcSimDataTypes.ReadMethod("string"));
    }

    [Theory]
    [InlineData("int16", "greaterThan", 5, 4, true)]
    [InlineData("int16", "greaterThan", 4, 4, false)]
    [InlineData("int16", "greaterOrEqual", 4, 4, true)]
    [InlineData("int16", "lessThan", 3, 4, true)]
    [InlineData("int16", "lessOrEqual", 5, 4, false)]
    [InlineData("int16", "notEquals", 5, 4, true)]
    [InlineData("int16", "equals", 4, 4, true)]
    public void Comparisons_IntegersAreExact(string dataType, string comparison, short observed, short expected, bool result)
        => Assert.Equal(result, PlcSimComparisons.Evaluate(dataType, comparison, observed, expected, null, null));

    [Fact]
    public void Comparisons_FloatEqualityUsesTheParsedSinglePrecisionValue()
    {
        Assert.True(PlcSimValueCodec.TryParse("float", Json("0.1"), out var expected, out _));
        Assert.True(PlcSimComparisons.Evaluate("float", "equals", 0.1f, expected, null, null));
        Assert.False(PlcSimComparisons.Evaluate("float", "equals", float.NaN, expected, null, null));
        Assert.True(PlcSimComparisons.Evaluate("float", "notEquals", float.NaN, expected, null, null));
        Assert.True(PlcSimComparisons.Evaluate("double", "between", 2.5, null, 2.0, 3.0));
        Assert.True(PlcSimComparisons.Evaluate("double", "between", 3.0, null, 2.0, 3.0));
        Assert.False(PlcSimComparisons.Evaluate("double", "between", 3.01, null, 2.0, 3.0));
    }

    [Fact]
    public void Comparisons_BoolAndStringOnlySupportEquality()
    {
        Assert.True(PlcSimComparisons.IsSupported("bool", "equals"));
        Assert.False(PlcSimComparisons.IsSupported("bool", "greaterThan"));
        Assert.False(PlcSimComparisons.IsSupported("string", "between"));
        Assert.True(PlcSimComparisons.Evaluate("string", "equals", "Ready", "Ready", null, null));
        Assert.False(PlcSimComparisons.Evaluate("string", "equals", "Ready", "ready", null, null));
        Assert.Throws<ArgumentException>(() => PlcSimComparisons.Evaluate("bool", "lessThan", true, false, null, null));
        Assert.False(PlcSimComparisons.IsKnown("Equals"));
    }

    [Fact]
    public void Rules_ValidateTagsStrictly()
    {
        var errors = new List<string>();
        PlcSimRequestRules.ValidateTags(new List<PlcSimTagRequestInfo?>
        {
            new() { Name = "A", DataType = "bool", Value = Json("true") },
            new() { Name = " ", DataType = "int16", Value = Json("1") },
            new() { Name = "B", DataType = "INT16", Value = Json("1") },
            new() { Name = "C", DataType = "string", Value = Json("\"x\"") },
            new() { Name = "D", DataType = "int8", Value = Json("999") },
            null,
        }, write: true, errors, "w: ");
        Assert.Equal(5, errors.Count);
        Assert.Contains(errors, error => error.Contains("tags[1].name", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("tags[2].dataType must be one of", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("WriteString is not a verified", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("tags[4].value", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("tags[5] must not be null", StringComparison.Ordinal));

        errors.Clear();
        PlcSimRequestRules.ValidateTags(new List<PlcSimTagRequestInfo?> { new() { Name = "A", DataType = "bool", Value = Json("true") } }, write: false, errors, "");
        Assert.Single(errors);
        Assert.Contains("value is not valid for read_tags", errors[0]);

        errors.Clear();
        PlcSimRequestRules.ValidateTags(Enumerable.Range(0, 101).Select(i => (PlcSimTagRequestInfo?)new PlcSimTagRequestInfo { Name = "T" + i, DataType = "bool" }).ToList(),
            write: false, errors, "");
        Assert.Contains("maximum of 100", Assert.Single(errors));
    }

    [Fact]
    public void Rules_ValidateScenarioSteps()
    {
        var errors = new List<string>();
        PlcSimRequestRules.ValidateTest(new PlcSimTestRequestInfo
        {
            MaxDurationMs = 700_000,
            Steps = new List<PlcSimTestStepInfo>
            {
                new() { Kind = "wait", Ms = 0 },
                new() { Kind = "wait", Ms = 10, Tag = "X" },
                new() { Kind = "write", Tag = "X", DataType = "string", Value = Json("\"a\"") },
                new() { Kind = "expect", Tag = "X", DataType = "bool", Comparison = "greaterThan", Value = Json("true") },
                new() { Kind = "expect", Tag = "X", DataType = "int16", Comparison = "between", Value = Json("1"), Min = Json("5"), Max = Json("1") },
                new() { Kind = "expect", Tag = "X", DataType = "int16", Comparison = "equals", Value = Json("1"), PollMs = 5, TimeoutMs = 60_001 },
                new() { Kind = "Expect" },
                new() { Kind = "expect", Tag = "X", DataType = "int16", Comparison = "equals", Value = Json("1"), Min = Json("1") },
            }
        }, errors);
        Assert.Contains(errors, error => error.StartsWith("maxDurationMs", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("steps[0].ms", StringComparison.Ordinal));
        Assert.Contains(errors, error => error == "steps[1].tag is not valid for a wait step.");
        Assert.Contains(errors, error => error.StartsWith("steps[2].dataType 'string' cannot be written", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("steps[3].comparison 'greaterThan' is not supported for bool", StringComparison.Ordinal));
        Assert.Contains(errors, error => error == "steps[4].value is not valid with between; use min and max.");
        Assert.Contains(errors, error => error == "steps[4].min must not be greater than max.");
        Assert.Contains(errors, error => error.StartsWith("steps[5].timeoutMs", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("steps[5].pollMs", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("steps[6].kind must be one of", StringComparison.Ordinal));
        Assert.Contains(errors, error => error == "steps[7].min and max are valid only with between.");

        errors.Clear();
        PlcSimRequestRules.ValidateTest(new PlcSimTestRequestInfo { Steps = Enumerable.Range(0, 201).Select(_ => new PlcSimTestStepInfo { Kind = "wait", Ms = 1 }).ToList() }, errors);
        Assert.Contains("maximum of 200", Assert.Single(errors));
    }

    [Fact]
    public void Rules_NormalizeAppliesExpectDefaultsOnly()
    {
        var normalized = PlcSimRequestRules.NormalizeTest(new PlcSimTestRequestInfo
        {
            Steps = new List<PlcSimTestStepInfo>
            {
                new() { Kind = "expect", Tag = "X", DataType = "bool", Comparison = "equals", Value = Json("true") },
                new() { Kind = "wait", Ms = 5 },
            }
        });
        Assert.Equal(PlcSimRequestRules.DefaultMaxDurationMs, normalized.MaxDurationMs);
        Assert.Equal(2000, normalized.Steps![0].TimeoutMs);
        Assert.Equal(50, normalized.Steps[0].PollMs);
        Assert.Null(normalized.Steps[1].TimeoutMs);
        Assert.Null(normalized.Steps[1].PollMs);
        Assert.Equal(1234, PlcSimRequestRules.NormalizeTest(new PlcSimTestRequestInfo { MaxDurationMs = 5 }, 1234).MaxDurationMs);
    }

    private static PlcSimApiLocationInfo Locate(string? env, string? registry, string[] files, string[] directories, Func<string, IEnumerable<string>>? children = null)
        => PlcSimApiLocator.Locate(env, () => registry, files.Contains, directories.Contains, children ?? (_ => Array.Empty<string>()));

    [Fact]
    public void Locator_EnvironmentOverrideWinsAndDisablesRegistry()
    {
        var dir = Path.GetFullPath(Path.Combine("opt", "plcsim"));
        var dll = Path.Combine(dir, PlcSimApiLocator.ApiDllName);
        var location = Locate(" " + dir + " ", "ignored", new[] { dll }, new[] { dir });
        Assert.Equal(PlcSimApiLocator.SourceEnvironment, location.Source);
        Assert.Equal(dll, location.DllPath);
        Assert.True(location.DllExists);
        Assert.Null(location.Problem);
        Assert.Null(location.RegistryPath);

        var missing = Locate(dir, "ignored", Array.Empty<string>(), new[] { dir });
        Assert.False(missing.DllExists);
        Assert.Contains("is not in", missing.Problem);
        Assert.Contains("does not exist", Locate(dir, null, Array.Empty<string>(), Array.Empty<string>()).Problem);
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("custom/api")]
    [InlineData(@"C:relative")]
    [InlineData(@"\rooted-on-current-drive")]
    public void Locator_RejectsANonAbsoluteDirectoryForDoctorAndWorkerAlike(string relative)
    {
        var probed = new List<string>();
        var location = PlcSimApiLocator.Locate(relative, () => "ignored", path => { probed.Add(path); return true; }, _ => true, _ => Array.Empty<string>());
        Assert.Equal(PlcSimApiLocator.SourceEnvironment, location.Source);
        Assert.False(location.DllExists);
        Assert.Null(location.ApiDirectory);
        Assert.Null(location.DllPath);
        Assert.Contains("must be an absolute directory path", location.Problem);
        Assert.Empty(probed);

        var registry = PlcSimApiLocator.Locate(null, () => "pf", _ => true, _ => true, _ => new[] { "6.0" });
        Assert.Contains("is not an absolute path", registry.Problem);
        Assert.False(registry.DllExists);
    }

    [Theory]
    [InlineData(@"C:\PLCSIM", true)]
    [InlineData("C:/PLCSIM", true)]
    [InlineData(@"\\server\share\API", true)]
    [InlineData("C:PLCSIM", false)]
    [InlineData("PLCSIM", false)]
    [InlineData("", false)]
    public void Locator_FullyQualifiedPaths(string path, bool expected)
        => Assert.Equal(expected, PlcSimApiLocator.IsFullyQualified(path));

    [Fact]
    public void Locator_RegistryPicksTheHighestVersionFolderWithoutFallback()
    {
        var root = Path.GetFullPath(Path.Combine("pf", "PLCSIMADV"));
        var api = Path.Combine(root, "API");
        var v6 = Path.Combine(api, "6.0");
        var v5 = Path.Combine(api, "5.0");
        IEnumerable<string> Children(string path) => path == api ? new[] { "5.0", "6.0", "Docs", "10" } : Array.Empty<string>();

        var highest = Locate(null, root, new[] { Path.Combine(Path.Combine(api, "10"), PlcSimApiLocator.ApiDllName) }, new[] { api }, Children);
        Assert.Equal(PlcSimApiLocator.SourceRegistry, highest.Source);
        Assert.Equal("10", highest.SelectedVersion);
        Assert.True(highest.DllExists);
        Assert.Equal(new[] { "10", "5.0", "6.0", "Docs" }, highest.VersionDirectories);

        var noFallback = Locate(null, root, new[] { Path.Combine(v5, PlcSimApiLocator.ApiDllName) }, new[] { api },
            path => path == api ? new[] { "5.0", "6.0" } : Array.Empty<string>());
        Assert.Equal("6.0", noFallback.SelectedVersion);
        Assert.Equal(Path.Combine(v6, PlcSimApiLocator.ApiDllName), noFallback.DllPath);
        Assert.False(noFallback.DllExists);
        Assert.Contains("does not contain", noFallback.Problem);

        Assert.Contains("is named as a version", Locate(null, root, Array.Empty<string>(), new[] { api }, path => new[] { "Docs" }).Problem);
        Assert.Contains("API folder", Locate(null, root, Array.Empty<string>(), Array.Empty<string>()).Problem);
        Assert.Equal(PlcSimApiLocator.NotInstalledProblem, Locate(null, null, Array.Empty<string>(), Array.Empty<string>()).Problem);
        var thrown = PlcSimApiLocator.Locate(null, () => throw new UnauthorizedAccessException("denied"), _ => false, _ => false, _ => Array.Empty<string>());
        Assert.Contains("could not be read", thrown.Problem);
    }

    [Fact]
    public void Policy_SimulationControlIsAppendedAndAllowedOnlyInWritableModes()
    {
        Assert.Equal(7, (int)OperationCapability.SimulationControl);
        // OnlineObserve (compare_to_online) was appended after it; SimulationControl keeps its value.
        Assert.Equal(OperationCapability.OnlineObserve, Enum.GetValues<OperationCapability>().Max());
        Assert.False(OperationPolicyCatalog.IsCapabilityAllowed(McpAccessMode.ReadOnly, OperationCapability.SimulationControl));
        Assert.True(OperationPolicyCatalog.IsCapabilityAllowed(McpAccessMode.ReadWrite, OperationCapability.SimulationControl));
        Assert.True(OperationPolicyCatalog.IsCapabilityAllowed(McpAccessMode.Full, OperationCapability.SimulationControl));
        foreach (var method in PlcSimMethods.Read)
        {
            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
        }

        foreach (var method in PlcSimMethods.Control)
        {
            Assert.Equal(OperationCapability.SimulationControl, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, method));
        }

        Assert.All(PlcSimMethods.All, method =>
        {
            Assert.StartsWith("plcsim_", method, StringComparison.Ordinal);
            Assert.False(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
            Assert.Null(ProjectCapabilityCatalog.OperationFor(method));
            Assert.False(ProjectCapabilityCatalog.RequiresBoundProject(method));
        });
        Assert.Equal(14, PlcSimMethods.All.Distinct().Count());
    }

    [Fact]
    public void FailureCategories_IncludeThePlcSimCategories()
    {
        Assert.True(WorkerFailureCategories.IsKnown("plcsim_api_unavailable"));
        Assert.True(WorkerFailureCategories.IsKnown("plcsim_api_member_missing"));
        Assert.True(WorkerFailureCategories.IsKnown("plcsim_runtime_error"));
        Assert.False(WorkerFailureCategories.IsPreconditionRefusal("plcsim_runtime_error"));
    }

    [Fact]
    public void UnsignedTypes_AreReadOnlyAndUseTheDocumentedReadMethods()
    {
        Assert.Equal("ReadUInt8", PlcSimDataTypes.ReadMethod("uint8"));
        Assert.Equal("ReadUInt64", PlcSimDataTypes.ReadMethod("uint64"));
        foreach (var dataType in new[] { "uint8", "uint16", "uint32", "uint64" })
        {
            Assert.Contains(dataType, PlcSimDataTypes.All);
            Assert.DoesNotContain(dataType, PlcSimDataTypes.Writable);
            Assert.Null(PlcSimDataTypes.WriteMethod(dataType));
            Assert.True(PlcSimDataTypes.IsInteger(dataType));
        }
    }

    [Fact]
    public void UnsignedApiValues_ConvertExactlyAndCompareAboveInt64()
    {
        Assert.True(PlcSimValueCodec.TryFromApiValue("uint64", ulong.MaxValue, out var typed, out var error), error);
        Assert.Equal(ulong.MaxValue, Assert.IsType<ulong>(typed));
        Assert.True(PlcSimValueCodec.TryFromApiValue("uint8", (byte)200, out var b, out error), error);
        Assert.Equal((byte)200, Assert.IsType<byte>(b));
        Assert.False(PlcSimValueCodec.TryFromApiValue("uint8", (short)-1, out _, out _));
        Assert.True(PlcSimComparisons.Evaluate("uint64", "greaterThan", ulong.MaxValue, (ulong)long.MaxValue, null, null));
        Assert.True(PlcSimComparisons.Evaluate("uint32", "between", 4000000000u, null, 3000000000u, 4294967295u));
        Assert.Equal("18446744073709551615", PlcSimValueCodec.ToJson(ulong.MaxValue).GetRawText());
    }
}
