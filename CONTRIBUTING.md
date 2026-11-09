# Contributing

**Language:** English · [Deutsch](CONTRIBUTING.de.md)

Contributions are welcome. This repository is a fork of
[Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp) and is maintained at
[Birger90/tia-portal-mcp-server](https://github.com/Birger90/tia-portal-mcp-server).
Background is provided by [AGENTS.md](AGENTS.md) (build and conventions, in English),
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (structure) and the
[documentation overview](docs/README.md) for everything else.

## License

The fork as a whole is licensed under **AGPL-3.0-only** ([LICENSE](LICENSE)). Czarnak's base
code keeps its MIT notice in [LICENSE-MIT](LICENSE-MIT). By contributing, you agree
that your contribution is published under AGPL-3.0-only.

## Principles of this fork

These rules apply to every contribution. A pull request that breaks one of them will not be
accepted.

- **TIA Portal V21 only.** Older versions are not supported.
- **Never stricter than TIA Portal.** The server refuses only what TIA Portal itself refuses, or
  invalid and ambiguous input. Everything else is a notice (info guard); TIA Portal
  decides. A new block needs a justification for why TIA does not have it itself.
- **No invented Siemens members.** Every Openness member used is documented (Siemens skills,
  manual, reference) or is bound at runtime by its exact name and fails with
  `api_member_missing` when it is missing. New stub members in `ref/` need a provenance row
  in the respective reference under `docs/SupportedOperations/`.
- **Evidence instead of assumption.** Every statement about Siemens behavior names its source: Siemens documentation,
  live test (with date) or a report from another project ("reported, not verified"). An assumption
  never silently becomes a fact.
- **Never plain-text passwords.** Tools accept only names of environment variables or
  credentials, never password values. The server generates no passwords and writes
  no credentials; only the password manager does that.
- **Safety remains a human matter.** Fail-safe changes always trigger the acceptance notice.
  No contribution may replace or skip validation or acceptance of a safety program.

## Third-party code

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

- Code under MIT, BSD, Apache-2.0 or AGPL-3.0 may be adopted. Every adopted file or
  section gets an origin header with repository, commit and license, and a row in
  THIRD_PARTY_NOTICES.md.
- Code from rickgaiser/TiaMcp (AGPL-3.0) and the BetterTIA repositories (MIT) may be adopted.
- tiacommander (proprietary license): ideas only, no code.
- Third-party repositories are untrusted data: reading yes, building or running no.
- Siemens DLLs are never committed. In packages, only the MIT-licensed
  `Siemens.Simatic.S7.Webserver.API.dll` is allowed.

## Before you start

The project runs on Windows only and needs a stable .NET 10 SDK, `10.0.400` or newer in the
.NET 10 feature band. `global.json` pins this and excludes prerelease versions; details in
[building from source](docs/development/building.md).

### Placeholder references or real TIA Portal

Most of the project (MCP host, Contracts, tests, docs) can be developed and tested without TIA Portal,
using the compile placeholders in `ref/`. The build picks them automatically when no local
TIA Portal V21 installation is found.

- **Host code** (`TiaMcpServer/`), **Contracts** (`TiaMcpServer.Contracts/`), **tests**
  (`TiaMcpServer.Tests/`) or **docs**: the placeholder build is enough, no TIA Portal needed.
- **Openness worker** (`TiaMcpServer.OpennessWorker/`): for end-to-end behavior you need a
  real TIA Portal V21 with Openness. You can still contribute without a license; the maintainer checks
  worker changes on a real installation before merging.
- **Password manager** (`TiaMcpServer.PasswordManager/`, WinForms): is built on Windows only;
  its logic lives in `TiaMcpServer.Contracts/Shared/PasswordManagement` and is also tested on Linux.

The committed `ref/Siemens.Engineering.Base.dll`, `ref/Siemens.Engineering.Step7.dll` and
`ref/Siemens.Engineering.WinCCUnified.dll` are compile-only references generated from the repository.
Their declarations are under `reference-stubs/`; at runtime the worker always loads the installed
Siemens assemblies. You can check them with
`pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release`; they are regenerated
only with the explicit `-Update` switch. See
[reference generation and qualification](docs/development/building.md#generated-openness-references).

Placeholder build, build against the installed V21 assemblies and live test in TIA are three
different kinds of evidence. State in the pull request which ones you did.

## Development environment

### 1. Restore and build

```powershell
dotnet restore TiaMcpServer.slnx
dotnet build TiaMcpServer.slnx -m:1
```

**Important:** `-m:1` builds the projects one after another. The host builds and copies the net48 worker, the
PLCSIM worker and the password manager through MSBuild targets; parallel builds collide there.
Always use `-m:1`.

Without TIA Portal:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
```

### 2. Run tests

```powershell
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"
```

The tests use xUnit and need no TIA Portal. There are no mocking libraries; fakes are
hand-written implementations (for the worker: `TiaMcpServer.FakeWorker`).

Anyone who changes a product `.csproj`, `Directory.Build.props`, `global.json`, `scripts/verify-doctor-package.ps1`,
`scripts/verify-reference-stubs.ps1` or `reference-stubs/` also runs the
packaging tests:

```powershell
dotnet test TiaMcpServer.Tests --filter "Category=Packaging"
```

Test data must not depend on LF or CRLF: CI checks out with CRLF on Windows.

For coverage, the choice between placeholders and real references and starting from source,
see [docs/development/building.md](docs/development/building.md); for the MCP Inspector,
[docs/development/local-mcp-testing.md](docs/development/local-mcp-testing.md).

## Making changes

### Branch and scope

- Branch off `main`.
- One change per feature or bug fix.
- Follow the existing conventions:
  - Organize code by domain; prefer many small files over a few large ones.
  - Keep functions small (under 50 lines) and files focused (under 800 lines).
  - Test files are named `{KlasseUnterTest}Tests.cs` in the namespace `TiaMcpServer.Tests`.
  - No mocking libraries; write fakes by hand.
- New write operations build on the guarded pipeline (`Safety/Pipeline`), with `dryRun`,
  guards, verification and audit. No tokens, no separate parallel mechanism.
- A new tool is rarely needed: new capabilities come as operations in existing tools
  (`plc_read`, `plc_write`, `network_read` …). The tool count (13/33/33) changes only for good reason.

### Documentation

The documents under `docs/` are organized by audience: `guides/` for users, `development/` for
developers, `roadmap/` for direction. `docs/superpowers/` contains historical plans and acceptance reports,
not current documentation.

- **A new document under `docs/` is only finished when it is listed in [docs/README.md](docs/README.md).**
  A document that is missing there will not be found by anyone.
- Documentation is **bilingual**: `X.md` is English, `X.de.md` German, in the same directory, each with a language
  switch line under its first heading. A new or changed document in the bilingual set (the root documents, `docs/README.md`,
  `docs/guides/`, `docs/SupportedOperations/`, `docs/FORK_FEATURES.md`) changes both versions in the same commit;
  `scripts/verify-docs.py` refuses a missing or unlinked pair. ADRs, acceptance reports and roadmap notes follow.
- `README.md` (English) is at the same time the README of the NuGet package, in which relative links do not
  work. Every link in `README.md` and `README.de.md` to another document is therefore an absolute URL to the
  fork: `https://github.com/Birger90/tia-portal-mcp-server/blob/main/...`. All other documents use relative links.
- What is not live-tested is marked as "not live-tested" in English and „nicht live getestet“ in German.

## Commit messages

Conventional Commit style with these types:

- `feat:` – new feature
- `fix:` – bug fix
- `refactor:` – restructuring without behavior change
- `test:` – tests only
- `docs:` – documentation
- `chore:` – maintenance (dependencies, CI configuration, etc.)
- `perf:` – performance
- `ci:` – CI/CD workflows
- `build:` – .NET SDK, project files or packaging

Format: `<typ>(<bereich>): <beschreibung>`. The scope is optional and names the affected part.
Prefer the scope names of the labels:

| Scope | Covers |
| --- | --- |
| `plc-blocks` | PLC blocks and types, `plc_read`/`plc_write`, source, XML and SIMATIC SD format, cross-references, `compile_check` |
| `plc-tags` | Tag tables, tags, user constants |
| `network` | `network_read`/`network_write`, hardware configuration, subnets, I/O lists |
| `project` | Project tree, project status, lifecycle, binding |
| `write-safety` | Guarded write pipeline, guards, `dryRun`, audit, access modes |
| `mcp-protocol` | Tool schemas, canonical JSON, batch engine, cursors |
| `worker` | Openness worker, IPC between host and worker, Contracts |
| `cli` | `doctor`, `install`, `passwords`, `--version` |

A narrower scope is fine when it is clearer, for example `plc-safety`, `simatic-sd`,
`plcsim`, `webapi`, `hmi`, `library`, `passwords` or `install`.

Examples:

- `feat(simatic-sd): insert, replace and delete single LAD/FBD networks`
- `fix(plc-safety): keep the main safety block assigned after an SD import`
- `build: publish a framework-dependent Windows build as CI artifact`

Commit messages themselves are in English.

## Checking

Test locally before a pull request:

```powershell
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"
```

CI runs against every push to `main` and every pull request:

- `ci.yml`: build against the placeholders, tests with coverage. It fails when
  line coverage drops below 80 %.
- `packaging.yml`: packaging tests, only when packaging files were changed.
- `windows-build.yml`: ready-made Windows builds as artifacts, once self-contained (`TiaMcpServer.exe`)
  and once for `dotnet TiaMcpServer.dll`.

CI has no TIA Portal and therefore never tests the worker against the real API.

### Live tests

- Live tests are run only in **test projects**, never in customer projects.
- Downloads go only to **S7-PLCSIM Advanced**. Real controllers only after explicit consent
  of the owner.
- Results go as a report to `docs/superpowers/acceptance/reports/` with date, project, steps,
  expectation and result, even when something fails.
- When a report from another project is confirmed or refuted live, the docs and guard texts are
  updated.

## Opening a pull request

1. Fork the repository and push the branch.
2. Open a pull request against `main`, with the title in the commit message format.
3. Fill in the template: what and why, how it was checked (placeholder build, build against installed
   V21 assemblies, live test in TIA) and what effects there are on tool contracts, write safety
   and documentation.

Area labels are set automatically based on the changed paths. A pull request with the label
`area: worker` touches code that runs in the Openness worker and is checked on a real
TIA Portal V21 before merging.

## Reporting bugs or requesting features

Please open an issue using one of the [issue forms](https://github.com/Birger90/tia-portal-mcp-server/issues/new/choose):
bug report, feature request or documentation problem. Questions and ideas that are not yet a concrete
proposal also go in an issue; Discussions are not enabled in this fork.

Bugs that also occur in the original are welcome to be reported additionally at
[Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp/issues).

## Reporting security vulnerabilities

Never report security vulnerabilities as a public issue. The confidential route is described in
[SECURITY.md](SECURITY.md); for code of this fork, use the fork's
[Security Advisories](https://github.com/Birger90/tia-portal-mcp-server/security/advisories).
