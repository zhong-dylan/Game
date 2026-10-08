package controlplane

import (
	"context"
	"crypto/subtle"
	"database/sql"
	"embed"
	"encoding/json"
	"errors"
	"golang.org/x/crypto/bcrypt"
	"io"
	"net"
	"net/http"
	"net/url"
	"strings"
	"time"
)

//go:embed web/index.html
var web embed.FS

const cookieName = "game_admin_session"

type Admin struct {
	Store         Store
	Sessions      Sessions
	SecureCookies bool
	dummyHash     []byte
}

func NewAdmin(store Store, sessions Sessions, secure bool) (*Admin, error) {
	hash, err := bcrypt.GenerateFromPassword([]byte("invalid-login-dummy-password"), bcrypt.DefaultCost)
	return &Admin{Store: store, Sessions: sessions, SecureCookies: secure, dummyHash: hash}, err
}
func (a *Admin) Handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /admin/{$}", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		data, _ := web.ReadFile("web/index.html")
		w.Write(data)
	})
	mux.HandleFunc("POST /admin/api/login", a.login)
	mux.HandleFunc("GET /admin/api/session", a.requireSession(false, a.currentSession))
	mux.HandleFunc("POST /admin/api/logout", a.requireSession(true, a.logout))
	mux.HandleFunc("GET /admin/api/routes", a.requireSession(false, a.listRoutes))
	mux.HandleFunc("PUT /admin/api/routes", a.requireSession(true, a.saveRoute))
	mux.HandleFunc("DELETE /admin/api/routes/{version}", a.requireSession(true, a.deleteRoute))
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Cache-Control", "no-store")
		w.Header().Set("X-Content-Type-Options", "nosniff")
		w.Header().Set("X-Frame-Options", "DENY")
		w.Header().Set("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'")
		ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
		defer cancel()
		mux.ServeHTTP(w, r.WithContext(ctx))
	})
}
func sameOrigin(r *http.Request) bool {
	origin := r.Header.Get("Origin")
	if origin == "" {
		return r.Header.Get("Sec-Fetch-Site") != "cross-site"
	}
	parsed, err := url.Parse(origin)
	return err == nil && strings.EqualFold(parsed.Host, r.Host) && (parsed.Scheme == "http" || parsed.Scheme == "https")
}
func decode(w http.ResponseWriter, r *http.Request, value any) bool {
	if !strings.HasPrefix(r.Header.Get("Content-Type"), "application/json") {
		http.Error(w, "JSON required", 415)
		return false
	}
	r.Body = http.MaxBytesReader(w, r.Body, 4096)
	decoder := json.NewDecoder(r.Body)
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(value); err != nil {
		http.Error(w, "invalid request", 400)
		return false
	}
	if err := decoder.Decode(new(any)); err != io.EOF {
		http.Error(w, "invalid request", 400)
		return false
	}
	return true
}
func writeJSON(w http.ResponseWriter, value any) {
	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(value)
}
func (a *Admin) login(w http.ResponseWriter, r *http.Request) {
	if !sameOrigin(r) {
		http.Error(w, "invalid origin", 403)
		return
	}
	ip, _, err := net.SplitHostPort(r.RemoteAddr)
	if err != nil {
		ip = r.RemoteAddr
	}
	allowed, err := a.Sessions.AllowLogin(r.Context(), ip)
	if err != nil {
		http.Error(w, "session storage unavailable", 503)
		return
	}
	if !allowed {
		w.Header().Set("Retry-After", "60")
		http.Error(w, "too many login attempts", 429)
		return
	}
	var credentials struct {
		Username string `json:"username"`
		Password string `json:"password"`
	}
	if !decode(w, r, &credentials) {
		return
	}
	if credentials.Username == "" || len(credentials.Username) > 64 || len(credentials.Password) > 72 {
		http.Error(w, "invalid credentials", 401)
		return
	}
	hash, err := a.Store.PasswordHash(r.Context(), credentials.Username)
	if err != nil && !errors.Is(err, sql.ErrNoRows) {
		http.Error(w, "database unavailable", 503)
		return
	}
	exists := err == nil
	if !exists {
		hash = string(a.dummyHash)
	}
	if bcrypt.CompareHashAndPassword([]byte(hash), []byte(credentials.Password)) != nil || !exists {
		http.Error(w, "invalid credentials", 401)
		return
	}
	csrf, err := randomToken()
	if err != nil {
		http.Error(w, "session creation failed", 500)
		return
	}
	session := Session{Username: credentials.Username, CSRF: csrf}
	token, err := a.Sessions.Create(r.Context(), session)
	if err != nil {
		http.Error(w, "session storage unavailable", 503)
		return
	}
	// Rotate and invalidate any existing session after successful authentication.
	if old, err := r.Cookie(cookieName); err == nil {
		_ = a.Sessions.Delete(r.Context(), old.Value)
	}
	http.SetCookie(w, &http.Cookie{Name: cookieName, Value: token, Path: "/admin", HttpOnly: true, Secure: a.SecureCookies, SameSite: http.SameSiteStrictMode, MaxAge: int(SessionTTL.Seconds())})
	writeJSON(w, session)
}
func (a *Admin) session(r *http.Request) (Session, error) {
	cookie, err := r.Cookie(cookieName)
	if err != nil {
		return Session{}, ErrNoSession
	}
	return a.Sessions.Get(r.Context(), cookie.Value)
}
func (a *Admin) requireSession(mutation bool, next http.HandlerFunc) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		session, err := a.session(r)
		if err != nil {
			if errors.Is(err, ErrNoSession) {
				http.Error(w, "login required", 401)
			} else {
				http.Error(w, "session storage unavailable", 503)
			}
			return
		}
		if mutation && (!sameOrigin(r) || session.CSRF == "" || subtle.ConstantTimeCompare([]byte(r.Header.Get("X-CSRF-Token")), []byte(session.CSRF)) != 1) {
			http.Error(w, "invalid CSRF token", 403)
			return
		}
		next(w, r)
	}
}
func (a *Admin) currentSession(w http.ResponseWriter, r *http.Request) {
	session, err := a.session(r)
	if err != nil {
		http.Error(w, "session storage unavailable", 503)
		return
	}
	writeJSON(w, session)
}
func (a *Admin) logout(w http.ResponseWriter, r *http.Request) {
	cookie, _ := r.Cookie(cookieName)
	if err := a.Sessions.Delete(r.Context(), cookie.Value); err != nil {
		http.Error(w, "session storage unavailable", 503)
		return
	}
	http.SetCookie(w, &http.Cookie{Name: cookieName, Value: "", Path: "/admin", HttpOnly: true, Secure: a.SecureCookies, SameSite: http.SameSiteStrictMode, MaxAge: -1})
	w.WriteHeader(204)
}
func (a *Admin) listRoutes(w http.ResponseWriter, r *http.Request) {
	routes, err := a.Store.ListRoutes(r.Context())
	if err != nil {
		http.Error(w, "database unavailable", 503)
		return
	}
	writeJSON(w, routes)
}
func (a *Admin) saveRoute(w http.ResponseWriter, r *http.Request) {
	var route Route
	if !decode(w, r, &route) {
		return
	}
	if err := ValidateRoute(route); err != nil {
		http.Error(w, err.Error(), 400)
		return
	}
	if err := a.Store.SaveRoute(r.Context(), route); err != nil {
		http.Error(w, "database unavailable", 503)
		return
	}
	writeJSON(w, route)
}
func (a *Admin) deleteRoute(w http.ResponseWriter, r *http.Request) {
	if err := a.Store.DeleteRoute(r.Context(), r.PathValue("version")); err != nil {
		http.Error(w, "database unavailable", 503)
		return
	}
	w.WriteHeader(204)
}
