// Command wgsidecar runs WireGuard in userspace with its own network stack, so it needs no root,
// no TUN device, and no container permissions. Tunnel traffic ends inside this process and is
// forwarded as ordinary sockets.
//
//	wgsidecar genkey               prints a new private key
//	wgsidecar pubkey < private     prints the public key for a private key
//	wgsidecar run                  reads JSON config lines on stdin and runs until stdin closes
//	wgsidecar run -config file     runs the tunnel described by a JSON config file
//	wgsidecar bench [-seconds 5]   measures this machine's tunnel ceiling over loopback
//
// The first config line starts the tunnel. Each later line replaces the peers and their limits
// without dropping the peers that stay. Log lines go to stderr and one JSON event per line goes to
// stdout: ready, peer-up, peer-down, and stats every ten seconds.
package main

import (
	"bufio"
	"context"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"net/http/httputil"
	"net/netip"
	"net/url"
	"os"
	"os/signal"
	"strings"
	"sync"
	"syscall"
	"time"

	"golang.org/x/crypto/curve25519"
	"golang.zx2c4.com/wireguard/conn"
	"golang.zx2c4.com/wireguard/device"
	"golang.zx2c4.com/wireguard/tun/netstack"
)

// Config is the contract between the plugin and the sidecar.
type Config struct {
	PrivateKey string   `json:"privateKey"`
	ListenPort int      `json:"listenPort"`
	Addresses  []string `json:"addresses"`
	MTU        int      `json:"mtu"`
	Peers      []Peer   `json:"peers"`
	// Inbound listens inside the tunnel and forwards to a local target, such as Jellyfin.
	Inbound []Inbound `json:"inbound"`
	// Outbound listens on a local address and dials a target across the tunnel.
	Outbound []Outbound `json:"outbound"`
	// DNS answers on port 53 of the first address when set.
	DNS *DNS `json:"dns,omitempty"`
}

// Peer is one WireGuard peer.
type Peer struct {
	PublicKey    string   `json:"publicKey"`
	PresharedKey string   `json:"presharedKey,omitempty"`
	Endpoint     string   `json:"endpoint,omitempty"`
	AllowedIPs   []string `json:"allowedIPs"`
	Keepalive    int      `json:"keepalive,omitempty"`
	// SendMbps caps what this side sends to the peer, and RecvMbps what it takes from the peer.
	// Zero is no limit. They apply to forwarded connections, which is all tunnel traffic.
	SendMbps float64 `json:"sendMbps,omitempty"`
	RecvMbps float64 `json:"recvMbps,omitempty"`
}

// Inbound forwards connections that arrive on a tunnel port to a local target.
type Inbound struct {
	Port   int    `json:"port"`
	Target string `json:"target"`
	// HTTP proxies requests instead of raw bytes, and drops any X-Forwarded-* header a peer sent.
	HTTP bool `json:"http"`
	// ForwardFor writes the peer's tunnel address to X-Forwarded-For. Only with HTTP.
	ForwardFor bool `json:"forwardFor"`
}

// Outbound forwards connections on a local address to a target across the tunnel.
type Outbound struct {
	Listen string `json:"listen"`
	Target string `json:"target"`
}

func main() {
	log.SetFlags(log.LstdFlags | log.LUTC)
	if len(os.Args) < 2 {
		usage()
	}
	switch os.Args[1] {
	case "genkey":
		fmt.Println(newKey())
	case "pubkey":
		line, _ := bufio.NewReader(os.Stdin).ReadString('\n')
		pub, err := publicKey(strings.TrimSpace(line))
		if err != nil {
			log.Fatal(err)
		}
		fmt.Println(pub)
	case "run":
		fs := flag.NewFlagSet("run", flag.ExitOnError)
		path := fs.String("config", "", "path to the JSON config; omit to read lines from stdin")
		_ = fs.Parse(os.Args[2:])
		if err := run(*path); err != nil {
			log.Fatal(err)
		}
	case "bench":
		if err := bench(os.Args[2:]); err != nil {
			log.Fatal(err)
		}
	default:
		usage()
	}
}

func usage() {
	fmt.Fprintln(os.Stderr, "usage: wgsidecar genkey | pubkey | run [-config file] | bench [-seconds n]")
	os.Exit(2)
}

func clamp(k *[32]byte) {
	k[0] &= 248
	k[31] = (k[31] & 127) | 64
}

func publicKey(private string) (string, error) {
	raw, err := base64.StdEncoding.DecodeString(private)
	if err != nil || len(raw) != 32 {
		return "", errors.New("private key must be 32 bytes of base64")
	}
	pub, err := curve25519.X25519(raw, curve25519.Basepoint)
	if err != nil {
		return "", err
	}
	return base64.StdEncoding.EncodeToString(pub), nil
}

// keyHex converts a base64 key to the hex form the WireGuard UAPI takes. The error never holds the
// key itself, since a private or preshared key must not reach a log.
func keyHex(b64 string) (string, error) {
	raw, err := base64.StdEncoding.DecodeString(b64)
	if err != nil || len(raw) != 32 {
		return "", errors.New("a key is not 32 bytes of base64")
	}
	return hex.EncodeToString(raw), nil
}

// peerUAPI writes one peer's settings. replace_allowed_ips makes a repeated write exact, so a
// live update changes a peer's routes without touching its session.
func peerUAPI(b *strings.Builder, p Peer) error {
	pk, err := keyHex(p.PublicKey)
	if err != nil {
		return err
	}
	fmt.Fprintf(b, "public_key=%s\n", pk)
	if p.PresharedKey != "" {
		psk, err := keyHex(p.PresharedKey)
		if err != nil {
			return err
		}
		fmt.Fprintf(b, "preshared_key=%s\n", psk)
	}
	// An endpoint that does not resolve leaves the peer's last endpoint in place rather than failing
	// the whole update, so other peers, and a peer cut off for its limit, still change.
	if p.Endpoint != "" {
		if ep, err := resolveEndpoint(p.Endpoint); err != nil {
			log.Printf("update: %v", err)
		} else {
			fmt.Fprintf(b, "endpoint=%s\n", ep)
		}
	}
	fmt.Fprintf(b, "persistent_keepalive_interval=%d\n", p.Keepalive)
	b.WriteString("replace_allowed_ips=true\n")
	for _, ip := range p.AllowedIPs {
		// Every prefix is checked before anything is applied, since the device applies lines one at a time.
		prefix, err := netip.ParsePrefix(ip)
		if err != nil {
			return fmt.Errorf("allowed IP %q: %w", ip, err)
		}
		fmt.Fprintf(b, "allowed_ip=%s\n", prefix.Masked())
	}
	return nil
}

// peersUAPI writes the change from the current peers to the next: removed peers go, and every
// other peer is written in full. A peer that stays keeps its session.
func peersUAPI(current map[string]bool, next []Peer) (string, error) {
	var b strings.Builder
	keep := map[string]bool{}
	for _, p := range next {
		keep[p.PublicKey] = true
	}
	for key := range current {
		if !keep[key] {
			pk, err := keyHex(key)
			if err != nil {
				return "", err
			}
			fmt.Fprintf(&b, "public_key=%s\nremove=true\n", pk)
		}
	}
	for _, p := range next {
		if err := peerUAPI(&b, p); err != nil {
			return "", err
		}
	}
	return b.String(), nil
}

// resolveEndpoint turns host:port into ip:port, since the UAPI takes only addresses.
func resolveEndpoint(endpoint string) (string, error) {
	addr, err := net.ResolveUDPAddr("udp", endpoint)
	if err != nil {
		return "", fmt.Errorf("endpoint %s: %w", endpoint, err)
	}
	return addr.AddrPort().String(), nil
}

// tunnel is a running device and the peers it was last given.
type tunnel struct {
	dev    *device.Device
	limits *limitTable
	mu     sync.Mutex
	peers  map[string]bool
}

func (t *tunnel) setPeers(peers []Peer) error {
	t.mu.Lock()
	defer t.mu.Unlock()
	set, err := peersUAPI(t.peers, peers)
	if err != nil {
		return err
	}
	// The limits come first, so a connection from a new peer never starts unlimited.
	t.limits.set(peers)
	if err := t.dev.IpcSet(set); err != nil {
		// The device applies lines one at a time, so a failure part way leaves some applied. The peers
		// it holds are read back, so the next update starts from what is really there.
		t.peers = map[string]bool{}
		for _, p := range readStats(t.dev, time.Now()) {
			t.peers[p.PublicKey] = true
		}
		return fmt.Errorf("wireguard: %w", err)
	}
	t.peers = map[string]bool{}
	for _, p := range peers {
		t.peers[p.PublicKey] = true
	}
	return nil
}

func readConfig(data []byte) (*Config, error) {
	var cfg Config
	if err := json.Unmarshal(data, &cfg); err != nil {
		return nil, fmt.Errorf("config: %w", err)
	}
	if cfg.MTU == 0 {
		cfg.MTU = 1420
	}
	return &cfg, nil
}

func run(path string) error {
	stdin := bufio.NewReader(os.Stdin)
	var data []byte
	var err error
	if path != "" {
		data, err = os.ReadFile(path)
	} else {
		// The plugin passes the config on stdin, so the private key never touches disk.
		data, err = stdin.ReadBytes('\n')
	}
	if err != nil && len(data) == 0 {
		return fmt.Errorf("config: %w", err)
	}
	cfg, err := readConfig(data)
	if err != nil {
		return err
	}

	var addrs []netip.Addr
	for _, a := range cfg.Addresses {
		p, err := netip.ParseAddr(strings.Split(a, "/")[0])
		if err != nil {
			return fmt.Errorf("address %s: %w", a, err)
		}
		addrs = append(addrs, p)
	}
	if len(addrs) == 0 {
		return errors.New("config: no address")
	}
	tdev, tnet, err := netstack.CreateNetTUN(addrs, nil, cfg.MTU)
	if err != nil {
		return err
	}
	dev := device.NewDevice(tdev, conn.NewDefaultBind(), device.NewLogger(device.LogLevelError, "wg: "))
	defer dev.Close()

	k, err := keyHex(cfg.PrivateKey)
	if err != nil {
		return err
	}
	base := fmt.Sprintf("private_key=%s\n", k)
	if cfg.ListenPort > 0 {
		base += fmt.Sprintf("listen_port=%d\n", cfg.ListenPort)
	}
	if err := dev.IpcSet(base); err != nil {
		return fmt.Errorf("wireguard: %w", err)
	}
	t := &tunnel{dev: dev, limits: newLimitTable(), peers: map[string]bool{}}
	if err := t.setPeers(cfg.Peers); err != nil {
		return err
	}
	if err := dev.Up(); err != nil {
		return err
	}
	log.Printf("up: %d peers, listening on UDP %d", len(cfg.Peers), cfg.ListenPort)

	for _, in := range cfg.Inbound {
		if _, _, err := net.SplitHostPort(in.Target); err != nil {
			return fmt.Errorf("inbound %d target %q: %w", in.Port, in.Target, err)
		}
		ln, err := tnet.ListenTCP(&net.TCPAddr{Port: in.Port})
		if err != nil {
			return fmt.Errorf("inbound %d: %w", in.Port, err)
		}
		limited := &limitedListener{Listener: ln, limits: t.limits}
		if in.HTTP {
			go serveHTTP(limited, in.Target, in.ForwardFor)
		} else {
			target := in.Target
			go serveTCP(limited, func(ctx context.Context) (net.Conn, error) {
				var d net.Dialer
				return d.DialContext(ctx, "tcp", target)
			})
		}
		log.Printf("inbound: tunnel :%d -> %s (http=%v, forwardFor=%v)", in.Port, in.Target, in.HTTP, in.ForwardFor)
	}
	for _, out := range cfg.Outbound {
		ln, err := net.Listen("tcp", out.Listen)
		if err != nil {
			return fmt.Errorf("outbound %s: %w", out.Listen, err)
		}
		target := out.Target
		go serveTCP(ln, func(ctx context.Context) (net.Conn, error) {
			c, err := tnet.DialContext(ctx, "tcp", target)
			if err != nil {
				return nil, err
			}
			// The tunnel side of an outbound connection is limited by the peer that routes it.
			return t.limits.wrap(c, c.RemoteAddr()), nil
		})
		log.Printf("outbound: %s -> tunnel %s", out.Listen, out.Target)
	}
	if cfg.DNS != nil {
		if err := serveDNS(tnet, addrs[0], cfg.DNS); err != nil {
			return fmt.Errorf("dns: %w", err)
		}
		log.Printf("dns: answering %s on %s:53", strings.Join(cfg.DNS.Names, ", "), addrs[0])
	}

	emit(Event{Event: "ready"})
	watchStop := make(chan struct{})
	defer close(watchStop)
	go watchPeers(dev, 10*time.Second, watchStop)

	stop := make(chan os.Signal, 1)
	signal.Notify(stop, os.Interrupt, syscall.SIGTERM)
	if path != "" {
		// A config file runs until a signal, whatever stdin holds.
		<-stop
		log.Print("stopping")
		return nil
	}
	// Each later line replaces the peers. The plugin closes stdin to stop the sidecar, so it never
	// outlives Jellyfin.
	go func() {
		for {
			line, err := stdin.ReadBytes('\n')
			if len(strings.TrimSpace(string(line))) > 0 {
				if next, perr := readConfig(line); perr != nil {
					log.Printf("update: %v", perr)
				} else if serr := t.setPeers(next.Peers); serr != nil {
					log.Printf("update: %v", serr)
				} else {
					log.Printf("update: %d peers", len(next.Peers))
				}
			}
			if err != nil {
				stop <- syscall.SIGTERM
				return
			}
		}
	}()
	<-stop
	log.Print("stopping")
	return nil
}

// serveHTTP proxies requests to target. Rewrite drops any X-Forwarded-* a peer sent, then
// SetXForwarded writes the peer's tunnel address, so a peer cannot claim another address.
func serveHTTP(ln net.Listener, target string, forwardFor bool) {
	u := &url.URL{Scheme: "http", Host: target}
	proxy := &httputil.ReverseProxy{
		Rewrite: func(r *httputil.ProxyRequest) {
			r.SetURL(u)
			r.Out.Host = r.In.Host
			if forwardFor {
				r.SetXForwarded()
			}
		},
		// Streams flush immediately, so playback and WebSockets are not buffered.
		FlushInterval: -1,
	}
	// A plain handler rather than a ServeMux, which would answer paths holding // or .. with a redirect
	// instead of passing them to Jellyfin.
	handler := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == SpeedTestPath {
			speedTest(w, r)
			return
		}
		proxy.ServeHTTP(w, r)
	})
	// IdleTimeout frees keep-alive connections a vanished peer left open. It applies only between
	// requests, and never to a stream or a WebSocket, so playback is not cut.
	srv := &http.Server{
		Handler:           handler,
		ReadHeaderTimeout: 30 * time.Second,
		IdleTimeout:       2 * time.Minute,
		ErrorLog:          log.New(io.Discard, "", 0),
	}
	if err := srv.Serve(ln); err != nil && !errors.Is(err, net.ErrClosed) {
		log.Printf("http: %v", err)
	}
}

func serveTCP(ln net.Listener, dial func(context.Context) (net.Conn, error)) {
	var backoff time.Duration
	for {
		c, err := ln.Accept()
		if err != nil {
			if errors.Is(err, net.ErrClosed) {
				return
			}
			// A failure such as running out of file handles passes, so the listener waits and tries
			// again rather than stopping for good.
			backoff = min(max(backoff*2, 5*time.Millisecond), time.Second)
			log.Printf("accept: %v", err)
			time.Sleep(backoff)
			continue
		}
		backoff = 0
		go func() {
			defer c.Close()
			ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
			r, err := dial(ctx)
			cancel()
			if err != nil {
				log.Printf("dial: %v", err)
				return
			}
			defer r.Close()
			pipe(c, r)
		}()
	}
}

func pipe(a, b net.Conn) {
	var wg sync.WaitGroup
	wg.Add(2)
	cp := func(dst, src net.Conn) {
		defer wg.Done()
		if _, err := io.Copy(dst, src); err != nil {
			// A broken side ends both, so the other copy does not wait forever on a peer that is gone.
			_ = a.Close()
			_ = b.Close()
			return
		}
		if cw, ok := dst.(interface{ CloseWrite() error }); ok {
			_ = cw.CloseWrite()
		}
	}
	go cp(a, b)
	go cp(b, a)
	wg.Wait()
}
