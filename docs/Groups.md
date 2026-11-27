# Groups

Groups set what their members may do in every Necto tool, and hold the account rules their members follow.

## The Groups Tool

**Groups** lists every group, **Everyone** first. Each row shows how many members the group has and which account rules it has on. Select a row to open the group in a sidecar, or select **Add group** to start a new one.

The sidecar holds the group's name, its members, **Account rules**, and one collapsible section per tool. Its shelf holds **Delete**, **Cancel**, and **Save**. **Delete** asks first, with the group's name in the title, such as "Delete Family". Its members keep their other groups, and a member in no other group takes **Everyone**.

Sections for tools that are not built show their fields with the note "This tool is coming." Those permissions are saved, and take effect once the tool is built.

## Everyone

**Everyone** always exists and cannot be deleted or renamed. It holds every user who is in no other group, so its permissions and rules apply only to those users. A user in a group never takes **Everyone**.

## Members

A user may be in several groups. Administrators may do everything, so no group holds them. A member promoted to administrator leaves every group on the next group save or server start, and a deleted user leaves every group at once.

**Settings > Groups > Group for new users** sets the group a new user joins. With **Everyone** picked, a new user joins no group.

## How Groups Combine

A member of several groups gets what any of them gives.

* An allow from any group allows, such as **Allow requests**.
* A count or limit takes the largest any allowing group gives. 0 is no limit, so it beats any number.
* A list holds what every group lists, such as the pages a member may open.

The account rules combine the other way, since any one group may keep a member's account.

* **Password requirements** hold when any group enforces them. The member meets the strictest rule of those groups, and gets the most generous **Password changes** of them.
* **Expiry** applies once every group has expired, on the latest date. **Delete the account** needs every group to ask for it, else the account is disabled.
* **Inactivity** and **Session management** apply only when every group has them on, after the largest window any group gives.

Administrators are never held to an account rule.

## Account Rules

### Password Requirements

* **Enforce password requirements** holds members' passwords to the rules below. Members on Jellyfin's own sign in move onto Necto's password rule provider, which signs them in as Jellyfin does. A member signed in elsewhere, such as LDAP, stays where they are.
* **Disallow empty passwords** refuses an empty password. While off, an empty password always passes, since it means the account has no password.
* **Minimum length** sets the shortest password allowed.
* **Password changes** sets whether members may set or change their own password.
* **Require a capital letter**, **Require a number**, and **Require a symbol** each add a rule.

| Action | Allowed | Initial password only | Disallowed |
| :--- | :--- | :--- | :--- |
| Member changes their password | If it meets the rules | Refused | Refused |
| Member with no password sets one | If it meets the rules | If it meets the rules | Refused |
| Administrator changes a member's password | Always | Always | Always |

A refused change shows the reason on the password form, and writes "Password change rejected" or "Password change blocked" to the activity log. The forgotten password flow always passes, since only an administrator can read its code.

!!! warning "Before removing Necto"
    Members held to password rules sign in through Necto's provider. Turn off every group's password requirements and save before removing the plugin, so each member moves back to the provider they had.

### Expiry

* **Expire members on a date** disables or deletes members once the date comes.
* **Expiry date** sets the day. The next **Process expired and inactive users** task run on or after it expires members.
* **Expiration method** sets what happens. **Disable the account** can be undone. **Delete the account** removes each member and their data for good.

### Inactivity

* **Disable inactive users** disables members who stop using the server.
* **Days of inactivity before disabling** sets the window. A member who never signed in is left alone.

### Session Management

* **Clean up old sessions** logs out members' devices that go too long without checking in.
* Each rule sets **Applies to**, its **Clients**, and **Days before logout**. **All clients** covers every client, **Only these clients** covers the listed ones, and **All except these clients** covers the rest.
* Where several rules of one group cover a client, the shortest window wins, and the editor names the clients that overlap.

## Tool Permissions

| Section | Fields |
| :--- | :--- |
| **Requests** | **Allow requests**, **Movie quota**, **Series quota**, **Auto approve requests**, **Show others' requests**, **Manage requests** |
| **Issues** | **Allow issue reports**, **Manage issues** |
| **Invites** | **Allow invites**, **Open invites**, **Invite to any group**, **Invite groups** |
| **WireGuard** | **Allow peers**, **Peers per member**, **Data per member (GB)**, **Period**. See [WireGuard](WireGuard.md#permissions) |
| **Scripts** | **Run every script**, **Scripts that run**. See [Scripts](Scripts.md#groups) |
| **Custom pages** | **Open every page**, **Pages members may open** |
| **Notifications** | **Receive every event** |

## Scheduled Tasks

| Task | What it does | Runs |
| :--- | :--- | :--- |
| **Process expired and inactive users** | Disables or deletes users whose groups have expired, and disables inactive users | Daily at midnight |
| **Clean expired sessions** | Logs out devices past their groups' session windows | Daily at midnight |
| **Apply password rules** | Drops administrators and deleted users from the groups, and moves users onto or off password rules | When the server starts |

## Settings

**Settings > Groups** holds the settings that are not permissions.

* **Group for new users** sets the group a new user joins.
* **Show reset codes** lists the codes of pending password resets in **Troubleshooting**. Use HTTPS so they cannot be read in transit.

## Troubleshooting

**Troubleshooting > Groups** holds the repairs.

* **Apply rules** moves users onto or off password rules by their groups, and drops administrators from every group.
* **Run now** beside **Process expired and inactive users** asks first, then runs the task. A deleted user cannot come back.
* **Run now** beside **Clean expired sessions** runs the task.
* **Password resets** lists each pending reset with its user and its code, while **Show reset codes** is on.

| Reason | What to do |
| :--- | :--- |
| A member is not held to the group's password rules | Check that the member signs in with Jellyfin's own provider, then select **Apply rules** |
| "Reset codes are off. Turn on Show reset codes in Settings to list them." | Turn on **Show reset codes** in **Settings > Groups** |

## Activity Log

| Entry | When |
| :--- | :--- |
| "Group created", "Group deleted" | A group is created or deleted |
| "Users enrolled in password rules", "Users removed from password rules" | Users move onto or off password rules |
| "Password change blocked", "Password change rejected" | A member's own change is refused |
| "Users disabled", "Users deleted" | Expiry or inactivity changed accounts in a run |
| "Sessions logged out" | Session cleanup logged out devices in a run |

An entry about one user uses the singular, such as "User disabled".

## API

Every route is for administrators.

| Route | What it does |
| :--- | :--- |
| `GET /Groups` | Lists every group, Everyone first |
| `GET /Groups/{id}` | Gives one group |
| `POST /Groups` | Creates a group |
| `PUT /Groups/{id}` | Saves a group's name, members, and permissions |
| `DELETE /Groups/{id}` | Deletes a group |
| `GET /Groups/Users` | Lists the users with the groups that apply to each |
| `GET /Groups/Users/{userId}/Permissions` | Gives what a user may do in every tool |
| `POST /Groups/PasswordRules/Apply` | Moves users onto or off password rules |
| `GET /Groups/ResetCodes` | Lists the pending password resets while **Show reset codes** is on |

A refused change answers `400` with `{ "Error": "..." }`, and a missing group or user answers `404`.
