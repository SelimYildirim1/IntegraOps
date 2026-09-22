package source

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"fmt"
	"strings"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"
)

// Postgres reads an outbox table written inside the source system's own transactions.
// The table contract matches samples/OrderSource (see docs/entegrasyon-sozlesmesi.md):
//
//	id uuid PRIMARY KEY, body text, state varchar(16) DEFAULT 'pending', attempts integer DEFAULT 0,
//	available_at timestamptz DEFAULT clock_timestamp(), lease_token uuid, lease_until timestamptz,
//	last_error text, created_at timestamptz DEFAULT clock_timestamp()
type Postgres struct {
	pool  *pgxpool.Pool
	table string
}

// NewPostgres connects to dsn. table may be schema-qualified; it is validated by the config package.
func NewPostgres(ctx context.Context, dsn, table string) (*Postgres, error) {
	cfg, err := pgxpool.ParseConfig(dsn)
	if err != nil {
		return nil, err
	}
	cfg.MaxConns = 4
	cfg.ConnConfig.ConnectTimeout = 10 * time.Second
	pool, err := pgxpool.NewWithConfig(ctx, cfg)
	if err != nil {
		return nil, err
	}
	parts := strings.Split(table, ".")
	for i, p := range parts {
		parts[i] = `"` + p + `"`
	}
	return &Postgres{pool: pool, table: strings.Join(parts, ".")}, nil
}

func (p *Postgres) Claim(ctx context.Context, n int, lease time.Duration) ([]Record, error) {
	var token [16]byte
	if _, err := rand.Read(token[:]); err != nil {
		return nil, err
	}
	leaseToken := formatUUID(token)
	rows, err := p.pool.Query(ctx, fmt.Sprintf(`
		UPDATE %s SET state='sending', attempts=attempts+1, lease_token=$1, lease_until=clock_timestamp()+$2::interval
		WHERE id IN (
			SELECT id FROM %s
			WHERE (state='pending' AND available_at<=clock_timestamp()) OR (state='sending' AND lease_until<clock_timestamp())
			ORDER BY created_at, id LIMIT $3 FOR UPDATE SKIP LOCKED)
		RETURNING id::text, body, attempts`, p.table, p.table), leaseToken, lease.String(), n)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []Record
	for rows.Next() {
		var r Record
		var body string
		if err := rows.Scan(&r.ID, &body, &r.Attempts); err != nil {
			return nil, err
		}
		r.Body = []byte(body)
		r.lease = leaseToken
		out = append(out, r)
	}
	return out, rows.Err()
}

func (p *Postgres) Complete(ctx context.Context, r Record, o Outcome) error {
	var errText *string
	if o.Error != "" {
		e := o.Error
		errText = &e
	}
	_, err := p.pool.Exec(ctx, fmt.Sprintf(`
		UPDATE %s SET state=$1, last_error=$2, lease_token=NULL, lease_until=NULL, available_at=clock_timestamp()+$3::interval
		WHERE id=$4 AND lease_token=$5 AND state='sending'`, p.table),
		stateName(o.State), errText, o.Delay.String(), r.ID, r.lease)
	return err
}

func (p *Postgres) Pending(ctx context.Context) (int, error) {
	var n int
	err := p.pool.QueryRow(ctx, fmt.Sprintf(`SELECT count(*) FROM %s WHERE state IN ('pending','sending')`, p.table)).Scan(&n)
	return n, err
}

func (p *Postgres) Close() error { p.pool.Close(); return nil }

func stateName(state string) string {
	switch state {
	case "sent", "dead":
		return state
	default:
		return "pending"
	}
}

func formatUUID(b [16]byte) string {
	b[6] = (b[6] & 0x0f) | 0x40
	b[8] = (b[8] & 0x3f) | 0x80
	h := hex.EncodeToString(b[:])
	return h[:8] + "-" + h[8:12] + "-" + h[12:16] + "-" + h[16:20] + "-" + h[20:]
}
