using System.Windows;
using GameTransfer.App.ViewModels;
using GameTransfer.App.Views;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Services;
using GameTransfer.Plugins.Steam;
using GameTransfer.Plugins.EpicGames;
using GameTransfer.Plugins.GOG;
using GameTransfer.Plugins.Ubisoft;
using GameTransfer.Plugins.EA;
using GameTransfer.Plugins.BattleNet;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GameTransfer.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GLM", "logs", "log-.txt"),
                rollingInterval: Serilog.RollingInterval.Day)
            .CreateLogger();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IFileTransferService, FileTransferService>();
        services.AddSingleton<IShortcutService, ShortcutService>();
        services.AddSingleton<ISymlinkService, SymlinkService>();
        services.AddSingleton<TransferOrchestrator>();

        // Launcher plugins
        services.AddSingleton<ILauncherPlugin, SteamPlugin>();
        services.AddSingleton<ILauncherPlugin, EpicGamesPlugin>();
        services.AddSingleton<ILauncherPlugin, GOGPlugin>();
        services.AddSingleton<ILauncherPlugin, UbisoftPlugin>();
        services.AddSingleton<ILauncherPlugin, EAPlugin>();
        services.AddSingleton<ILauncherPlugin, BattleNetPlugin>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<GameLibraryViewModel>();
        services.AddSingleton<TransferViewModel>();
        services.AddSingleton<DriveManagerViewModel>();
        services.AddSingleton<LibraryImportViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
