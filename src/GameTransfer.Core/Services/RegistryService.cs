using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using Microsoft.Win32;
using Serilog;

namespace GameTransfer.Core.Services;

public class RegistryService : IRegistryService
{
    public string? ReadValue(RegistryHive hive, string subKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey);
            if (key is null)
            {
                Log.Warning("Registry key not found: {Hive}\\{SubKey}", hive, subKey);
                return null;
            }

            var value = key.GetValue(valueName);
            return value?.ToString();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read registry value {Hive}\\{SubKey}\\{ValueName}", hive, subKey, valueName);
            return null;
        }
    }

    public void WriteValue(RegistryHive hive, string subKey, string valueName, object value, RegistryValueKind kind = RegistryValueKind.String)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey, writable: true)
                ?? baseKey.CreateSubKey(subKey, writable: true);

            key.SetValue(valueName, value, kind);
            Log.Debug("Wrote registry value {Hive}\\{SubKey}\\{ValueName}", hive, subKey, valueName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to write registry value {Hive}\\{SubKey}\\{ValueName}", hive, subKey, valueName);
            throw;
        }
    }

    public void DeleteKey(RegistryHive hive, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            Log.Information("Deleted registry key {Hive}\\{SubKey}", hive, subKey);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete registry key {Hive}\\{SubKey}", hive, subKey);
            throw;
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey);
            if (key is null)
            {
                Log.Warning("Registry key not found: {Hive}\\{SubKey}", hive, subKey);
                return Array.Empty<string>();
            }

            return key.GetSubKeyNames().ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get sub key names for {Hive}\\{SubKey}", hive, subKey);
            return Array.Empty<string>();
        }
    }

    public RegistryBackup BackupKey(RegistryHive hive, string subKey)
    {
        var backup = new RegistryBackup
        {
            Hive = hive,
            SubKey = subKey
        };

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey);
            if (key is null)
            {
                Log.Warning("Registry key not found for backup: {Hive}\\{SubKey}", hive, subKey);
                return backup;
            }

            foreach (var valueName in key.GetValueNames())
            {
                var value = key.GetValue(valueName);
                var kind = key.GetValueKind(valueName);
                backup.Values[valueName] = (kind, value);
            }

            Log.Information("Backed up {Count} values from {Hive}\\{SubKey}", backup.Values.Count, hive, subKey);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to backup registry key {Hive}\\{SubKey}", hive, subKey);
        }

        return backup;
    }

    public void RestoreBackup(RegistryBackup backup)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(backup.Hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(backup.SubKey, writable: true)
                ?? baseKey.CreateSubKey(backup.SubKey, writable: true);

            foreach (var (valueName, (kind, value)) in backup.Values)
            {
                if (value is not null)
                {
                    key.SetValue(valueName, value, kind);
                }
            }

            Log.Information("Restored {Count} values to {Hive}\\{SubKey}", backup.Values.Count, backup.Hive, backup.SubKey);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore registry backup to {Hive}\\{SubKey}", backup.Hive, backup.SubKey);
            throw;
        }
    }
}
