# Start Here

Read this file before you change anything in this repository. It applies to everyone who works on it.

## The Guides

Each guide in [skills](skills/) covers one kind of work. Read the guide that matches the task before you start it.

| Guide | Read it before you |
| :--- | :--- |
| [skills/style.md](skills/style.md) | Write or change anything a user reads or sees, including labels, messages, and layout. |
| [skills/code.md](skills/code.md) | Write or change code or comments in any language. |
| [skills/testing.md](skills/testing.md) | Write or run tests. |
| [skills/documentation.md](skills/documentation.md) | Write or change the README, the docs site, screenshots, or release notes. |
| [skills/review.md](skills/review.md) | Call any change done. |
| [skills/release.md](skills/release.md) | Cut a release. |
| [skills/branches.md](skills/branches.md) | Pick a branch, sync the two, and promote unstable. |

A task that touches several areas needs every matching guide. A new setting, for example, needs the style, code, testing, documentation, and review guides.

Read the [Plan](docs/Plan.md) before you add a tool or change what one holds.

## Ground Rules

* **Stolon comes first.** Anything reusable lives in [Siphonophore.Stolon](https://github.com/siphonophore-org/stolon). Search it before you build anything, use what it has, and never copy or override it locally.
* **Describe the current release only.** Docs, labels, and comments say what the plugin does today. Release notes are the only place for what changed.
* **Keep it short.** Every label, message, and comment is as brief as it can be while staying clear.
* **Permissions live on groups.** A tool's page sets how the tool works, never who may use it.
* **Prove it with a test.** A change is not done until a test shows that its inputs give the right output.

## Verify Before Done

Work is done when it has been checked, not when it has been written.

* Run every check the change needs, and read its output. A check that did not run has not passed, and neither has one that ran before the last change.
* Look at every changed page on a test server.
* Read the whole diff before reporting, using [skills/review.md](skills/review.md).
* Check every new string against the style checklist.
* Check every claim in a doc against the code or the running plugin.
* Report what was checked, what was not, and why. Never call work done while a check fails.

## Stop and Ask

Stop and ask before any of these, even when the task seems to call for it.

* **Routes.** A route Jellyfin or another plugin may already answer at.
* **Data loss.** Anything that deletes or replaces users, files, disks, or stored records.
* **Security.** A change to authentication, secrets, permissions, the proxy's protection, certificates, or WireGuard keys.
* **Stolon.** Any change to Stolon, since every plugin takes it.
* **Settings.** Renaming, removing, or changing the default of a setting users already have.
* **Releases.** Raising the version, publishing, or pushing to `main`.
* **The rules.** Any case where following a guide would make the result worse.

Explain what you would change, why, and what it risks. Wait for an answer before you act.

## The Repository

| Path | What it holds |
| :--- | :--- |
| `Jellyfin.Plugin.Necto/` | The plugin source. |
| `Jellyfin.Plugin.Necto/Web/` | The Necto page and its tools, as HTML, JS, and CSS. |
| `Jellyfin.Plugin.Necto/Localization/` | The plugin's own strings, in `en-us.json`, and its casing list. |
| `Jellyfin.Plugin.Necto.Tests/` | The C# tests, and the page tests in `js/`. |
| `docs/` | The documentation site, built with MkDocs. |
| `build.yaml` | The version, the artifacts, and the release notes. |
| `skills/` | The guides. |
| `../stolon/` | A local checkout of Stolon, when present. Read it, and never change it from work on this plugin. |
