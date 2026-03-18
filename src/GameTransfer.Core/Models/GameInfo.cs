namespace GameTransfer.Core.Models;

public class GameInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string InstallPath { get; init; }
    public long SizeBytes { get; init; }
    public LauncherType Launcher { get; init; }
    public string? ExecutablePath { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}
