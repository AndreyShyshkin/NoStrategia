package models

import (
	"encoding/json"
	"time"
)

// SetupPayload represents vehicle setup revisions captured by TelemetryAgent FileSystemWatcher.
type SetupPayload struct {
	SessionID string          `json:"sessionId,omitempty"`
	FileName  string          `json:"fileName"`
	CarModel  string          `json:"carModel"`
	Track     string          `json:"track"`
	Hash      string          `json:"hash"`
	Timestamp time.Time       `json:"timestamp"`
	SetupData json.RawMessage `json:"setupData"`
}

func (s *SetupPayload) Normalize() {
	if s.Timestamp.IsZero() {
		s.Timestamp = time.Now().UTC()
	}
	if s.SessionID == "" {
		s.SessionID = "4a73b2c1-840e-436f-bca8-e9f3b259d640"
	}
}
