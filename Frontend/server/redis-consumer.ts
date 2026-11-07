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
          if (times > 3) return null; // stop reconnect spam if Redis is not running
          return 2000;
        },
      });

      this.redis.on("connect", () => {
        this.isConnected = true;
        console.log(`[RedisConsumer] Connected to Redis at ${redisAddr}`);
        this.ensureConsumerGroup();
      });

      this.redis.on("error", (err) => {
        this.isConnected = false;
        // Keep quiet on connection refusal in development
        if (err.message.includes("ECONNREFUSED")) {
          // Expected when Redis container is not running
        } else {
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

  private async ensureConsumerGroup() {
    if (!this.redis) return;
    try {
      // MKSTREAM creates the stream if not existing yet
      await this.redis.xgroup("CREATE", this.streamName, this.consumerGroup, "$", "MKSTREAM");
      console.log(`[RedisConsumer] Created consumer group ${this.consumerGroup} on ${this.streamName}`);
    } catch (err: any) {
      if (err.message && err.message.includes("BUSYGROUP")) {
        // Group already exists, which is normal
      } else {
        console.warn(`[RedisConsumer] xgroup create notice: ${err.message}`);
      }
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
        await new Promise((res) => setTimeout(res, 1000));
        continue;
      }

      try {
        // Read new messages with XREADGROUP (block up to 250ms)
        const response = await this.redis.xreadgroup(
          "GROUP",
          this.consumerGroup,
          this.consumerName,
          "COUNT",
          "10",
          "BLOCK",
          "250",
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
            if (parsed && this.onTelemetryCallback) {
              this.onTelemetryCallback(parsed);
            }
            // Auto-acknowledge
            await this.redis.xack(this.streamName, this.consumerGroup, id);
          }
        }
      } catch (err: any) {
        if (!this.isRunning) break;
        await new Promise((res) => setTimeout(res, 500));
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
      if (map["payload"]) {
        const raw = JSON.parse(map["payload"]);
        return this.normalizePacket(raw);
      }
      if (map["data"]) {
        const raw = JSON.parse(map["data"]);
        return this.normalizePacket(raw);
      }

      // Otherwise parse flattened Redis stream fields
      return {
        type: "FAST_PACKET",
        timestamp: parseInt(map["timestamp"] || `${Date.now()}`, 10),
        packetId: parseInt(map["packetId"] || "0", 10),
        sessionId: map["sessionId"] || "default-session",
        carModel: map["carModel"] || "mercedes_amg_gt3_evo",
        track: map["track"] || "spa",
        speed: parseFloat(map["speedKmh"] || map["speed"] || "0"),
        gas: parseFloat(map["gas"] || "0"),
        brake: parseFloat(map["brake"] || "0"),
        steer: parseFloat(map["steerAngle"] || map["steer"] || "0"),
        gear: parseInt(map["gear"] || "0", 10),
        rpm: parseInt(map["rpm"] || "0", 10),
        distNorm: parseFloat(map["distNorm"] || map["normalizedCarPosition"] || "0"),
        currentSector: parseInt(map["currentSectorIndex"] || "0", 10),
        lap: parseInt(map["completedLaps"] || map["lap"] || "1", 10),
        latG: parseFloat(map["latG"] || "0"),
        longG: parseFloat(map["longG"] || "0"),
        yawRate: parseFloat(map["yawRate"] || "0"),
        fuel: parseFloat(map["fuel"] || "50"),
      };
    } catch {
      return null;
    }
  }

  private normalizePacket(raw: any): FastTelemetryPacket {
    return {
      type: "FAST_PACKET",
      timestamp: raw.timestamp ? new Date(raw.timestamp).getTime() : Date.now(),
      packetId: raw.packetId || 0,
      sessionId: raw.sessionId || "live-session",
      carModel: raw.carModel || "mercedes_amg_gt3_evo",
      track: raw.track || "spa",
      speed: raw.speedKmh ?? raw.speed ?? 0,
      gas: raw.gas ?? 0,
      brake: raw.brake ?? 0,
      steer: raw.steerAngle ?? raw.steer ?? 0,
      gear: raw.gear ?? 0,
      rpm: raw.rpm ?? 0,
      distNorm: raw.normalizedCarPosition ?? raw.distNorm ?? 0,
      currentSector: raw.currentSectorIndex ?? raw.currentSector ?? 0,
      lap: raw.completedLaps ?? raw.lap ?? 1,
      latG: raw.latG ?? (raw.accG ? raw.accG[0] : 0),
      longG: raw.longG ?? (raw.accG ? raw.accG[2] : 0),
      yawRate: raw.yawRate ?? (raw.localAngularVel ? raw.localAngularVel[1] : 0),
      fuel: raw.fuel ?? 50,
    };
  }
}
