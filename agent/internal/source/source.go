// Package source reads durable event records from the customer system and records each delivery outcome.
package source

import (
	"context"
	"time"
)

// Record is one claimed event. Body is the JSON the source system produced; ID is its event id.
type Record struct {
	ID       string
	Body     []byte
	Attempts int // including the current one
	lease    string
	path     string
}

// Outcome of one delivery attempt.
type Outcome struct {
	State string // "sent", "retry" or "dead"
	Error string
	Delay time.Duration // for retry: when the record becomes available again
}

// Source is a durable queue owned by the customer system. Records stay there until delivered.
type Source interface {
	// Claim reserves up to n available records for this process.
	Claim(ctx context.Context, n int, lease time.Duration) ([]Record, error)
	// Complete records the outcome of a claimed record. It is a no-op when the lease was lost.
	Complete(ctx context.Context, r Record, o Outcome) error
	// Pending counts records still waiting for delivery.
	Pending(ctx context.Context) (int, error)
	Close() error
}

// Backoff is the retry delay after the given attempt number (1-based), capped at one minute.
func Backoff(attempt int) time.Duration {
	if attempt > 6 {
		attempt = 6
	}
	return time.Duration(1<<uint(attempt)) * time.Second
}
