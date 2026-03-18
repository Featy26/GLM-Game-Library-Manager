using GameTransfer.Core.Models;
using Microsoft.Win32;

namespace GameTransfer.Core.Interfaces;

public interface IRegistryService
{
    string? ReadValue(RegistryHive hive, string subKey, string valueName);
    void WriteValue(RegistryHive hive, string subKey, string valueName, object value, RegistryValueKind kind = RegistryValueKind.String);
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string subKey);
    void DeleteKey(RegistryHive hive, string subKey);
    RegistryBackup BackupKey(RegistryHive hive, string subKey);
    void RestoreBackup(RegistryBackup backup);
}
