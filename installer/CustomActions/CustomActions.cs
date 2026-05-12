using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace PolicyCollector.Installer.CustomActions;

public static class CustomActions
{
    [CustomAction]
    public static ActionResult SaveCredentials(Session session)
    {
        session.Log("SaveCredentials custom action starting");

        try
        {
            var data = new CustomActionData(session["CustomActionData"]);
            var apiKey = data["API_KEY"];
            var hmacSecret = data["HMAC_SECRET"];

            if (!string.IsNullOrEmpty(apiKey))
            {
                WriteCredential("PolicyCollector/ApiKey", apiKey);
                session.Log("API key saved to Credential Manager");
            }

            if (!string.IsNullOrEmpty(hmacSecret))
            {
                WriteCredential("PolicyCollector/HmacSecret", hmacSecret);
                session.Log("HMAC secret saved to Credential Manager");
            }

            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"SaveCredentials failed: {ex.Message}");
            return ActionResult.Failure;
        }
    }

    [CustomAction]
    public static ActionResult WriteAgentConfig(Session session)
    {
        session.Log("WriteAgentConfig custom action starting");

        try
        {
            var data = new CustomActionData(session["CustomActionData"]);
            var backendUrl = data["BACKEND_URL"];
            var intervalMinStr = data["INTERVAL_MIN"];

            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "PolicyCollector", "appsettings.json");

            if (File.Exists(configPath))
            {
                session.Log("Config file already exists, preserving existing configuration");
                return ActionResult.Success;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

            var config = new
            {
                Logging = new
                {
                    LogLevel = new
                    {
                        Default = "Information"
                    }
                },
                Agent = new
                {
                    IntervalMinutes = int.TryParse(intervalMinStr, out var interval) ? interval : 60,
                    CollectOnStartup = true,
                    CollectorTimeoutSeconds = 300,
                    Modules = new
                    {
                        HostInfo = true,
                        Gpo = true,
                        SecurityPolicy = true,
                        Firewall = true,
                        Defender = true,
                        BitLocker = true,
                        AppInventory = true,
                        AppxPackages = true,
                        Services = true,
                        ScheduledTasks = true,
                        StartupEntries = true
                    }
                },
                Transport = new
                {
                    BackendUrl = string.IsNullOrEmpty(backendUrl) ? "https://localhost/api/v1/ingest" : backendUrl,
                    TimeoutSeconds = 30,
                    MaxRetries = 3,
                    InitialRetryDelaySeconds = 1
                },
                LocalQueue = new
                {
                    MaxEntries = 1000,
                    MaxAgeHours = 168,
                    RetryIntervalMinutes = 5
                }
            };

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);

            session.Log($"Config written to {configPath}");
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"WriteAgentConfig failed: {ex.Message}");
            return ActionResult.Failure;
        }
    }

    [CustomAction]
    public static ActionResult InstallService(Session session)
    {
        session.Log("InstallService custom action starting");

        try
        {
            var data = new CustomActionData(session["CustomActionData"]);
            var installDir = data["INSTALLDIR"];
            var exePath = Path.Combine(installDir, "PolicyCollector.Agent.exe");

            if (!File.Exists(exePath))
            {
                session.Log($"Agent executable not found: {exePath}");
                return ActionResult.Failure;
            }

            RunSc("stop PolicyCollectorSvc", session);
            System.Threading.Thread.Sleep(1000);
            RunSc("delete PolicyCollectorSvc", session);
            System.Threading.Thread.Sleep(1000);

            RunSc($"create PolicyCollectorSvc binPath= \"{exePath}\" start= auto obj= LocalSystem", session);
            RunSc("description PolicyCollectorSvc \"Collects system policy configuration and endpoints\"", session);
            RunSc("failure PolicyCollectorSvc reset= 86400 actions= restart/60000/restart/60000/restart/60000", session);
            RunSc("start PolicyCollectorSvc", session);

            session.Log("Service installed successfully");
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"InstallService failed: {ex.Message}");
            return ActionResult.Failure;
        }
    }

    [CustomAction]
    public static ActionResult UninstallService(Session session)
    {
        session.Log("UninstallService custom action starting");

        try
        {
            RunSc("stop PolicyCollectorSvc", session);
            System.Threading.Thread.Sleep(2000);
            RunSc("delete PolicyCollectorSvc", session);
            return ActionResult.Success;
        }
        catch
        {
            return ActionResult.Success;
        }
    }

    private static void RunSc(string arguments, Session session)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                throw new InvalidOperationException("Failed to start sc.exe");

            process.WaitForExit(10000);
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            session.Log($"sc.exe {arguments} -> Exit: {process.ExitCode}");
            if (!string.IsNullOrEmpty(output))
                session.Log($"  Output: {output}");
            if (!string.IsNullOrEmpty(error))
                session.Log($"  Error: {error}");
        }
        catch (Exception ex)
        {
            session.Log($"RunSc failed for '{arguments}': {ex.Message}");
        }
    }

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref CREDENTIAL credential, [In] uint flags);

    private static void WriteCredential(string target, string secret)
    {
        var blob = System.Text.Encoding.Unicode.GetBytes(secret);
        var cred = new CREDENTIAL
        {
            TargetName = target,
            CredentialBlobSize = (uint)blob.Length,
            CredentialBlob = Marshal.AllocHGlobal(blob.Length),
            Persist = 2,
            Type = 1
        };

        try
        {
            Marshal.Copy(blob, 0, cred.CredentialBlob, blob.Length);
            if (!CredWrite(ref cred, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(cred.CredentialBlob);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
