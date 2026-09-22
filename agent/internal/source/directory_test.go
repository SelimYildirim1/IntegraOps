package source

import (
	"context"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func write(t *testing.T, dir, name, eventID string) {
	t.Helper()
	tmp := filepath.Join(dir, "pending", "."+name+".tmp")
	if err := os.WriteFile(tmp, []byte(`{"eventId":"`+eventID+`","flow":"f"}`), 0o640); err != nil {
		t.Fatal(err)
	}
	if err := os.Rename(tmp, filepath.Join(dir, "pending", name)); err != nil {
		t.Fatal(err)
	}
}

func TestDirectoryLifecycle(t *testing.T) {
	root := t.TempDir()
	d, err := NewDirectory(root, true)
	if err != nil {
		t.Fatal(err)
	}
	write(t, root, "a.json", "11111111-1111-4111-8111-111111111111")
	write(t, root, "b.json", "22222222-2222-4222-8222-222222222222")
	ctx := context.Background()
	records, err := d.Claim(ctx, 10, time.Minute)
	if err != nil || len(records) != 2 {
		t.Fatalf("claim: %v %d", err, len(records))
	}
	if records[0].ID != "11111111-1111-4111-8111-111111111111" || records[0].Attempts != 1 {
		t.Fatalf("%+v", records[0])
	}
	if n, _ := d.Pending(ctx); n != 2 {
		t.Fatalf("sending sayılmalı: %d", n)
	}
	if err := d.Complete(ctx, records[0], Outcome{State: "sent"}); err != nil {
		t.Fatal(err)
	}
	if err := d.Complete(ctx, records[1], Outcome{State: "retry", Error: "http_503", Delay: time.Hour}); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(filepath.Join(root, "sent", "a.json")); err != nil {
		t.Fatal("teslim edilen dosya sent/ altında olmalı")
	}
	again, _ := d.Claim(ctx, 10, time.Minute)
	if len(again) != 0 {
		t.Fatal("bir saat sonraya ertelenen dosya hemen alınmamalı")
	}
	// Retry becomes due: rename its schedule to the past.
	entries, _ := os.ReadDir(filepath.Join(root, "pending"))
	old := filepath.Join(root, "pending", entries[0].Name())
	if err := os.Rename(old, filepath.Join(root, "pending", "1__1__b.json")); err != nil {
		t.Fatal(err)
	}
	due, _ := d.Claim(ctx, 10, time.Minute)
	if len(due) != 1 || due[0].Attempts != 2 {
		t.Fatalf("ikinci deneme bekleniyordu: %+v", due)
	}
	if err := d.Complete(ctx, due[0], Outcome{State: "dead", Error: "http_400"}); err != nil {
		t.Fatal(err)
	}
	if data, err := os.ReadFile(filepath.Join(root, "dead", "b.json.error")); err != nil || string(data) != "http_400\n" {
		t.Fatalf("hata dosyası: %v %q", err, data)
	}
	if n, _ := d.Pending(ctx); n != 0 {
		t.Fatalf("bekleyen kalmamalı: %d", n)
	}
}

func TestDirectoryRecoversStaleSending(t *testing.T) {
	root := t.TempDir()
	d, _ := NewDirectory(root, false)
	stale := filepath.Join(root, "sending", "5__3__c.json")
	_ = os.WriteFile(stale, []byte(`{"eventId":"33333333-3333-4333-8333-333333333333"}`), 0o640)
	past := time.Now().Add(-2 * time.Minute)
	_ = os.Chtimes(stale, past, past)
	records, _ := d.Claim(context.Background(), 10, time.Minute)
	if len(records) != 1 || records[0].Attempts != 4 {
		t.Fatalf("çöken koşunun dosyası geri alınmalı: %+v", records)
	}
	_ = d.Complete(context.Background(), records[0], Outcome{State: "sent"})
	if _, err := os.Stat(filepath.Join(root, "sent", "c.json")); !os.IsNotExist(err) {
		t.Fatal("keepSent kapalıyken dosya silinmeli")
	}
}

func TestBackoffCaps(t *testing.T) {
	if Backoff(1) != 2*time.Second || Backoff(6) != 64*time.Second || Backoff(40) != 64*time.Second {
		t.Fatal("bekleme süresi beklenen gibi değil")
	}
}
