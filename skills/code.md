# Code

How to write, comment, and test code in this plugin. It covers C#, JavaScript, HTML, CSS, shell, and YAML.

## Layout

Folders inside `Jellyfin.Plugin.Necto/` set the namespace, so a file in `Api/` is in `Jellyfin.Plugin.Necto.Api`.

| Path | Holds |
| :--- | :--- |
| `Plugin.cs` | The plugin class, its tool keys, and its pages. |
| `PluginServiceRegistrator.cs` | DI registration. |
| `Api/` | Controllers. `NectoController` answers at `/Necto` for the page's own strings. |
| `Configuration/` | `PluginConfiguration` and the C# classes it is made of. |
| `Web/` | The Necto page and its tools, as HTML, JS, and CSS, embedded by a wildcard. |
| `Localization/` | The plugin's own strings and its casing list. |

Each tool takes a folder of its own name under `Api/`, `Configuration/`, and any other folder it needs, such as `Api/Proxy/`.

At the repository root, `docs/` holds the documentation site and `skills/` the guides.

## Stolon First

[Siphonophore.Stolon](https://github.com/siphonophore-org/stolon) holds everything two plugins could share. Its README lists every component and helper. Read it before you write anything new.

### Use What Stolon Has

* Use the Stolon type or component when one fits. Never write a local version, a wrapper that renames it, or a CSS override of a `stolon-` class.
* When Stolon almost fits, change Stolon. Never patch around it locally.
* Delete a local copy as soon as Stolon offers the same thing. Do it in the commit that moves to that Stolon version.
* Never shadow a Stolon name. A local `FormatUtilities` beside Stolon's `FormatUtilities` is ambiguous.
* Read Stolon from the package this plugin builds against. The C# source is in `~/.nuget/packages/siphonophore.stolon/<version>/src/`, and the JS typings are in `assets/stolon.d.ts` beside it.
* Treat the local Stolon checkout as read only. Stolon has its own owner and its own release, so report a gap and wait for the version that closes it.
* When Stolon differs from the local code it replaces, keep the local behavior in a small step around the Stolon call, and cover it with a test.

| Need | Use from Stolon |
| :--- | :--- |
| Plugin class and config lock | `PluginBase<TPlugin, TConfiguration>` |
| Scheduled task | `PluginScheduledTask` |
| Outbound HTTP | `StolonHttp` and `HttpResult` |
| HTTP handlers | `StolonRetryHandler` with `StolonRetryOptions`, and `RedirectRefusingHandler` |
| Addresses another server or a user supplied | `SsrfGuard`, with `requireHttpsForPublic: true` |
| Retry and backoff | `RetryPolicy`, `BackoffPolicy`, `CircuitBreaker` |
| Parallel work | `ConcurrentTaskRunner` |
| Secrets | `SecretProtector`, `AddPluginSecretProtector` |
| Comparing a presented secret | `SecretCompare.FixedTimeEquals` |
| Writing a file whole or not at all | `AtomicFile`, with `ownerOnly: true` for a key or secret |
| Paths inside a folder | `PathContainment` |
| Streams | `StreamUtilities.CopyWithIdleTimeoutAsync` and `CopyThrottledAsync`, `SpeedBucket`, `ThrottledReadStream`, `CappedReadStream`, `ProgressReportingStream`, `StreamLimits` |
| Hashes and sizes | `HashUtilities`, `ByteSize` |
| The clock | `TimeProvider`, and `UtcTime` for stored times |
| Activity log | `ActivityLogger`, `ActivitySummary`, `ActivityText` |
| Paged lists | `PagedResult<T>`, `PagedQuery` |
| SQLite versioning | `DatabaseSchema` |
| JSON state file | `JsonFileStore<T>` |
| Formatting and names | `FormatUtilities`, `ItemTitles`, `RateUtilities`, `FileNameSanitizer` |
| Tool app and sidebar | `createToolApp`, through `createNectoApp` in `necto_shared.js` |
| Config page lifecycle | `createConfigPage`, through `createNectoPage` in `necto_shared.js` |
| Page helpers | `createShared`, through `createNectoShared` in `necto_shared.js` |
| Empty regions | `stolon-empty-section` and `shared.emptySection` |
| Tables | `createPaginatedTable` |
| Lists and row editors | `createCheckboxList`, `createRepeater`, `createSortableCardList` |
| Charts | `createPieChart`, `createUsageMeter`, `createBarChart` |
| Dialogs and sidecars | `openSidecar` and `bindSidecar` for item details and inputs, `confirmDialog` and `openDialog` for confirmations, `createDialogItem` for a poster and name context |

### Move Code Into Stolon

Code belongs in Stolon when another plugin could use it unchanged. A helper with no Necto types in its signature is a candidate.

1. Add it to `../stolon/src/`, one file per component, following Stolon's [code guide](https://github.com/siphonophore-org/stolon/blob/main/skills/code.md).
2. Add tests in `Siphonophore.Stolon.Tests/`, and JS tests in `Siphonophore.Stolon.Tests/js/`.
3. Document it in Stolon's docs site inventory.
4. Pack it locally with `scripts/pack.sh` and build this plugin against it with `-p:RestoreAdditionalProjectSources=<base>/nupkg`.
5. Commit and push to Stolon, then run Stolon's **Release** workflow.
6. Raise the `Siphonophore.Stolon` version in this plugin's csproj, delete the local copy, and add a `developer` changelog line naming the Stolon version.

Never commit this plugin against an unreleased Stolon package.

## Comments

### The Rules

* Every class, method, function, and component gets a header comment.
* The header is the name, then a summary of one or two sentences, leaning to one.
* List inputs and outputs when there are any. One line each, as `name [type]: What or why`.
* Write no inline comments, with one exception. A branch whose reason is not obvious gets one short line above it.
* A branch comment says why, not what. Keep it under 15 words.
* Never comment what the code already says. `// Check if values differ` is noise.
* Never leave commented out code, TODO, FIXME, or HACK. Open an issue instead.
* Never describe history. "Used to", "now", and "was changed to" belong in commit messages.
* Use no em dashes or semicolons in comments.

```text
<Name>
<What it does, in one sentence.>

Inputs
name [type]: What it is or why it is needed.

Output
[type]: What comes back.
```

Leave out **Inputs** when there are none, and **Output** when nothing comes back.

### C#

C# uses XML docs so the compiler and IntelliSense read them. The member name is the title. The type comes from the signature, so a `param` gives only the what or why.

```csharp
/// <summary>
/// Works out how many WireGuard peers a user may have.
/// </summary>
/// <param name="groups">The groups the user is in.</param>
/// <returns>The largest count any of the groups gives, or 0 when none allows peers.</returns>
public static int PeerLimit(IReadOnlyList<Group> groups)
{
    ArgumentNullException.ThrowIfNull(groups);

    // A count takes the largest any group gives, so one generous group is enough.
    return groups.Where(g => g.MayHavePeers).Select(g => g.PeerCount).DefaultIfEmpty(0).Max();
}
```

* Every type and every member that is not private gets `<summary>`, every `<param>`, and `<returns>` when it returns a value.
* Private members get a `<summary>` when the name does not say enough.
* Use `<inheritdoc/>` on an interface or override member.
* Write `true` and `false` as `<c>true</c>` and `<c>false</c>`.
* A constructor summary is "Initializes a new instance of the <see cref="Name"/> class."
* Skip tautologies. `<param name="item">The item.</param>` says nothing, so write what the item is for.
* Put one type in each file, named after the type. A small enum used by one class may share its file.
* Match the namespace to the folder.

### JavaScript

```js
// createRouteTable
// Builds the Proxy tool's table of routes.
//
// Inputs
// view [HTMLElement]: The page that holds the table.
// shared [object]: The helper bag from createNectoShared.
//
// Output
// [object]: A handle with reload and destroy.
export function createRouteTable(view, shared) {
```

* Comment every top level function, every function assigned to `shared`, and every exported function.
* Do not comment the methods inside a returned handle. List them in the Output line instead.
* Open each file with a header naming the file and saying what it holds in one sentence. Use no `// ====` banners.
* Only `necto_shared.js` imports Stolon, by the relative path `./configurationpage?name=necto_stolon.js`. It exports each Stolon builder a page uses, and every page imports from `necto_shared.js`.

### HTML

```html
<!-- Routes -->
<!-- Lists each host name the proxy answers for. -->
<div class="verticalSection">
```

* One comment line naming each section or dialog where it starts, and an optional second line saying what it is for.
* Use no inline `style` attributes.

### CSS

```css
/* necto-route-host */
/* Host name of a proxy route, in the route table's first column. */
/* Example: <span class="necto-route-host">media.example.com</span> */
.necto-route-host { ... }
```

* Three lines above each component, the name, what it looks like, and one example.
* Prefix every class with `necto-` in kebab case. `stolon-` is reserved for Stolon.
* Use Stolon tokens such as `--stolon-border` and `--stolon-fg` for colors. Never write a hex value for something Stolon already colors.

### Shell and YAML

* A shell script opens with a comment saying what it does and how to run it.
* A YAML file opens with a comment saying what it is for. Comment a step only when its reason is not obvious.

## C# Conventions

* Target `net10.0` with `Nullable` on, `TreatWarningsAsErrors` on, and `AnalysisMode` set to `AllEnabledByDefault`.
* Keep `jellyfin.ruleset` identical across plugins. Raise a rule change in every plugin at once.
* Suppress a rule only at the narrowest scope, with a reason. `#pragma warning disable CA2100 // SQL is internal and parameterized.`
* Use file scoped namespaces, `sealed` classes, and records for data.
* Name private fields `_camelCase`.
* End every async method name in `Async`, pass the `CancellationToken` through, and use `ConfigureAwait(false)` on every await.
* Guard public arguments with `ArgumentNullException.ThrowIfNull`.
* Log through `ILogger<T>` with static, named templates. Pass untrusted text through `FormatUtilities.SanitizeForLog`.
* Catch a specific exception, or filter with `when`. Rethrow `OperationCanceledException` when the token was cancelled.
* Read the clock once in a method and reuse the value. Two reads can straddle a second and disagree.
* Take a `TimeProvider` wherever time decides an outcome, so a test can move it.
* Register services in `PluginServiceRegistrator`.

## JavaScript Conventions

* Each tool page is an ES module whose default export calls `createNectoPage`, which wraps Stolon's `createConfigPage` and waits for the strings.
* A tool is listed in `nectoTools()` in `necto_shared.js` and in `Plugin.ToolKeys`, in the same order, with `necto_<key>.html` and `necto_<key>.js` in `Web/`.
* Keep one handle for each Stolon component a page draws. Call `destroy` before drawing it again, and `setValue` when only its numbers change.
* Drop an answer that arrives after the page moved on. Remember the picked key before the request and compare it when the answer lands.
* Escape every value placed in HTML with `escapeHtml`.
* Report results with `shared.setStatus`, and errors with `shared.errorText`.
* Register every poller and observer with the page handle so `viewhide` stops it.

## Security

* Store every credential with `SecretProtector`, built by `AddPluginSecretProtector`. Never write a secret to the config, a file, or a log in plain text.
* Send a stored secret to the browser only as `SECRET_KEPT`. Read it back with `readSecretField` and resolve it with `SecretProtector.ResolveIncoming`.
* Pass every value from a request or a file through `FormatUtilities.SanitizeForLog` before logging it.
* Check every address a user supplies with `SsrfGuard` before calling it, and never follow a redirect to another host.
* Compare a secret a caller presents with `SecretCompare.FixedTimeEquals`, never `==`.
* Write a key or secret file with `AtomicFile` and `ownerOnly: true`, so it is never readable by others, even for a moment.
* Guard every administrator route with `[Authorize(Policy = "RequiresElevation")]`. Only the strings route, `GET /Necto/Translations`, allows anonymous callers.
* Check a user's groups before returning anything for that user.
* Build SQL with parameters only. Never put a value into SQL text.
* Keep files inside the folders they belong to with `PathContainment` and `FileNameSanitizer`.
* Escape every value placed in HTML with `escapeHtml`.
* Never put a secret or a user's data in a URL.

## Tests

Every source file has a test file. See [testing.md](testing.md) for each layer, how to write tests, and the fakes.

## Checklist

* [ ] Stolon's source and typings were searched, nothing here duplicates Stolon, and anything reusable went to Stolon first.
* [ ] Every class, method, function, and component has a header.
* [ ] Inline comments appear only above branches, and say why.
* [ ] Every changed source file has tests that pass, per [testing.md](testing.md).
* [ ] The [security](#security) rules hold.
* [ ] `dotnet build` shows no warnings.
