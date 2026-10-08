package controlplane

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"github.com/alicebob/miniredis/v2"
	"github.com/redis/go-redis/v9"
	"golang.org/x/crypto/bcrypt"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

type fakeStore struct {
	hash   string
	routes map[string]Route
	err    error
}

func (s *fakeStore) PasswordHash(ctx context.Context, user string) (string, error) {
	if s.err != nil {
		return "", s.err
	}
	if user != "admin" {
		return "", sql.ErrNoRows
	}
	return s.hash, nil
}
func (s *fakeStore) ListRoutes(context.Context) ([]Route, error) {
	routes := []Route{}
	for _, r := range s.routes {
		routes = append(routes, r)
	}
	return routes, s.err
}
func (s *fakeStore) SaveRoute(ctx context.Context, r Route) error {
	if s.err != nil {
		return s.err
	}
	s.routes[r.Version] = r
	return nil
}
func (s *fakeStore) DeleteRoute(ctx context.Context, v string) error {
	delete(s.routes, v)
	return s.err
}
func (s *fakeStore) Resolve(ctx context.Context, v string) (string, error) {
	if s.err != nil {
		return "", s.err
	}
	r, ok := s.routes[v]
	if !ok || !r.Enabled {
		return "", sql.ErrNoRows
	}
	return r.ServerURL, nil
}

func setup(t *testing.T) (http.Handler, *fakeStore, *RedisSessions, *miniredis.Miniredis) {
	t.Helper()
	mini := miniredis.RunT(t)
	client := redis.NewClient(&redis.Options{Addr: mini.Addr()})
	t.Cleanup(func() { client.Close() })
	hash, err := bcrypt.GenerateFromPassword([]byte("correct-admin-password"), bcrypt.MinCost)
	if err != nil {
		t.Fatal(err)
	}
	store := &fakeStore{hash: string(hash), routes: map[string]Route{}}
	sessions := &RedisSessions{Client: client}
	admin, err := NewAdmin(store, sessions, false)
	if err != nil {
		t.Fatal(err)
	}
	return admin.Handler(), store, sessions, mini
}
func adminRequest(handler http.Handler, method, path, body string, cookie *http.Cookie, csrf, origin string) *httptest.ResponseRecorder {
	r := httptest.NewRequest(method, path, strings.NewReader(body))
	r.RemoteAddr = "127.0.0.1:1234"
	r.Header.Set("Content-Type", "application/json")
	if cookie != nil {
		r.AddCookie(cookie)
	}
	if csrf != "" {
		r.Header.Set("X-CSRF-Token", csrf)
	}
	if origin != "" {
		r.Header.Set("Origin", origin)
	}
	w := httptest.NewRecorder()
	handler.ServeHTTP(w, r)
	return w
}
func login(t *testing.T, h http.Handler) (*http.Cookie, string) {
	t.Helper()
	w := adminRequest(h, "POST", "/admin/api/login", `{"username":"admin","password":"correct-admin-password"}`, nil, "", "")
	if w.Code != 200 {
		t.Fatalf("login: %d %s", w.Code, w.Body.String())
	}
	var session Session
	if err := json.Unmarshal(w.Body.Bytes(), &session); err != nil {
		t.Fatal(err)
	}
	cookies := w.Result().Cookies()
	if len(cookies) != 1 || !cookies[0].HttpOnly || cookies[0].SameSite != http.SameSiteStrictMode || session.CSRF == "" {
		t.Fatal("invalid session cookie")
	}
	return cookies[0], session.CSRF
}
func TestLoginRouteManagementAndLogout(t *testing.T) {
	h, store, _, _ := setup(t)
	if w := adminRequest(h, "GET", "/admin/api/routes", "", nil, "", ""); w.Code != 401 {
		t.Fatal(w.Code)
	}
	cookie, csrf := login(t, h)
	if w := adminRequest(h, "GET", "/admin/api/session", "", cookie, "", ""); w.Code != 200 || !strings.Contains(w.Body.String(), "admin") {
		t.Fatal(w.Code, w.Body.String())
	}
	body := `{"version":"1.0","server_url":"http://game-1.0:8081","enabled":true}`
	if w := adminRequest(h, "PUT", "/admin/api/routes", body, cookie, "", ""); w.Code != 403 {
		t.Fatal(w.Code)
	}
	if w := adminRequest(h, "PUT", "/admin/api/routes", body, cookie, csrf, "http://evil.test"); w.Code != 403 {
		t.Fatal(w.Code)
	}
	if w := adminRequest(h, "PUT", "/admin/api/routes", body, cookie, csrf, ""); w.Code != 200 {
		t.Fatal(w.Code, w.Body.String())
	}
	if url, err := store.Resolve(context.Background(), "1.0"); err != nil || url != "http://game-1.0:8081" {
		t.Fatal(url, err)
	}
	if w := adminRequest(h, "GET", "/admin/api/routes", "", cookie, "", ""); w.Code != 200 || !strings.Contains(w.Body.String(), "1.0") {
		t.Fatal(w.Code)
	}
	body = `{"version":"1.0","server_url":"http://game-1.0:8081","enabled":false}`
	if w := adminRequest(h, "PUT", "/admin/api/routes", body, cookie, csrf, ""); w.Code != 200 {
		t.Fatal(w.Code)
	}
	if _, err := store.Resolve(context.Background(), "1.0"); !errors.Is(err, sql.ErrNoRows) {
		t.Fatal("disabled route resolved")
	}
	if w := adminRequest(h, "DELETE", "/admin/api/routes/1.0", "", cookie, csrf, ""); w.Code != 204 {
		t.Fatal(w.Code)
	}
	if len(store.routes) != 0 {
		t.Fatal("route not removed")
	}
	if w := adminRequest(h, "POST", "/admin/api/logout", "", cookie, csrf, ""); w.Code != 204 {
		t.Fatal(w.Code)
	}
	if w := adminRequest(h, "GET", "/admin/api/routes", "", cookie, "", ""); w.Code != 401 {
		t.Fatal("session survived logout", w.Code)
	}
}
func TestInvalidLoginRateLimitAndSessionExpiry(t *testing.T) {
	h, _, sessions, mini := setup(t)
	for _, body := range []string{`{"username":"admin","password":"wrong"}`, `{"username":"absent","password":"correct-admin-password"}`} {
		if w := adminRequest(h, "POST", "/admin/api/login", body, nil, "", ""); w.Code != 401 {
			t.Fatal(w.Code)
		}
	}
	if w := adminRequest(h, "POST", "/admin/api/login", `{}`, nil, "", "http://evil.test"); w.Code != 403 {
		t.Fatal(w.Code)
	}
	cookie, _ := login(t, h)
	mini.FastForward(SessionTTL + time.Second)
	if w := adminRequest(h, "GET", "/admin/api/session", "", cookie, "", ""); w.Code != 401 {
		t.Fatal("expired session accepted", w.Code)
	}
	for i := 0; i < 10; i++ {
		allowed, err := sessions.AllowLogin(context.Background(), "other-ip")
		if err != nil || !allowed {
			t.Fatal(i, err)
		}
	}
	if allowed, err := sessions.AllowLogin(context.Background(), "other-ip"); err != nil || allowed {
		t.Fatal("rate limit not enforced", err)
	}
	mini.FastForward(time.Minute)
	if allowed, err := sessions.AllowLogin(context.Background(), "other-ip"); err != nil || !allowed {
		t.Fatal("rate limit not reset", err)
	}
}
func TestInvalidConfigurationAndStorageFailure(t *testing.T) {
	h, store, _, mini := setup(t)
	cookie, csrf := login(t, h)
	for _, body := range []string{`{"version":"1.0","server_url":"file:///tmp/game","enabled":true}`, `{"version":"1.0","server_url":"http://game","enabled":true} {}`, `{"version":"1.0","server_url":"http://game","enabled":true,"extra":1}`} {
		if w := adminRequest(h, "PUT", "/admin/api/routes", body, cookie, csrf, ""); w.Code != 400 {
			t.Fatal(w.Code, w.Body.String())
		}
	}
	store.err = errors.New("database offline")
	if w := adminRequest(h, "GET", "/admin/api/routes", "", cookie, "", ""); w.Code != 503 {
		t.Fatal(w.Code)
	}
	mini.Close()
	if w := adminRequest(h, "GET", "/admin/api/routes", "", cookie, "", ""); w.Code != 503 {
		t.Fatal(w.Code)
	}
}
func TestSessionRotation(t *testing.T) {
	h, _, _, _ := setup(t)
	old, _ := login(t, h)
	w := adminRequest(h, "POST", "/admin/api/login", `{"username":"admin","password":"correct-admin-password"}`, old, "", "")
	if w.Code != 200 {
		t.Fatal(w.Code)
	}
	if w := adminRequest(h, "GET", "/admin/api/session", "", old, "", ""); w.Code != 401 {
		t.Fatal("old session survived rotation")
	}
}
