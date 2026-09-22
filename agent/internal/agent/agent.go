// Package agent runs the claim → send → complete loop, the status server and the heartbeat.
package agent

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/SelimYildirim1/IntegraOps/agent/internal/config"
	"github.com/SelimYildirim1/IntegraOps/agent/internal/sender"
	"github.com/SelimYildirim1/IntegraOps/agent/internal/source"
)

const maxAttempts = 50 // about 50 minutes of one-minute retries before a record is parked as dead

// Stats are the counters exposed on /status, /metrics and in heartbeats.
type Stats struct {
	Sent, Retried, Dead, Duplicates atomic.Int64
	LastSuccess                     atomic.Int64 // unix seconds
	LastErrorAt                     atomic.Int64
	lastError                       atomic.Value // string
	Connected                       atomic.Bool
	Pending                         atomic.Int64
}

func (s *Stats) setError(e string) {
	s.lastError.Store(e)
	s.LastErrorAt.Store(time.Now().Unix())
}
func (s *Stats) LastError() string {
	if v, ok := s.lastError.Load().(string); ok {
		return v
	}
	return ""
}

type Agent struct {
	cfg     config.Config
	src     source.Source
	snd     *sender.Sender
	stats   Stats
	version string
	log     *slog.Logger
	started time.Time
}

func New(cfg config.Config, src source.Source, snd *sender.Sender, version string, log *slog.Logger) *Agent {
	return &Agent{cfg: cfg, src: src, snd: snd, version: version, log: log, started: time.Now()}
}

// Run blocks until ctx ends. It never returns an error for delivery problems; those are retried.
func (a *Agent) Run(ctx context.Context) error {
	var wg sync.WaitGroup
	if a.cfg.Listen != "" {
		wg.Add(1)
		go func() { defer wg.Done(); a.serve(ctx) }()
	}
	if a.cfg.Heartbeat > 0 {
		wg.Add(1)
		go func() { defer wg.Done(); a.heartbeats(ctx) }()
	}
	a.loop(ctx)
	wg.Wait()
	return nil
}

func (a *Agent) loop(ctx context.Context) {
	idle := a.cfg.Poll
	for ctx.Err() == nil {
		records, err := a.src.Claim(ctx, a.cfg.Batch, a.cfg.Lease)
		if err != nil {
			if ctx.Err() != nil {
				return
			}
			a.stats.setError("source_" + short(err))
			a.log.Warn("kaynak okunamadı", "error", err)
			sleep(ctx, 5*time.Second)
			continue
		}
		if n, err := a.src.Pending(ctx); err == nil {
			a.stats.Pending.Store(int64(n))
		}
		if len(records) == 0 {
			sleep(ctx, idle)
			continue
		}
		unreachable := a.deliver(ctx, records)
		if unreachable {
			// The API is down: keep claimed records available and wait before hammering it.
			sleep(ctx, 5*time.Second)
		}
	}
}

// deliver sends a batch with bounded concurrency; returns true when the API was unreachable.
func (a *Agent) deliver(ctx context.Context, records []source.Record) bool {
	sem := make(chan struct{}, a.cfg.Concurrency)
	var wg sync.WaitGroup
	var unreachable atomic.Bool
	for _, r := range records {
		if ctx.Err() != nil || unreachable.Load() {
			// Do not burn attempts while the API is down; release the lease immediately.
			_ = a.src.Complete(context.Background(), r, source.Outcome{State: "retry", Error: "unreachable", Delay: time.Second})
			continue
		}
		sem <- struct{}{}
		wg.Add(1)
		go func(r source.Record) {
			defer wg.Done()
			defer func() { <-sem }()
			res := a.snd.Send(ctx, r.ID, r.Body)
			if !res.Reachable && res.Error == "unreachable" {
				unreachable.Store(true)
			}
			a.record(ctx, r, res)
		}(r)
	}
	wg.Wait()
	a.stats.Connected.Store(!unreachable.Load())
	return unreachable.Load()
}

func (a *Agent) record(ctx context.Context, r source.Record, res sender.Result) {
	out := source.Outcome{State: res.State, Error: res.Error}
	switch res.State {
	case "sent":
		a.stats.Sent.Add(1)
		if res.Duplicate {
			a.stats.Duplicates.Add(1)
		}
		a.stats.LastSuccess.Store(time.Now().Unix())
	case "dead":
		a.stats.Dead.Add(1)
		a.stats.setError(res.Error)
		a.log.Error("olay kalıcı olarak reddedildi", "eventId", r.ID, "reason", res.Error)
	default:
		if r.Attempts >= maxAttempts && res.Error != "unreachable" {
			out.State = "dead"
			out.Error = "attempt_limit_" + res.Error
			a.stats.Dead.Add(1)
		} else {
			out.Delay = source.Backoff(r.Attempts)
			if res.Error == "unreachable" {
				out.Delay = 5 * time.Second
			}
			a.stats.Retried.Add(1)
		}
		a.stats.setError(res.Error)
	}
	// Completion must not be cancelled by shutdown, or the record would be re-sent after restart.
	cctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := a.src.Complete(cctx, r, out); err != nil {
		a.log.Warn("sonuç kaydedilemedi; kira dolunca yeniden denenecek", "eventId", r.ID, "error", err)
	}
}

// ---------- status server ----------

func (a *Agent) snapshot() map[string]any {
	return map[string]any{
		"name": a.cfg.Name, "version": a.version, "source": a.cfg.SourceKind, "connected": a.stats.Connected.Load(),
		"pending": a.stats.Pending.Load(), "sent": a.stats.Sent.Load(), "duplicates": a.stats.Duplicates.Load(),
		"retried": a.stats.Retried.Load(), "dead": a.stats.Dead.Load(),
		"lastSuccessAt": unixOrNil(a.stats.LastSuccess.Load()), "lastError": a.stats.LastError(), "lastErrorAt": unixOrNil(a.stats.LastErrorAt.Load()),
		"uptimeSeconds": int(time.Since(a.started).Seconds()), "config": a.cfg.Summary(),
	}
}

func (a *Agent) serve(ctx context.Context) {
	mux := http.NewServeMux()
	mux.HandleFunc("/health", func(w http.ResponseWriter, r *http.Request) { fmt.Fprintln(w, "ok") })
	mux.HandleFunc("/status", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(a.snapshot())
	})
	mux.HandleFunc("/metrics", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/plain; version=0.0.4")
		connected := 0
		if a.stats.Connected.Load() {
			connected = 1
		}
		fmt.Fprintf(w, "# TYPE integraops_agent_pending gauge\nintegraops_agent_pending %d\n", a.stats.Pending.Load())
		fmt.Fprintf(w, "# TYPE integraops_agent_connected gauge\nintegraops_agent_connected %d\n", connected)
		fmt.Fprintf(w, "# TYPE integraops_agent_sent_total counter\nintegraops_agent_sent_total %d\n", a.stats.Sent.Load())
		fmt.Fprintf(w, "# TYPE integraops_agent_retried_total counter\nintegraops_agent_retried_total %d\n", a.stats.Retried.Load())
		fmt.Fprintf(w, "# TYPE integraops_agent_dead_total counter\nintegraops_agent_dead_total %d\n", a.stats.Dead.Load())
		fmt.Fprintf(w, "# TYPE integraops_agent_last_success_timestamp_seconds gauge\nintegraops_agent_last_success_timestamp_seconds %d\n", a.stats.LastSuccess.Load())
	})
	srv := &http.Server{Addr: a.cfg.Listen, Handler: mux, ReadHeaderTimeout: 5 * time.Second}
	go func() {
		<-ctx.Done()
		shutdown, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		defer cancel()
		_ = srv.Shutdown(shutdown)
	}()
	if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
		a.log.Error("durum sunucusu açılamadı", "error", err)
	}
}

// ---------- heartbeat ----------

func (a *Agent) heartbeats(ctx context.Context) {
	ticker := time.NewTicker(a.cfg.Heartbeat)
	defer ticker.Stop()
	client := &http.Client{Timeout: a.cfg.Timeout}
	send := func() {
		host, _ := os.Hostname()
		payload := map[string]any{
			"name": a.cfg.Name, "version": a.version, "host": host, "sourceKind": a.cfg.SourceKind,
			"pending": a.stats.Pending.Load(), "sentTotal": a.stats.Sent.Load(), "deadTotal": a.stats.Dead.Load(), "retryTotal": a.stats.Retried.Load(),
			"lastError": a.stats.LastError(), "lastSuccessAt": unixOrNil(a.stats.LastSuccess.Load()), "config": a.cfg.Summary(),
		}
		body, _ := json.Marshal(payload)
		req, err := http.NewRequestWithContext(ctx, http.MethodPost, strings.TrimRight(a.cfg.APIURL, "/")+"/api/v1/agents/heartbeat", bytes.NewReader(body))
		if err != nil {
			return
		}
		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("X-API-Key", a.cfg.APIKey)
		resp, err := client.Do(req)
		if err != nil {
			return
		}
		resp.Body.Close()
		if resp.StatusCode >= 300 {
			a.log.Warn("yaşam sinyali kabul edilmedi", "status", resp.StatusCode)
		}
	}
	send()
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			send()
		}
	}
}

func unixOrNil(v int64) any {
	if v == 0 {
		return nil
	}
	return time.Unix(v, 0).UTC().Format(time.RFC3339)
}

func short(err error) string {
	s := err.Error()
	if i := strings.IndexAny(s, ":\n"); i > 0 {
		s = s[:i]
	}
	if len(s) > 60 {
		s = s[:60]
	}
	return strings.ReplaceAll(strings.TrimSpace(s), " ", "_")
}

func sleep(ctx context.Context, d time.Duration) {
	select {
	case <-ctx.Done():
	case <-time.After(d):
	}
}
