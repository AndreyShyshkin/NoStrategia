import Redis from "ioredis";
import { FastTelemetryPacket } from "../src/types/telemetry";

export class RedisTelemetryConsumer {
  private redis: Redis | null = null;
  private isConnected: boolean = false;
  private streamName: string;
  private consumerGroup: string;
  private consumerName: string;
  private onTelemetryCallback: ((packet: FastTelemetryPacket) => void) | null = null;
  private isRunning: boolean = false;
  private lastKnownCarModel: string = "Waiting for Car...";
  private lastKnownTrack: string = "Waiting for Track...";
  private lastKnownSessionId: string = "Waiting for Session...";
  private latestPacket: FastTelemetryPacket | null = null;

  constructor(
    redisAddr: string = process.env.REDIS_ADDR || "127.0.0.1:6379",
    streamName: string = process.env.REDIS_STREAM_TELEMETRY || "stream:telemetry:raw",
    consumerGroup: string = process.env.REDIS_CONSUMER_GROUP || "cg:gateway"
  ) {
    this.streamName = streamName;
    this.consumerGroup = consumerGroup;
    this.consumerName = `gw-worker-${Math.random().toString(36).substring(2, 7)}`;

    const [host, portStr] = redisAddr.split(":");
    const port = parseInt(portStr || "6379", 10);

    try {
      this.redis = new Redis({
        host: host || "127.0.0.1",
        port: port || 6379,
        password: process.env.REDIS_PASSWORD || undefined,
        db: parseInt(process.env.REDIS_DB || "0", 10),
        lazyConnect: true,
        maxRetriesPerRequest: 1,
        retryStrategy: (times) => {
          if (times > 5) return 5000;
          return 2000;
        },
      });

      this.redis.on("connect", () => {
        this.isConnected = true;
        console.log(`[RedisConsumer] Connected to Redis at ${redisAddr}`);
        this.ensureConsumerGroup();
        this.fetchLatestKnownPacket();
      });

      this.redis.on("error", (err) => {
        this.isConnected = false;
        if (!err.message.includes("ECONNREFUSED")) {
          console.warn(`[RedisConsumer] Redis status: ${err.message}`);
        }
      });
    } catch (e) {
      this.redis = null;
    }
  }

  public async connect(): Promise<boolean> {
    if (!this.redis) return false;
    try {
      await this.redis.connect();
      return true;
    } catch {
      this.isConnected = false;
      return false;
    }
  }

  public get connected(): boolean {
    return this.isConnected;
  }

  public getMetadata(): { carModel: string; track: string; sessionId: string; latestPacket: FastTelemetryPacket | null } {
    return {
      carModel: this.lastKnownCarModel,
      track: this.lastKnownTrack,
      sessionId: this.lastKnownSessionId,
      latestPacket: this.latestPacket,
    };
  }

  private async ensureConsumerGroup() {
    if (!this.redis) return;
    try {
      await this.redis.xgroup("CREATE", this.streamName, this.consumerGroup, "$", "MKSTREAM");
      console.log(`[RedisConsumer] Group ${this.consumerGroup} ready on ${this.streamName}`);
    } catch (err: any) {
      if (err.message && err.message.includes("BUSYGROUP")) {
        // Group already exists, standard
      } else {
        console.warn(`[RedisConsumer] xgroup create notice: ${err.message}`);
      }
    }
  }

  private async fetchLatestKnownPacket() {
    if (!this.redis) return;
    try {
      const res = await this.redis.xrevrange(this.streamName, "+", "-", "COUNT", 1);
      if (res && res.length > 0) {
        const [, fields] = res[0] as [string, string[]];
        const packet = this.parseStreamFields(fields);
        if (packet) {
          this.latestPacket = packet;
          if (packet.carModel && packet.carModel !== "unknown") this.lastKnownCarModel = packet.carModel;
          if (packet.track && packet.track !== "unknown") this.lastKnownTrack = packet.track;
          if (packet.sessionId) this.lastKnownSessionId = packet.sessionId;
        }
      }
    } catch {
      // Stream may be empty on cold start
    }
  }

  public startListening(callback: (packet: FastTelemetryPacket) => void) {
    this.onTelemetryCallback = callback;
    this.isRunning = true;
    this.readLoop();
  }

  public stop() {
    this.isRunning = false;
    if (this.redis) {
      this.redis.disconnect();
    }
  }

  private async readLoop() {
    while (this.isRunning) {
      if (!this.isConnected || !this.redis) {
        await new Promise((res) => setTimeout(res, 500));
        continue;
      }

      try {
        // Read new messages with XREADGROUP (block up to 100ms for lowest latency)
        const response = await this.redis.xreadgroup(
          "GROUP",
          this.consumerGroup,
          this.consumerName,
          "COUNT",
          "50",
          "BLOCK",
          "100",
          "STREAMS",
          this.streamName,
          ">"
        );

        if (response && (response as any[]).length > 0) {
          const streamData = (response as any[])[0] as [string, any[]];
          const messages = streamData[1] as Array<[string, string[]]>;

          for (const msg of messages) {
            const [id, fields] = msg;
            const parsed = this.parseStreamFields(fields);
            if (parsed) {
              this.latestPacket = parsed;
              if (parsed.carModel && parsed.carModel !== "unknown") this.lastKnownCarModel = parsed.carModel;
              if (parsed.track && parsed.track !== "unknown") this.lastKnownTrack = parsed.track;
              if (parsed.sessionId) this.lastKnownSessionId = parsed.sessionId;

              if (this.onTelemetryCallback) {
                this.onTelemetryCallback(parsed);
              }
            }
            // Auto-acknowledge
            await this.redis.xack(this.streamName, this.consumerGroup, id);
          }
        }
      } catch (err: any) {
        if (!this.isRunning) break;
        await new Promise((res) => setTimeout(res, 200));
      }
    }
  }

  private parseStreamFields(fields: string[]): FastTelemetryPacket | null {
    try {
      const map: Record<string, string> = {};
      for (let i = 0; i < fields.length; i += 2) {
        map[fields[i]] = fields[i + 1];
      }

      // If payload was JSON stringified in field "data" or "payload"
      if (map["data"]) {
        const raw = JSON.parse(map["data"]);
        return this.normalizePacket(raw, map);
      }
      if (map["payload"]) {
        const raw = JSON.parse(map["payload"]);
        return this.normalizePacket(raw, map);
      }

      // Otherwise parse flattened fields
      return this.normalizePacket(map, map);
    } catch {
      return null;
    }
  }

  private normalizePacket(raw: any, fallbackMap: Record<string, string>): FastTelemetryPacket {
    const timestamp = raw.timestamp ? new Date(raw.timestamp).getTime() : Date.now();
    const packetId = raw.packetId ?? parseInt(fallbackMap["packetId"] || "0", 10);
    const speed = parseFloat((raw.speedKmh ?? raw.speed ?? fallbackMap["speed"] ?? 0).toFixed(1));
    const gas = parseFloat((raw.gas ?? fallbackMap["gas"] ?? 0).toFixed(3));
    const brake = parseFloat((raw.brake ?? fallbackMap["brake"] ?? 0).toFixed(3));
    const steer = parseFloat((raw.steerAngle ?? raw.steer ?? fallbackMap["steerAngle"] ?? 0).toFixed(3));
    const gear = raw.gear ?? parseInt(fallbackMap["gear"] || "1", 10);
    const rpm = Math.round(raw.rpm ?? parseInt(fallbackMap["rpm"] || "0", 10));
    const distNorm = parseFloat((raw.normalizedCarPosition ?? raw.distNorm ?? fallbackMap["distNorm"] ?? 0).toFixed(4));
    const currentSector = raw.currentSectorIndex ?? raw.currentSector ?? 0;
    const lap = raw.completedLaps ?? raw.lap ?? parseInt(fallbackMap["lap"] || "0", 10);

    const latG = parseFloat((raw.latG ?? (raw.accG ? raw.accG[0] : fallbackMap["latG"]) ?? 0).toFixed(2));
    const longG = parseFloat((raw.longG ?? (raw.accG ? raw.accG[1] : fallbackMap["longG"]) ?? 0).toFixed(2));
    const yawRate = parseFloat((raw.yawRate ?? (raw.localAngularVel ? raw.localAngularVel[0] : fallbackMap["yawRate"]) ?? 0).toFixed(3));
    const fuel = parseFloat((raw.fuel ?? fallbackMap["fuel"] ?? 50.0).toFixed(1));

    const status = raw.status !== undefined ? raw.status : parseInt(fallbackMap["status"] || "2", 10);
    const isInPitLane = raw.isInPitLane !== undefined ? raw.isInPitLane : parseInt(fallbackMap["isInPitLane"] || "0", 10);
    const isInPit = raw.isInPit !== undefined ? raw.isInPit : parseInt(fallbackMap["isInPit"] || "0", 10);

    const iCurrentTime = raw.iCurrentTime ?? 0;
    const iLastTime = raw.iLastTime ?? 0;
    const iBestTime = raw.iBestTime ?? 0;

    const wheelsPressure = raw.wheelsPressure ?? undefined;
    const tyreCoreTemp = raw.tyreCoreTemperature ?? undefined;
    const brakeTemp = raw.brakeTemp ?? undefined;

    return {
      type: "FAST_PACKET",
      timestamp,
      packetId,
      sessionId: raw.sessionId || fallbackMap["sessionId"] || "live-session",
      carModel: raw.carModel || fallbackMap["carModel"] || "mercedes_amg_gt3_evo",
      track: raw.track || fallbackMap["track"] || "spa",
      speed,
      gas,
      brake,
      steer,
      gear,
      rpm,
      distNorm,
      currentSector,
      lap,
      latG,
      longG,
      yawRate,
      fuel,
      status,
      isInPitLane,
      isInPit,
      iCurrentTime,
      iLastTime,
      iBestTime,
      wheelsPressure,
      tyreCoreTemp,
      brakeTemp,
      rawArray: [timestamp, packetId, distNorm, speed, gas, brake, steer, gear, rpm, yawRate, latG, longG],
    };
  }
}
