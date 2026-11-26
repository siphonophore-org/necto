package main

import (
	"context"
	"net"
	"net/netip"
	"sync"

	"golang.org/x/time/rate"
)

// limitChunk is the most one limiter wait covers, and the burst every limiter allows.
const limitChunk = 64 * 1024

// peerLimits holds one peer's two limiters. They outlive config updates, so a changed limit
// applies to the peer's open connections at once.
type peerLimits struct {
	send *rate.Limiter
	recv *rate.Limiter
}

func newPeerLimits() *peerLimits {
	return &peerLimits{send: rate.NewLimiter(rate.Inf, limitChunk), recv: rate.NewLimiter(rate.Inf, limitChunk)}
}

func setRate(l *rate.Limiter, mbps float64) {
	if mbps <= 0 {
		l.SetLimit(rate.Inf)
		return
	}
	l.SetLimit(rate.Limit(mbps * 125000))
}

type route struct {
	prefix netip.Prefix
	key    string
}

// limitTable finds the peer that owns an address, by the longest of the peers' allowed IPs.
type limitTable struct {
	mu     sync.RWMutex
	routes []route
	byKey  map[string]*peerLimits
}

func newLimitTable() *limitTable {
	return &limitTable{byKey: map[string]*peerLimits{}}
}

func (t *limitTable) set(peers []Peer) {
	t.mu.Lock()
	defer t.mu.Unlock()
	var routes []route
	next := map[string]*peerLimits{}
	for _, p := range peers {
		l := t.byKey[p.PublicKey]
		if l == nil {
			l = newPeerLimits()
		}
		setRate(l.send, p.SendMbps)
		setRate(l.recv, p.RecvMbps)
		next[p.PublicKey] = l
		for _, ip := range p.AllowedIPs {
			if prefix, err := netip.ParsePrefix(ip); err == nil {
				routes = append(routes, route{prefix: prefix.Masked(), key: p.PublicKey})
			}
		}
	}
	t.routes = routes
	t.byKey = next
}

func (t *limitTable) lookup(addr netip.Addr) *peerLimits {
	t.mu.RLock()
	defer t.mu.RUnlock()
	best := -1
	var key string
	for _, r := range t.routes {
		if r.prefix.Contains(addr) && r.prefix.Bits() > best {
			best = r.prefix.Bits()
			key = r.key
		}
	}
	return t.byKey[key]
}

// wrap limits a connection on the tunnel side by the peer that owns its remote address.
func (t *limitTable) wrap(c net.Conn, remote net.Addr) net.Conn {
	ap, err := netip.ParseAddrPort(remote.String())
	if err != nil {
		return c
	}
	l := t.lookup(ap.Addr().Unmap())
	if l == nil {
		return c
	}
	return &limitedConn{Conn: c, limits: l}
}

// limitedConn waits on the peer's limiters: reads take from what the peer sends, writes from
// what is sent to the peer. An unlimited limiter returns at once.
type limitedConn struct {
	net.Conn
	limits *peerLimits
}

func (c *limitedConn) Read(b []byte) (int, error) {
	if len(b) > limitChunk {
		b = b[:limitChunk]
	}
	n, err := c.Conn.Read(b)
	if n > 0 {
		_ = c.limits.recv.WaitN(context.Background(), n)
	}
	return n, err
}

func (c *limitedConn) Write(b []byte) (int, error) {
	written := 0
	for len(b) > 0 {
		chunk := b
		if len(chunk) > limitChunk {
			chunk = chunk[:limitChunk]
		}
		_ = c.limits.send.WaitN(context.Background(), len(chunk))
		n, err := c.Conn.Write(chunk)
		written += n
		if err != nil {
			return written, err
		}
		b = b[n:]
	}
	return written, nil
}

// CloseWrite passes through, so a forwarded connection still half closes.
func (c *limitedConn) CloseWrite() error {
	if cw, ok := c.Conn.(interface{ CloseWrite() error }); ok {
		return cw.CloseWrite()
	}
	return nil
}

// limitedListener limits every connection it accepts by the peer it came from.
type limitedListener struct {
	net.Listener
	limits *limitTable
}

func (l *limitedListener) Accept() (net.Conn, error) {
	c, err := l.Listener.Accept()
	if err != nil {
		return nil, err
	}
	return l.limits.wrap(c, c.RemoteAddr()), nil
}
