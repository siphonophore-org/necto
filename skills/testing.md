# Testing

How to test this plugin. A change is done when the tests for every layer it touches pass.

## The Rule

Every source file has a test file, and every test proves that given inputs produce the expected output. A bug fix starts with a test that fails without the fix.

## Layers

Test each behavior at the lowest layer that can prove it.

| Layer | Proves | Runs |
| :--- | :--- | :--- |
| Unit | One class or function gives the right output for each input | `dotnet test` |
| Store | SQL and schema against a real SQLite file | `dotnet test` |
| Controller | A route's template, guards, status codes, and response shape | `dotnet test` |
| JavaScript | Page logic, such as the tool list, formatting, filters, and state | `node --test` |

## Layout

Mirror the source folders inside the test project so a missing test is easy to spot.

| Source | Test |
| :--- | :--- |
| `Jellyfin.Plugin.Necto/Plugin.cs` | `Jellyfin.Plugin.Necto.Tests/PluginTests.cs` |
| `Jellyfin.Plugin.Necto/Api/NectoController.cs` | `Jellyfin.Plugin.Necto.Tests/Api/NectoControllerTests.cs` |
| `Jellyfin.Plugin.Necto/Web/necto_shared.js` | `Jellyfin.Plugin.Necto.Tests/js/necto_app.test.mjs` |

* Shared fakes live in `Jellyfin.Plugin.Necto.Tests/Fakes/`, one per file.
* `Localization/TranslationKeyTests.cs` checks that every key a page in `Web/` names has a string.

## C# Tests

Tests use xUnit with plain `Assert`. There is no mocking library.

```csharp
/// <summary>
/// The plugin is named Necto.
/// True: the dashboard and the catalog show the same name.
/// False: the plugin shows under another name than its manifest's.
/// </summary>
[Fact]
public void Name_IsNecto()
{
    var plugin = TestPlugin.Create();

    Assert.Equal("Necto", plugin.Name);
}
```

* Name the class `<Subject>Tests` and each method `Method_Scenario_Expectation`.
* Give the class a one sentence summary of what its subject must guarantee.
* Give each test the three line summary. What it proves, what passing means, and what failing would cause.
* Arrange, act, and assert, separated by blank lines.
* Use `[Theory]` with `[InlineData]` for one rule over several inputs.
* Test the edges, such as null, empty, the largest value, and a cancelled token.
* Never sleep. Pass a `TimeProvider` in and move it from the test.
* A test never reaches the network. Use a stub `HttpMessageHandler`.
* A store test uses a real SQLite file in a temp folder and deletes it in `Dispose`.

### Fakes

| Fake | Stands in for |
| :--- | :--- |
| `InterfaceStub` | Any Jellyfin interface a test needs but does not exercise, recording every call |
| `TestPlugin` | The plugin, built over stand-ins for Jellyfin's paths and serializer |

Never copy a fake into a test file. Extend the shared one.

```bash
dotnet test
```

## JavaScript Tests

JS tests run on node's own test runner with Stolon's fake DOM and no dependencies. `support.mjs` installs the fake DOM and the Jellyfin globals, serves the plugin's US English strings at `Necto/Translations`, and `loader.mjs` maps each `./configurationpage?name=` import to its file.

```js
test('nectoTools lists the 14 tools in sidebar order, each with its icon and its own page', async function () {
    installPage({});
    var shared = await loadScript('necto_shared.js');
    assert.equal(shared.nectoTools().length, 14);
});
```

```bash
node --test 'Jellyfin.Plugin.Necto.Tests/js/*.test.mjs'
```

* Name each test as a sentence about the behavior.
* Test logic, not layout. Move logic out of event handlers into exported functions so a test can call them.
* The tests load Stolon's bundle from `../stolon/dist/`, or from `STOLON_ROOT` when it is set. Keep that checkout on the Stolon version the csproj names.
* Test a page through `installPage` and `loadScript`. Dispatch `viewshow` on the view, since Stolon loads the page when it shows, then wait with `settle`.
* Read the sidebar with `sidebar` and a status line with `statusText`.
* Assert on what a Stolon component shows or saves. Never assert on Stolon's inner markup beyond a documented class.

## Checklist

* [ ] Every changed source file has tests, in the mirrored folder.
* [ ] A bug fix has a test that fails without it.
* [ ] Edges are covered, including null, empty, and cancelled.
* [ ] Fakes come from `Fakes/`.
* [ ] `dotnet test` passes.
* [ ] The JS tests pass.
