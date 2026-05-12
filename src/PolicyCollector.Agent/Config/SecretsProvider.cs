using System.Runtime.InteropServices;

namespace PolicyCollector.Agent.Config;

public sealed class SecretsProvider
{
    private const string ApiKeyTarget = "PolicyCollector/ApiKey";
    private const string HmacSecretTarget = "PolicyCollector/HmacSecret";

    public string? GetApiKey() => ReadCredential(ApiKeyTarget);
    public string? GetHmacSecret() => ReadCredential(HmacSecretTarget);

    public void SaveApiKey(string key) => WriteCredential(ApiKeyTarget, key);
    public void SaveHmacSecret(string secret) => WriteCredential(HmacSecretTarget, secret);

    private static string? ReadCredential(string target)
    {
        try
        {
            if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var credPtr))
                return null;

            using var cred = new SafeCredentialHandle(credPtr);
            var credential = Marshal.PtrToStructure<Credential>(credPtr);
            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)(credential.CredentialBlobSize / sizeof(char)));
        }
        catch
        {
            return null;
        }
    }

    private static void WriteCredential(string target, string value)
    {
        try
        {
            var cred = new Credential
            {
                TargetName = Marshal.StringToCoTaskMemUni(target),
                CredentialBlob = Marshal.StringToCoTaskMemUni(value),
                CredentialBlobSize = (uint)((value.Length + 1) * sizeof(char)),
                Type = CRED_TYPE_GENERIC,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = Marshal.StringToCoTaskMemUni("agent")
            };

            if (!CredWrite(ref cred, 0))
                throw new InvalidOperationException("Failed to save credential");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to write credential: {ex.Message}", ex);
        }
    }

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credPtr);

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern bool CredFree(IntPtr credPtr);

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public IntPtr TargetName;
        public IntPtr CredentialBlob;
        public uint CredentialBlobSize;
        public uint Type;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    private sealed class SafeCredentialHandle : IDisposable
    {
        private readonly IntPtr _handle;

        public SafeCredentialHandle(IntPtr handle) => _handle = handle;

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
                CredFree(_handle);
        }
    }
}
