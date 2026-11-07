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
  | { action: "trigger_event"; priority: AudioPriority; text: string; category?: string }
  | { action: "set_simulation"; enabled: boolean };

export interface GatewayStatusMessage {
  type: "GATEWAY_STATUS";
  clientsCount: number;
  tickRate: number;
  source: "agent" | "waiting_for_agent" | "simulator";
  isLive: boolean;
  agentStatus: "ON_TRACK" | "WAITING_FOR_SESSION" | "DISCONNECTED";
  lastPacketTime: number;
  carModel: string;
  track: string;
  simulationEnabled: boolean;
  totalPacketsSent: number;
  uptime: number;
}

export type WebSocketServerMessage =
  | FastTelemetryPacket
  | TacticalEvent
  | { type: "PONG"; timestamp: number; serverTime: number }
  | { type: "SUBSCRIPTION_ACK"; channels: string[]; sessionId: string }
  | GatewayStatusMessage;

/**
 * Helper to format raw ACC car model identifiers to human-readable names
 */
export function formatCarModel(raw?: string): string {
  if (!raw || raw === "Waiting for Car..." || raw === "unknown") return "Waiting for Car...";
  const key = raw.toLowerCase().replace(/[- ]/g, "_");
  const known: Record<string, string> = {
    amg_gt3_evo: "Mercedes-AMG GT3 Evo",
    mercedes_amg_gt3_evo: "Mercedes-AMG GT3 Evo",
    ferrari_296_gt3: "Ferrari 296 GT3",
    ferrari_488_gt3_evo: "Ferrari 488 GT3 Evo",
    porsche_992_gt3_r: "Porsche 992 GT3 R",
    porsche_991ii_gt3_r: "Porsche 991 II GT3 R",
    bmw_m4_gt3: "BMW M4 GT3",
    audi_r8_lms_evo_ii: "Audi R8 LMS Evo II",
    mclaren_720s_gt3_evo: "McLaren 720S GT3 Evo",
    lamborghini_huracan_gt3_evo2: "Lamborghini Huracán GT3 Evo 2",
    aston_martin_v8_vantage_gt3: "Aston Martin Vantage V8 GT3",
  };
  return known[key] || raw.replace(/_/g, " ").toUpperCase();
}

/**
 * Helper to format raw ACC track identifiers to human-readable circuit names
 */
export function formatTrackName(raw?: string): string {
  if (!raw || raw === "Waiting for Track..." || raw === "unknown") return "Waiting for Track...";
  const key = raw.toLowerCase().replace(/[- ]/g, "_");
  const known: Record<string, string> = {
    spa: "Circuit de Spa-Francorchamps (7.004 km)",
    monza: "Autodromo Nazionale Monza (5.793 km)",
    silverstone: "Silverstone Circuit (5.891 km)",
    nurburgring: "Nürburgring GP (5.148 km)",
    kyalami: "Kyalami Grand Prix Circuit (4.529 km)",
    mount_panorama: "Mount Panorama Circuit (6.213 km)",
    bathurst: "Mount Panorama Circuit (6.213 km)",
    barcelona: "Circuit de Barcelona-Catalunya (4.675 km)",
    brands_hatch: "Brands Hatch (3.908 km)",
    hungaroring: "Hungaroring (4.381 km)",
    imola: "Autodromo Enzo e Dino Ferrari (4.909 km)",
    misano: "Misano World Circuit Marco Simoncelli (4.226 km)",
    paul_ricard: "Circuit Paul Ricard (5.842 km)",
    zandvoort: "Circuit Zandvoort (4.259 km)",
    zolder: "Circuit Zolder (4.000 km)",
    red_bull_ring: "Red Bull Ring (4.318 km)",
  };
  return known[key] || raw.replace(/_/g, " ").toUpperCase();
}
