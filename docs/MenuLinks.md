# Menu Links

**Settings > Menu links** edits the custom links in the web client's side menu. They live in the `menuLinks` of the web client's `config.json`, beside the web client on the server. Reload the web client to see a change.

## The List

Each link shows as a row with its icon, its name, and its address, in the order the menu shows them. The arrows on a row move it up and down. **Add menu link** opens a new link, and the **Edit link** button on a row opens that link. **Save** at the end of the section writes the list.

A link opens in a sidecar with three fields.

| Field | What it holds |
| :--- | :--- |
| **Name** | The name the side menu shows |
| **Icon** | A Material icon name. The grid under the field shows about 70 common icons, narrowed by what is typed, and selecting one puts its name in the field. Any other Material icon name can be typed. Blank shows the web client's default icon |
| **Address** | What the link opens. It offers the Requests page, the Invites page, and each page of [Pages](Pages.md), and takes any other address |

**Done** keeps the change in the list, and **Remove** asks first, then takes the link out of the list. Neither writes the file until **Save**.

## Managed Links

Necto adds a **Requests** link while [Requests](Requests.md) is on, and an **Issues** link while [Issues](Issues.md) is on, and takes each out when its tool is turned off. While its tool is on, the link carries a **Managed** badge.

* A managed link cannot be removed, and its address stays as it is.
* Its name and icon can change. Necto keeps them, and adds the link back with them when its tool is turned off and on again.
* Each is added once when its tool is turned on, so one removed from `config.json` by hand stays removed until its tool is turned off and on.

## The File

A save reads `config.json`, replaces only `menuLinks`, and swaps the whole file in, so the web client never reads half a file. Every other key keeps its value and its place. A key the editor does not show, such as one added to a link by hand, stays on the link to the same address.

When `config.json` cannot be found, read, or written, the section says why and **Add menu link** is turned off. **Troubleshooting > Menu links** shows where the file is and whether it can be saved.

| Problem | Fix |
| :--- | :--- |
| The web client's config.json was not found | The server serves no web client, or keeps it elsewhere. Add links to that web client's own `config.json` |
| The web client's config.json is not valid JSON | Fix the file by hand, then reload Settings |
| The web client's config.json cannot be written | Give the account Jellyfin runs as write access to the file. In Docker, mount the file or its folder writable |

## API

| Route | Who | What it does |
| :--- | :--- | :--- |
| `GET /Necto/MenuLinks` | Administrators | Gives the links, where `config.json` is, whether it can be saved, and why not |
| `PUT /Necto/MenuLinks` | Administrators | Saves the links in their order, refusing to drop a managed link while its tool is on |
