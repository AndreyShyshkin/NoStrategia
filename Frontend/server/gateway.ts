import http from "http";
import { WebSocketServer, WebSocket } from "ws";
import { SpaTelemetrySimulator } from "./simulator";
import { RedisTelemetryConsumer } from "./redis-consumer";
import {
  FastTelemetryPacket,
  TacticalEvent,
  WebSocketClientMessage,
  WebSocketServerMessage,
  GatewayStatusMessage,
  AudioPriority,
} from "../src/types/telemetry";

const PORT = parseInt(process.env.WS_PORT || "4001", 10);
const HOST = process.env.WS_HOST || "0.0.0.0";
const TICK_RATE_HZ = parseInt(process.env.SIMULATION_TICK_RATE_HZ || "60", 10);
const TICK_INTERVAL_MS = Math.floor(1000 / TICK_RATE_HZ);

interface ClientSubscription {
  ws: WebSocket;
  sessionId: string;
  channels: Set<string>;
  ip: string;
  connectedAt: number;
}

export class TelemetryWebSocketGateway {
  private server: http.Server;
  private wss: WebSocketServer;
  private clients: Map<WebSocket, ClientSubscription> = new Map();
  private simulator: SpaTelemetrySimulator;
  private redisConsumer: RedisTelemetryConsumer;
  private simulationTimer: NodeJS.Timeout | null = null;
  private statusHeartbeatTimer: NodeJS.Timeout | null = null;
  private startTime: number = Date.now();
  private lastRedisPacketTime: number = 0;
  private totalPacketsSent: number = 0;
  private port: number;
  private host: string;
  private simulationEnabled: boolean = process.env.SIMULATION_ENABLED === "true"; // default false: wait for real agent

  constructor(
    port: number = parseInt(process.env.WS_PORT || "4001", 10),
    host: string = process.env.WS_HOST || "0.0.0.0"
  ) {
    this.port = port;
    this.host = host;
    this.simulator = new SpaTelemetrySimulator();
    this.redisConsumer = new RedisTelemetryConsumer();

    // Create HTTP server for health checks & manual triggers
    this.server = http.createServer((req, res) => {
      // CORS headers
      res.setHeader("Access-Control-Allow-Origin", "*");
      res.setHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
      res.setHeader("Access-Control-Allow-Headers", "Content-Type");

      if (req.method === "OPTIONS") {
        res.writeHead(204);
        res.end();
        return;
      }

      if (req.url === "/health" || req.url === "/") {
        const metadata = this.redisConsumer.getMetadata();
        const status = {
          status: "healthy",
          clients: this.clients.size,
          mode: this.isReceivingLiveRedis()
            ? "live-agent"
            : this.simulationEnabled
            ? "simulator-60fps"
            : "waiting-for-agent",
          isLive: this.isReceivingLiveRedis(),
          tickRateHz: TICK_RATE_HZ,
          carModel: metadata.carModel,
          track: metadata.track,
          simulationEnabled: this.simulationEnabled,
          totalPacketsSent: this.totalPacketsSent,
          lastPacketAgeMs: this.lastRedisPacketTime > 0 ? Date.now() - this.lastRedisPacketTime : null,
          uptimeSec: Math.floor((Date.now() - this.startTime) / 1000),
        };
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify(status, null, 2));
        return;
      }

      if (req.url === "/api/simulation" && req.method === "POST") {
        let body = "";
        req.on("data", (chunk) => (body += chunk));
        req.on("end", () => {
          try {
            const data = JSON.parse(body);
            this.setSimulationMode(Boolean(data.enabled));
            res.writeHead(200, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ status: "ok", simulationEnabled: this.simulationEnabled }));
          } catch (e: any) {
            res.writeHead(400, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: e.message }));
          }
        });
        return;
      }

      if (req.url === "/api/trigger" && req.method === "POST") {
        let body = "";
        req.on("data", (chunk) => (body += chunk));
        req.on("end", () => {
          try {
            const data = JSON.parse(body);
            const event: TacticalEvent = {
              id: `api-evt-${Date.now()}`,
              type: "TACTICAL_EVENT",
              priority: data.priority || AudioPriority.P1_CRITICAL,
              text: data.text || "Manual tactical alert triggered.",
              category: data.category || "STRATEGY",
              timestamp: Date.now(),
            };
            this.broadcastEvent(event);
            res.writeHead(200, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ status: "broadcasted", event }));
          } catch (e: any) {
            res.writeHead(400, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: e.message }));
          }
        });
        return;
      }

      res.writeHead(404, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ error: "Not Found" }));
    });

    // Create WebSocket Server mounted on HTTP server
    this.wss = new WebSocketServer({
      server: this.server,
      path: "/ws/stream",
    });

    this.setupWebSocketHandlers();
  }

  public setSimulationMode(enabled: boolean) {
    this.simulationEnabled = enabled;
    console.log(`[WS Gateway] Simulation mode set to: ${enabled ? "ON (Demo)" : "OFF (Strictly Agent Only)"}`);
    this.broadcastGatewayStatus();
  }

  private setupWebSocketHandlers() {
    this.wss.on("connection", (ws: WebSocket, req) => {
      const ip = req.socket.remoteAddress || "unknown";
      const subscription: ClientSubscription = {
        ws,
        sessionId: "default",
        channels: new Set(["telemetry:fast", "strategy:tactics"]),
        ip,
        connectedAt: Date.now(),
      };

      this.clients.set(ws, subscription);
      console.log(`[WS Gateway] Client connected from ${ip}. Active clients: ${this.clients.size}`);

      // Send initial acknowledgement & current status
      const ack: WebSocketServerMessage = {
        type: "SUBSCRIPTION_ACK",
        channels: Array.from(subscription.channels),
        sessionId: subscription.sessionId,
      };
      ws.send(JSON.stringify(ack));
      ws.send(JSON.stringify(this.buildStatusMessage()));

      ws.on("message", (raw: string | Buffer) => {
        try {
          const msg = JSON.parse(raw.toString()) as WebSocketClientMessage;
          this.handleClientMessage(ws, msg);
        } catch (err: any) {
          console.warn("[WS Gateway] Invalid message received:", err.message);
        }
      });

      ws.on("close", () => {
        this.clients.delete(ws);
        console.log(`[WS Gateway] Client disconnected. Active clients: ${this.clients.size}`);
      });

      ws.on("error", (err) => {
        console.warn("[WS Gateway] Client socket error:", err.message);
        this.clients.delete(ws);
      });
    });
  }

  private handleClientMessage(ws: WebSocket, msg: WebSocketClientMessage) {
    const sub = this.clients.get(ws);
    if (!sub) return;

    switch (msg.action) {
      case "subscribe":
        if (msg.sessionId) sub.sessionId = msg.sessionId;
        if (msg.channels && Array.isArray(msg.channels)) {
          msg.channels.forEach((ch) => sub.channels.add(ch));
        }
        ws.send(
          JSON.stringify({
            type: "SUBSCRIPTION_ACK",
            channels: Array.from(sub.channels),
            sessionId: sub.sessionId,
          })
        );
        break;

      case "unsubscribe":
        if (msg.channels && Array.isArray(msg.channels)) {
          msg.channels.forEach((ch) => sub.channels.delete(ch));
        }
        break;

      case "ping":
        ws.send(
          JSON.stringify({
            type: "PONG",
            timestamp: msg.timestamp || Date.now(),
            serverTime: Date.now(),
          })
        );
        break;

      case "set_simulation":
        this.setSimulationMode(Boolean(msg.enabled));
        break;

      case "trigger_event":
        const event: TacticalEvent = {
          id: `client-evt-${Date.now()}`,
          type: "TACTICAL_EVENT",
          priority: msg.priority || AudioPriority.P1_CRITICAL,
          text: msg.text || "Tactical alert from client.",
          category: (msg.category as any) || "STRATEGY",
          timestamp: Date.now(),
        };
        this.broadcastEvent(event);
        break;
    }
  }

  public isReceivingLiveRedis(): boolean {
    return this.lastRedisPacketTime > 0 && Date.now() - this.lastRedisPacketTime < 2000;
  }

  private buildStatusMessage(): GatewayStatusMessage {
    const isLive = this.isReceivingLiveRedis();
    const metadata = this.redisConsumer.getMetadata();

    let source: "agent" | "waiting_for_agent" | "simulator";
    if (isLive) {
      source = "agent";
    } else if (this.simulationEnabled) {
      source = "simulator";
    } else {
      source = "waiting_for_agent";
    }

    return {
      type: "GATEWAY_STATUS",
      clientsCount: this.clients.size,
      tickRate: TICK_RATE_HZ,
      source,
      isLive,
      agentStatus: isLive ? "ON_TRACK" : "WAITING_FOR_SESSION",
      lastPacketTime: this.lastRedisPacketTime,
      carModel: metadata.carModel,
      track: metadata.track,
      simulationEnabled: this.simulationEnabled,
      totalPacketsSent: this.totalPacketsSent,
      uptime: Math.floor((Date.now() - this.startTime) / 1000),
    };
  }

  public broadcastGatewayStatus() {
    if (this.clients.size === 0) return;
    const json = JSON.stringify(this.buildStatusMessage());
    for (const [ws] of this.clients.entries()) {
      if (ws.readyState === WebSocket.OPEN) {
        ws.send(json);
      }
    }
  }

  public broadcastTelemetry(packet: FastTelemetryPacket) {
    if (this.clients.size === 0) return;
    const json = JSON.stringify(packet);

    for (const [ws, sub] of this.clients.entries()) {
      if (ws.readyState === WebSocket.OPEN && sub.channels.has("telemetry:fast")) {
        ws.send(json);
      }
    }
    this.totalPacketsSent++;
  }

  public broadcastEvent(event: TacticalEvent) {
    if (this.clients.size === 0) return;
    const json = JSON.stringify(event);

    for (const [ws, sub] of this.clients.entries()) {
      if (ws.readyState === WebSocket.OPEN && sub.channels.has("strategy:tactics")) {
        ws.send(json);
      }
    }
  }

  public async start() {
    // Attempt Redis connection
    await this.redisConsumer.connect();
    this.redisConsumer.startListening((packet) => {
      this.lastRedisPacketTime = Date.now();
      this.broadcastTelemetry(packet);
    });

    // Simulation Timer: ONLY runs if simulation is explicitly enabled AND Redis is not streaming!
    this.simulationTimer = setInterval(() => {
      if (this.simulationEnabled && !this.isReceivingLiveRedis()) {
        const { packet, event } = this.simulator.tick();
        this.broadcastTelemetry(packet);
        if (event) {
          this.broadcastEvent(event);
        }
      }
    }, TICK_INTERVAL_MS);

    // Periodic Heartbeat Status Timer (every 1 second)
    this.statusHeartbeatTimer = setInterval(() => {
      this.broadcastGatewayStatus();
    }, 1000);

    return new Promise<void>((resolve) => {
      this.server.listen(this.port, this.host, () => {
        console.log(`[WS Gateway] Server listening on http://${this.host}:${this.port}`);
        console.log(`[WS Gateway] WebSocket stream ready at ws://${this.host}:${this.port}/ws/stream`);
        console.log(`[WS Gateway] Mode: ${this.simulationEnabled ? "SIMULATOR ACTIVE" : "WAITING FOR AGENT (Redis stream:telemetry:raw)"}`);
        resolve();
      });
    });
  }

  public stop() {
    if (this.simulationTimer) clearInterval(this.simulationTimer);
    if (this.statusHeartbeatTimer) clearInterval(this.statusHeartbeatTimer);
    this.redisConsumer.stop();
    this.wss.close();
    this.server.close();
  }
}

// Auto-run if executed directly
if (process.argv[1] && process.argv[1].endsWith("gateway.ts")) {
  const gateway = new TelemetryWebSocketGateway();
  gateway.start().catch((err) => {
    console.error("[WS Gateway] Fatal start error:", err);
    process.exit(1);
  });
}
