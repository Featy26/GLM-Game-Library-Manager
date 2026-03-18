using Microsoft.Win32;

namespace GameTransfer.Core.Models;

public class RegistryBackup
{
    public RegistryHive Hive { get; init; }
    public required string SubKey { get; init; }
    public Dictionary<string, (RegistryValueKind Kind, object? Value)> Values { get; init; } = new();
}
