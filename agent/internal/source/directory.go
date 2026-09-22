package source

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"
)

// Directory reads one JSON event per file. The source system writes files atomically into
// <root>/pending (write to a temporary name, then rename). The agent moves files through
// sending/, and finally to sent/ (or deletes them) or dead/ with a .error sidecar.
//
// Retry scheduling is encoded in the pending file name: <availableAtUnix>__<attempts>__<name>.
// Files without the prefix are treated as new.
type Directory struct {
	root     string
	keepSent bool
}

func NewDirectory(root string, keepSent bool) (*Directory, error) {
	for _, sub := range []string{"pending", "sending", "sent", "dead"} {
		if err := os.MkdirAll(filepath.Join(root, sub), 0o750); err != nil {
			return nil, err
		}
	}
	return &Directory{root: root, keepSent: keepSent}, nil
}

type pendingFile struct {
	name        string
	availableAt int64
	attempts    int
	original    string
}

func parsePending(name string) pendingFile {
	parts := strings.SplitN(name, "__", 3)
	if len(parts) == 3 {
		at, err1 := strconv.ParseInt(parts[0], 10, 64)
		attempts, err2 := strconv.Atoi(parts[1])
		if err1 == nil && err2 == nil {
			return pendingFile{name: name, availableAt: at, attempts: attempts, original: parts[2]}
		}
	}
	return pendingFile{name: name, original: name}
}

func (d *Directory) Claim(ctx context.Context, n int, lease time.Duration) ([]Record, error) {
	now := time.Now().Unix()
	// Stale sending/ files belong to a crashed run; recover them once their lease is over.
	if stale, err := os.ReadDir(filepath.Join(d.root, "sending")); err == nil {
		for _, e := range stale {
			info, err := e.Info()
			if err != nil || time.Since(info.ModTime()) < lease {
				continue
			}
			p := parsePending(e.Name())
			_ = os.Rename(filepath.Join(d.root, "sending", e.Name()),
				filepath.Join(d.root, "pending", fmt.Sprintf("%d__%d__%s", now, p.attempts, p.original)))
		}
	}
	entries, err := os.ReadDir(filepath.Join(d.root, "pending"))
	if err != nil {
		return nil, err
	}
	var files []pendingFile
	for _, e := range entries {
		if e.IsDir() || !strings.HasSuffix(e.Name(), ".json") || strings.HasPrefix(e.Name(), ".") {
			continue
		}
		p := parsePending(e.Name())
		if p.availableAt <= now {
			files = append(files, p)
		}
	}
	sort.Slice(files, func(i, j int) bool { return files[i].name < files[j].name })
	var out []Record
	for _, p := range files {
		if len(out) >= n {
			break
		}
		claimed := fmt.Sprintf("%d__%d__%s", now, p.attempts+1, p.original)
		dst := filepath.Join(d.root, "sending", claimed)
		if err := os.Rename(filepath.Join(d.root, "pending", p.name), dst); err != nil {
			continue // another process took it
		}
		body, err := os.ReadFile(dst)
		if err != nil {
			continue
		}
		_ = os.Chtimes(dst, time.Now(), time.Now())
		out = append(out, Record{ID: eventID(body), Body: body, Attempts: p.attempts + 1, path: dst})
	}
	return out, nil
}

func (d *Directory) Complete(ctx context.Context, r Record, o Outcome) error {
	if r.path == "" {
		return errors.New("kayıt bu kaynaktan alınmadı")
	}
	p := parsePending(filepath.Base(r.path))
	switch o.State {
	case "sent":
		if !d.keepSent {
			return os.Remove(r.path)
		}
		return os.Rename(r.path, filepath.Join(d.root, "sent", p.original))
	case "dead":
		if err := os.WriteFile(filepath.Join(d.root, "dead", p.original+".error"), []byte(o.Error+"\n"), 0o640); err != nil {
			return err
		}
		return os.Rename(r.path, filepath.Join(d.root, "dead", p.original))
	default:
		at := time.Now().Add(o.Delay).Unix()
		return os.Rename(r.path, filepath.Join(d.root, "pending", fmt.Sprintf("%d__%d__%s", at, p.attempts, p.original)))
	}
}

func (d *Directory) Pending(ctx context.Context) (int, error) {
	total := 0
	for _, sub := range []string{"pending", "sending"} {
		entries, err := os.ReadDir(filepath.Join(d.root, sub))
		if err != nil {
			return 0, err
		}
		for _, e := range entries {
			if !e.IsDir() && strings.HasSuffix(e.Name(), ".json") {
				total++
			}
		}
	}
	return total, nil
}

func (d *Directory) Close() error { return nil }

// eventID extracts "eventId" without a full decode; the sender validates the body anyway.
func eventID(body []byte) string {
	s := string(body)
	i := strings.Index(s, `"eventId"`)
	if i < 0 {
		return ""
	}
	s = s[i+len(`"eventId"`):]
	if j := strings.Index(s, `"`); j >= 0 {
		s = s[j+1:]
		if k := strings.Index(s, `"`); k >= 0 {
			return s[:k]
		}
	}
	return ""
}
