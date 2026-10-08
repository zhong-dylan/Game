package gateway

import (
	"context"
	"database/sql"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
)

type testResolver struct {
	mu     sync.RWMutex
	routes map[string]string
	err    error
}

func (s *testResolver) Resolve(ctx context.Context, version string) (string, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	if s.err != nil {
		return "", s.err
	}
	address, ok := s.routes[version]
	if !ok {
		return "", sql.ErrNoRows
	}
	return address, nil
}
func (s *testResolver) set(version, address string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if address == "" {
		delete(s.routes, version)
	} else {
		s.routes[version] = address
	}
}
func request(handler http.Handler, version string) *httptest.ResponseRecorder {
	r := httptest.NewRequest("POST", "/api/game/login?user=7", strings.NewReader("payload"))
	if version != "" {
		r.Header.Set(VersionHeader, version)
	}
	r.Header.Set("Connection", VersionHeader)
	w := httptest.NewRecorder()
	handler.ServeHTTP(w, r)
	return w
}
func TestVersionRoutingAndImmediateChanges(t *testing.T) {
	store := &testResolver{routes: map[string]string{}}
	handler := NewDynamicHandler(store)
	for _, version := range []string{"1.0", "2.0"} {
		version := version
		backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			if r.Header.Get(VersionHeader) != version {
				t.Errorf("version = %q", r.Header.Get(VersionHeader))
			}
			if r.URL.RequestURI() != "/api/game/login?user=7" || r.Method != "POST" {
				t.Errorf("request = %s %s", r.Method, r.URL.RequestURI())
			}
			body, _ := io.ReadAll(r.Body)
			if string(body) != "payload" {
				t.Errorf("body = %s", body)
			}
			w.Write([]byte(version))
		}))
		defer backend.Close()
		if w := request(handler, version); w.Code != 426 {
			t.Fatalf("unconfigured version: %d", w.Code)
		}
		store.set(version, backend.URL)
		if w := request(handler, version); w.Code != 200 || w.Body.String() != version {
			t.Fatalf("configured version: %d %s", w.Code, w.Body.String())
		}
		store.set(version, "")
		if w := request(handler, version); w.Code != 426 {
			t.Fatalf("removed version still accessible: %d", w.Code)
		}
	}
}
func TestRejectedVersionsAndStorageFailure(t *testing.T) {
	store := &testResolver{routes: map[string]string{}}
	handler := NewDynamicHandler(store)
	if w := request(handler, ""); w.Code != 400 {
		t.Fatal(w.Code)
	}
	if w := request(handler, "unknown"); w.Code != 426 {
		t.Fatal(w.Code)
	}
	r := httptest.NewRequest("POST", "/api/game/login", nil)
	r.Header.Add(VersionHeader, "1.0")
	r.Header.Add(VersionHeader, "2.0")
	w := httptest.NewRecorder()
	handler.ServeHTTP(w, r)
	if w.Code != 400 {
		t.Fatal(w.Code)
	}
	store.err = errors.New("database offline")
	if w := request(handler, "1.0"); w.Code != 503 {
		t.Fatal(w.Code)
	}
}
func TestChangedBackendAndUnavailableBackend(t *testing.T) {
	first := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.Write([]byte("first")) }))
	defer first.Close()
	second := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.Write([]byte("second")) }))
	defer second.Close()
	store := &testResolver{routes: map[string]string{"1.0": first.URL}}
	handler := NewDynamicHandler(store)
	if w := request(handler, "1.0"); w.Body.String() != "first" {
		t.Fatal(w.Body.String())
	}
	store.set("1.0", second.URL)
	if w := request(handler, "1.0"); w.Body.String() != "second" {
		t.Fatal(w.Body.String())
	}
	second.Close()
	if w := request(handler, "1.0"); w.Code != 502 {
		t.Fatal(w.Code)
	}
	store.set("1.0", "file:///tmp/server")
	if w := request(handler, "1.0"); w.Code != 503 {
		t.Fatal(w.Code)
	}
}
