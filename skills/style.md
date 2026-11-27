# Style

How every page, dialog, label, and message in this plugin looks and reads. It is drawn from Stolon and the other Siphonophore plugins, so the same rules carry to each of them.

Stolon is [Siphonophore.Stolon](https://github.com/siphonophore-org/stolon). Its README documents each component's options. This guide says which component to use, when, and what to write in it.

## Principles

1. **One component per concept.** When Stolon has it, use it. Look before you build, per [Find the Component](#find-the-component). When Stolon lacks it and a second plugin needs it, add it to Stolon. Never build a second version.
2. **Short beats complete.** A label is a few words. A description is one sentence. A message is one sentence.
3. **The page explains itself.** A reader understands a setting from its label and its one line description, without the docs.
4. **The same thing has the same name everywhere.** The label, the docs, the message, and the activity log use one word for one concept.
5. **Follow Jellyfin.** Use Jellyfin's own markup and theme, so the plugin looks like part of the dashboard.

## Writing

### The Rules

These rules apply to every string a user can see. That includes HTML, JS, C# messages the API returns, and activity log entries.

* **No em dashes or en dashes as punctuation.** Split the sentence, or join it with a comma and "and". An en dash in a numeric range, such as `22–6`, is fine.
* **No semicolons.** Split the sentence.
* **Avoid colons.** Never end a label with one. In a sentence, rewrite it. "How fast files move: downloads and uploads." becomes "Sets how fast files move in each direction."
* **One sentence.** Two only when the second gives the fix or the consequence.
* **End every sentence with a period.** Labels, buttons, titles, tabs, and table cells are not sentences and take no period.
* **No "please", "successfully", "simply", or "just".**
* **No exclamation marks.**
* **No ALL CAPS for emphasis.** Say why it matters instead.
* **No "(s)".** Write the right plural for the count.
* **No contractions.** Write "cannot", not "can't".
* **No "e.g." or "i.e.".** Write "such as".
* **American spelling.** Color, behavior, favorite, center.
* **Use the reader's words.** "Server" and "library", not "host process" and "collection folder".

### Casing

Every string is in sentence case, mirroring Jellyfin, such as "Allow media playback". Only the first word and proper nouns take a capital.

| Text | Example |
| :--- | :--- |
| Tool, short | Pages |
| Section and collapsible title | Certificate renewal |
| Field label | Update interval |
| Checkbox label | Allow requests |
| Button, verb first | Add route |
| Dialog title | Remove media.example.com |
| Badge and status chip | Pending approval |
| Column header | Last update |
| Activity log name | Invite used |
| Description, message, placeholder sentence | Sends each event to the group's channels. |

* Write product names, services, formats, and acronyms as they are written, such as "Necto", "DDNS", and "WireGuard". Each one the plugin uses goes in `Localization/casing.json`, per [Translations](#translations).
* Write a choice by its own label inside a sentence, such as "Select Test connection" or "With Allow requests on". A one word choice such as **Blank** keeps its capital.
* Leave a Jellyfin string as Jellyfin writes it, unless it reads wrong beside its neighbors. Then give it `Case="sentence"`.

### Words to Use

Pick the word on the left every time.

| Use | Not | When |
| :--- | :--- | :--- |
| Remove | Delete | Takes an entry out of a list or the config, and nothing on disk is lost |
| Delete | Remove | Destroys data, files, or history that cannot come back |
| Clear | Empty, Reset | Removes every entry in a list or log |
| Reset | Restore | Puts settings back to their defaults |
| Save | Apply, Update | Writes changes to the config |
| Done | OK, Close | Closes an editor and keeps its changes |
| Cancel | Close, No | Closes without keeping changes |
| Add | New, Create | Puts a new entry into a list on the page |
| New | Add, Create | Starts a new record in a record picker |
| Test connection | Test, Check | Calls the other side and reports the result |
| Run now | Start, Trigger | Starts a scheduled task right away |
| Select | Pick, Choose, Tick, Click | Any instruction to make a choice |
| Turn on, Turn off | Enable, Disable, Tick, Untick | An instruction about a checkbox or toggle |
| Leave blank | Leave empty | An empty field means the default |
| Could not | Failed to, Unable to | An error message |

The control names its own result. "Remove route" leaves the upstream service alone, so it is Remove. "Delete certificate" destroys the key, so it is Delete.

### Labels

* Two to four words. Name the thing the field holds, not the action.
* Put a unit at the end in lowercase parentheses, such as **Update every (minutes)** or **Free space floor (GB)**. Use symbols for standard units, such as `GB` and `Mbps`.
* No colons, periods, or question marks.
* A checkbox label names the feature it turns on, never "Enable X". Write **Allow requests**, not **Enable requests**. The one exception is the master switch at the top of a section, which reads **Enable** followed by the section name, such as **Enable WireGuard**.

### Descriptions

A `fieldDescription` sits under every field that is not obvious from its label.

* One sentence that says what the setting does, starting with a verb. "Sends each event to the group's channels."
* Add a second sentence only for the default or a consequence. "0 is no limit."
* Describe the current behavior only. Never "now" or "no longer".
* Never repeat the label.
* Put units, ranges, and what blank means here, never in the label or the placeholder.

| Bad | Good |
| :--- | :--- |
| Who gets it: admins, groups, and channels. | Sends each event to the group's channels. |
| Run in order; drag or use the arrows to reorder. | Runs in order. Drag a step to move it. |
| Server time. 22 to 6 runs overnight; equal hours run all day | Uses the server's clock. Equal hours run all day. |
| This setting enables the sending of notifications to groups. | Sends each event to the group's channels. |

A section intro is one `fieldDescription` right under the section title. It says what the section is for in one sentence.

### Placeholders

A placeholder shows an example of a valid value. It is never the only place an instruction lives, since it disappears once the reader types.

* Show a realistic example with no prefix. `https://jellyfin.example.com`, not `e.g. https://...`.
* Use `example.com` addresses and generic names only.
* For an override that falls back to a default, show the value it falls back to. When that value is not known, write `Default`.
* For a search box, write "Search" and the noun, with no ellipsis. `Search items`.
* For a secret already stored, Stolon's `bindSecretField` writes "Saved, leave blank to keep". Leave it as is.
* Never put a sentence in a placeholder. Move it to the description.

### Buttons

* A verb, or a verb and a noun. **Save**, **Add route**, **Run now**.
* Name the object when the page has more than one of that action. **Add route** and **Add certificate** sit on the same page.
* A primary Save is **Save**. Use **Save** and a noun only when one page has several saves, such as **Save certificates**.
* An icon only button carries a `title` of one to three words, such as `Remove`, `Move up`, or `Retry`. Never a sentence.
* A button that runs for a while shows progress in place. See [Progress](#progress).

### Messages

A message reports what happened in one sentence that ends with a period.

| Kind | Pattern | Example |
| :--- | :--- | :--- |
| Saved | `<Thing> saved.` or `Saved.` | Route saved. |
| Done | `<Thing> <past verb>.` | Log cleared. |
| Copied | `<Thing> copied.` | Invite link copied. |
| Working | `<Verb>ing…` | Saving… |
| Could not | `Could not <verb> the <thing>.` | Could not save the route. |
| Could not, with a fix | Add one sentence with the fix | Could not load the settings. Reload the page. |
| Missing input | `Enter <the thing>.` or `Select <the thing>.` | Enter the host name. |
| Nothing to do | `No <things> to <verb>.` | No items to retry. |

* Use the ellipsis character `…`, never three dots, and only for work in progress.
* When the server gave a reason, show it with `shared.errorText(err, 'Could not save the route.')`.
* Name the count with a correct plural. "Deleted 3 items." and "Deleted 1 item."
* Never say "error occurred". Say what could not happen.

### Confirm Dialogs

A confirm's title names the action and what it acts on. Its message says what follows from it.

```js
confirmDialog('The proxy stops answering for it. The upstream service stays.', {
    title: 'Remove media.example.com',
    confirmText: 'Remove',
    destructive: true
});
```

* Never repeat the title as a question, such as **Remove route** over "Remove this route?". The message is one or two short sentences on the consequence, such as "The key goes with it. This cannot be undone."
* The title is in sentence case and names its target, such as **Delete 3 scripts** or **Reset the proxy settings**. Never leave the default **Confirm**.
* A confirm about one item shows it as the context, with `createDialogItem`, such as the name and avatar of a user to remove.
* The confirm button repeats the action verb, such as **Remove** or **Delete**. Never **OK** or **Yes**.
* Set `destructive: true` for anything that loses data.
* Never "Are you sure you want to". Ask the question directly.
* Never use `window.confirm` or `Dashboard.confirm`.

### Empty States

| Where | Pattern | Example |
| :--- | :--- | :--- |
| Record picker | `No <things> yet. Select New to add one.` | No profiles yet. Select New to add one. |
| List in a form | `No <things> yet. Add one below.` | No routes yet. Add one below. |
| Live list | `No <things> right now.` | No peers are connected right now. |
| Filtered list | `No <things> match.` | No requests match. |
| Feature off | `<Feature> is off. Turn it on in Settings to <result>.` | WireGuard is off. Turn it on in Settings to add peers. |

Use `shared.emptySection(text)` for a region and `shared.emptySection(text, { inline: true })` for a list.

### Activity Log

Use `ActivitySummary` and `ActivityText` from Stolon.

* The name is generic and in sentence case, with keys for one and many and no count. "Invites used".
* The overview is one sentence with no closing period, as Stolon writes it. "viewer joined with an invite to Family".
* Use the same verb the UI uses for the action.

### Messages From C#

The API returns text that the page shows as is, so it follows the same rules.

* A full sentence that starts with a capital and ends with a period. "The route media.example.com has no upstream address."
* Say what is wrong and, when it helps, what to do. "The certificate could not renew. Check the DDNS record's credentials."
* Never return an exception message, a stack trace, or a type name.

## Color

### Tokens

Use Stolon tokens. They follow the dashboard's theme, light themes included. Never write a hex value for something these cover.

| Token | Use |
| :--- | :--- |
| `--stolon-accent` | Links, focus rings, primary buttons, progress |
| `--stolon-accent-hover` | Hover on accent elements |
| `--stolon-fg` | Washes on elements that fade their own text |
| `--stolon-surface` | Panel and dialog backgrounds |
| `--stolon-border` | Every border and divider |
| `--stolon-gap-wide` | Space between components, such as one field row and the next |
| `--stolon-gap-narrow` | Space inside a component, such as between a label and its field |
| `--stolon-mono` | Code, ids, paths, and raw errors |
| `--stolon-success`, `--stolon-warning`, `--stolon-error` | Solid status colors |
| `--stolon-<color>-bg`, `--stolon-<color>-fg` | Tinted badge and card backgrounds and text, for green, red, orange, purple, blue, and gray |

### Meaning

A color means the same thing on every page and in every plugin.

| Color | Means | States |
| :--- | :--- | :--- |
| Green | Done and healthy | Active, Connected, Approved, Succeeded |
| Red | Failed, or about to lose data | Error, Errored, Failed, Deleting, Pending deletion |
| Orange | Waiting, or needs attention soon | Queued, Expired, Warning |
| Purple | Working right now | Pending, Running, Locked, Renewing |
| Blue | Informational | Admin, Imported |
| Gray | Off or not applicable | Disabled, Ignored, Skipped, Group |

* Map a status to a color through `BADGE_STATES`. Never keep a second map in the plugin.
* Disabled is gray when it means off. Red is only for failure.
* Use "orange" for the warning color in class names, never "amber", "yellow", or "warning".
* Never use color alone. A badge always has a word, and a dot always has a `title`.

## Page Structure

### Shell

Every tool page uses Jellyfin's page shell. The tool app takes the page's view out of its markup and shows it beside the sidebar, and the shell's heading names it.

```html
<div data-role="page" class="page type-interior pluginConfigurationPage"
     data-require="emby-input,emby-button,emby-select,emby-checkbox">
    <div>
        <div class="content-primary">
            <div class="verticalSection">
                <div class="fieldDescription">Forwards each host name to its service.</div>
            </div>
        </div>
    </div>
    <link rel="stylesheet" href="configurationpage?name=necto_stolon.css">
</div>
```

* List `data-require` in the order `emby-input,emby-button,emby-select,emby-checkbox`, dropping any the page does not use.
* Use no inline `style` attributes. Add a class to the plugin CSS, or to Stolon when it is reusable.

### Sidebar

* Necto is one page, `necto`, built by `createNectoApp` with Stolon's tool app. Each sidebar entry is its own registered page and script, wired with `createNectoPage`, and swapped in place. A page sets no Jellyfin tabs.
* The dashboard is the first entry. Its cards open the tool they sum up with `openTool`.
* A page has no title of its own. The shell's heading names it, and `setPageTitle` changes the heading when a page needs another.
* Entry names are short, each with a Material icon.
* Keep the list in one place, `nectoTools` in the shared module, never copied into each page. `Plugin.ToolKeys` registers the same tools in the same order.
* Order entries from what is used most to what is set once. Necto is **Dashboard**, **Requests**, **Issues**, **Invites**, **Groups**, **Proxy**, **DDNS**, **WireGuard**, **Scripts**, **Pages**, **Storage**, **Notifications**, **Settings**, **Troubleshooting**.
* A tool turned off in **Settings** leaves the sidebar.
* A tool page holds no settings and no permissions. Settings go in the tool's section of **Settings**, and permissions go on groups.

### Sections

| Need | Component |
| :--- | :--- |
| A page with several independent groups of settings | `stolon-collapsible-section`, one per group |
| A group that is always open | `stolon-collapsible-section fixed`, whose header is a `div` with no icon, so it matches the collapsible sections around it |
| A heading inside a section | `stolon-subsection-title` |
| Settings that only matter when a checkbox is on | `stolon-indent` under the checkbox, shown with `shared.bindReveal` |

* A collapsible header is a `button.stolon-collapsible-header` holding `stolon-collapsible-title` and `stolon-collapsible-icon`. Stolon replaces the icon with `expand_more`.

#### Settings and Troubleshooting Pages

Every section on a Settings or Troubleshooting page leads with an icon tile, and so does every reset or action row. The tile names the topic at a glance, and makes a long page easy to scan.

* A section header holds `<span class="stolon-collapsible-tile" aria-hidden="true"><span class="material-icons">…</span></span>` before its `stolon-collapsible-title`. A section built in script passes `icon` to `createCollapsibleSection`, which draws the same tile.
* A reset or action row holds the same tile before its `stolon-reset-info`. A reset row's tile keeps the accent while there is something to reset, and turns `stolon-gray` while there is nothing, such as an empty table.
* Pick the Material icon that names the topic, such as `movie_filter` for Requests, `vpn_lock` for WireGuard. A section and the tool it belongs to use the same icon as the sidebar.
* The tile takes the accent color when its section has no on or off switch. A section that turns its feature on or off takes `stolon-green` while it is on and `stolon-gray` while it is off, and follows the switch as it changes. The most destructive row, such as a full reset, takes `stolon-red`.
* Give each section its own icon. Two sections on one page never share one.
* Never use a tile for decoration alone. Steps of a setup, such as a server editor, take tiles too, never numbers.
* Start collapsibles closed, except the first one on a page someone opens to act on.
* Give every top level group on one page the same header. A chart or a summary takes the `fixed` section, never a bare `stolon-subsection-title`.
* Hide a whole section while its feature is off or it has nothing to show. Never show an empty section.
* Put anything that depends on a picker under that picker, and redraw it when the pick changes.

### Saving

Pick one model per page and keep it.

| Model | Use it when | Save |
| :--- | :--- | :--- |
| One Save per page | Settings depend on each other | One Save Bar, the last thing on the page |
| One Save per section | Sections are independent and long | One Save Bar, the last thing in each section's content |
| Editor sidecar | Each record is edited on its own | **Save** in `openEditorSidecar` |

Every page and section Save is the same:

1. It is Stolon's `createSaveBar` with `fill: true`. Its button reads **Save**, never **Save settings** or a noun.
2. The Save Bar is the last element of what it saves. Nothing sits beside it or below it in that section.
3. Any other actions that belong with that Save, such as **Reset to defaults**, **Restart**, or **Test**, go in one `<div class="stolon-button-row">` directly above it. Each is `raised stolon-button secondary fill`, or `destructive fill` for one that deletes or resets data. A select that belongs there stays a normal `emby-select` in the row. No `small` buttons in that row. Stolon spaces that row. Its buttons have no margin of their own, so their edges line up with the Save Bar's, the row sits one `--stolon-gap-narrow` above the Save Bar, and its buttons use the same font weight as Save. A plugin adds no margin, gap, or weight of its own. The Save Bar's element goes directly after the row, never inside a wrapper, since Stolon spaces them as siblings.
4. Never hand place a Save button, a small Save, a Save Bar without `fill`, or a status line or unsaved indicator of its own beside it. No plugin CSS positions a Save or its neighbours.

* Call `setDirty` on the Save Bar on every input and change, from `createChangeTracker` or the page's own check.
* `onSave` returns the save's promise. It rejects with an Error holding the reason, and the Save Bar shows it.
* Guard leaving the page with `page.guardUnsaved`.
* Use `shared.saveConfigWith` for get, change, and save inside `onSave`.

## Components

Every component below is in Stolon. Use it as Stolon ships it. When it does not fit, change Stolon.

### Find the Component

Before writing markup for a list, a row editor, a chart, or a picker, look for it in Stolon.

```bash
grep -o "export function create[A-Za-z]*" ~/.nuget/packages/siphonophore.stolon/<version>/assets/stolon.d.ts
```

* The typings in that file document every option of every component. Read the component's block before using it.
* Rows of fields with Add and Remove are a `createRepeater`. A list of checkboxes is a `createCheckboxList`. A used amount out of a limit is a `createUsageMeter`. Parts of a whole are a `createPieChart`.
* A hand built version of any of these is a defect, even when it looks the same.
* When a Stolon component lacks one thing the page needs, use the component and report the gap to Stolon. Never fall back to building it by hand.

### Fields

| Need | Use |
| :--- | :--- |
| Text, number, or URL | `inputContainer` with `<label for>`, `<input is="emby-input">`, and a `fieldDescription` |
| Choice from a short list | `selectContainer` with `label.selectLabel` and `<select is="emby-select">` |
| On or off | `checkboxContainer checkboxContainer-withDescription` with `emby-checkbox` and `fieldDescription checkboxFieldDescription` |
| A mode that needs a sentence per option | `createChoiceGroup` with `stolon-choice` cards |
| Several on or off options in a row | `stolon-toggle-group` with `stolon-toggle` pills |
| Many checkboxes | `stolon-check-grid` |
| Many items from a long or searchable list | `createCheckboxList`, with `renderRow` giving each row a `label`, a `sublabel`, and an `image` loader |
| Rows of several fields that are added and removed | `createRepeater` |
| Pairs of a name and a value | `createKeyValueEditor` |
| A list of plain text values | `createStringList` or `createTagInput` |
| Typeahead with removable chips | `createChipSelect`, with `single: true` for one choice |
| One user | `createUserSelector` |
| Several users | `createUserMultiSelector` |
| Several fields side by side | `stolon-field-row` with `stolon-field` |
| A secret | `bindSecretField`, `readSecretField`, and `clearSecretField` |
| A template with `{name}` tokens | `placeholderHint` and `bindPlaceholders` |
| Code or JSON, read only | `stolon-code-block` |

* Use `emby-select`, never `stolon-selector-dropdown`, for a field. `stolon-selector-dropdown` is only for the record picker.
* Every input has a `<label for>`.

### Buttons

| Need | Class |
| :--- | :--- |
| Primary action | `raised stolon-button primary` |
| Destructive action | `raised stolon-button destructive` |
| Secondary action | `raised stolon-button secondary` |
| Compact action in a section | add `small` |
| Actions that go with a Save | `raised stolon-button secondary fill`, or `destructive fill`, in one `stolon-button-row` directly above the Save Bar |
| Icon and label in a toolbar | `raised stolon-button` holding a `material-icons` span and a label |
| Icon only in a row | `stolon-row-btn`, with `.dirty` for unsaved and `.success` after saving |

* Every button is `<button is="emby-button" type="button">`.
* Icons are `<span class="material-icons" aria-hidden="true">name</span>`.
* A sidecar's close button is a `stolon-row-btn stolon-dialog-close` holding the `close` icon, at the end of its header. Never "✕", `&times;`, or another glyph. A dialog has no close button.

| Action | Icon |
| :--- | :--- |
| Add | `add` |
| Edit | `edit` |
| Remove or delete | `delete` |
| Copy | `content_copy` |
| Move up and down | `keyboard_arrow_up`, `keyboard_arrow_down` |
| Drag | `drag_indicator` |
| Expand | `expand_more` |
| Retry | `refresh` |
| Open | `chevron_right` |
| Import and export | `upload`, `download` |
| Show | `visibility` |

### Records

| Need | Use |
| :--- | :--- |
| One record at a time from a list | `stolon-selector` with New, Rename, and Remove |
| Records in priority order | `createSortableCardList` |
| A plain list of records | `stolon-card-list` with `stolon-card-item-*` |
| Edit one record in a sidecar | `openEditorSidecar` |
| A large searchable list | `createPaginatedTable` |
| Edit table rows in place | `stolon-bulk-edit-bar` and `stolon-edit-row` |
| Uploaded files | `stolon-asset-list` |
| A thumbnail in a table row | A column `image(item)` loader with `imageShape`, one shape for each column |
| A mark on a table row, such as picked | The table's `rowClass(item, index)` |

* A record picker's actions read **New**, **Rename**, **Remove**, then **Import** and **Export**, in that order.
* Use `createPaginatedTable` for any list that can grow past one screen. Rows open with `actions.onRowClick`.

### Status

| Need | Use |
| :--- | :--- |
| The state of one item | `shared.statusBadge(text, state)` |
| A label on an item | `shared.badge(color, label)` |
| Counts at the top of a page | `shared.renderCards` with `stolon-cards`, and `compact` for many counts on a phone |
| Label and value pairs | `stolon-record-card` |
| A notice in the page | `stolon-info`, `stolon-success`, `stolon-warn`, `stolon-error`, `stolon-neutral` |
| Raw failure text | `stolon-error-message` |
| The result of an action | `shared.setStatus` |
| A row of reset actions | `stolon-reset-group` with `stolon-reset-row` |

* Never use `Dashboard.alert` for a result. It blocks the page. Use `setStatus`.
* A notice holds one sentence. Use a callout for something the reader must know before acting, not for a result.

### Charts

| Need | Use |
| :--- | :--- |
| Parts of a whole, such as space by disk | `createPieChart`, with `donut` and the total in `centerText` |
| How much of a limit is used, such as a disk | `createUsageMeter`, with `bytes: true` for sizes |
| Values compared across names | `createBarChart` |
| A value over time | `createLineChart` or `createSparkline` |
| One number with a label | `createStatCard` |

* Pick the chart by the question it answers. A single used of total bar is a Usage Meter, never a Bar Chart.
* Name each slice in sentence case, such as **Free space**.
* Take colors from Stolon's series colors. Set a color only when it carries a [meaning](#meaning).
* Give every chart a `label`, and an `emptyText` sentence for when it has nothing to draw.
* Scope a chart to what the page shows. A chart beside a disk picker counts that disk only.

### Progress

* A task started from a button shows progress in that button with `shared.pollTaskProgress`.
* Progress inside a row uses `stolon-progress` and `stolon-progress-bar`.
* Never block the page with `Dashboard.showLoadingMsg`.

### Dialogs and Sidecars

A dialog is only for a confirmation. It never scrolls and has no close button. Everything else, inputs, an editor, and the details of a selected item, is a sidecar that slides in from the trailing edge.

| Need | Use |
| :--- | :--- |
| A yes or no question | `confirmDialog`, compact |
| A yes or no question about one item | `confirmDialog` with a `context` from `createDialogItem`, full |
| A confirmation with its own layout | `openDialog`, compact or full |
| Inputs, an editor, or the details of a row | `openSidecar` |
| A sidecar already in the page markup | `bindSidecar` |

* A compact dialog is a title, a description, and buttons. A full one is a title, a subtitle, a context, and buttons.
* A sidecar keeps its header, the title and the close button, and its shelf of buttons pinned, and only its body scrolls. Its shelf reads, from left to right, the destructive action, then **Cancel** and the other actions, then the primary action.
* Build sidecars in JS with `openSidecar`. Use `bindSidecar` only when the markup is large and static, such as a detail panel.

## Layout

* Use Stolon's breakpoints, 768 px for tablet and 600 px for phone. Dialogs and sidecars size themselves.
* Test every page at 375 px wide. Nothing scrolls sideways except a table.
* Look at every changed page on a test server before calling it done. Reload the page after installing a build, since the dashboard caches scripts.
* Spacing comes from `--stolon-gap-wide` and `--stolon-gap-narrow`. Never add margins between fields.
* Never set a width in pixels on a field. Let it fill its container.

## Accessibility

* Every input has a label, and every icon only button has a `title` and an `aria-label`.
* Every icon span has `aria-hidden="true"`.
* Rows that open on click also open with Enter and Space. Stolon's table and card list do this already.
* Never rely on color alone.

## CSS in This Plugin

* Plugin classes use the `necto-` prefix in kebab case. `stolon-` is reserved for Stolon.
* Keep plugin CSS for layout that only this plugin has. A component another plugin could use goes to Stolon.
* Never override a `stolon-` class. Change Stolon instead.
* When Stolon has a defect that breaks a page, report it and add one marked stopgap rule under a comment that names the defect. Delete the rule in the commit that moves to the Stolon version with the fix.
* Size a column in `rem` or `fr`. An `em` width changes with the font size of each row, so a smaller heading row falls out of line with its columns.
* Never define a color token. Use Stolon's.

## Translations

Every word a person reads comes from a key. Stolon builds the strings in and checks them on every build. Its [Translations](https://siphonophore-org.github.io/stolon/Translations/) guide is the full reference.

* Use a Jellyfin string whenever Jellyfin has the words, declared as `<JellyfinString Include="Key" />` in `Jellyfin.Plugin.Necto/Jellyfin.Plugin.Necto.csproj`.
* Add a local key to `Localization/en-us.json` only when Jellyfin has no string for it. A key is PascalCase and says what the text is, such as `TabProxy` or `DashboardErrorLoad`. A key that more than one tool uses takes a neutral name with no tool prefix, such as `ButtonClear`.
* Mark markup with `data-str`, `data-str-title`, `data-str-label`, or `data-str-placeholder`, with the English as the element's own text. In a script, use `shared.t.tr('Key')` or `shared.t.format('Key', value)`. A sidebar name uses `tabName('Key', 'English')` in `necto_shared.js`, since the sidebar shows before the strings load.
* Keep placeholders as `{0}`, `{1}`. Never build a sentence from pieces.
* Add each word with special casing, such as "DDNS", to `Localization/casing.json`.
* The pages load their strings from `GET /Necto/Translations`. The build fails on a key or English said twice, so fix every `STOL` error and warning.

## Checklist

Run every new or changed string through this list.

* [ ] No em dashes, en dashes as punctuation, or semicolons.
* [ ] No colon at the end of a label, and none in a sentence unless it introduces a list.
* [ ] One sentence, or two when the second is the fix or the consequence.
* [ ] Sentences end with a period. Labels, buttons, and titles do not.
* [ ] Sentence case everywhere, with casing.json words kept as written.
* [ ] No "please", "successfully", exclamation marks, ALL CAPS, "(s)", or contractions.
* [ ] The word matches [Words to Use](#words-to-use).
* [ ] Stolon's typings were searched before any markup was built by hand.
* [ ] The component comes from Stolon, unchanged.
* [ ] Each Settings or Troubleshooting section, and each reset row, leads with its icon tile.
* [ ] Colors come from Stolon tokens and mean what [Meaning](#meaning) says.
* [ ] The string comes from a key, per [Translations](#translations).
* [ ] The docs use the same label.
