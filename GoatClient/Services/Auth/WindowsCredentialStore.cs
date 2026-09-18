using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GoatClient.Services.Auth;

/// <summary>
/// Stores secrets in the Windows Credential Manager (generic credentials, current user, encrypted by Windows).
/// Long secrets are split into chunks because a single credential blob is limited to 2560 bytes.
/// </summary>
public sealed class WindowsCredentialStore : ISecureTokenStore
{
    private const string Prefix = "GoatClient/";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int MaxBlobBytes = 2560;
    private const int ErrorNotFound = 1168;
    private const int ChunkChars = MaxBlobBytes / 2;

    public string? Read(string key)
    {
        var countText = ReadSingle(CountTarget(key));
        if (countText is null || !int.TryParse(countText, out var count) || count <= 0 || count > 64)
        {
            return null;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var chunk = ReadSingle(ChunkTarget(key, i));
            if (chunk is null)
            {
                return null;
            }

            builder.Append(chunk);
        }

        return builder.ToString();
    }

    public void Write(string key, string secret)
    {
        Delete(key);
        var chunks = Enumerable.Range(0, (secret.Length + ChunkChars - 1) / ChunkChars)
            .Select(i => secret.Substring(i * ChunkChars, Math.Min(ChunkChars, secret.Length - (i * ChunkChars))))
            .ToList();

        for (var i = 0; i < chunks.Count; i++)
        {
            WriteSingle(ChunkTarget(key, i), chunks[i]);
        }

        WriteSingle(CountTarget(key), chunks.Count.ToString());
    }

    public void Delete(string key)
    {
        var countText = ReadSingle(CountTarget(key));
        var count = int.TryParse(countText, out var c) ? Math.Clamp(c, 0, 64) : 0;
        for (var i = 0; i < Math.Max(count, 16); i++)
        {
            DeleteSingle(ChunkTarget(key, i));
        }

        DeleteSingle(CountTarget(key));
    }

    private static string CountTarget(string key) => $"{Prefix}{key}/count";

    private static string ChunkTarget(string key, int index) => $"{Prefix}{key}/{index}";

    private static string? ReadSingle(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "Reading from the Windows Credential Manager failed.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private static void WriteSingle(string target, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlob = blob,
                CredentialBlobSize = (uint)bytes.Length,
                Persist = CredPersistLocalMachine,
                UserName = "GOAT CLIENT",
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Writing to the Windows Credential Manager failed.");
            }
        }
        finally
        {
            // Clear the unmanaged copy of the secret before freeing it.
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
        }
    }

    private static void DeleteSingle(string target)
    {
        if (!CredDelete(target, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "Deleting from the Windows Credential Manager failed.");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
