package main

import (
	"context"
	"encoding/json"
	"game/server/internal/gateway"
	"game/server/internal/platform"
	"log"
	"net/http"
	"time"
)

func main() {
	version := platform.Env("GAME_VERSION", "1.0")
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	storage, err := platform.OpenStorage(ctx)
	cancel()
	if err != nil {
		log.Fatal("storage connection failed: ", err)
	}
	defer storage.Close()
	mux := http.NewServeMux()
	mux.HandleFunc("/healthz", func(w http.ResponseWriter, r *http.Request) {
		ctx, cancel := context.WithTimeout(r.Context(), 2*time.Second)
		defer cancel()
		if storage.Ping(ctx) != nil {
			http.Error(w, "storage unavailable", http.StatusServiceUnavailable)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(map[string]string{"status": "ok", "version": version})
	})
	mux.HandleFunc("/api/game/info", func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodGet {
			w.Header().Set("Allow", "GET")
			http.Error(w, "method not allowed", 405)
			return
		}
		if r.Header.Get(gateway.VersionHeader) != version {
			http.Error(w, "game version mismatch", http.StatusConflict)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(map[string]string{"service": "gameserver", "version": version})
	})
	if err := platform.Serve(platform.Env("GAME_ADDR", ":8081"), mux); err != nil {
		log.Fatal(err)
	}
}
