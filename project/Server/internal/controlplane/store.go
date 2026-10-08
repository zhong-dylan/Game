package controlplane

import (
	"context"
	"database/sql"
	"fmt"
	"net/url"
	"regexp"
	"strings"
)

type Route struct {
	Version   string `json:"version"`
	ServerURL string `json:"server_url"`
	Enabled   bool   `json:"enabled"`
}

var versionPattern = regexp.MustCompile(`^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`)

func ValidateVersion(version string) error {
	if !versionPattern.MatchString(version) {
		return fmt.Errorf("版本号需为 1–64 个字母、数字、点、下划线或连字符")
	}
	return nil
}

func ValidateRoute(route Route) error {
	if err := ValidateVersion(route.Version); err != nil {
		return err
	}
	target, err := url.Parse(route.ServerURL)
	if err != nil || target.Hostname() == "" || (target.Scheme != "http" && target.Scheme != "https") || target.User != nil || target.RawQuery != "" || target.Fragment != "" || len(route.ServerURL) > 512 || strings.TrimSpace(route.ServerURL) != route.ServerURL {
		return fmt.Errorf("Game Server 地址需为有效的 HTTP/HTTPS URL，不包含账户、查询参数或片段")
	}
	return nil
}

type Store interface {
	PasswordHash(context.Context, string) (string, error)
	ListRoutes(context.Context) ([]Route, error)
	SaveRoute(context.Context, Route) error
	DeleteRoute(context.Context, string) error
	Resolve(context.Context, string) (string, error)
}

type SQLStore struct{ DB *sql.DB }

func (s *SQLStore) PasswordHash(ctx context.Context, username string) (string, error) {
	var hash string
	err := s.DB.QueryRowContext(ctx, "SELECT password_hash FROM admin_users WHERE username = ?", username).Scan(&hash)
	return hash, err
}
func (s *SQLStore) ListRoutes(ctx context.Context) ([]Route, error) {
	rows, err := s.DB.QueryContext(ctx, "SELECT version, server_url, enabled FROM game_routes ORDER BY version")
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	routes := []Route{}
	for rows.Next() {
		var route Route
		if err := rows.Scan(&route.Version, &route.ServerURL, &route.Enabled); err != nil {
			return nil, err
		}
		routes = append(routes, route)
	}
	return routes, rows.Err()
}
func (s *SQLStore) SaveRoute(ctx context.Context, route Route) error {
	if err := ValidateRoute(route); err != nil {
		return err
	}
	_, err := s.DB.ExecContext(ctx, "INSERT INTO game_routes (version, server_url, enabled) VALUES (?, ?, ?) ON DUPLICATE KEY UPDATE server_url = ?, enabled = ?", route.Version, route.ServerURL, route.Enabled, route.ServerURL, route.Enabled)
	return err
}
func (s *SQLStore) DeleteRoute(ctx context.Context, version string) error {
	_, err := s.DB.ExecContext(ctx, "DELETE FROM game_routes WHERE version = ?", version)
	return err
}
func (s *SQLStore) Resolve(ctx context.Context, version string) (string, error) {
	var address string
	err := s.DB.QueryRowContext(ctx, "SELECT server_url FROM game_routes WHERE version = ? AND enabled = TRUE", version).Scan(&address)
	return address, err
}
