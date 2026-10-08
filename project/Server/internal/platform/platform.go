package platform

import (
	"context"
	"database/sql"
	"errors"
	"log"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	_ "github.com/go-sql-driver/mysql"
	"github.com/redis/go-redis/v9"
)

func Env(key, fallback string) string {
	if value := os.Getenv(key); value != "" {
		return value
	}
	return fallback
}

type Storage struct {
	MySQL *sql.DB
	Redis *redis.Client
}

func OpenStorage(ctx context.Context) (*Storage, error) {
	dsn := os.Getenv("MYSQL_DSN")
	if dsn == "" {
		return nil, errors.New("MYSQL_DSN is required")
	}
	db, err := sql.Open("mysql", dsn)
	if err != nil {
		return nil, err
	}
	db.SetMaxOpenConns(20)
	db.SetMaxIdleConns(10)
	db.SetConnMaxLifetime(3 * time.Minute)
	options, err := redis.ParseURL(Env("REDIS_URL", "redis://localhost:6379/0"))
	if err != nil {
		db.Close()
		return nil, err
	}
	storage := &Storage{MySQL: db, Redis: redis.NewClient(options)}
	if err = storage.Ping(ctx); err != nil {
		storage.Close()
		return nil, err
	}
	return storage, nil
}
func (s *Storage) Ping(ctx context.Context) error {
	if err := s.MySQL.PingContext(ctx); err != nil {
		return err
	}
	return s.Redis.Ping(ctx).Err()
}
func (s *Storage) Close() { s.Redis.Close(); s.MySQL.Close() }

func Serve(address string, handler http.Handler) error {
	server := &http.Server{Addr: address, Handler: handler, ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout: 30 * time.Second, WriteTimeout: 30 * time.Second, IdleTimeout: 60 * time.Second}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	done := make(chan struct{})
	go func() {
		defer close(done)
		<-ctx.Done()
		shutdown, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		if err := server.Shutdown(shutdown); err != nil {
			server.Close()
		}
	}()
	log.Printf("listening on %s", address)
	err := server.ListenAndServe()
	stop()
	<-done
	if errors.Is(err, http.ErrServerClosed) {
		return nil
	}
	return err
}
