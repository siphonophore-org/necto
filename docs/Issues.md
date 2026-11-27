# Issues

Issues lets users report a problem with something they play, such as a movie whose sound drops out or an episode with the wrong subtitles, and lets managers confirm, resolve, or decline each report.

Issues is off until **Enable issues** is turned on in **Settings > Issues**.

## The Issue Page

The issue page is the request page served at `https://jellyfin.example.com/issues/`, with the same header, menu, sign in, and app install. It looks and works like the web client's search page.

* With nothing typed, **Suggestions** lists what the viewer played last.
* Typing searches the library for things the viewer can play, in **Movies**, **Episodes**, **Songs**, **Videos**, and **Audiobooks** rows. Nothing from a library the viewer cannot see is listed.

Select an item to open its dialog. An item with no open issue offers a new report. An open issue the viewer reported or joined offers their type and note to change, with **Save** and **Delete**. An open issue someone else reported offers **Join issue**. A chip above the title says **Issue reported**, **Issue confirmed**, or **Issue resolved** for 30 days after its last issue was resolved.

A report asks for an **Issue type** (**Video**, **Audio**, **Subtitles**, **Wrong item or metadata**, **Does not play**, or **Other**) and a **Note**. **Other** needs a note. An item has at most one open issue.

**Delete** marks the issue **Rescinded** when the viewer is its only reporter. When others joined it, it takes the viewer and their note off it, and it stays open for them.

The history button beside the title opens **Issue history**, each issue with its state, type, date, and notes. A manager can **Decline**, **Confirm**, or **Resolve** an open issue from there, with an optional note.

The page's address for one item is `#/item?id=…`, which opens its dialog. A notification about an issue opens there.

## States

| State | Meaning |
| :--- | :--- |
| Reported | Waiting for a manager |
| Confirmed | A manager agrees something is wrong |
| Resolved | A manager fixed it |
| Declined | A manager found nothing to fix |
| Rescinded | Its only reporter deleted it |

Reported and Confirmed issues are open. The rest are closed.

## Notes

An issue's notes are kept in the order they were written. On the issue page a reporter sees their own notes and the managers' replies, signed **Administrator**. Only managers see who reported an issue and every note with its writer's name.

## File Changed

Each issue keeps the item's file as it was reported, with its path, size, and the time it last changed. When the file is replaced or rescanned and any of those differ, or the item leaves the library, the issue is marked **File changed** for a manager to check. Nothing is resolved on its own. A scan's changes are checked a few seconds after it touches the item, and **Clean expired requests** checks every open issue each night.

## The Issues Tool

**Issues** in the Necto sidebar lists every issue, most recently changed first.

* Cards count the **Open**, **Reported**, **Confirmed**, **Resolved**, and **Declined** issues.
* Each row shows the item and its poster, what the reporters say is wrong, who reported it and when it last changed, its state, and **File changed** when the file changed.
* **Search** matches the item, its show, and the reporters' names. The state filter and the type filter are kept for the next visit.

Select a row to open the issue in a sidecar, with its reporters, its file, and every note in order. It offers **Confirm** a reported issue, **Resolve** or **Decline** an open one, **Reopen** a closed one, **Open in library**, and **Open in Sonarr** or **Open in Radarr**. Each action asks for an optional note, shown to the reporters.

While **Enable issues** is off, **Issues** leaves the sidebar, and opened by its address it says "Issues is off. Turn it on in Settings to take reports."

## Settings

**Settings > Issues** holds the tool's settings. Who may report and manage is set on groups.

| Setting | What it does | Default |
| :--- | :--- | :--- |
| **Enable issues** | Serves the issue page at `/issues/` and adds an **Issues** link to the web client's menu. Off hides the page and takes the link out, and keeps the issues already reported | Off |
| **Keep closed issues (days)** | Removes resolved, declined, and rescinded issues after this long. 0 keeps them | 90 |
| **Issue limit window (days)** | Sets how many days back a group's **Issue limit** counts | 1 |

## Permissions

Each group's **Issues** section sets what its members may do. See [Groups](Groups.md#how-groups-combine) for how several groups combine.

| Permission | What it does |
| :--- | :--- |
| **Allow reports** | Lets members report and join issues while issues are on |
| **Issue limit** | Sets how many issues a member may report or join in the window set in **Settings**. 0 is no limit |
| **Manage issues** | Lets members confirm, resolve, decline, and reopen issues, and see every reporter and note |

Administrators may do everything, with no limit.

## Notifications

Issues sends these events through **Notifications**. Every reporter receives an issue's events when their groups allow. The administrators' channels leave out the reporters of a new issue, and the administrator who resolved or declined one.

| Event | When |
| :--- | :--- |
| `issues.created` | Someone reports an issue |
| `issues.updated` | A manager resolves or declines an issue, with the manager's note |

A webhook gets the issue's id, state, type, its item's id, and its TMDb, TVDB, and IMDb ids. A notification shows the item's poster when **Settings > Notifications > Server address** is set. See [Notifications](Notifications.md#what-posts-carry) for what else a post carries.

## Troubleshooting

**Troubleshooting > Issues** runs **Clean expired requests** now, which clears closed issues past **Keep closed issues (days)** and marks open issues whose file changed.

| Reason | What to do |
| :--- | :--- |
| `/issues/` answers 404 | Turn on **Enable issues** in **Settings > Issues** |
| The **Issues** link is missing from the web client's menu | Save **Settings > Issues** again. The server log says when the web client's `config.json` cannot be written |
| A user cannot report | Check that one of their groups has **Allow reports**, and that they are under the **Issue limit** |
| An issue says **File changed** | Check the item's file, then **Resolve** or **Reopen** it |
