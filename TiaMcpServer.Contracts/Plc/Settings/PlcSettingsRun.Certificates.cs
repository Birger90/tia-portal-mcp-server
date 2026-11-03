using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using M = TiaMcpServer.Contracts.Plc.Settings.PlcCertificateMethods;
using N = TiaMcpServer.Contracts.Plc.Settings.PlcCertificateNames;

namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Issue #14: certificates of the CPU's local certificate store and the dynamic certificate settings (see
/// <see cref="PlcCertificateMethods"/> for the evidence). No operation trusts a certificate or answers a TLS question: the read
/// reports fingerprints (SHA-256 over the DER bytes of a temporary <c>Export(FileInfo)</c>) so the human can compare them with the
/// ones a TLS prompt of <c>plc_download</c>, <c>plc_tia_online</c> or <c>plc_station_upload</c> shows (K27/K52/K53).
/// </summary>
public static partial class PlcSettingsRun
{
    private static readonly string[] CertificateProperties = { "Id", "SubjectCommonName", "ValidUntil", "HasPrivateKey" };

    private static readonly string[] FingerprintProperties = { "Sha256", "Sha1Thumbprint", "Subject", "Issuer", "NotBefore", "NotAfter", "FingerprintError" };

    private static PlcSettingsReadInfo ReadCertificates(IReadOnlyList<PlcSettingsContext> plcs, PlcSettingsReadInfo result)
    {
        foreach (var plc in plcs)
        {
            var entry = new PlcSettingsPlcInfo { PlcName = plc.PlcName ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty };
            var fingerprints = new Dictionary<object, PlcCertificateFingerprintInfo>(ReferenceComparer.Instance);
            entry.Sections.Add(Section(plc, "localCertificateManager", plc.CpuOwners(), N.Manager, N.ManagerProperties, manager => new List<PlcSettingsCollectionInfo>
            {
                Collection(plc, "Certificates", () => Get(Get(manager, "LocalCertificateStore") ?? throw Missing("LocalCertificateManager.LocalCertificateStore"), "Certificates"), "Id",
                    CertificateProperties.Concat(FingerprintProperties).ToArray(),
                    special: (certificate, property) => FingerprintProperties.Contains(property) ? FingerprintValue(plc, certificate, property, fingerprints) : null),
            }, note: "Fingerprints are computed from a temporary Export(FileInfo) of each certificate (deleted afterwards); SHA-256 over the DER bytes, as the TLS prompt of plc_download/plc_tia_online/plc_station_upload lists them. Nothing is trusted here."));
            entry.Sections.Add(Section(plc, "certificateManagement", plc.CpuOwners(), N.Management, N.ManagementProperties, management => new List<PlcSettingsCollectionInfo>
            {
                Collection(plc, "CertificateSupportedServices", () => Get(management, "CertificateSupportedServices"), "Id",
                    new[] { "Id", "ServiceType", "ServiceGroupName", "ApplicationUri", "Guid" }),
            }, note: "Dynamic certificate management (manual: PLC V3.0 and later)."));
            entry.Sections.Add(AssignmentSection(plc, fingerprints));
            result.Plcs.Add(entry);
        }

        return result;
    }

    private static RuntimeBindingException Missing(string what) => new(WorkerFailureCategories.WorkerOperationFailed, $"{what} returned null.");

    /// <summary>The certificates assigned through the dynamic attributes (web server: the CPU; OPC UA server: the item that has the attribute).</summary>
    private static PlcSettingsSectionInfo AssignmentSection(PlcSettingsContext context, Dictionary<object, PlcCertificateFingerprintInfo> fingerprints)
    {
        var section = new PlcSettingsSectionInfo { Name = "certificateAssignments", Offered = context.Cpu is not null };
        if (context.Cpu is null) return section;
        section.Owner = context.Owner(context.Cpu);
        foreach (var role in N.Roles)
        {
            var owner = AttributeOwner(context, role);
            var name = N.AttributeOf(role);
            if (owner is null)
            {
                section.Properties.Add(new PlcSettingPropertyInfo { Name = name, Value = new RuntimeValueInfo { State = RuntimeValueStates.Missing, Error = $"No device item of PLC '{context.PlcName}' answers GetAttribute(\"{name}\")." } });
                continue;
            }

            section.Properties.Add(new PlcSettingPropertyInfo
            {
                Name = name,
                Value = ReadAttribute(context, owner, name, certificate => certificate is null ? null
                    : $"Id {RuntimeServiceValues.Project(SafeGet(certificate, "Id")).Value}, {SafeGet(certificate, "SubjectCommonName")}, sha256 {Fingerprint(context, certificate, fingerprints).Sha256 ?? "(unavailable)"} at {context.Owner(owner)}"),
            });
        }

        section.Note = "The dynamic attributes WebserverCertificate (PLC) and OpcUaServerCertificate (OPC UA submodule) of the manual \"Managing certificate\".";
        return section;
    }

    /// <summary>The device item that answers the role's attribute: the CPU for the web server, the first item that has OpcUaServerCertificate otherwise.</summary>
    private static object? AttributeOwner(PlcSettingsContext context, string role)
    {
        var name = N.AttributeOf(role);
        foreach (var owner in role == N.WebserverRole ? new[] { context.Cpu! } : context.CpuAndItems())
        {
            try
            {
                GetAttribute(owner, name);
                return owner;
            }
            catch (RuntimeBindingException)
            {
            }
            catch (Exception exception) when (context.Recoverable(exception))
            {
            }
        }

        return null;
    }

    private static object? SafeGet(object target, string name)
    {
        try
        {
            return Get(target, name);
        }
        catch (RuntimeBindingException)
        {
            return null;
        }
    }

    private static RuntimeValueInfo FingerprintValue(PlcSettingsContext context, object certificate, string property, Dictionary<object, PlcCertificateFingerprintInfo> cache)
    {
        var info = Fingerprint(context, certificate, cache);
        var value = property switch
        {
            "Sha256" => info.Sha256,
            "Sha1Thumbprint" => info.Sha1Thumbprint,
            "Subject" => info.Subject,
            "Issuer" => info.Issuer,
            "NotBefore" => info.NotBefore,
            "NotAfter" => info.NotAfter,
            _ => info.Error,
        };
        return RuntimeServiceValues.Project(value);
    }

    /// <summary>Exports the certificate to a temporary file, parses it as X.509 and hashes its DER bytes; the file is always deleted.</summary>
    internal static PlcCertificateFingerprintInfo Fingerprint(PlcSettingsContext context, object certificate, Dictionary<object, PlcCertificateFingerprintInfo> cache)
    {
        if (cache.TryGetValue(certificate, out var known)) return known;
        var info = new PlcCertificateFingerprintInfo();
        var path = Path.Combine(Path.GetTempPath(), $"tia-mcp-certificate-{Guid.NewGuid():N}{N.FingerprintExportExtension}");
        try
        {
            var export = RequireMethod(certificate, "Export", new[] { typeof(FileInfo) }, "Certificate.Export(FileInfo)");
            Call(export, certificate, new FileInfo(path));
            if (!File.Exists(path)) throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed, "Certificate.Export(FileInfo) wrote no file.");
            Describe(info, File.ReadAllBytes(path));
        }
        catch (RuntimeBindingException exception)
        {
            info.Error = exception.Message;
        }
        catch (CryptographicException exception)
        {
            info.Error = $"The exported file is no X.509 certificate this server can read ({exception.Message}).";
        }
        catch (Exception exception) when (context.Recoverable(exception) || exception is IOException or UnauthorizedAccessException)
        {
            info.Error = RuntimeServiceValues.Bound($"{exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        cache[certificate] = info;
        return info;
    }

    /// <summary>The X.509 fields of an exported certificate (DER or Base64/PEM): SHA-256 over <c>RawData</c> as the TLS policy hashes it.</summary>
    internal static void Describe(PlcCertificateFingerprintInfo info, byte[] bytes)
    {
        using var certificate = new X509Certificate2(bytes);
        using var sha256 = SHA256.Create();
        info.Sha256 = Hex(sha256.ComputeHash(certificate.RawData));
        info.Sha1Thumbprint = certificate.Thumbprint;
        info.Subject = certificate.Subject;
        info.Issuer = certificate.Issuer;
        info.NotBefore = certificate.NotBefore.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        info.NotAfter = certificate.NotAfter.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }

    private static string Hex(byte[] bytes)
    {
        var builder = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static ReferenceComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static bool WriteCertificates(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info,
        PlcSettingsResultInfo result, List<string> warnings)
    {
        if (PlcSettingsCatalog.Find(method) is not { Issue: 14 }) return false;
        switch (method)
        {
            case M.AssignCpuCertificate:
            case M.UnassignCpuCertificate:
                AssignCertificate(method, context, info, result);
                return true;
            case M.SetCertificateManagement:
            case M.AddCertificateSupportedService:
            case M.UpdateCertificateSupportedService:
            case M.RemoveCertificateSupportedService:
                Management(method, definition, context, info, result);
                return true;
        }

        var (manager, owner) = RequireService(context, context.CpuOwners(), N.Manager, CpuWhere(context));
        Describe(context, result, owner, N.Manager);
        if (method == M.SetCertificateManager)
        {
            SetProperties(context, manager, "LocalCertificateManager", info.Settings, definition.Settings, result);
            return true;
        }

        var store = Get(manager, "LocalCertificateStore") ?? throw Missing("LocalCertificateManager.LocalCertificateStore");
        var certificates = Get(store, "Certificates") ?? throw Missing("LocalCertificateStore.Certificates");
        var cache = new Dictionary<object, PlcCertificateFingerprintInfo>(ReferenceComparer.Instance);
        switch (method)
        {
            case M.CreateCpuCertificate:
            {
                var usage = info.Usage ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "usage is required. Nothing was changed.");
                var getTemplate = RequireMethodByShape(store, "GetCertificateTemplate", p => p.Length == 1 && p[0].ParameterType.IsEnum, "GetCertificateTemplate(CertificateUsage)");
                var usageValue = ConvertValue(getTemplate.GetParameters()[0].ParameterType, usage, "usage");
                // Every SAN type is converted before anything is created.
                var names = (info.SubjectAlternativeNames ?? new List<string>()).Select(entry =>
                {
                    var colon = entry.IndexOf(':');
                    if (colon <= 0) throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"subjectAlternativeNames entry '{entry}' must be Type:Value. Nothing was changed.");
                    return (Type: entry.Substring(0, colon), Value: entry.Substring(colon + 1));
                }).ToList();
                var template = Call(getTemplate, store, usageValue) ?? throw Missing("LocalCertificateStore.GetCertificateTemplate");
                result.Calls.Add($"LocalCertificateStore.GetCertificateTemplate(CertificateUsage.{usage})");
                if (info.Settings.Count > 0) SetProperties(context, template, "CertificateTemplate", info.Settings, definition.Settings, result);
                if (names.Count > 0)
                {
                    var sans = Get(template, "SubjectAlternativeNames") ?? throw Missing("CertificateTemplate.SubjectAlternativeNames");
                    var createSan = RequireMethodByShape(sans, "Create", p => p.Length == 2 && p[0].ParameterType.IsEnum && p[1].ParameterType == typeof(string),
                        "SubjectAlternativeNameComposition.Create(SubjectAlternativeNameType, String)");
                    foreach (var (type, value) in names)
                    {
                        Call(createSan, sans, ConvertValue(createSan.GetParameters()[0].ParameterType, type, "subjectAlternativeNames"), value);
                        result.Calls.Add($"SubjectAlternativeNames.Create({type}, \"{value}\")");
                    }
                }

                var create = RequireMethodByShape(certificates, "Create", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(template), "CertificateComposition.Create(CertificateTemplate)");
                var created = Call(create, certificates, template) ?? throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed, "CertificateComposition.Create returned null.");
                result.Calls.Add("CertificateComposition.Create(CertificateTemplate)");
                ReportCertificate(context, created, certificates, result, cache, "created");
                return true;
            }

            case M.ImportCpuCertificate:
            {
                RequireExistingFile(info.FilePath, "filePath");
                object? imported = null;
                if (info.PasswordVariable is not null)
                {
                    var import = RequireMethod(certificates, "Import", new[] { typeof(FileInfo), typeof(System.Security.SecureString) }, "CertificateComposition.Import(FileInfo, SecureString)");
                    WithPassword(context, info.PasswordVariable, "CertificateFile", result, secure => imported = Call(import, certificates, new FileInfo(info.FilePath!), secure));
                    result.Calls.Add("CertificateComposition.Import(FileInfo, SecureString)");
                }
                else
                {
                    var import = RequireMethod(certificates, "Import", new[] { typeof(FileInfo) }, "CertificateComposition.Import(FileInfo)");
                    imported = Call(import, certificates, new FileInfo(info.FilePath!));
                    result.Calls.Add("CertificateComposition.Import(FileInfo)");
                }

                ReportCertificate(context, imported ?? throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed, "CertificateComposition.Import returned null."),
                    certificates, result, cache, "imported");
                warnings.Add("The imported certificate is used by the CPU only after it is assigned (assign_cpu_certificate) and downloaded; compare its sha256 with the source before you rely on it. Nothing was trusted.");
                return true;
            }

            case M.ExportCpuCertificate:
            {
                var certificate = FindCertificate(context, certificates, info);
                result.Target = $"certificate {info.CertificateId}";
                var export = RequireMethod(certificate, "Export", new[] { typeof(FileInfo) }, "Certificate.Export(FileInfo)");
                result.File = WriteFile(context, info.FilePath, info.Overwrite, "Certificate.Export(FileInfo)", temporary => Call(export, certificate, new FileInfo(temporary)), result.Messages);
                result.Calls.Add("Certificate.Export(FileInfo)");
                return true;
            }

            case M.DeleteCpuCertificate:
            {
                var certificate = FindCertificate(context, certificates, info);
                result.Target = $"certificate {info.CertificateId}";
                Delete(certificate, "Certificate.Delete()", result);
                Exists(result, "certificate", false, Elements(certificates, "Certificates").Any(c => c is not null && IdOf(c) == info.CertificateId), $"Certificate {info.CertificateId}", context.PlcName);
                return true;
            }
        }

        return false;
    }

    private static string? IdOf(object certificate) => RuntimeServiceValues.Project(SafeGet(certificate, "Id")).Value;

    private static object FindCertificate(PlcSettingsContext context, object certificates, PlcSettingsRequestInfo info)
    {
        var id = info.CertificateId ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "certificateId is required. Nothing was changed.");
        var matches = Elements(certificates, "LocalCertificateStore.Certificates").Where(c => c is not null && IdOf(c!) == id).ToList();
        return matches.Count switch
        {
            1 => matches[0]!,
            0 => throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"The local certificate store of PLC '{context.PlcName}' has no certificate with Id {id} (get_cpu_certificates lists them). Nothing was changed."),
            _ => throw new RuntimeBindingException(WorkerFailureCategories.TargetAmbiguous, $"Several certificates of PLC '{context.PlcName}' have Id {id}. Nothing was changed."),
        };
    }

    /// <summary>The new certificate's Id and fingerprint, and that the store lists it now.</summary>
    private static void ReportCertificate(PlcSettingsContext context, object certificate, object certificates, PlcSettingsResultInfo result,
        Dictionary<object, PlcCertificateFingerprintInfo> cache, string what)
    {
        var id = IdOf(certificate);
        result.Target = $"certificate {id ?? "(id unreadable)"}";
        result.Items.Add($"Id {id ?? "(unreadable)"}");
        var fingerprint = Fingerprint(context, certificate, cache);
        result.Items.Add(fingerprint.Sha256 is null ? $"sha256 unavailable: {fingerprint.Error}" : $"sha256 {fingerprint.Sha256}");
        if (fingerprint.Subject is not null) result.Items.Add($"subject {fingerprint.Subject}");
        Exists(result, "certificate", true, Elements(certificates, "Certificates").Any(c => c is not null && (ReferenceEquals(c, certificate) || Equals(c, certificate))),
            $"The {what} certificate", context.PlcName);
    }

    private static void AssignCertificate(string method, PlcSettingsContext context, PlcSettingsRequestInfo info, PlcSettingsResultInfo result)
    {
        var role = info.CertificateRole ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "certificateRole is required. Nothing was changed.");
        var attribute = N.AttributeOf(role);
        var owner = AttributeOwner(context, role) ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
            $"No device item of PLC '{context.PlcName}' answers GetAttribute(\"{attribute}\"). Nothing was changed.");
        Describe(context, result, owner, null);
        object? certificate = null;
        if (method == M.AssignCpuCertificate)
        {
            var (manager, _) = RequireService(context, context.CpuOwners(), N.Manager, CpuWhere(context));
            var store = Get(manager, "LocalCertificateStore") ?? throw Missing("LocalCertificateManager.LocalCertificateStore");
            certificate = FindCertificate(context, Get(store, "Certificates") ?? throw Missing("LocalCertificateStore.Certificates"), info);
        }

        result.Target = $"{attribute}";
        var before = ReadAttribute(context, owner, attribute, value => value is null ? null : IdOf(value));
        SetAttribute(owner, attribute, certificate);
        result.Calls.Add($"SetAttribute(\"{attribute}\", {(certificate is null ? "null" : "Certificate")})");
        var after = GetAttribute(owner, attribute);
        var matched = certificate is null ? after is null : after is not null && (ReferenceEquals(after, certificate) || after.Equals(certificate));
        result.Changes.Add(new PlcSettingChangeInfo
        {
            Name = attribute, Requested = certificate is null ? null : info.CertificateId, Before = before,
            After = RuntimeServiceValues.Project(after is null ? null : IdOf(after)), Matched = matched,
        });
        if (!matched)
            throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                $"{attribute} of {result.Owner} does not read back as {(certificate is null ? "unassigned" : $"certificate {info.CertificateId}")}. Inspect it with plc_read get_cpu_certificates.");
    }

    private static void Management(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info, PlcSettingsResultInfo result)
    {
        var (management, owner) = RequireService(context, context.CpuOwners(), N.Management, CpuWhere(context));
        Describe(context, result, owner, N.Management);
        if (method == M.SetCertificateManagement)
        {
            SetProperties(context, management, "CertificateManagementConfiguration", info.Settings, definition.Settings, result);
            return;
        }

        var services = Get(management, "CertificateSupportedServices") ?? throw Missing("CertificateManagementConfiguration.CertificateSupportedServices");
        var idText = info.CertificateId ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "certificateId is required. Nothing was changed.");
        if (!uint.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "certificateId of a certificate supported service must be an unsigned integer (UInt32). Nothing was changed.");
        result.Target = $"supported service {idText}";
        object? FindService() => Find(services, typeof(uint), id, "CertificateSupportedServiceComposition.Find(UInt32)");
        switch (method)
        {
            case M.AddCertificateSupportedService:
            {
                var create = RequireMethodByShape(services, "Create", p => p.Length == 3 && p[0].ParameterType == typeof(uint) && p[1].ParameterType == typeof(string) && p[2].ParameterType.IsEnum,
                    "CertificateSupportedServiceComposition.Create(UInt32, String, CertificateSupportedServiceName)");
                var type = ConvertValue(create.GetParameters()[2].ParameterType, info.ServiceType ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "serviceType is required. Nothing was changed."), "serviceType");
                Call(create, services, id, info.Name ?? string.Empty, type);
                result.Calls.Add($"CertificateSupportedServiceComposition.Create({idText}, \"{info.Name}\", {info.ServiceType})");
                var created = FindService();
                Exists(result, "service", true, created is not null, $"Certificate supported service {idText}", context.PlcName);
                Compare(context, result, "ServiceGroupName", info.Name ?? string.Empty, null, ReadValue(context, created!, "ServiceGroupName"), "CertificateSupportedService");
                break;
            }

            case M.UpdateCertificateSupportedService:
                SetProperties(context, FindService() ?? throw NotFoundService(context, idText), "CertificateSupportedService", info.Settings, definition.Settings, result);
                break;
            default:
                Delete(FindService() ?? throw NotFoundService(context, idText), "CertificateSupportedService.Delete()", result);
                Exists(result, "service", false, FindService() is not null, $"Certificate supported service {idText}", context.PlcName);
                break;
        }
    }

    private static RuntimeBindingException NotFoundService(PlcSettingsContext context, string id)
        => new(WorkerFailureCategories.TargetNotFound, $"PLC '{context.PlcName}' has no certificate supported service {id}. Nothing was changed.");
}
