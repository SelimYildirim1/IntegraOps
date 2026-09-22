// Package sender delivers event bodies to the IntegraOps API and classifies every response.
package sender

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"regexp"
	"strings"
	"sync"
	"time"
)

var uuidPattern = regexp.MustCompile(`^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$`)

// Result of one delivery attempt.
type Result struct {
	State     string // "sent", "retry" or "dead"
	Error     string // short machine-readable reason
	Duplicate bool   // the API had already accepted this event id
	Reachable bool   // false when the API could not be contacted at all
}

// Sender is safe for concurrent use.
type Sender struct {
	client  *http.Client
	url     string
	key     string
	limiter *Limiter
	version string
}

// New builds a sender for baseURL with the ingest key. ratePerSec 0 disables rate limiting.
func New(baseURL, key string, timeout time.Duration, ratePerSec float64, version string) *Sender {
	transport := &http.Transport{
		Proxy: http.ProxyFromEnvironment, MaxConnsPerHost: 16, IdleConnTimeout: 90 * time.Second,
		DialContext: (&net.Dialer{Timeout: 10 * time.Second}).DialContext, TLSHandshakeTimeout: 10 * time.Second,
	}
	return &Sender{
		client:  &http.Client{Timeout: timeout, Transport: transport, CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }},
		url:     strings.TrimRight(baseURL, "/") + "/api/v1/events",
		key:     key,
		limiter: NewLimiter(ratePerSec),
		version: version,
	}
}

// Send posts body and returns how the record should be completed. expectedID must match the
// receipt so a proxy or misrouted request can never mark the wrong record as delivered.
func (s *Sender) Send(ctx context.Context, expectedID string, body []byte) Result {
	if !uuidPattern.MatchString(expectedID) {
		return Result{State: "dead", Error: "invalid_event_id", Reachable: true}
	}
	var probe struct {
		EventID string `json:"eventId"`
	}
	if err := json.Unmarshal(body, &probe); err != nil || !strings.EqualFold(probe.EventID, expectedID) {
		return Result{State: "dead", Error: "body_event_id_mismatch", Reachable: true}
	}
	if err := s.limiter.Wait(ctx); err != nil {
		return Result{State: "retry", Error: "cancelled", Reachable: true}
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, s.url, bytes.NewReader(body))
	if err != nil {
		return Result{State: "dead", Error: "request_build_failed", Reachable: true}
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("X-API-Key", s.key)
	req.Header.Set("User-Agent", "integraops-agent/"+s.version)
	resp, err := s.client.Do(req)
	if err != nil {
		if errors.Is(err, context.Canceled) {
			return Result{State: "retry", Error: "cancelled"}
		}
		return Result{State: "retry", Error: "unreachable"}
	}
	defer resp.Body.Close()
	data, _ := io.ReadAll(io.LimitReader(resp.Body, 64*1024))
	return classify(resp.StatusCode, data, expectedID)
}

func classify(code int, data []byte, expectedID string) Result {
	switch {
	case code == 200 || code == 202:
		var receipt struct {
			EventID   string `json:"eventId"`
			Duplicate bool   `json:"duplicate"`
		}
		if err := json.Unmarshal(data, &receipt); err != nil || !strings.EqualFold(receipt.EventID, expectedID) {
			// A success we cannot verify is retried: the API is idempotent on eventId.
			return Result{State: "retry", Error: "receipt_mismatch", Reachable: true}
		}
		return Result{State: "sent", Duplicate: receipt.Duplicate || code == 200, Reachable: true}
	case code == 408 || code == 425 || code == 429 || code >= 500:
		return Result{State: "retry", Error: fmt.Sprintf("http_%d", code), Reachable: true}
	case code >= 400:
		// Validation, authentication, unknown flow or a conflicting event id: retrying cannot help.
		return Result{State: "dead", Error: fmt.Sprintf("http_%d", code), Reachable: true}
	default:
		return Result{State: "retry", Error: fmt.Sprintf("http_%d", code), Reachable: true}
	}
}

// Limiter is a token bucket; a zero rate never blocks.
type Limiter struct {
	mu     sync.Mutex
	rate   float64
	burst  float64
	tokens float64
	last   time.Time
}

func NewLimiter(ratePerSec float64) *Limiter {
	burst := ratePerSec
	if burst < 1 {
		burst = 1
	}
	return &Limiter{rate: ratePerSec, burst: burst, tokens: burst, last: time.Now()}
}

// Wait blocks until a token is available or ctx ends.
func (l *Limiter) Wait(ctx context.Context) error {
	if l.rate <= 0 {
		return nil
	}
	for {
		l.mu.Lock()
		now := time.Now()
		l.tokens += now.Sub(l.last).Seconds() * l.rate
		if l.tokens > l.burst {
			l.tokens = l.burst
		}
		l.last = now
		if l.tokens >= 1 {
			l.tokens--
			l.mu.Unlock()
			return nil
		}
		wait := time.Duration((1 - l.tokens) / l.rate * float64(time.Second))
		l.mu.Unlock()
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-time.After(wait):
		}
	}
}
