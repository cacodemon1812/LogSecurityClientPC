using Microsoft.Win32;

namespace PolicyCollector.Agent.Infrastructure;

public sealed class RegistryReader
{
    public string? GetString(RegistryHive hive, string keyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    public int? GetDword(RegistryHive hive, string keyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            var value = key?.GetValue(valueName);
            return value is int intVal ? intVal : null;
        }
        catch
        {
            return null;
        }
    }

    public long? GetQword(RegistryHive hive, string keyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            var value = key?.GetValue(valueName);
            return value is long longVal ? longVal : null;
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetSubKeys(RegistryHive hive, string keyPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public IReadOnlyDictionary<string, object?> GetAllValues(RegistryHive hive, string keyPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            if (key is null) return new Dictionary<string, object?>();

            var result = new Dictionary<string, object?>();
            foreach (var valueName in key.GetValueNames())
            {
                result[valueName] = key.GetValue(valueName);
            }
            return result;
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    public bool KeyExists(RegistryHive hive, string keyPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }
}
