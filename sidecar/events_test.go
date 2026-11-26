package main

import (
	"testing"
	"time"
)

func TestParseStats(t *testing.T) {
	now := time.Unix(1_000_000, 0)
	raw := "private_key=00\nlisten_port=51820\n" +
		"public_key=0101010101010101010101010101010101010101010101010101010101010101\n" +
		"endpoint=203.0.113.10:41000\nlast_handshake_time_sec=999950\nrx_bytes=100\ntx_bytes=200\n" +
		"public_key=0202020202020202020202020202020202020202020202020202020202020202\n" +
		"last_handshake_time_sec=999000\n" +
		"public_key=0303030303030303030303030303030303030303030303030303030303030303\n" +
		"last_handshake_time_sec=0\n"

	got := parseStats(raw, now)
	if len(got) != 3 {
		t.Fatalf("want 3 peers, got %d", len(got))
	}
	if !got[0].Connected || got[0].Endpoint != "203.0.113.10:41000" || got[0].RxBytes != 100 || got[0].TxBytes != 200 {
		t.Errorf("peer 1: %+v", got[0])
	}
	if got[0].PublicKey != "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=" {
		t.Errorf("peer 1 key: %s", got[0].PublicKey)
	}
	if got[1].Connected {
		t.Error("a handshake 1000 seconds old is not connected")
	}
	if got[2].Connected {
		t.Error("a peer that never shook hands is not connected")
	}
}
