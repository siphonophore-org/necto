using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// An activity logger over a stand-in for Jellyfin's activity manager, which keeps every entry written.
/// </summary>
public sealed class FakeActivity
{
    public FakeActivity()
    {
        var (manager, stub) = InterfaceStub.Create<IActivityManager>();
        stub.Answer("CreateAsync", args =>
        {
            lock (Entries)
            {
                Entries.Add((ActivityLog)args[0]!);
            }

            return Task.CompletedTask;
        });
        Logger = new ActivityLogger(manager, NullLogger<ActivityLogger>.Instance);
    }

    public ActivityLogger Logger { get; }

    public List<ActivityLog> Entries { get; } = new();

    public IReadOnlyList<string> Names
    {
        get
        {
            lock (Entries)
            {
                return Entries.Select(e => e.Name).ToList();
            }
        }
    }
}
