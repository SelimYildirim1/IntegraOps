package sender

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"sync/atomic"
	"testing"
	"time"
)

const id = "0192f3c4-8b1a-7c2e-9f10-3a4b5c6d7e8f"

func body(eventID string) []byte {
	b, _ := json.Marshal(map[string]any{"eventId": eventID, "flow": "f", "transactionId": "T-1", "step": "s", "status": "completed", "occurredAt": "2026-09-22T10:00:00Z"})
	return b
}

func server(t *testing.T, status int, receipt any) *httptest.Server {
	t.Helper()
	return httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("X-API-Key") != "k" || r.URL.Path != "/api/v1/events" {
			t.Errorf("beklenmeyen istek: %s %s", r.URL.Path, r.Header.Get("X-API-Key"))
		}
		w.WriteHeader(status)
		if receipt != nil {
			_ = json.NewEncoder(w).Encode(receipt)
		}
	}))
}

func TestAcceptedAndDuplicate(t *testing.T) {
	for _, tc := range []struct {
		status int
		dup    bool
	}{{202, false}, {200, true}} {
		s := server(t, tc.status, map[string]any{"eventId": id, "duplicate": tc.dup})
		res := New(s.URL, "k", time.Second, 0, "test").Send(context.Background(), id, body(id))
		s.Close()
		if res.State != "sent" || res.Duplicate != tc.dup {
			t.Fatalf("status %d: %+v", tc.status, res)
		}
	}
}

func TestReceiptMismatchIsRetried(t *testing.T) {
	s := server(t, 202, map[string]any{"eventId": "11111111-1111-4111-8111-111111111111"})
	defer s.Close()
	res := New(s.URL, "k", time.Second, 0, "test").Send(context.Background(), id, body(id))
	if res.State != "retry" || res.Error != "receipt_mismatch" {
		t.Fatalf("%+v", res)
	}
}

func TestClassification(t *testing.T) {
	cases := map[int]string{400: "dead", 401: "dead", 403: "dead", 404: "dead", 409: "dead", 408: "retry", 429: "retry", 500: "retry", 503: "retry"}
	for status, want := range cases {
		s := server(t, status, nil)
		res := New(s.URL, "k", time.Second, 0, "test").Send(context.Background(), id, body(id))
		s.Close()
		if res.State != want {
			t.Errorf("status %d: got %s want %s", status, res.State, want)
		}
	}
}

func TestBodyMismatchIsDeadWithoutRequest(t *testing.T) {
	var hits atomic.Int32
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { hits.Add(1) }))
	defer s.Close()
	res := New(s.URL, "k", time.Second, 0, "test").Send(context.Background(), id, body("22222222-2222-4222-8222-222222222222"))
	if res.State != "dead" || hits.Load() != 0 {
		t.Fatalf("%+v hits=%d", res, hits.Load())
	}
	if res := New(s.URL, "k", time.Second, 0, "test").Send(context.Background(), "not-a-uuid", body(id)); res.State != "dead" {
		t.Fatalf("%+v", res)
	}
}

func TestUnreachable(t *testing.T) {
	res := New("http://127.0.0.1:1", "k", time.Second, 0, "test").Send(context.Background(), id, body(id))
	if res.State != "retry" || res.Error != "unreachable" || res.Reachable {
		t.Fatalf("%+v", res)
	}
}

func TestLimiterPacesRequests(t *testing.T) {
	l := NewLimiter(10) // burst 10, then 10/s
	start := time.Now()
	for i := 0; i < 20; i++ {
		if err := l.Wait(context.Background()); err != nil {
			t.Fatal(err)
		}
	}
	if elapsed := time.Since(start); elapsed < 900*time.Millisecond || elapsed > 3*time.Second {
		t.Fatalf("20 istek %s sürdü; yaklaşık 1 s beklenirdi", elapsed)
	}
	if err := NewLimiter(0).Wait(context.Background()); err != nil {
		t.Fatal(err)
	}
}
