package config

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestLoadFileAndEnvironment(t *testing.T) {
	path := filepath.Join(t.TempDir(), "agent.conf")
	_ = os.WriteFile(path, []byte("# yorum\nNAME=erp-ajan\nAPI_URL=https://api.example.com\nAPI_KEY=\"io_v1_0123456789abcdef0123456789abcdef\"\nPOSTGRES_DSN=postgres://u:p@h/db\nTABLE=erp.outbox\nRATE_PER_SEC=5\n"), 0o600)
	t.Setenv("AGENT_CONCURRENCY", "2")
	c, err := Load(path)
	if err != nil {
		t.Fatal(err)
	}
	if c.Name != "erp-ajan" || c.Table != "erp.outbox" || c.Concurrency != 2 || c.RatePerSec != 5 || c.APIKey != "io_v1_0123456789abcdef0123456789abcdef" {
		t.Fatalf("%+v", c)
	}
	if _, ok := c.Summary()["table"]; !ok {
		t.Fatal("özet tabloyu içermeli")
	}
	if strings.Contains(strings.Join(keys(c.Summary()), ","), "key") {
		t.Fatal("özet anahtar içermemeli")
	}
}

func keys(m map[string]any) []string {
	var out []string
	for k := range m {
		out = append(out, k)
	}
	return out
}

func TestValidation(t *testing.T) {
	base := Defaults()
	base.APIURL = "https://api.example.com"
	base.APIKey = strings.Repeat("k", 40)
	base.PostgresDSN = "postgres://x"
	if err := base.Validate(); err != nil {
		t.Fatalf("geçerli yapılandırma reddedildi: %v", err)
	}
	bad := base
	bad.APIURL = "http://api.example.com"
	if err := bad.Validate(); err == nil || !strings.Contains(err.Error(), "https") {
		t.Fatal("uzak http kabul edilmemeli")
	}
	local := base
	local.APIURL = "http://127.0.0.1:8080"
	if err := local.Validate(); err != nil {
		t.Fatal("yerel http kabul edilmeli")
	}
	inj := base
	inj.Table = `outbox"; DROP TABLE x; --`
	if err := inj.Validate(); err == nil {
		t.Fatal("tablo adı doğrulanmalı")
	}
	dir := base
	dir.SourceKind = "directory"
	if err := dir.Validate(); err == nil || !strings.Contains(err.Error(), "DIRECTORY") {
		t.Fatal("dizin kaynağı yol istemeli")
	}
}
