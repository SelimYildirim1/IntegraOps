// integraops-agent forwards durable event records from a customer system to the IntegraOps API.
//
//	integraops-agent -config agent.conf
//	integraops-agent -check -config agent.conf   # validate configuration and exit
package main

import (
	"context"
	"flag"
	"fmt"
	"log/slog"
	"os"
	"os/signal"
	"syscall"

	"github.com/SelimYildirim1/IntegraOps/agent/internal/agent"
	"github.com/SelimYildirim1/IntegraOps/agent/internal/config"
	"github.com/SelimYildirim1/IntegraOps/agent/internal/sender"
	"github.com/SelimYildirim1/IntegraOps/agent/internal/source"
)

// Set with -ldflags "-X main.version=v1.2.0" at build time.
var version = "dev"

func main() {
	configPath := flag.String("config", "", "key=value configuration file (AGENT_* environment variables override it)")
	check := flag.Bool("check", false, "validate the configuration and exit")
	showVersion := flag.Bool("version", false, "print the version and exit")
	flag.Parse()
	if *showVersion {
		fmt.Println(version)
		return
	}
	log := slog.New(slog.NewJSONHandler(os.Stdout, nil))
	cfg, err := config.Load(*configPath)
	if err != nil {
		log.Error("yapılandırma geçersiz", "error", err)
		os.Exit(2)
	}
	if *check {
		fmt.Println("yapılandırma geçerli")
		return
	}
	ctx, stop := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer stop()

	var src source.Source
	switch cfg.SourceKind {
	case "postgres":
		src, err = source.NewPostgres(ctx, cfg.PostgresDSN, cfg.Table)
	case "directory":
		src, err = source.NewDirectory(cfg.Directory, cfg.KeepSent)
	}
	if err != nil {
		log.Error("kaynak açılamadı", "error", err)
		os.Exit(1)
	}
	defer src.Close()
	snd := sender.New(cfg.APIURL, cfg.APIKey, cfg.Timeout, cfg.RatePerSec, version)
	log.Info("ajan başlıyor", "name", cfg.Name, "version", version, "source", cfg.SourceKind, "api", cfg.APIURL, "listen", cfg.Listen)
	if err := agent.New(cfg, src, snd, version, log).Run(ctx); err != nil {
		log.Error("ajan durdu", "error", err)
		os.Exit(1)
	}
	log.Info("ajan durduruldu")
}
