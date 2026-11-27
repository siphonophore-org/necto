# Notifications

Notifications sends every event a Necto tool reports to the people and channels it goes to.

## Channels

**Notifications** lists every channel, each with **Test**, which sends the test event to that channel and shows what happened. **Channel settings** opens **Settings > Notifications**, where the channels are set.

| Channel | Sends to | Events |
| :--- | :--- | :--- |
| **Browser notifications** | The browsers a user turned notifications on in, from a user page | The events the user's groups give them |
| **Jellyfin messages** | A message on the user's Jellyfin apps, held until an app comes online | The events the user's groups give them |
| **Administrators** | Every administrator's Jellyfin apps, and the browsers they turned notifications on in | The events the channel picks, every event by default |
| **Activity log** | Jellyfin's activity log | The events the channel picks, none by default |
| Discord | A Discord channel webhook, as an embed | The events the channel picks |
| Webhook | Any address that takes a POST, as JSON or a custom body | The events the channel picks |
| Email | The addresses the channel lists, through the SMTP server | The events the channel picks |

A test of **Browser notifications**, **Jellyfin messages**, or **Administrators** goes to the administrator who selected it. A channel that fails never stops the others, and every send is written to the send log.

## Who Gets an Event

* An event a tool marks for users goes to the user it is about, when the account is on and their groups give them the event. **Groups > Notifications** sets the events, with **Receive every event** or a pick from **Events**. Administrators get every event about them.
* Every event goes to each administrators' channel that picks it, or takes every event.
* An administrator the event is about gets it once, as a user, and not again through **Administrators**.

## Settings

**Settings > Notifications** holds the settings. It saves through its own route, so no stored address, header, or password reaches the page.

### User Channels

* **Browser notifications** shows events in the browsers users turned notifications on in.
* **Jellyfin messages** shows events on users' Jellyfin apps, held until an app comes online.

### Links

* **Server address** starts the links in Discord, webhook, and email notifications, such as `https://jellyfin.example.com`. Left blank, links stay relative.
* **Push contact** gives push services a way to reach you, as a `mailto:` or https address. Left blank, it is the server address.

### Email Server

* **SMTP host** and **SMTP port** name the server. 587 is the usual port with STARTTLS.
* **Use STARTTLS** encrypts the connection.
* **SMTP user name** and **SMTP password** sign in. Leave the user name blank to send without signing in. The password is stored encrypted.
* **From address** and **From name** say who email comes from.

### Administrators' Channels

Each channel opens in a sidecar. **Add Discord**, **Add webhook**, and **Add email** start a new one, and **Done** keeps it until the section's **Save**.

* **Name** names an added channel. **Administrators** and **Activity log** keep their names and cannot be removed.
* **Send to this channel** turns the channel on or off.
* **Discord webhook address** and **Webhook address** are stored encrypted and never shown again.
* **Post as** and **Avatar address** set who a Discord post comes from.
* **Headers** sends one `Name: value` header a line, such as a token. They are stored encrypted, and kept only while the address stays the same.
* **Use a custom body** sends your own body in place of the one Necto builds. **Body** takes placeholders, and JSON is sent as JSON.
* **Addresses** lists an email channel's recipients, one a line.
* **Every event** sends every event, or the list below it picks them.
* **Preview** shows what the channel would get for the test event.

| Placeholder | Holds |
| :--- | :--- |
| `{title}` | The event's title |
| `{body}` | The event's text |
| `{event}` | The event's key, such as `invites.used` |
| `{eventName}` | The event's name, such as Invite used |
| `{level}` | `Info`, `Warning`, or `Error` |
| `{link}` | The page the event opens, made absolute with **Server address** |
| `{time}` | When it was sent, in UTC |
| `{color}` | The Discord color of its level |
| `{serverURL}` | **Server address** |
| `{userId}` | The user the event is about |

An event's own values, such as `{inviteId}`, are placeholders too, and a webhook's JSON carries them under the event's key.

## Browser Notifications

Necto makes its VAPID key pair the first time a page needs it, and keeps the private key encrypted. Every user page registers one service worker, `Notifications/Push/Worker.js`, at the server's root, so a browser keeps one subscription for all of them. A user page shows **Turn on notifications** while the browser can take them, and **Turn off notifications** once it has.

!!! info "HTTPS"
    Browsers allow notifications only on a page served over HTTPS, or on localhost. On an iPhone or iPad the page must be added to the home screen first.

## Events

Each tool lists the events it sends in a static class named `<Tool>Events`, with `public static IReadOnlyList<NectoEventInfo> All`. Necto finds every list by reflection, so a tool's events show in every picker once its list exists.

A tool sends each event through `INotifier.SendAsync` as a `NectoEvent`: its key, title, and text, with the user it is about, any `OtherUserIds`, the page it opens, its level, and its data. `Hold` set off sends a Jellyfin message only to apps online now, `OneDevice` shows it on each user's most recent app only, and `ImageUrl` adds a poster to Discord and browser notifications.

| Key | Name | For users | Sent when |
| :--- | :--- | :--- | :--- |
| `notifications.test` | Test notification | Yes | **Test** or **Send a test to me** is selected |
| `invites.used` | Invite used | Yes, to the member who made the invite | An invite makes an account |
| `invites.usedUp` | Invite used up | Yes, to the member who made the invite | An invite makes its last account |
| `invites.locked` | Invite locked | Yes, to the member who made the invite | Wrong PINs lock an invite |
| `invites.expired` | Invites expired | No | The expiry task turns invites off |
| `invites.created` | Member invite made | No | A member makes an invite |

## Troubleshooting

**Troubleshooting > Notifications** holds the tests and repairs.

* **Send a test to me** sends the test to your browsers and your Jellyfin apps.
* **Send log** lists the latest 200 sends, newest first, with what happened. It starts empty when the server starts. **Refresh** reads it again, and **Clear log** empties it.
* **Browsers** lists the browsers with notifications on, by user, with the count of Jellyfin messages waiting for an app.
* **Clear waiting messages** asks first, then drops every held Jellyfin message.
* **Make new push keys** asks first, then makes a new key pair. Every browser must turn notifications on again.

| Reason | What to do |
| :--- | :--- |
| "No browser has notifications on. Turn them on from a user page first." | Open a user page, such as `/invites`, in that browser and select **Turn on notifications** |
| "Set the SMTP server in Settings first." | Fill **SMTP host** and **From address** in **Settings > Notifications** |
| "Add an address to the channel first." | Add an address to the email channel |
| "Could not send the email. Check the SMTP settings." | Check the host, port, STARTTLS, and sign in, then select **Test** |
| A Discord or webhook test fails | Check the address, then select **Preview** to see what is sent |

## Limitations

* Email uses STARTTLS. A server that only takes TLS from the start, usually on port 465, cannot be used.
* The send log lives in memory, so a restart clears it.

## API

| Route | Who | What it does |
| :--- | :--- | :--- |
| `GET /Notifications/Events` | Administrators | Lists every event, by tool |
| `GET /Notifications/Channels` | Administrators | Lists the channels the page shows |
| `POST /Notifications/Channels/{id}/Test` | Administrators | Sends the test to one channel |
| `POST /Notifications/Preview` | Administrators | Shows what a channel would get for the test |
| `GET /Notifications/Settings` | Administrators | Gives the settings, every secret hidden |
| `PUT /Notifications/Settings` | Administrators | Saves the settings, keeping secrets sent back as kept |
| `GET /Notifications/Log`, `DELETE /Notifications/Log` | Administrators | Gives or clears the send log |
| `GET /Notifications/Push` | Administrators | Lists the browsers and the held count |
| `POST /Notifications/Push/Keys` | Administrators | Makes a new VAPID key pair |
| `DELETE /Notifications/Held` | Administrators | Drops every held Jellyfin message |
| `GET /Notifications/Push/Key` | Signed in users | Gives the VAPID public key, or `404` while browser notifications are off |
| `POST /Notifications/Push/Subscribe` | Signed in users | Turns on notifications for the caller in one browser |
| `POST /Notifications/Push/Unsubscribe` | Signed in users | Turns them off for the caller in one browser |
| `GET /Notifications/Push/Worker.js` | Anyone | The service worker every user page shares |

A refused save answers `400` with `{ "Error": "..." }`.
