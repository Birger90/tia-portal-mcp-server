using System.ComponentModel;
using System.Runtime.InteropServices;
using TiaMcpServer.Contracts.Shared.PasswordManagement;

namespace TiaMcpServer.PasswordManager;

/// <summary>
/// Windows Credential Manager generic credentials through the documented Win32 functions of advapi32 (wincred.h):
/// <c>CredWriteW</c> (type <c>CRED_TYPE_GENERIC</c>, persistence <c>CRED_PERSIST_LOCAL_MACHINE</c>: this Windows user on
/// this computer, not roamed), <c>CredDeleteW</c>, <c>CredReadW</c> and <c>CredFree</c>. The password is written as
/// UTF-16LE, the form <c>cmdkey /generic ... /pass</c> stores and the server's <c>WindowsCredentialReader</c> reads. The
/// unmanaged copy is zeroed before it is freed; a check reads only the blob size and zeroes the blob, never looking at it.
/// Only the password manager writes credentials; the server process only reads them.
/// </summary>
internal sealed class WindowsCredentialStore : ICredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const string UserName = "TiaMcpServer";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);

    public string? Write(string target, char[] secret, string comment)
    {
        if (secret.Length == 0) return "Bitte ein Passwort eingeben.";
        var size = checked(secret.Length * 2);
        var blob = IntPtr.Zero;
        var targetName = IntPtr.Zero;
        var commentText = IntPtr.Zero;
        var user = IntPtr.Zero;
        try
        {
            blob = Marshal.AllocHGlobal(size);
            Marshal.Copy(secret, 0, blob, secret.Length);
            targetName = Marshal.StringToHGlobalUni(target);
            commentText = Marshal.StringToHGlobalUni(comment);
            user = Marshal.StringToHGlobalUni(UserName);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = targetName,
                Comment = commentText,
                CredentialBlobSize = size,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = user,
            };
            if (!CredWrite(ref credential, 0))
                return "Die Anmeldeinformation konnte nicht gespeichert werden: " + Win32Message(Marshal.GetLastWin32Error());
            return null;
        }
        finally
        {
            if (blob != IntPtr.Zero)
            {
                for (var offset = 0; offset < size; offset++) Marshal.WriteByte(blob, offset, 0);
                Marshal.FreeHGlobal(blob);
            }

            if (targetName != IntPtr.Zero) Marshal.FreeHGlobal(targetName);
            if (commentText != IntPtr.Zero) Marshal.FreeHGlobal(commentText);
            if (user != IntPtr.Zero) Marshal.FreeHGlobal(user);
        }
    }

    public string? Delete(string target)
    {
        if (CredDelete(target, CredTypeGeneric, 0)) return null;
        var error = Marshal.GetLastWin32Error();
        return error == ErrorNotFound ? null : "Die Anmeldeinformation konnte nicht gelöscht werden: " + Win32Message(error);
    }

    public CredentialCheckResult Check(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var handle))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound
                ? new CredentialCheckResult(CredentialState.Missing, "Keine Anmeldeinformation mit diesem Namen für diesen Windows-Benutzer vorhanden.")
                : new CredentialCheckResult(CredentialState.Failed, "Die Prüfung ist fehlgeschlagen: " + Win32Message(error));
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            var size = credential.CredentialBlobSize;
            var blob = credential.CredentialBlob;
            if (blob != IntPtr.Zero)
                for (var offset = 0; offset < size; offset++) Marshal.WriteByte(blob, offset, 0);
            if (size <= 0 || blob == IntPtr.Zero)
                return new CredentialCheckResult(CredentialState.Unusable, "Die Anmeldeinformation ist vorhanden, ihr Passwort ist aber leer.");
            if (size % 2 != 0)
                return new CredentialCheckResult(CredentialState.Unusable, "Die Anmeldeinformation ist vorhanden, enthält aber kein UTF-16-Passwort; bitte hier neu speichern.");
            return new CredentialCheckResult(CredentialState.Present, "Die Anmeldeinformation ist vorhanden (der Wert wird nicht angezeigt).");
        }
        finally
        {
            CredFree(handle);
        }
    }

    private static string Win32Message(int error) => $"{new Win32Exception(error).Message} (Win32-Fehler {error}).";
}
