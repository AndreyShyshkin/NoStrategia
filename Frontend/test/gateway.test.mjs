import test from "node:test";
import assert from "node:assert/strict";
import http from "node:http";
import { WebSocket } from "ws";
import { TelemetryWebSocketGateway } from "../server/gateway.js";
import { AudioPriority } from "../src/types/telemetry.js";

test("WebSocket Gateway & HTTP Health Endpoint E2E Test", async (t) => {
  const TEST_PORT = 4099;
  const gateway = new TelemetryWebSocketGateway(TEST_PORT);
  gateway.setSimulationMode(true); // Enable simulation explicitly for automated test
  await gateway.start();

  t.after(() => {
    gateway.stop();
  });

  await t.test("HTTP /health returns healthy status", async () => {
    const res = await fetch("http://127.0.0.1:4099/health");
    assert.equal(res.status, 200);
    const data = await res.json();
    assert.equal(data.status, "healthy");
    assert.equal(typeof data.clients, "number");
    assert.equal(data.tickRateHz, 60);
  });

  await t.test("WebSocket client receives SUBSCRIPTION_ACK and FAST_PACKET stream at 60 FPS", async () => {
    const ws = new WebSocket("ws://127.0.0.1:4099/ws/stream");

    const receivedMessages = [];
    let resolveAck;
    const ackPromise = new Promise((res) => (resolveAck = res));

    let resolvePackets;
    const packetsPromise = new Promise((res) => (resolvePackets = res));

    ws.on("open", () => {
      ws.send(
        JSON.stringify({
          action: "subscribe",
          channels: ["telemetry:fast", "strategy:tactics"],
        })
      );
    });

    ws.on("message", (raw) => {
      const msg = JSON.parse(raw.toString());
      receivedMessages.push(msg);

      if (msg.type === "SUBSCRIPTION_ACK") {
        resolveAck(msg);
      } else if (msg.type === "FAST_PACKET" && receivedMessages.filter((m) => m.type === "FAST_PACKET").length >= 5) {
        resolvePackets();
      }
    });

    const ack = await ackPromise;
    assert.equal(ack.type, "SUBSCRIPTION_ACK");
    assert.ok(ack.channels.includes("telemetry:fast"));

    await packetsPromise;
    const fastPackets = receivedMessages.filter((m) => m.type === "FAST_PACKET");
    assert.ok(fastPackets.length >= 5, "Received at least 5 fast telemetry packets");

    const sample = fastPackets[0];
    assert.equal(sample.carModel, "mercedes_amg_gt3_evo");
    assert.equal(sample.track, "spa");
    assert.ok(typeof sample.speed === "number" && sample.speed >= 0);
    assert.ok(typeof sample.gas === "number" && sample.gas >= 0 && sample.gas <= 1.0);
    assert.ok(typeof sample.brake === "number" && sample.brake >= 0 && sample.brake <= 1.0);
    assert.ok(Array.isArray(sample.rawArray) && sample.rawArray.length === 12);

    ws.close();
  });

  await t.test("Manual tactical event triggers broadcast to subscribed clients", async () => {
    const ws = new WebSocket("ws://127.0.0.1:4099/ws/stream");

    let resolveEvent;
    const eventPromise = new Promise((res) => (resolveEvent = res));

    ws.on("open", () => {
      ws.send(
        JSON.stringify({
          action: "subscribe",
          channels: ["strategy:tactics"],
        })
      );

      // Trigger test event via WebSocket action
      setTimeout(() => {
        ws.send(
          JSON.stringify({
            action: "trigger_event",
            priority: AudioPriority.P1_CRITICAL,
            text: "E2E Lockup Alert Verified",
            category: "LOCKUP",
          })
        );
      }, 50);
    });

    ws.on("message", (raw) => {
      const msg = JSON.parse(raw.toString());
      if (msg.type === "TACTICAL_EVENT" && msg.text === "E2E Lockup Alert Verified") {
        resolveEvent(msg);
      }
    });

    const evt = await eventPromise;
    assert.equal(evt.type, "TACTICAL_EVENT");
    assert.equal(evt.priority, AudioPriority.P1_CRITICAL);
    assert.equal(evt.text, "E2E Lockup Alert Verified");

    ws.close();
  });
});
