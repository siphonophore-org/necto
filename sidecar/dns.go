package main

import (
	"errors"
	"log"
	"net"
	"net/netip"
	"strings"
	"time"

	"golang.org/x/net/dns/dnsmessage"
	"golang.zx2c4.com/wireguard/tun/netstack"
)

// DNS answers the server's own names inside the tunnel and passes every other query to an
// upstream resolver. Only queries cross the tunnel, so a peer's other traffic stays off it.
type DNS struct {
	// Names resolve to the tunnel address, such as jellyfin.internal.
	Names []string `json:"names"`
	// Upstreams take every other query, as ip:port.
	Upstreams []string `json:"upstreams"`
}

func serveDNS(tnet *netstack.Net, addr netip.Addr, cfg *DNS) error {
	pc, err := tnet.ListenUDPAddrPort(netip.AddrPortFrom(addr, 53))
	if err != nil {
		return err
	}
	names := map[string]bool{}
	for _, n := range cfg.Names {
		names[normalizeName(n)] = true
	}
	// At most this many queries are answered at once. More are dropped, as a busy resolver does, so a
	// flood cannot open sockets until the process runs out of file handles.
	busy := make(chan struct{}, 256)
	go func() {
		buf := make([]byte, 1500)
		for {
			n, from, err := pc.ReadFrom(buf)
			if err != nil {
				if !errors.Is(err, net.ErrClosed) {
					log.Printf("dns: %v", err)
				}
				return
			}
			select {
			case busy <- struct{}{}:
			default:
				continue
			}
			query := append([]byte(nil), buf[:n]...)
			go func() {
				defer func() { <-busy }()
				if reply := answerDNS(query, addr, names, cfg.Upstreams); reply != nil {
					_, _ = pc.WriteTo(reply, from)
				}
			}()
		}
	}()
	return nil
}

func normalizeName(name string) string {
	return strings.TrimSuffix(strings.ToLower(strings.TrimSpace(name)), ".")
}

// answerDNS answers a query for one of names with addr, and forwards any other query.
func answerDNS(query []byte, addr netip.Addr, names map[string]bool, upstreams []string) []byte {
	var p dnsmessage.Parser
	header, err := p.Start(query)
	if err != nil {
		return nil
	}
	q, err := p.Question()
	if err != nil || !names[normalizeName(q.Name.String())] {
		return forwardDNS(query, upstreams)
	}

	b := dnsmessage.NewBuilder(nil, dnsmessage.Header{
		ID: header.ID, Response: true, Authoritative: true, RecursionDesired: header.RecursionDesired, RecursionAvailable: true,
	})
	b.EnableCompression()
	_ = b.StartQuestions()
	_ = b.Question(q)
	_ = b.StartAnswers()
	// A name the server owns answers with the tunnel address, as A or AAAA to match its family. The
	// other type answers empty, so a client falls back to the one that exists rather than to another
	// resolver.
	head := dnsmessage.ResourceHeader{Name: q.Name, Class: dnsmessage.ClassINET, TTL: 300}
	if q.Type == dnsmessage.TypeA && addr.Is4() {
		_ = b.AResource(head, dnsmessage.AResource{A: addr.As4()})
	}
	if q.Type == dnsmessage.TypeAAAA && addr.Is6() && !addr.Is4In6() {
		_ = b.AAAAResource(head, dnsmessage.AAAAResource{AAAA: addr.As16()})
	}
	reply, err := b.Finish()
	if err != nil {
		return nil
	}
	return reply
}

func forwardDNS(query []byte, upstreams []string) []byte {
	for _, up := range upstreams {
		c, err := net.DialTimeout("udp", up, 3*time.Second)
		if err != nil {
			continue
		}
		_ = c.SetDeadline(time.Now().Add(3 * time.Second))
		if _, err := c.Write(query); err != nil {
			c.Close()
			continue
		}
		// The largest UDP reply, so an EDNS answer is never cut short.
		buf := make([]byte, 65535)
		n, err := c.Read(buf)
		c.Close()
		if err == nil {
			return buf[:n]
		}
	}
	return nil
}
