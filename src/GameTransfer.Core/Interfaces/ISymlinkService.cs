namespace GameTransfer.Core.Interfaces;

public interface ISymlinkService
{
    bool CreateJunction(string linkPath, string targetPath);
    bool RemoveJunction(string linkPath);
    bool IsJunction(string path);
}
