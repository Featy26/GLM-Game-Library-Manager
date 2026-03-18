namespace GameTransfer.Core.Interfaces;

public interface IShortcutService
{
    IReadOnlyList<string> FindShortcutsPointingTo(string targetPath);
    void UpdateShortcut(string shortcutPath, string oldBasePath, string newBasePath);
    void UpdateAllShortcuts(string oldBasePath, string newBasePath);
}
