using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Registers the Storage services, with the disk readers of the operating system the server runs on.
/// </summary>
public static class StorageServices
{
    /// <summary>
    /// Adds every Storage service to the container.
    /// </summary>
    /// <param name="services">The container.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMountTable, DriveMountTable>();

        // Finds disks with no file system and reads SMART data. Each OS reports both its own way.
        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IDiskInventory, LinuxDiskInventory>();
            services.AddSingleton<ISmartReader, LinuxSmartReader>();
            services.AddSingleton<IRemounter, LinuxRemounter>();
            services.AddSingleton<IDiskFormatter, LinuxDiskFormatter>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IDiskInventory, MacDiskInventory>();
            services.AddSingleton<ISmartReader, MacSmartReader>();
            services.AddSingleton<IRemounter, MacRemounter>();
            services.AddSingleton<IDiskFormatter, MacDiskFormatter>();
        }
        else if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IDiskInventory, WindowsDiskInventory>();
            services.AddSingleton<ISmartReader, WindowsSmartReader>();
            services.AddSingleton<IRemounter, WindowsRemounter>();
            services.AddSingleton<IDiskFormatter, WindowsDiskFormatter>();
        }
        else
        {
            services.AddSingleton<IDiskInventory, NoDiskInventory>();
            services.AddSingleton<ISmartReader, NoSmartReader>();
            services.AddSingleton<IRemounter, NoRemounter>();
            services.AddSingleton<IDiskFormatter, NoDiskFormatter>();
        }

        services.AddSingleton<VolumeProbe>();
        services.AddSingleton<StorageSettings>();
        services.AddSingleton<StorageActivity>();
        services.AddSingleton<MonitorMuter>();
        services.AddSingleton<JellyfinFolders>();
        services.AddSingleton<StorageService>();
        services.AddSingleton<LibraryFolders>();
        services.AddSingleton<DiskFormatting>();
        services.AddSingleton<FolderCreator>();

        // Blocks a library scan while a disk it would scan is missing.
        services.AddHostedService<ScanGuard>();
    }
}
