using TiaMcpServer.Contracts.Library;
using Xunit;

namespace TiaMcpServer.Tests.Library;

/// <summary>Binding by documented signature: enum parameters by full type name, collections by element type name, fail closed otherwise.</summary>
public sealed class LibrarySignatureBindingTests
{
    public enum Mode { None, Compressed }

    [Flags]
    public enum Options { None = 0, HarmonizePaths = 1, HarmonizeNames = 2 }

    public interface ISelection { }

    public sealed class Selected : ISelection { }

    public sealed class Target
    {
        public string? Called { get; private set; }

        public void Archive(DirectoryInfo directory, string name, Mode mode) => Called = $"{directory.Name}|{name}|{mode}";

        public void Harmonize(IEnumerable<ISelection> selections, Options options) => Called = $"{selections.Count()}|{options}";

        public void Twice(string value) => Called = "string";

        public void Twice(object value) => Called = "object";
    }

    [Fact]
    public void Bind_MatchesEnumsByFullName_AndBuildsTheValue()
    {
        var target = new Target();
        var bound = LibrarySignatureBinding.Bind(typeof(Target), "Archive", new[]
        {
            SignatureArgument.Of(new DirectoryInfo("out")), SignatureArgument.Of("Plant"), SignatureArgument.Enum(typeof(Mode).FullName!, "Compressed"),
        }, out var error);
        Assert.NotNull(bound);
        Assert.Null(error);
        bound!.Value.Method.Invoke(target, bound.Value.Arguments);
        Assert.Equal("out|Plant|Compressed", target.Called);
    }

    [Fact]
    public void Bind_RefusesAnUnknownMember_OrAnotherEnumType()
    {
        Assert.Null(LibrarySignatureBinding.Bind(typeof(Target), "Archive", new[]
        {
            SignatureArgument.Of(new DirectoryInfo("out")), SignatureArgument.Of("Plant"), SignatureArgument.Enum(typeof(Mode).FullName!, "Zip"),
        }, out var unknown));
        Assert.Contains("no member 'Zip'", unknown, StringComparison.Ordinal);
        Assert.Null(LibrarySignatureBinding.Bind(typeof(Target), "Archive", new[]
        {
            SignatureArgument.Of(new DirectoryInfo("out")), SignatureArgument.Of("Plant"), SignatureArgument.Enum("Siemens.Engineering.Library.LibraryArchivationMode", "Compressed"),
        }, out var other));
        Assert.Contains("exposes no Archive", other, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_CombinesMembersOnlyOnFlagsEnums_AndBuildsCollections()
    {
        var target = new Target();
        var bound = LibrarySignatureBinding.Bind(typeof(Target), "Harmonize", new[]
        {
            SignatureArgument.Collection(typeof(ISelection).FullName!, new object[] { new Selected(), new Selected() }),
            SignatureArgument.Enum(typeof(Options).FullName!, "HarmonizePaths, HarmonizeNames"),
        }, out _);
        bound!.Value.Method.Invoke(target, bound.Value.Arguments);
        Assert.Equal("2|HarmonizePaths, HarmonizeNames", target.Called);
        Assert.Null(LibrarySignatureBinding.EnumValue(typeof(Mode), "None, Compressed", out var notFlags));
        Assert.Contains("not a [Flags] enum", notFlags, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_RefusesSeveralApplicableOverloads()
    {
        Assert.Null(LibrarySignatureBinding.Bind(typeof(Target), "Twice", new[] { SignatureArgument.Of("x") }, out var error));
        Assert.Contains("2 Twice overloads", error, StringComparison.Ordinal);
    }
}
