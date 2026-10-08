package controlplane

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"github.com/redis/go-redis/v9"
	"time"
)

type Session struct {
	Username string `json:"username"`
	CSRF     string `json:"csrf"`
}

var ErrNoSession = errors.New("session not found")

const SessionTTL = 8 * time.Hour

type Sessions interface {
	Create(context.Context, Session) (string, error)
	Get(context.Context, string) (Session, error)
	Delete(context.Context, string) error
	AllowLogin(context.Context, string) (bool, error)
}

type RedisSessions struct{ Client *redis.Client }

func randomToken() (string, error) {
	var data [32]byte
	if _, err := rand.Read(data[:]); err != nil {
		return "", err
	}
	return hex.EncodeToString(data[:]), nil
}
func sessionKey(token string) string {
	sum := sha256.Sum256([]byte(token))
	return "admin:session:" + hex.EncodeToString(sum[:])
}
func (s *RedisSessions) Create(ctx context.Context, session Session) (string, error) {
	token, err := randomToken()
	if err != nil {
		return "", err
	}
	data, err := json.Marshal(session)
	if err != nil {
		return "", err
	}
	return token, s.Client.Set(ctx, sessionKey(token), data, SessionTTL).Err()
}
func (s *RedisSessions) Get(ctx context.Context, token string) (Session, error) {
	var session Session
	if len(token) != 64 {
		return session, ErrNoSession
	}
	data, err := s.Client.Get(ctx, sessionKey(token)).Bytes()
	if errors.Is(err, redis.Nil) {
		return session, ErrNoSession
	}
	if err != nil {
		return session, err
	}
	err = json.Unmarshal(data, &session)
	if err == nil && (session.Username == "" || len(session.CSRF) != 64) {
		err = ErrNoSession
	}
	return session, err
}
func (s *RedisSessions) Delete(ctx context.Context, token string) error {
	return s.Client.Del(ctx, sessionKey(token)).Err()
}

// Atomic fixed-window rate limit, also shared across multiple gateways.
func (s *RedisSessions) AllowLogin(ctx context.Context, ip string) (bool, error) {
	sum := sha256.Sum256([]byte(ip))
	key := "admin:login:" + hex.EncodeToString(sum[:])
	count, err := s.Client.Eval(ctx, `local n = redis.call('INCR', KEYS[1]); if n == 1 then redis.call('EXPIRE', KEYS[1], 60) end; return n`, []string{key}).Int64()
	return count <= 10, err
}
