// Package config loads the agent configuration from a key=value file and AGENT_* environment variables.
package config

import (
	"bufio"
	"errors"
	"fmt"
	"net/url"
	"os"
	"regexp"
	"strconv"
	"strings"
	"time"
)

// Config is the complete runtime configuration. Environment variables override the file.
type Config struct {
	Name        string        // agent name reported to IntegraOps; unique per project
	APIURL      string        // https://api.example.com
	APIKey      string        // ingest key (X-API-Key)
	SourceKind  string        // "postgres" or "directory"
	PostgresDSN string        // postgres://user:pass@host/db
	Table       string        // outbox table, optionally schema-qualified
	Directory   string        // root directory for the directory source
	KeepSent    bool          // directory source: keep delivered files under sent/
	Batch       int           // records claimed per poll
	Concurrency int           // parallel senders
	RatePerSec  float64       // maximum sends per second; 0 = unlimited
	Lease       time.Duration // how long a claimed record stays reserved
	Poll        time.Duration // idle wait between polls
	Listen      string        // status/metrics listen address; empty disables
	Heartbeat   time.Duration // heartbeat interval; 0 disables
	Timeout     time.Duration // HTTP timeout per send
}

var identifier = regexp.MustCompile(`^[a-z_][a-z0-9_]{0,62}(\.[a-z_][a-z0-9_]{0,62})?$`)
var namePattern = regexp.MustCompile(`^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`)

// Defaults returns the configuration before any file or environment values.
func Defaults() Config {
	host, _ := os.Hostname()
	return Config{
		Name: host, SourceKind: "postgres", Table: "source_outbox", Batch: 50, Concurrency: 4,
		RatePerSec: 50, Lease: 60 * time.Second, Poll: time.Second, Listen: "127.0.0.1:9464",
		Heartbeat: 30 * time.Second, Timeout: 15 * time.Second,
	}
}

// Load reads path (when not empty) and then the environment.
func Load(path string) (Config, error) {
	c := Defaults()
	values := map[string]string{}
	if path != "" {
		f, err := os.Open(path)
		if err != nil {
			return c, err
		}
		defer f.Close()
		s := bufio.NewScanner(f)
		for n := 1; s.Scan(); n++ {
			line := strings.TrimSpace(s.Text())
			if line == "" || strings.HasPrefix(line, "#") {
				continue
			}
			k, v, ok := strings.Cut(line, "=")
			if !ok {
				return c, fmt.Errorf("%s: satır %d anahtar=değer biçiminde değil", path, n)
			}
			values[strings.ToUpper(strings.TrimSpace(k))] = strings.Trim(strings.TrimSpace(v), `"'`)
		}
		if err := s.Err(); err != nil {
			return c, err
		}
	}
	for _, e := range os.Environ() {
		k, v, _ := strings.Cut(e, "=")
		if strings.HasPrefix(k, "AGENT_") {
			values[strings.TrimPrefix(k, "AGENT_")] = v
		}
	}
	return apply(c, values)
}

func apply(c Config, values map[string]string) (Config, error) {
	var err error
	str := func(key string, target *string) {
		if v, ok := values[key]; ok {
			*target = v
		}
	}
	num := func(key string, target *int) {
		if v, ok := values[key]; ok && err == nil {
			*target, err = strconv.Atoi(v)
		}
	}
	dur := func(key string, target *time.Duration) {
		if v, ok := values[key]; ok && err == nil {
			*target, err = time.ParseDuration(v)
		}
	}
	str("NAME", &c.Name)
	str("API_URL", &c.APIURL)
	str("API_KEY", &c.APIKey)
	str("SOURCE", &c.SourceKind)
	str("POSTGRES_DSN", &c.PostgresDSN)
	str("TABLE", &c.Table)
	str("DIRECTORY", &c.Directory)
	str("LISTEN", &c.Listen)
	num("BATCH", &c.Batch)
	num("CONCURRENCY", &c.Concurrency)
	dur("LEASE", &c.Lease)
	dur("POLL", &c.Poll)
	dur("HEARTBEAT", &c.Heartbeat)
	dur("TIMEOUT", &c.Timeout)
	if v, ok := values["RATE_PER_SEC"]; ok && err == nil {
		c.RatePerSec, err = strconv.ParseFloat(v, 64)
	}
	if v, ok := values["KEEP_SENT"]; ok && err == nil {
		c.KeepSent, err = strconv.ParseBool(v)
	}
	if err != nil {
		return c, err
	}
	return c, c.Validate()
}

// Validate checks the configuration without touching the network.
func (c Config) Validate() error {
	var problems []string
	if !namePattern.MatchString(c.Name) {
		problems = append(problems, "NAME harf/rakam ile başlamalı, en fazla 64 karakter")
	}
	u, err := url.Parse(c.APIURL)
	if err != nil || (u.Scheme != "https" && u.Scheme != "http") || u.Host == "" {
		problems = append(problems, "API_URL http(s)://sunucu biçiminde olmalı")
	} else if u.Scheme == "http" && !isLocal(u.Hostname()) {
		problems = append(problems, "API_URL yerel adres dışında https olmalı")
	}
	if len(c.APIKey) < 32 || len(c.APIKey) > 128 {
		problems = append(problems, "API_KEY 32–128 karakter olmalı")
	}
	switch c.SourceKind {
	case "postgres":
		if c.PostgresDSN == "" {
			problems = append(problems, "POSTGRES_DSN gerekli")
		}
		if !identifier.MatchString(c.Table) {
			problems = append(problems, "TABLE geçerli bir tablo adı olmalı (isteğe bağlı şema.tablo)")
		}
	case "directory":
		if c.Directory == "" {
			problems = append(problems, "DIRECTORY gerekli")
		}
	default:
		problems = append(problems, "SOURCE postgres veya directory olmalı")
	}
	if c.Batch < 1 || c.Batch > 1000 {
		problems = append(problems, "BATCH 1–1000 olmalı")
	}
	if c.Concurrency < 1 || c.Concurrency > 64 {
		problems = append(problems, "CONCURRENCY 1–64 olmalı")
	}
	if c.RatePerSec < 0 || c.RatePerSec > 10000 {
		problems = append(problems, "RATE_PER_SEC 0–10000 olmalı")
	}
	if c.Lease < 10*time.Second || c.Lease > time.Hour {
		problems = append(problems, "LEASE 10s–1h olmalı")
	}
	if c.Poll < 100*time.Millisecond || c.Poll > time.Minute {
		problems = append(problems, "POLL 100ms–1m olmalı")
	}
	if c.Timeout < time.Second || c.Timeout > time.Minute {
		problems = append(problems, "TIMEOUT 1s–1m olmalı")
	}
	if len(problems) > 0 {
		return errors.New(strings.Join(problems, "; "))
	}
	return nil
}

func isLocal(host string) bool {
	return host == "localhost" || host == "127.0.0.1" || host == "::1" || strings.HasSuffix(host, ".localhost")
}

// Summary is the non-secret part of the configuration, reported in heartbeats.
func (c Config) Summary() map[string]any {
	s := map[string]any{
		"source": c.SourceKind, "batch": c.Batch, "concurrency": c.Concurrency, "ratePerSec": c.RatePerSec,
		"lease": c.Lease.String(), "poll": c.Poll.String(), "listen": c.Listen,
	}
	if c.SourceKind == "postgres" {
		s["table"] = c.Table
	} else {
		s["directory"] = c.Directory
	}
	return s
}
