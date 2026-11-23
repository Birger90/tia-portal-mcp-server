using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

/// <summary>
/// Runs the packaging script tests one class at a time, after the parallel part of the suite. On Windows a process
/// started while another test redirects its output can inherit that test's pipe handles: a long-lived child of
/// an isolated <c>dotnet build</c> (DoctorPackageVerificationScriptTests) then keeps the reference-stub verifier's
/// stdout open after it exited ("Verifier output streams did not close", packaging runs from 2026-11-02 on).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PackagingScriptProcessCollection
{
    public const string Name = "Packaging script processes";
}
