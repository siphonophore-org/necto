using System;
using System.IO;
using System.Net.Http;
using Jellyfin.Plugin.Necto.Services.Scripts;
using Siphonophore.Stolon;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Scripts;

// A runner and its step executor wired to substitutes for the server, with the history and the secret vault in a
// temporary folder. Without a Data Protection provider the protector keeps secrets in plaintext.
internal sealed class TestServices : IDisposable
{
    public TestServices()
    {
        Folder = Path.Combine(Path.GetTempPath(), "scripts-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        var (paths, pathStub) = InterfaceStub.Create<IApplicationPaths>();
        pathStub.Answer("get_PluginConfigurationsPath", _ => Folder);

        (ActivityManager, ActivityStub) = InterfaceStub.Create<IActivityManager>();
        var (sessions, sessionStub) = InterfaceStub.Create<ISessionManager>();
        sessionStub.Answer("get_Sessions", _ => System.Array.Empty<MediaBrowser.Controller.Session.SessionInfo>());
        SessionManager = sessions;
        LibraryManager = Stub<ILibraryManager>();
        var activityLogger = new ActivityLogger(ActivityManager, NullLogger<ActivityLogger>.Instance);
        Executor = new OperationExecutor(
            Stub<IHttpClientFactory>(),
            SessionManager,
            Stub<IProviderManager>(),
            Stub<ITaskManager>(),
            Stub<IFileSystem>(),
            activityLogger,
            Stub<IUserManager>(),
            Stub<IUserDataManager>(),
            LibraryManager,
            Stub<ICollectionManager>(),
            NullLogger<OperationExecutor>.Instance);
        UserManager = Stub<IUserManager>();
        Builder = new EventContextBuilder(
            LibraryManager,
            Stub<MediaBrowser.Common.Net.INetworkManager>(),
            Stub<MediaBrowser.Controller.IServerApplicationHost>(),
            SessionManager,
            NullLogger<EventContextBuilder>.Instance);
        Expander = new ForEachExpander(LibraryManager, UserManager, Stub<IUserDataManager>(), Builder);
        Activity = new ScriptsActivity(activityLogger);
        History = new HistoryStore(new ScriptPaths(paths), NullLogger<HistoryStore>.Instance);
        Runner = new ScriptRunner(
            Executor,
            History,
            Activity,
            new SecretProtector("test", NullLogger.Instance),
            new SecretVault(Path.Combine(Folder, "secrets.json"), NullLogger.Instance),
            Expander,
            NullLogger<ScriptRunner>.Instance);
    }

    public string Folder { get; }

    public InterfaceStub ActivityStub { get; }

    public IActivityManager ActivityManager { get; }

    public ISessionManager SessionManager { get; }

    public ILibraryManager LibraryManager { get; }

    public IUserManager UserManager { get; }

    public EventContextBuilder Builder { get; }

    public ForEachExpander Expander { get; }

    public OperationExecutor Executor { get; }

    public HistoryStore History { get; }

    public ScriptsActivity Activity { get; }

    public ScriptRunner Runner { get; }

    public static T Stub<T>()
        where T : class
        => InterfaceStub.Create<T>().Proxy;

    public void Dispose()
    {
        Runner.Dispose();
        Activity.Dispose();
        History.Dispose();
        try
        {
            Directory.Delete(Folder, true);
        }
        catch (IOException)
        {
            // A temp folder left behind is harmless.
        }
    }
}
