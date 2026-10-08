package database

import (
	"context"
	"database/sql"
	_ "embed"
	"errors"
	"golang.org/x/crypto/bcrypt"
	"strings"
)

//go:embed schema.sql
var schema string

// Initialize is repeatable: it never overwrites existing accounts or version routes.
func Initialize(ctx context.Context, db *sql.DB, username, password string) error {
	if strings.TrimSpace(username) != username || username == "" || len(username) > 64 {
		return errors.New("ADMIN_USERNAME must contain 1–64 bytes without surrounding whitespace")
	}
	if len(password) < 12 || len(password) > 72 {
		return errors.New("ADMIN_PASSWORD must contain 12–72 bytes")
	}
	for _, statement := range strings.Split(schema, ";") {
		if strings.TrimSpace(statement) == "" {
			continue
		}
		if _, err := db.ExecContext(ctx, statement); err != nil {
			return err
		}
	}
	hash, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	if err != nil {
		return err
	}
	_, err = db.ExecContext(ctx, "INSERT INTO admin_users (username, password_hash) VALUES (?, ?) ON DUPLICATE KEY UPDATE username = username", username, string(hash))
	return err
}
