package models

import "time"

// TelemetryPayload represents the high-frequency telemetry frame received from TelemetryAgent.
type TelemetryPayload struct {
	SessionID             string      `json:"sessionId,omitempty"`
	Timestamp             time.Time   `json:"timestamp"`
	PacketID              int64       `json:"packetId"`
	Status                int         `json:"status"`
	SessionType           int         `json:"sessionType"`
	CarModel              string      `json:"carModel"`
	Track                 string      `json:"track"`
	SpeedKmh              float32     `json:"speedKmh"`
	Gear                  int         `json:"gear"`
	Rpm                   int         `json:"rpm"`
	SteerAngle            float32     `json:"steerAngle"`
	Gas                   float32     `json:"gas"`
	Brake                 float32     `json:"brake"`
	Clutch                float32     `json:"clutch"`
	Fuel                  float32     `json:"fuel"`
	ICurrentTime          int         `json:"iCurrentTime"`
	ILastTime             int         `json:"iLastTime"`
	IBestTime             int         `json:"iBestTime"`
	CompletedLaps         int         `json:"completedLaps"`
	NormalizedCarPosition float32     `json:"normalizedCarPosition"`
	CurrentSectorIndex    int         `json:"currentSectorIndex"`
	IsInPitLane           int         `json:"isInPitLane"`
	IsInPit               int         `json:"isInPit"`
	TC                    float32     `json:"tc"`
	Abs                   float32     `json:"abs"`
	EngineMap             int         `json:"engineMap"`
	Velocity              []float32   `json:"velocity,omitempty"`
	AccG                  []float32   `json:"accG,omitempty"`
	LocalAngularVel       []float32   `json:"localAngularVel,omitempty"`
	WheelSlip             []float32   `json:"wheelSlip,omitempty"`
	WheelsPressure        []float32   `json:"wheelsPressure,omitempty"`
	WheelAngularSpeed     []float32   `json:"wheelAngularSpeed,omitempty"`
	TyreCoreTemperature   []float32   `json:"tyreCoreTemperature,omitempty"`
	BrakeTemp             []float32   `json:"brakeTemp,omitempty"`
	SuspensionTravel      []float32   `json:"suspensionTravel,omitempty"`
	SlipRatio             []float32   `json:"slipRatio,omitempty"`
	SlipAngle             []float32   `json:"slipAngle,omitempty"`
	TyreCompound          string      `json:"tyreCompound,omitempty"`

	// Compatibility aliases from architecture documentation
	DistNorm float32 `json:"distNorm,omitempty"`
	Speed    float32 `json:"speed,omitempty"`
	YawRate  float32 `json:"yawRate,omitempty"`
	LatG     float32 `json:"latG,omitempty"`
	LongG    float32 `json:"longG,omitempty"`
}

// Normalize resolves field aliases so downstream consumers receive consistent attributes.
func (p *TelemetryPayload) Normalize() {
	if p.Timestamp.IsZero() {
		p.Timestamp = time.Now().UTC()
	}
	if p.NormalizedCarPosition == 0 && p.DistNorm != 0 {
		p.NormalizedCarPosition = p.DistNorm
	}
	if p.SpeedKmh == 0 && p.Speed != 0 {
		p.SpeedKmh = p.Speed
	}
	if p.SessionID == "" {
		p.SessionID = "4a73b2c1-840e-436f-bca8-e9f3b259d640" // Default benchmark session
	}
	// Extract lateral/longitudinal G if present in AccG array
	if len(p.AccG) >= 2 {
		if p.LatG == 0 {
			p.LatG = p.AccG[0]
		}
		if p.LongG == 0 {
			p.LongG = p.AccG[1]
		}
	}
	// Extract yaw rate if present in LocalAngularVel
	if len(p.LocalAngularVel) >= 3 && p.YawRate == 0 {
		p.YawRate = p.LocalAngularVel[1]
	}
}
