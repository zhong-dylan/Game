package database

import (
	"context"
	"database/sql/driver"
	"github.com/DATA-DOG/go-sqlmock"
	"golang.org/x/crypto/bcrypt"
	"testing"
)

type passwordHash struct{ password string }

func (a passwordHash) Match(value driver.Value) bool {
	text, ok := value.(string)
	return ok && text != a.password && bcrypt.CompareHashAndPassword([]byte(text), []byte(a.password)) == nil
}
func TestInitializeWithoutResettingExistingAdminOrRoutes(t *testing.T) {
	db, mock, err := sqlmock.New()
	if err != nil {
		t.Fatal(err)
	}
	defer db.Close()
	for i := 0; i < 2; i++ {
		mock.ExpectExec("CREATE TABLE IF NOT EXISTS admin_users").WillReturnResult(sqlmock.NewResult(0, 0))
		mock.ExpectExec("CREATE TABLE IF NOT EXISTS game_routes").WillReturnResult(sqlmock.NewResult(0, 0))
		// Repeat initialization updates neither the password hash nor game route data.
		mock.ExpectExec("INSERT INTO admin_users .* ON DUPLICATE KEY UPDATE username = username").WithArgs("admin", passwordHash{"test-admin-password"}).WillReturnResult(sqlmock.NewResult(0, 0))
		if err := Initialize(context.Background(), db, "admin", "test-admin-password"); err != nil {
			t.Fatal(err)
		}
	}
	if err := mock.ExpectationsWereMet(); err != nil {
		t.Fatal(err)
	}
}
func TestRejectInvalidBootstrapCredentials(t *testing.T) {
	for _, values := range [][2]string{{"", "test-admin-password"}, {"admin", "short"}, {" admin", "test-admin-password"}} {
		if err := Initialize(context.Background(), nil, values[0], values[1]); err == nil {
			t.Fatal("invalid credentials accepted")
		}
	}
}
