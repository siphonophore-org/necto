using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Storage registers one reader of each kind for the server's OS, every service the page and task use, and the scan guard.
/// </summary>
public class StorageServicesTests
{
    /// <summary>
    /// Every Storage service is registered once, with the scan guard as a hosted service.
    /// True: the controller, the task, and the guard all resolve on any OS.
    /// False: Jellyfin would fail to start the plugin.
    /// </summary>
    [Fact]
    public void Register_AddsEveryService()
    {
        var services = new ServiceCollection();

        StorageServices.Register(services);

        foreach (var type in new[] { typeof(IMountTable), typeof(IDiskInventory), typeof(ISmartReader), typeof(IRemounter), typeof(IDiskFormatter), typeof(StorageService), typeof(StorageSettings), typeof(StorageActivity), typeof(DiskFormatting), typeof(FolderCreator), typeof(LibraryFolders), typeof(MonitorMuter), typeof(JellyfinFolders), typeof(VolumeProbe), typeof(TimeProvider) })
        {
            Assert.Single(services, d => d.ServiceType == type);
        }

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ScanGuard));
    }

    /// <summary>
    /// The OS readers match the OS the server runs on.
    /// True: a Mac server reads disks the Mac way, and a Linux server the Linux way.
    /// False: the disk list would come from the wrong OS and show nothing.
    /// </summary>
    [Fact]
    public void Register_PicksTheReadersOfThisOs()
    {
        var services = new ServiceCollection();

        StorageServices.Register(services);

        var inventory = services.Single(d => d.ServiceType == typeof(IDiskInventory)).ImplementationType;
        var expected = OperatingSystem.IsLinux() ? typeof(LinuxDiskInventory)
            : OperatingSystem.IsMacOS() ? typeof(MacDiskInventory)
            : OperatingSystem.IsWindows() ? typeof(WindowsDiskInventory)
            : typeof(NoDiskInventory);
        Assert.Equal(expected, inventory);
    }
}
