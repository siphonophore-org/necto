package main

import (
	"bufio"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"os"
	"strconv"
	"strings"
	"sync"
	"time"

	"golang.zx2c4.com/wireguard/device"
)

// A peer is connected while its last handshake is younger than this. WireGuard renews the
// handshake every two minutes while traffic flows, so three minutes of silence means it left.
const handshakeTimeout = 180 * time.Second

// Event is one line the sidecar writes to stdout for the plugin.
type Event struct {
	Event     string      `json:"event"`
	PublicKey string      `json:"publicKey,omitempty"`
	Endpoint  string      `json:"endpoint,omitempty"`
	Peers     []PeerStats `json:"peers,omitempty"`
}

// PeerStats is one peer's traffic and last handshake, sent with every stats event.
type PeerStats struct {
	PublicKey     string `json:"publicKey"`
	Endpoint      string `json:"endpoint,omitempty"`
	LastHandshake int64  `json:"lastHandshake"`
	RxBytes       int64  `json:"rxBytes"`
	TxBytes       int64  `json:"txBytes"`
	Connected     bool   `json:"connected"`
}

var stdoutMu sync.Mutex

func emit(e Event) {
	stdoutMu.Lock()
	defer stdoutMu.Unlock()
	_ = json.NewEncoder(os.Stdout).Encode(e)
}

// watchPeers polls the device and emits peer-up and peer-down when a peer's handshake starts or
// goes stale, plus a stats event on every poll.
func watchPeers(dev *device.Device, every time.Duration, stop <-chan struct{}) {
	up := map[string]bool{}
	tick := time.NewTicker(every)
	defer tick.Stop()
	for {
		stats := readStats(dev, time.Now())
		for _, p := range stats {
			if p.Connected && !up[p.PublicKey] {
				emit(Event{Event: "peer-up", PublicKey: p.PublicKey, Endpoint: p.Endpoint})
			} else if !p.Connected && up[p.PublicKey] {
				emit(Event{Event: "peer-down", PublicKey: p.PublicKey, Endpoint: p.Endpoint})
			}
			up[p.PublicKey] = p.Connected
		}
		// A peer removed while connected, such as one over its data limit, is gone from the stats.
		present := map[string]bool{}
		for _, p := range stats {
			present[p.PublicKey] = true
		}
		for key, connected := range up {
			if !present[key] {
				if connected {
					emit(Event{Event: "peer-down", PublicKey: key})
				}
				delete(up, key)
			}
		}
		emit(Event{Event: "stats", Peers: stats})
		select {
		case <-stop:
			return
		case <-tick.C:
		}
	}
}

func readStats(dev *device.Device, now time.Time) []PeerStats {
	raw, err := dev.IpcGet()
	if err != nil {
		return nil
	}
	return parseStats(raw, now)
}

// parseStats reads the WireGuard UAPI get output, where each peer starts at a public_key line.
func parseStats(raw string, now time.Time) []PeerStats {
	var out []PeerStats
	var cur *PeerStats
	sc := bufio.NewScanner(strings.NewReader(raw))
	for sc.Scan() {
		k, v, ok := strings.Cut(sc.Text(), "=")
		if !ok {
			continue
		}
		if k == "public_key" {
			b, err := hex.DecodeString(v)
			if err != nil {
				cur = nil
				continue
			}
			out = append(out, PeerStats{PublicKey: base64.StdEncoding.EncodeToString(b)})
			cur = &out[len(out)-1]
			continue
		}
		if cur == nil {
			continue
		}
		n, _ := strconv.ParseInt(v, 10, 64)
		switch k {
		case "endpoint":
			cur.Endpoint = v
		case "last_handshake_time_sec":
			cur.LastHandshake = n
		case "rx_bytes":
			cur.RxBytes = n
		case "tx_bytes":
			cur.TxBytes = n
		}
	}
	for i := range out {
		hs := out[i].LastHandshake
		out[i].Connected = hs > 0 && now.Sub(time.Unix(hs, 0)) < handshakeTimeout
	}
	return out
}
