package main

import (
	"context"
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/netip"
	"os"
	"strconv"
	"time"

	"golang.zx2c4.com/wireguard/conn"
	"golang.zx2c4.com/wireguard/device"
	"golang.zx2c4.com/wireguard/tun/netstack"
)

// SpeedTestPath is served by the sidecar itself on every HTTP inbound, so it exists only
// inside the tunnel and measures the tunnel, not Jellyfin.
const SpeedTestPath = "/wireguard/speedtest"

const maxSpeedTestBytes = 1 << 30

var zeros = make([]byte, 64*1024)

// speedTest answers GET with ?bytes=N bytes, and POST by reading the body and reporting how much
// arrived and how long it took. WireGuard encrypts every packet, so zeros measure the same as random data.
func speedTest(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Cache-Control", "no-store")
	switch r.Method {
	case http.MethodGet:
		n, err := strconv.ParseInt(r.URL.Query().Get("bytes"), 10, 64)
		if err != nil || n <= 0 || n > maxSpeedTestBytes {
			http.Error(w, "bytes must be 1 to 1073741824", http.StatusBadRequest)
			return
		}
		w.Header().Set("Content-Type", "application/octet-stream")
		w.Header().Set("Content-Length", strconv.FormatInt(n, 10))
		for n > 0 {
			chunk := int64(len(zeros))
			if n < chunk {
				chunk = n
			}
			if _, err := w.Write(zeros[:chunk]); err != nil {
				return
			}
			n -= chunk
		}
	case http.MethodPost:
		start := time.Now()
		n, _ := io.Copy(io.Discard, io.LimitReader(r.Body, maxSpeedTestBytes))
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(map[string]int64{"bytes": n, "ms": time.Since(start).Milliseconds()})
	default:
		w.WriteHeader(http.StatusMethodNotAllowed)
	}
}

// bench runs two tunnels in this process over loopback UDP and pushes data through for a few
// seconds. It measures this machine's ceiling: encryption and the userspace stack, no network.
func bench(args []string) error {
	fs := flag.NewFlagSet("bench", flag.ExitOnError)
	seconds := fs.Int("seconds", 5, "how long to send in each direction")
	_ = fs.Parse(args)

	keyA, keyB := newKey(), newKey()
	pubA, _ := publicKey(keyA)
	pubB, _ := publicKey(keyB)
	port, err := freeUDPPort()
	if err != nil {
		return err
	}

	a, tnetA, err := benchDevice(keyA, "10.99.0.1", fmt.Sprintf("listen_port=%d\n", port), pubB, "10.99.0.2/32", "")
	if err != nil {
		return err
	}
	defer a.Close()
	b, tnetB, err := benchDevice(keyB, "10.99.0.2", "", pubA, "10.99.0.1/32", fmt.Sprintf("127.0.0.1:%d", port))
	if err != nil {
		return err
	}
	defer b.Close()

	ln, err := tnetA.ListenTCP(&net.TCPAddr{Port: 9})
	if err != nil {
		return err
	}
	defer ln.Close()
	go func() {
		for {
			c, err := ln.Accept()
			if err != nil {
				return
			}
			go func() {
				defer c.Close()
				// The first byte picks the direction: d sends to the dialer, anything else is discarded.
				one := make([]byte, 1)
				if _, err := io.ReadFull(c, one); err != nil {
					return
				}
				if one[0] == 'd' {
					for {
						if _, err := c.Write(zeros); err != nil {
							return
						}
					}
				}
				_, _ = io.Copy(io.Discard, c)
			}()
		}
	}()

	dur := time.Duration(*seconds) * time.Second
	up, err := benchRun(tnetB, 'u', dur)
	if err != nil {
		return err
	}
	down, err := benchRun(tnetB, 'd', dur)
	if err != nil {
		return err
	}
	_ = json.NewEncoder(os.Stdout).Encode(map[string]float64{"uploadMbps": up, "downloadMbps": down})
	return nil
}

func benchRun(tnet *netstack.Net, dir byte, dur time.Duration) (float64, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	c, err := tnet.DialContext(ctx, "tcp", "10.99.0.1:9")
	cancel()
	if err != nil {
		return 0, fmt.Errorf("bench dial: %w", err)
	}
	defer c.Close()
	if _, err := c.Write([]byte{dir}); err != nil {
		return 0, err
	}
	var total int64
	start := time.Now()
	_ = c.SetDeadline(start.Add(dur))
	buf := make([]byte, len(zeros))
	for time.Since(start) < dur {
		var n int
		if dir == 'd' {
			n, err = c.Read(buf)
		} else {
			n, err = c.Write(zeros)
		}
		total += int64(n)
		if err != nil {
			break
		}
	}
	return float64(total*8) / time.Since(start).Seconds() / 1e6, nil
}

func benchDevice(key, addr, extra, peerPub, allowed, endpoint string) (*device.Device, *netstack.Net, error) {
	tdev, tnet, err := netstack.CreateNetTUN([]netip.Addr{netip.MustParseAddr(addr)}, nil, 1420)
	if err != nil {
		return nil, nil, err
	}
	dev := device.NewDevice(tdev, conn.NewDefaultBind(), device.NewLogger(device.LogLevelError, "bench: "))
	k, _ := keyHex(key)
	p, _ := keyHex(peerPub)
	set := fmt.Sprintf("private_key=%s\n%spublic_key=%s\nallowed_ip=%s\n", k, extra, p, allowed)
	if endpoint != "" {
		set += "endpoint=" + endpoint + "\n"
	}
	if err := dev.IpcSet(set); err != nil {
		return nil, nil, err
	}
	return dev, tnet, dev.Up()
}

func newKey() string {
	var k [32]byte
	_, _ = rand.Read(k[:])
	clamp(&k)
	return base64.StdEncoding.EncodeToString(k[:])
}

func freeUDPPort() (int, error) {
	c, err := net.ListenUDP("udp", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if err != nil {
		return 0, err
	}
	defer c.Close()
	return c.LocalAddr().(*net.UDPAddr).Port, nil
}
