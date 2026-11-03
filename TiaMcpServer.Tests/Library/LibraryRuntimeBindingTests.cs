using TiaMcpServer.Contracts.Library;
using Xunit;

namespace TiaMcpServer.Tests.Library;

/// <summary>Exact runtime binding of library members with undocumented return or collection parameter types, on plain .NET stand-ins.</summary>
public sealed class LibraryRuntimeBindingTests
{
    public interface ISelection;
    public interface IScope;
    public sealed class Selection : ISelection;
    public sealed class Scope : IScope;
    public class Base;
    public sealed class Derived : Base;
    public enum Mode { ThrowIfExists = 3, Replace = 7 }

    public sealed class Library
    {
        public object Open(FileInfo file, Mode mode) => file.Name + mode;
        public void Save() { }
        public void Overloaded(Base value) { }
        public void Overloaded(Derived value) { }
        public void Twice(object value) { }
        public void Twice(Base value, int extra = 0) { }
        public T Create<T>(DirectoryInfo directory, string name) where T : Base, new() => new();
        public int UpdateProject(IEnumerable<ISelection> selections, IList<IScope> scopes) => selections.Count() + scopes.Count;
    }

    public sealed class ArrayLibrary
    {
        public string UpdateProject(ISelection[] selections, IScope[] scopes) => "arrays";
    }

    public sealed class AmbiguousLibrary
    {
        public void UpdateProject(IEnumerable<ISelection> selections, IEnumerable<IScope> scopes) { }
        public void UpdateProject(ICollection<ISelection> selections, IReadOnlyList<IScope> scopes) { }
    }

    [Fact]
    public void Find_BindsTheDocumentedSignature_OrFailsClosed()
    {
        Assert.NotNull(LibraryRuntimeBinding.Find(typeof(Library), "Open", new[] { typeof(FileInfo), typeof(Mode) }, out var error));
        Assert.Null(error);
        Assert.NotNull(LibraryRuntimeBinding.Find(typeof(Library), "Save", Type.EmptyTypes, out _));
        Assert.Null(LibraryRuntimeBinding.Find(typeof(Library), "Open", new[] { typeof(FileInfo) }, out error));
        Assert.Contains("exposes no Open(FileInfo)", error);
        Assert.Null(LibraryRuntimeBinding.Find(typeof(Library), "Close", Type.EmptyTypes, out error));
        Assert.Contains("exposes no Close()", error);
    }

    [Fact]
    public void Find_PrefersAnExactOverload_AndRefusesRealAmbiguity()
    {
        var exact = LibraryRuntimeBinding.Find(typeof(Library), "Overloaded", new[] { typeof(Derived) }, out _);
        Assert.Equal(typeof(Derived), exact!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(Base), LibraryRuntimeBinding.Find(typeof(Library), "Overloaded", new[] { typeof(Base) }, out _)!.GetParameters()[0].ParameterType);
        // Twice(object) accepts a Derived; Twice(Base, int) has two parameters: one applicable member, bound.
        Assert.NotNull(LibraryRuntimeBinding.Find(typeof(Library), "Twice", new[] { typeof(Derived) }, out _));
    }

    [Fact]
    public void FindGeneric_ClosesTheDocumentedTypeArgument_AndRespectsConstraints()
    {
        var create = LibraryRuntimeBinding.FindGeneric(typeof(Library), "Create", new[] { typeof(Derived) }, new[] { typeof(DirectoryInfo), typeof(string) }, out _);
        Assert.Equal(typeof(Derived), create!.ReturnType);
        Assert.Null(LibraryRuntimeBinding.FindGeneric(typeof(Library), "Create", new[] { typeof(string) }, new[] { typeof(DirectoryInfo), typeof(string) }, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void FindForCollections_BuildsListsOrArrays_ForTheSingleApplicableOverload()
    {
        var items = new IReadOnlyList<object>[] { new object[] { new Selection() }, new object[] { new Scope(), new Scope() } };
        var bound = LibraryRuntimeBinding.FindForCollections(typeof(Library), "UpdateProject", new[] { typeof(ISelection), typeof(IScope) }, items, out var error);
        Assert.Null(error);
        Assert.Equal(3, bound!.Value.Method.Invoke(new Library(), bound.Value.Arguments));
        var arrays = LibraryRuntimeBinding.FindForCollections(typeof(ArrayLibrary), "UpdateProject", new[] { typeof(ISelection), typeof(IScope) }, items, out _);
        Assert.Equal("arrays", arrays!.Value.Method.Invoke(new ArrayLibrary(), arrays.Value.Arguments));
        Assert.Null(LibraryRuntimeBinding.FindForCollections(typeof(AmbiguousLibrary), "UpdateProject", new[] { typeof(ISelection), typeof(IScope) }, items, out error));
        Assert.Contains("2 UpdateProject overloads", error);
        Assert.Null(LibraryRuntimeBinding.BuildCollection(typeof(IEnumerable<ISelection>), typeof(ISelection), new object[] { new Scope() }));
    }

    [Fact]
    public void EnumValues_AreParsedByExactMemberName()
    {
        Assert.Equal(Mode.Replace, LibraryRuntimeBinding.EnumValue(typeof(Mode), "Replace"));
        Assert.Null(LibraryRuntimeBinding.EnumValue(typeof(Mode), "replace"));
        Assert.Null(LibraryRuntimeBinding.EnumValue(typeof(Mode), "7"));
        Assert.Equal("ThrowIfExists, Replace", LibraryRuntimeBinding.MemberList(typeof(Mode)));
    }

    [Fact]
    public void Properties_AreReadOnTheNamedDeclaringType()
    {
        Assert.NotNull(LibraryRuntimeBinding.Property(typeof(FileInfo), "FullName", out var error));
        Assert.Null(error);
        Assert.Null(LibraryRuntimeBinding.Property(typeof(FileInfo), "Versions", out error));
        Assert.Contains("no readable Versions", error);
    }
}
