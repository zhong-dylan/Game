package main

import (
	"context"
	"encoding/json"
	"flag"
	"game/server/internal/controlplane"
	"game/server/internal/gateway"
	"game/server/internal/platform"
	"log"
	"net/http"
	"time"
)

func main() {
	address := flag.String("addr", platform.Env("GATEWAY_ADDR", ":8080"), "gateway listen address")
	flag.Parse()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	storage, err := platform.OpenStorage(ctx)
	cancel()
	if err != nil {
		log.Fatal("storage connection failed: ", err)
	}
	defer storage.Close()
	store := &controlplane.SQLStore{DB: storage.MySQL}
	admin, err := controlplane.NewAdmin(store, &controlplane.RedisSessions{Client: storage.Redis}, platform.Env("ADMIN_COOKIE_SECURE", "false") == "true")
	if err != nil {
		log.Fatal(err)
	}
	// Fail early if schema initialization was skipped; an empty route list is valid.
	ctx, cancel = context.WithTimeout(context.Background(), 5*time.Second)
	_, err = store.ListRoutes(ctx)
	cancel()
	if err != nil {
		log.Fatal("database not initialized; run init-db.sh: ", err)
	}
	mux := http.NewServeMux()
	mux.Handle("/admin/", admin.Handler())
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) {
		ctx, cancel := context.WithTimeout(r.Context(), 2*time.Second)
		defer cancel()
		if storage.Ping(ctx) != nil {
			http.Error(w, "storage unavailable", 503)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(map[string]string{"status": "ok", "service": "gateway"})
	})
	mux.Handle("/api/game/", gateway.NewDynamicHandler(store))
	if err := platform.Serve(*address, mux); err != nil {
		log.Fatal(err)
	}
}
