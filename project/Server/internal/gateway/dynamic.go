package gateway

import (
	"context"
	"database/sql"
	"errors"
	"game/server/internal/controlplane"
	"net/http"
	"net/http/httputil"
	"net/url"
	"time"
)

type RouteResolver interface {
	Resolve(context.Context, string) (string, error)
}

// Each request reads the authoritative route. Removal/disable is effective for
// every subsequent request, across all gateways, with no stale fallback.
func NewDynamicHandler(resolver RouteResolver) http.Handler {
	transport := http.DefaultTransport.(*http.Transport).Clone()
	transport.ResponseHeaderTimeout = 15 * time.Second
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		versions := r.Header.Values(VersionHeader)
		if len(versions) != 1 || versions[0] == "" {
			http.Error(w, "exactly one X-Game-Version header is required", 400)
			return
		}
		version := versions[0]
		if err := controlplane.ValidateVersion(version); err != nil {
			http.Error(w, "invalid game version", 400)
			return
		}
		ctx, cancel := context.WithTimeout(r.Context(), 2*time.Second)
		address, err := resolver.Resolve(ctx, version)
		cancel()
		if errors.Is(err, sql.ErrNoRows) {
			http.Error(w, "game version not configured or disabled", http.StatusUpgradeRequired)
			return
		}
		if err != nil {
			http.Error(w, "route storage unavailable", 503)
			return
		}
		if err := controlplane.ValidateRoute(controlplane.Route{Version: version, ServerURL: address}); err != nil {
			http.Error(w, "invalid route configuration", 503)
			return
		}
		target, _ := url.Parse(address)
		proxy := &httputil.ReverseProxy{
			Transport: transport,
			Rewrite: func(request *httputil.ProxyRequest) {
				request.SetURL(target)
				request.SetXForwarded()
				request.Out.Header.Set(VersionHeader, version)
			},
			ErrorHandler: func(w http.ResponseWriter, r *http.Request, err error) { http.Error(w, "game server unavailable", 502) },
		}
		proxy.ServeHTTP(w, r)
	})
}
