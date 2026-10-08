package main

import (
	"context"
	"game/server/internal/database"
	"game/server/internal/platform"
	"log"
	"os"
	"time"
)

func main() {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	storage, err := platform.OpenStorage(ctx)
	if err != nil {
		log.Fatal("storage connection failed: ", err)
	}
	defer storage.Close()
	if err := database.Initialize(ctx, storage.MySQL, os.Getenv("ADMIN_USERNAME"), os.Getenv("ADMIN_PASSWORD")); err != nil {
		log.Fatal(err)
	}
	log.Print("database initialized; existing administrator and version routes preserved")
}
