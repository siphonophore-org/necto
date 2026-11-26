package main

import (
	"net/netip"
	"strings"
	"testing"

	"golang.org/x/net/dns/dnsmessage"
	"golang.org/x/time/rate"
)

func query(t *testing.T, name string, typ dnsmessage.Type) []byte {
	t.Helper()
	b := dnsmessage.NewBuilder(nil, dnsmessage.Header{ID: 7, RecursionDesired: true})
	_ = b.StartQuestions()
	_ = b.Question(dnsmessage.Question{Name: dnsmessage.MustNewName(name), Type: typ, Class: dnsmessage.ClassINET})
	msg, err := b.Finish()
	if err != nil {
		t.Fatal(err)
	}
	return msg
}

func TestAnswerDNS_OwnNameGetsTheTunnelAddress(t *testing.T) {
	addr := netip.MustParseAddr("10.66.0.1")
	reply := answerDNS(query(t, "Jellyfin.Internal.", dnsmessage.TypeA), addr, map[string]bool{"jellyfin.internal": true}, nil)

	var m dnsmessage.Message
	if err := m.Unpack(reply); err != nil {
		t.Fatal(err)
	}
	if m.Header.ID != 7 || !m.Header.Response || len(m.Answers) != 1 {
		t.Fatalf("reply: %+v", m)
	}
	if got := m.Answers[0].Body.(*dnsmessage.AResource).A; netip.AddrFrom4(got) != addr {
		t.Errorf("answer %v", got)
	}
}

func TestAnswerDNS_OwnNameAnswersOtherTypesEmpty(t *testing.T) {
	reply := answerDNS(query(t, "jellyfin.internal.", dnsmessage.TypeAAAA), netip.MustParseAddr("10.66.0.1"), map[string]bool{"jellyfin.internal": true}, nil)

	var m dnsmessage.Message
	if err := m.Unpack(reply); err != nil {
		t.Fatal(err)
	}
	if m.Header.RCode != dnsmessage.RCodeSuccess || len(m.Answers) != 0 {
		t.Errorf("reply: %+v", m)
	}
}

func TestAnswerDNS_OtherNamesGoUpstream(t *testing.T) {
	// No upstream answers, so a forwarded query has no reply.
	if reply := answerDNS(query(t, "example.com.", dnsmessage.TypeA), netip.MustParseAddr("10.66.0.1"), map[string]bool{"jellyfin.internal": true}, nil); reply != nil {
		t.Error("a name the server does not own was answered locally")
	}
}

func TestLimitTable_LongestPrefixOwnsTheAddress(t *testing.T) {
	table := newLimitTable()
	table.set([]Peer{
		{PublicKey: "router", AllowedIPs: []string{"10.66.0.2/32", "192.168.50.0/24"}, SendMbps: 10},
		{PublicKey: "phone", AllowedIPs: []string{"10.66.0.3/32"}},
		{PublicKey: "all", AllowedIPs: []string{"0.0.0.0/0"}, RecvMbps: 5},
	})

	router := table.lookup(netip.MustParseAddr("192.168.50.23"))
	if router == nil || router.send.Limit() != rate.Limit(10*125000) {
		t.Fatalf("router limits: %+v", router)
	}
	if phone := table.lookup(netip.MustParseAddr("10.66.0.3")); phone == nil || phone.send.Limit() != rate.Inf {
		t.Error("an unlimited peer has a limit")
	}
	if other := table.lookup(netip.MustParseAddr("203.0.113.10")); other == nil || other.recv.Limit() != rate.Limit(5*125000) {
		t.Error("the default route did not match")
	}
}

func TestLimitTable_KeepsLimitersAcrossUpdates(t *testing.T) {
	table := newLimitTable()
	table.set([]Peer{{PublicKey: "a", AllowedIPs: []string{"10.66.0.2/32"}}})
	before := table.lookup(netip.MustParseAddr("10.66.0.2"))
	table.set([]Peer{{PublicKey: "a", AllowedIPs: []string{"10.66.0.2/32"}, SendMbps: 1}})
	after := table.lookup(netip.MustParseAddr("10.66.0.2"))

	if before != after || after.send.Limit() != rate.Limit(125000) {
		t.Error("an open connection would not see the new limit")
	}
}

func TestPeersUAPI_RemovesGonePeersAndKeepsTheRest(t *testing.T) {
	a, b := newKey(), newKey()
	ha, _ := keyHex(a)
	hb, _ := keyHex(b)

	set, err := peersUAPI(map[string]bool{a: true, b: true}, []Peer{{PublicKey: b, AllowedIPs: []string{"10.66.0.3/32"}}})
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(set, "public_key="+ha+"\nremove=true\n") {
		t.Error("the removed peer is not removed")
	}
	if strings.Contains(set, "public_key="+hb+"\nremove=true") {
		t.Error("the kept peer is removed")
	}
	if !strings.Contains(set, "public_key="+hb+"\n") || !strings.Contains(set, "replace_allowed_ips=true\nallowed_ip=10.66.0.3/32\n") {
		t.Errorf("the kept peer is not written in full:\n%s", set)
	}
}

func TestKeyHex_ErrorNeverHoldsTheKey(t *testing.T) {
	secret := "c2VjcmV0LWtleS10aGF0LWlzLW5vdC0zMi1ieXRlcw=="
	_, err := keyHex(secret)
	if err == nil || strings.Contains(err.Error(), secret) {
		t.Fatalf("error leaks the key: %v", err)
	}
}

func TestPeersUAPI_RefusesABadPrefixBeforeAnythingApplies(t *testing.T) {
	if _, err := peersUAPI(map[string]bool{}, []Peer{{PublicKey: newKey(), AllowedIPs: []string{"10.0.0.0/33"}}}); err == nil {
		t.Error("a prefix longer than 32 bits was accepted")
	}
}

func TestPeersUAPI_KeepsTheUpdateWhenAnEndpointDoesNotResolve(t *testing.T) {
	set, err := peersUAPI(map[string]bool{}, []Peer{{PublicKey: newKey(), Endpoint: "nowhere.invalid:51820", AllowedIPs: []string{"0.0.0.0/0"}}})
	if err != nil {
		t.Fatalf("one unresolved endpoint failed the update: %v", err)
	}
	if strings.Contains(set, "endpoint=") {
		t.Error("an unresolved endpoint was written")
	}
}

func TestAnswerDNS_IPv6HostAnswersAAAA(t *testing.T) {
	addr := netip.MustParseAddr("fd66::1")
	reply := answerDNS(query(t, "jellyfin.internal.", dnsmessage.TypeAAAA), addr, map[string]bool{"jellyfin.internal": true}, nil)

	var m dnsmessage.Message
	if err := m.Unpack(reply); err != nil {
		t.Fatal(err)
	}
	if len(m.Answers) != 1 || netip.AddrFrom16(m.Answers[0].Body.(*dnsmessage.AAAAResource).AAAA) != addr {
		t.Errorf("reply: %+v", m)
	}
}
