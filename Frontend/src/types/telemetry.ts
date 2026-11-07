/**
 * NoStrategia Telemetry Types & Protocol Definitions
 */

export enum AudioPriority {
  P1_CRITICAL = 1,
  P2_STRATEGY = 2,
  P3_TELEMETRY = 3,
  P4_STATUS = 4,
}

export interface TacticalEvent {
  id: string;
  type: "TACTICAL_EVENT";
  priority: AudioPriority;
  text: string;
  category: "LOCKUP" | "SPIN" | "STRATEGY" | "TYRE_TEMP" | "SECTOR" | "PACE" | "LAP" | "GENERAL";
  timestamp: number;
}

export interface FastTelemetryPacket {
  type: "FAST_PACKET";
  timestamp: number;
  packetId: number;
  sessionId: string;
  carModel: string;
  track: string;
  speed: number;        // km/h (0..350)
  gas: number;          // 0.0 .. 1.0
  brake: number;        // 0.0 .. 1.0
  clutch?: number;      // 0.0 .. 1.0
  steer: number;        // -1.0 .. 1.0 or angle degrees
  gear: number;         // -1 (R), 0 (N), 1..6
  rpm: number;          // 0..9000
  distNorm: number;     // 0.0 .. 1.0 lap progress
  currentSector: number;// 0, 1, 2
  lap: number;          // completed laps
  latG: number;         // lateral G-force
  longG: number;        // longitudinal G-force
  yawRate: number;      // rad/s
  fuel?: number;        // liters
  tyreCoreTemp?: [number, number, number, number]; // FL, FR, RL, RR
  brakeTemp?: [number, number, number, number];    // FL, FR, RL, RR
  
  // Compact array representation:
  // [timestamp, packetId, distNorm, speed, gas, brake, steer, gear, rpm, yawRate, latG, longG]
  rawArray?: [number, number, number, number, number, number, number, number, number, number, number, number];
}

export type WebSocketClientMessage =
  | { action: "subscribe"; sessionId?: string; channels: string[] }
  | { action: "unsubscribe"; channels: string[] }
  | { action: "ping"; timestamp?: number }
  | { action: "trigger_event"; priority: AudioPriority; text: string; category?: string };

export type WebSocketServerMessage =
  | FastTelemetryPacket
  | TacticalEvent
  | { type: "PONG"; timestamp: number; serverTime: number }
  | { type: "SUBSCRIPTION_ACK"; channels: string[]; sessionId: string }
  | { type: "GATEWAY_STATUS"; clientsCount: number; tickRate: number; source: "redis" | "simulator"; uptime: number };
