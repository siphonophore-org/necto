# wgsidecar

Runs WireGuard in userspace with wireguard-go and its gVisor network stack. It needs no root, no TUN device, and no container permissions, only one UDP port. Tunnel traffic ends inside the process and is forwarded as ordinary sockets.

The binary is statically linked, so the host needs nothing installed.

## Commands

| Command | What it does |
| :--- | :--- |
| `wgsidecar genkey` | Prints a new private key. |
| `wgsidecar pubkey` | Reads a private key on stdin and prints its public key. |
| `wgsidecar run` | Reads JSON config lines on stdin and runs until stdin closes or SIGTERM. |
| `wgsidecar run -config file.json` | Runs the tunnel from a config file. |
| `wgsidecar bench [-seconds 5]` | Runs two tunnels over loopback and prints this machine's upload and download ceiling in Mbit/s. |

The plugin always passes the config on stdin, so the private key never touches disk, and keeps stdin open. Closing it stops the sidecar, so it never outlives Jellyfin.

The first line starts the tunnel. Each later line replaces the peers and their limits live. Peers that stay keep their sessions, removed peers are dropped, and new ones are added. Any other change needs a restart, which the plugin does on its own.

## Config

```json
{
  "privateKey": "base64",
  "listenPort": 51820,
  "addresses": ["10.66.0.1"],
  "peers": [
    { "publicKey": "base64", "presharedKey": "base64", "allowedIPs": ["10.66.0.2/32", "192.168.50.0/24"], "sendMbps": 40, "recvMbps": 10 }
  ],
  "inbound": [
    { "port": 8096, "target": "127.0.0.1:8096", "http": true, "forwardFor": true }
  ],
  "outbound": [
    { "listen": "127.0.0.1:18096", "target": "10.0.0.1:8096" }
  ],
  "dns": { "names": ["jellyfin.internal"], "upstreams": ["1.1.1.1:53"] }
}
```

* **inbound** listens on a tunnel port and forwards to a local target. With `http`, it proxies requests and drops any `X-Forwarded-*` a peer sent. With `forwardFor` too, it writes the peer's tunnel address to `X-Forwarded-For`.
* **outbound** listens on a local address and dials a target across the tunnel.
* A peer's `endpoint` and `keepalive` are set for outbound connections. A host name in `endpoint` is resolved with each config line. One that does not resolve keeps the peer's last endpoint, and the rest of the update still applies.
* `sendMbps` caps what this side sends to the peer and `recvMbps` what it takes from it. They apply to every forwarded connection the peer owns, matched by the longest of its allowed IPs. A changed limit applies to open connections at once.
* **dns** answers on port 53 of the first address. Each name in `names` resolves to that address, and every other query goes to `upstreams`. Only queries cross the tunnel, so a peer's other traffic stays off it.

## Output

Log lines go to stderr. One JSON event per line goes to stdout:

| Event | When |
| :--- | :--- |
| `ready` | The tunnel is up. |
| `peer-up` | A peer's first handshake, with its `publicKey` and `endpoint`. |
| `peer-down` | A peer's last handshake is over 3 minutes old. WireGuard renews it every 2 minutes while traffic flows. |
| `stats` | Every 10 seconds, each peer's handshake, bytes, and `connected`. |

## Speed test

Every HTTP inbound answers `/wireguard/speedtest` itself, so it exists only inside the tunnel and measures the tunnel, not Jellyfin.

* `GET /wireguard/speedtest?bytes=N` sends N bytes, up to 1 GiB.
* `POST /wireguard/speedtest` reads the body and answers `{"bytes": n, "ms": t}`.

## Building

`./build.sh` builds every platform into `../dist-sidecar/` as `wgsidecar-{os}-{arch}`, with a `SHA256SUMS` file. The **Release wgsidecar** workflow runs it and publishes the files as the release `wgsidecar-<version>`, where the version is `SidecarRelease.Version`. Necto downloads the file for its server from that release, checks it against `SHA256SUMS`, and keeps it in its data folder.

`go test ./...` runs the sidecar's tests.
