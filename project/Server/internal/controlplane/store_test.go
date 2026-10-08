package controlplane

import (
	"context"
	"database/sql"
	"errors"
	"github.com/DATA-DOG/go-sqlmock"
	"testing"
)

func TestResolveOnlyEnabledConfiguredVersions(t *testing.T) {
	db, mock, err := sqlmock.New()
	if err != nil {
		t.Fatal(err)
	}
	defer db.Close()
	store := &SQLStore{DB: db}
	query := "SELECT server_url FROM game_routes WHERE version = \\? AND enabled = TRUE"
	mock.ExpectQuery(query).WithArgs("1.0").WillReturnRows(sqlmock.NewRows([]string{"server_url"}).AddRow("http://game-1.0:8081"))
	address, err := store.Resolve(context.Background(), "1.0")
	if err != nil || address != "http://game-1.0:8081" {
		t.Fatal(address, err)
	}
	mock.ExpectQuery(query).WithArgs("2.0").WillReturnRows(sqlmock.NewRows([]string{"server_url"}))
	if _, err := store.Resolve(context.Background(), "2.0"); !errors.Is(err, sql.ErrNoRows) {
		t.Fatal(err)
	}
	if err := mock.ExpectationsWereMet(); err != nil {
		t.Fatal(err)
	}
}
func TestValidateRoutes(t *testing.T) {
	for _, url := range []string{"file:///tmp/game", "http://", "http://user:password@host", "http://host?x=1", "http://host#x", " http://host"} {
		if err := ValidateRoute(Route{Version: "1.0", ServerURL: url}); err == nil {
			t.Fatal("invalid address accepted", url)
		}
	}
	for _, version := range []string{"", " 1.0", "1.0/evil", "1.0;DROP"} {
		if err := ValidateRoute(Route{Version: version, ServerURL: "http://game-1.0:8081"}); err == nil {
			t.Fatal("invalid version accepted", version)
		}
	}
	if err := ValidateRoute(Route{Version: "1.0", ServerURL: "http://game-1.0:8081"}); err != nil {
		t.Fatal(err)
	}
}
