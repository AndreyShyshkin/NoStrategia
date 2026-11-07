import { FastTelemetryPacket, TacticalEvent, AudioPriority } from "../src/types/telemetry";

export class SpaTelemetrySimulator {
  private packetId: number = 0;
  private lap: number = 1;
  private distNorm: number = 0.0;
  private speed: number = 180.0;
  private gas: number = 1.0;
  private brake: number = 0.0;
  private steer: number = 0.0;
  private gear: number = 4;
  private rpm: number = 6500;
  private latG: number = 0.0;
  private longG: number = 0.2;
  private yawRate: number = 0.0;
  private fuel: number = 52.4;
  private sessionId: string = "sim-spa-gt3-session";
  private carModel: string = "mercedes_amg_gt3_evo";
  private track: string = "spa";
  private lastSector: number = 0;
  private lastEventTriggerLap: number = 0;

  // Track progress rate at 60 FPS: ~1 lap in 135 seconds (8100 ticks) -> step ~0.000123
  private readonly TICK_STEP: number = 0.000125;

  public tick(): { packet: FastTelemetryPacket; event?: TacticalEvent } {
    this.packetId++;
    this.distNorm += this.TICK_STEP;
    let event: TacticalEvent | undefined;

    if (this.distNorm >= 1.0) {
      this.distNorm = 0.0;
      this.lap++;
      this.fuel = Math.max(5.0, this.fuel - 2.85); // Realistic GT3 consumption ~2.85L/lap
      event = {
        id: `lap-${this.lap}-${Date.now()}`,
        type: "TACTICAL_EVENT",
        priority: AudioPriority.P4_STATUS,
        text: `Lap ${this.lap - 1} completed. Personal best delta: minus 0.18 seconds.`,
        category: "LAP",
        timestamp: Date.now(),
      };
    }

    // Determine current sector: 0 (La Source -> Eau Rouge -> Kemmel), 1 (Les Combes -> Pouhon -> Stavelot), 2 (Blanchimont -> Bus Stop)
    let currentSector = 0;
    if (this.distNorm > 0.32 && this.distNorm <= 0.70) {
      currentSector = 1;
    } else if (this.distNorm > 0.70) {
      currentSector = 2;
    }

    if (currentSector !== this.lastSector) {
      const prev = this.lastSector;
      this.lastSector = currentSector;
      if (!event) {
        event = {
          id: `sec-${currentSector}-${Date.now()}`,
          type: "TACTICAL_EVENT",
          priority: AudioPriority.P4_STATUS,
          text: `Sector ${prev + 1} green. Delta minus zero point one.`,
          category: "SECTOR",
          timestamp: Date.now(),
        };
      }
    }

    // Physics curve based on Spa track zones
    const p = this.distNorm;

    if (p < 0.05) {
      // Zone 1: La Source turn 1 braking & exit
      if (p < 0.02) {
        // Heavy braking zone
        this.gas = 0.0;
        this.brake = 0.95 + Math.sin(p * 200) * 0.04;
        this.gear = 1;
        this.speed = Math.max(55, this.speed - 3.2);
        this.rpm = Math.max(4800, 4000 + (this.speed / 70) * 2500);
        this.steer = 0.15;
        this.latG = 0.6;
        this.longG = -1.8;

        // Occasional lockup event around p = 0.015
        if (p > 0.013 && p < 0.016 && this.lap !== this.lastEventTriggerLap) {
          this.lastEventTriggerLap = this.lap;
          event = {
            id: `lockup-${this.packetId}`,
            type: "TACTICAL_EVENT",
            priority: AudioPriority.P1_CRITICAL,
            text: "Lockup front right into turn one! Modulate braking.",
            category: "LOCKUP",
            timestamp: Date.now(),
          };
        }
      } else {
        // Turn 1 Apex and acceleration
        this.brake = 0.0;
        this.gas = Math.min(1.0, this.gas + 0.04);
        this.gear = 2;
        this.speed = Math.min(130, this.speed + 1.2);
        this.rpm = 5200 + (this.gas * 2000);
        this.steer = 0.25 * (1 - (p - 0.02) / 0.03);
        this.latG = 1.2;
        this.longG = 0.9;
      }
    } else if (p < 0.18) {
      // Zone 2: Downhill run into Eau Rouge & Raidillon
      this.brake = 0.0;
      this.gas = 1.0;
      if (p < 0.10) {
        this.gear = 3;
        this.speed = Math.min(185, this.speed + 1.1);
        this.steer = 0.0;
        this.latG = 0.1;
      } else if (p < 0.14) {
        // Eau Rouge compression (left-right flick)
        this.gear = 4;
        this.speed = Math.min(225, this.speed + 0.8);
        this.steer = Math.sin((p - 0.10) * 80) * 0.25;
        this.latG = Math.sin((p - 0.10) * 80) * 2.2; // massive G-force
      } else {
        // Raidillon crest
        this.gear = 5;
        this.speed = Math.min(245, this.speed + 0.6);
        this.steer = 0.02;
        this.latG = 0.4;
      }
      this.rpm = 6400 + Math.sin(this.packetId * 0.2) * 200;
      this.longG = 0.6;
    } else if (p < 0.30) {
      // Zone 3: Kemmel Straight (Top speed run)
      this.gas = 1.0;
      this.brake = 0.0;
      this.steer = 0.0;
      this.gear = 6;
      this.speed = Math.min(268, this.speed + 0.4);
      this.rpm = 6800 + ((this.speed - 245) / 25) * 850;
      this.latG = 0.02;
      this.longG = 0.3;

      // Strategy callout on Kemmel straight at lap 3
      if (this.lap === 3 && p > 0.22 && p < 0.23 && !event) {
        event = {
          id: `strat-pit-${this.packetId}`,
          type: "TACTICAL_EVENT",
          priority: AudioPriority.P2_STRATEGY,
          text: "Box window opens in two laps. Target fuel consumption two point eight liters.",
          category: "STRATEGY",
          timestamp: Date.now(),
        };
      }
    } else if (p < 0.38) {
      // Zone 4: Les Combes Chicane (Heavy braking 265 -> 115 km/h)
      if (p < 0.33) {
        this.gas = 0.0;
        this.brake = 0.98;
        this.gear = 3;
        this.speed = Math.max(115, this.speed - 3.8);
        this.rpm = 5500;
        this.steer = 0.05;
        this.latG = 0.2;
        this.longG = -1.95;
      } else {
        // Chicane flick right then left
        this.brake = 0.0;
        this.gas = 0.65;
        this.gear = 3;
        this.speed = 120 + Math.sin((p - 0.33) * 60) * 15;
        this.steer = Math.sin((p - 0.33) * 90) * 0.45;
        this.latG = this.steer * 3.5;
        this.rpm = 5800;
        this.longG = 0.4;
      }
    } else if (p < 0.46) {
      // Zone 5: Bruxelles Hairpin (Downhill carousel)
      this.brake = p < 0.40 ? 0.45 : 0.0;
      this.gas = p < 0.40 ? 0.0 : 0.55;
      this.gear = 2;
      this.speed = 95 + Math.sin(p * 50) * 10;
      this.steer = 0.55; // sustained right steering
      this.latG = 1.75;
      this.rpm = 5400;
      this.longG = -0.1;
    } else if (p < 0.54) {
      // Zone 6: Pouhon (High speed double-left)
      this.brake = (p > 0.48 && p < 0.50) ? 0.35 : 0.0;
      this.gas = (p > 0.48 && p < 0.50) ? 0.2 : 0.90;
      this.gear = 4;
      this.speed = 185 + Math.sin(p * 40) * 12;
      this.steer = -0.42; // hard left
      this.latG = -2.1;
      this.rpm = 6900;
      this.longG = 0.3;

      // Telemetry scrub warning
      if (p > 0.51 && p < 0.515 && this.lap % 2 === 0 && !event) {
        event = {
          id: `tel-scrub-${this.packetId}`,
          type: "TACTICAL_EVENT",
          priority: AudioPriority.P3_TELEMETRY,
          text: "Front tyre scrub detected in Pouhon. Ease steering angle.",
          category: "TYRE_TEMP",
          timestamp: Date.now(),
        };
      }
    } else if (p < 0.85) {
      // Zone 7: Stavelot, Campus and Blanchimont (Long full throttle sweep)
      this.brake = 0.0;
      this.gas = 1.0;
      this.gear = p < 0.72 ? 5 : 6;
      this.speed = Math.min(252, this.speed + 0.6);
      this.steer = Math.sin(p * 30) * 0.08;
      this.latG = this.steer * 5.0;
      this.rpm = 6800 + ((this.speed - 220) / 32) * 700;
      this.longG = 0.45;
    } else if (p < 0.94) {
      // Zone 8: Bus Stop Chicane (Hard deceleration 250 -> 65 km/h)
      if (p < 0.90) {
        this.gas = 0.0;
        this.brake = 0.97;
        this.gear = 1;
        this.speed = Math.max(65, this.speed - 4.1);
        this.steer = 0.02;
        this.latG = 0.1;
        this.longG = -2.1;
        this.rpm = 5100;
      } else {
        // Tight right-left curb jump
        this.brake = 0.0;
        this.gas = 0.45;
        this.gear = 2;
        this.speed = 75 + (p - 0.90) * 400;
        this.steer = Math.sin((p - 0.90) * 120) * 0.6;
        this.latG = this.steer * 2.8;
        this.rpm = 5700;
        this.longG = 0.7;
      }
    } else {
      // Zone 9: Acceleration out of Bus Stop onto Main Straight
      this.brake = 0.0;
      this.gas = 1.0;
      this.gear = 3;
      this.speed = Math.min(195, this.speed + 1.5);
      this.steer = 0.0;
      this.latG = 0.05;
      this.rpm = 6200 + (this.speed / 200) * 1200;
      this.longG = 0.85;
    }

    this.yawRate = (this.latG / 9.81) * 0.18;

    const now = Date.now();
    const packet: FastTelemetryPacket = {
      type: "FAST_PACKET",
      timestamp: now,
      packetId: this.packetId,
      sessionId: this.sessionId,
      carModel: this.carModel,
      track: this.track,
      speed: parseFloat(this.speed.toFixed(1)),
      gas: parseFloat(this.gas.toFixed(3)),
      brake: parseFloat(this.brake.toFixed(3)),
      steer: parseFloat(this.steer.toFixed(3)),
      gear: this.gear,
      rpm: Math.round(this.rpm),
      distNorm: parseFloat(this.distNorm.toFixed(4)),
      currentSector,
      lap: this.lap,
      latG: parseFloat(this.latG.toFixed(2)),
      longG: parseFloat(this.longG.toFixed(2)),
      yawRate: parseFloat(this.yawRate.toFixed(3)),
      fuel: parseFloat(this.fuel.toFixed(1)),
      tyreCoreTemp: [88.5, 91.2, 85.0, 87.4],
      brakeTemp: [450, 480, 390, 410],
      rawArray: [
        now,
        this.packetId,
        parseFloat(this.distNorm.toFixed(4)),
        parseFloat(this.speed.toFixed(1)),
        parseFloat(this.gas.toFixed(3)),
        parseFloat(this.brake.toFixed(3)),
        parseFloat(this.steer.toFixed(3)),
        this.gear,
        Math.round(this.rpm),
        parseFloat(this.yawRate.toFixed(3)),
        parseFloat(this.latG.toFixed(2)),
        parseFloat(this.longG.toFixed(2)),
      ],
    };

    return { packet, event };
  }
}
