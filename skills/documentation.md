# Documentation

How to write the README, the docs site, screenshots, and release notes for this plugin. It applies to every page under `docs/`, to `README.md`, and to the `changelog` in `build.yaml`.

## The One Rule

Documentation describes the release it ships with. It reads as if this version were the only one that ever existed.

* Write what the plugin does. Never write what it used to do, what it no longer does, or what it now does.
* Write how a feature works. Never write that a bug was fixed, a problem is gone, or a limit was lifted.
* History belongs in the release notes. See [Release Notes](#release-notes).

| Do not write | Write |
| :--- | :--- |
| Certificates now renew on their own. | Certificates renew on their own. |
| The setup wizard was removed. | Leave it out. |
| The page no longer asks twice. | The page asks once. |
| Fixed an issue where DDNS missed updates. | DDNS updates each record when the public IP changes. |
| New in this release, groups decide... | Groups decide... |

Quote an on-screen message exactly as the code writes it. Everywhere else, leave out "now", "no longer", "new", "changed", and "fixes".

A version number belongs in docs only when the reader needs it to act, such as the minimum Jellyfin version. Name the version outright instead of "this release". Example version strings in samples are `12.4.0.0`.

## Writing Style

### Sentences

* Use present tense and plain declarative sentences.
* Describe the system in the third person. "Necto renews the certificate."
* Address the reader in the second person only in steps. "Turn on **Proxy** in **Settings**."
* State one fact per sentence. Aim for 20 words or fewer and split anything over 30.
* Use no contractions. Write "cannot", not "can't".
* Use no em dashes, en dashes as dashes, or semicolons. Split the sentence, or join it with a comma and "and".
* Use colons only to introduce a list or a code block.
* Use parentheses rarely. Move the aside into its own sentence.
* End every sentence with a period.
* Write compounds as separate words the way the UI does, such as "one time setup" and "sign in".
* Write one paragraph per line. Never hard wrap prose.
* Use no marketing words such as "powerful", "seamless", "easily", or "simply".
* Write examples with generic names. Use `example.com` addresses and names such as `viewer`, never a real server, person, or address.

### Headings

* Use Title Case for every heading. "## Certificates and Renewal".
* Page titles are `#` and appear once per page.
* Name the thing, not the action, unless the section is a procedure. "## Settings" and "## Add the Plugin Repository" are both correct.
* Never rename a heading without fixing its anchors. `mkdocs build --strict` fails on a broken anchor.

### UI Names

* Write a label exactly as the page shows it, in bold. **Update every (minutes)**.
* Write a path through the UI with `>` between the steps, in bold. **Dashboard > Plugins > Necto > Settings**.
* Write a tool as its sidebar name, in bold. **Proxy**, **DDNS**, **Custom pages**, **Settings**.
* Quote activity log names and on-screen messages in double quotes. "Route saved."
* Write routes, files, settings keys, and values in code. `GET /Necto/Translations`.

A docs page and the UI must use the same word for the same thing. When a label changes, search `docs/` for the old one and change it everywhere in the same commit.

## Site Structure

The docs site is MkDocs with the Material theme, built from `docs/` by `mkdocs.yml` and published to `https://necto.siphonophore.org`.

### Navigation

Keep the nav in this order.

1. **Overview** (`index.md`). What the plugin is, the risk warning, and the tools.
2. **Getting Started**. Installation and setup.
3. **Tools**. One page per tool, in sidebar order.
4. **Reference**. Groups and permissions, and the API.
5. **Development**. The [Plan](../docs/Plan.md), then building and testing.

Name page files in PascalCase with no spaces, such as `CustomPages.md`.

### Page Templates

A tool page follows this shape.

```markdown
# Proxy

One sentence that says what the tool does.

## Settings

* **Setting name** does one thing.

## <Feature Sections>

## Permissions
```

A feature page opens with one sentence on what the feature is, then one section per question a reader asks, then **Security** and **Limitations** where they apply.

An API route follows this shape.

````markdown
### POST /Proxy/Example?routeId={id}

One sentence on what the route does and who calls it.

```json
{ "Example": true }
```

| Status | Meaning |
| :--- | :--- |
| `200` | The change was saved |
````

### Settings References

List settings as bullets in the order the page shows them. Start each with the label in bold and follow it with a verb.

```markdown
* **Update every (minutes)** sets how often each record checks the public IP.
```

When a page has many settings with defaults, use a table instead.

| Setting | What it does | Default |
| :--- | :--- | :--- |
| **Update every (minutes)** | Sets how often each record checks the public IP | 5 |

Table cells have no closing period. Keep them to one sentence.

### Troubleshooting

Write trouble as a table of what the reader sees and what to do.

| Reason | What to do |
| :--- | :--- |
| Could not reach *provider* | Check the record's credentials, then select **Test** |

Put the exact on-screen text in the first column. Use italics for a placeholder inside it, such as *server*.

### Admonitions

Give every admonition a short title.

```markdown
!!! warning "Use at your own risk"
    One or two sentences.
```

* `note` for a detail the reader needs.
* `tip` for a better way to do something.
* `warning` for anything that can lose data or break a server.
* `info` for a requirement, such as a port the router must forward.

### Links

* Link pages relatively with their `.md` name, such as `[Plan](Plan.md#groups)`.
* End a Getting Started page with one "Next" line, such as `Next: [Setup](Setup.md).`
* Link to Stolon's README for a shared component instead of describing it again.

## README

The README is a landing page. The docs site holds the detail.

1. The banner from `brand/necto/banner.svg` in the Siphonophore repository, then a one paragraph pitch.
2. `## The Name`. What a nectophore is, and why the plugin carries the name.
3. `## Status` or `## Documentation`. A bold link to the docs site once it holds the tools, then the repository URL for Jellyfin in a code block.
4. `## License`.

Use `##` for README headings. Keep the README under 40 lines.

## Screenshots

### How to Take Them

Take every screenshot on a test server running the build, and save each PNG to `docs/screenshots/`.

* Capture every page, dialog, and sidecar a docs page describes.
* Capture the dark theme only, which is Jellyfin's default.
* Keep every name generic, with `example.com` addresses.
* Fill lists, counts, and badges so each shot shows what the text around it describes.
* Hide nothing with editing. When a value is private, change the sample data instead.
* Look at every PNG before committing it.

### Sizes

| Kind | Size | How |
| :--- | :--- | :--- |
| Page or section | 800 wide at 2x | The page at 800 px, cropped to the content or to the section |
| Dialog or sidecar | 800 wide at 2x | The page at 800 px, cropped to the dialog or sidecar |
| Phone | 375 wide at 2x | The page at 375 px |

Crop out the Jellyfin header.

### Names and Placement

* Save to `docs/screenshots/`, with the sections of the Settings tool in `docs/screenshots/settings/`.
* Name the file in Title Case after what it shows, with spaces. Add ` Phone` to the name of a phone shot.
* Link with `%20` for spaces and alt text that names the subject. `![Page Name](screenshots/Page%20Name.png)`.
* Place two related shots side by side in a table whose header row holds the captions.

```markdown
| Page Name | Page Name on a phone |
| :---: | :---: |
| ![Page Name](screenshots/Page%20Name.png) | ![Page Name on a phone](screenshots/Page%20Name%20Phone.png) |
```

### When to Retake

Retake a screenshot in the same commit as any change that alters what it shows, including a label, a tool, a layout, or the Jellyfin version in the header. A new Stolon version changes how every component looks, so retake every screenshot when it is raised. A screenshot that shows a tool or label the plugin does not have is a bug.

## Installing a Build by Hand

```bash
./build.sh
```

The zip lands in `dist/`. Unzip it into `<config>/plugins/Necto_<version>/` on any Jellyfin 12.4 server and restart the server.

## Building the Docs

```bash
pip install -r .github/docs/requirements.txt
```

```bash
mkdocs serve -a 127.0.0.1:8765
```

```bash
mkdocs build --strict
```

Run the strict build before every docs commit. CI runs the same build and refuses broken links, anchors, and nav entries.

## Release Notes

Release notes live in the `changelog` of `build.yaml`, under four keys. The release notes take them, and the Siphonophore manifests copy those, so every line reaches users' plugin catalogs.

| Key | Holds | Voice |
| :--- | :--- | :--- |
| `added` | A feature that did not exist | The feature name, then what it does |
| `changed` | Behavior a user will notice | The new behavior, in present tense |
| `fixed` | A bug | What went wrong in past tense, then the fix in present tense |
| `developer` | Routes, schema, tests, and tooling | Plain statements for other developers |

```yaml
changelog:
  added:
    - DDNS, with several records, each with its own provider and credentials
  fixed:
    - A record with an expired token stopped updating. It reports the failure on Notifications
```

* This is the one place that may say "now", "no longer", and "was".
* Write full sentences with no closing period.
* Write one entry per change a user would notice. Fold internal steps into the change they serve.
* Name UI labels exactly as the page shows them, without bold or backticks.
* Name the Stolon version in `developer`, with what it replaced locally.
* Give a new or changed route or query parameter a `developer` line, and document it in the docs in the same commit.

## Commit Messages

* Write one line of nine words or fewer, casually, such as `add ddns records`.
* Use no body, no trailers, and no prefixes such as `fix:` or `feat:`.
* Commit docs and screenshots with the change they describe.

## Checklist

* [ ] Nothing describes an earlier version, a removed feature, or a fixed bug.
* [ ] Every UI label matches the page exactly and is bold.
* [ ] Every heading is Title Case.
* [ ] No em dashes, semicolons, or contractions.
* [ ] Every screenshot matches the current UI.
* [ ] The [Plan](../docs/Plan.md) matches what is built.
* [ ] `mkdocs build --strict` passes.
* [ ] `build.yaml` has a changelog entry for any change a user would notice.
