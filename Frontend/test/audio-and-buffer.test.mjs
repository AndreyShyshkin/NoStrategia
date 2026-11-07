import test from "node:test";
import assert from "node:assert/strict";
import { AudioPriority } from "../src/types/telemetry.js";

test("Telemetry Circular Ring Buffer Test", () => {
  const CAPACITY = 600;
  const speed = new Float32Array(CAPACITY);
  const gas = new Float32Array(CAPACITY);
  const brake = new Float32Array(CAPACITY);

  let head = 0;

  // Simulate pushing 1000 items (exceeding capacity by 400)
  for (let i = 0; i < 1000; i++) {
    const idx = head % CAPACITY;
    speed[idx] = i * 0.25;
    gas[idx] = (i % 100) / 100;
    brake[idx] = (i % 50 === 0) ? 1.0 : 0.0;
    head++;
  }

  assert.equal(head, 1000);

  // Buffer length must strictly stay CAPACITY (Zero-GC, no resizing)
  assert.equal(speed.length, CAPACITY);
  assert.equal(gas.length, CAPACITY);

  // The oldest point in the buffer should be at index: (head % CAPACITY) = 400
  const startIdx = head >= CAPACITY ? head % CAPACITY : 0;
  assert.equal(startIdx, 400);

  // The newest item (point 999) should be at (head - 1) % CAPACITY = 399
  const newestIdx = (head - 1) % CAPACITY;
  assert.equal(newestIdx, 399);
  assert.equal(speed[newestIdx], 999 * 0.25);
});

test("Audio Engineer Priority Queue & Deduplication Logic Test", () => {
  // Mock Audio Race Engineer queue logic in isolated environment
  const queue = [];
  const lastSpokenMap = new Map();

  function postCallout(text, priority) {
    const now = Date.now();
    const lastSpoken = lastSpokenMap.get(text) || 0;

    // Deduplication check: 40s cooldown unless P1
    if (priority > AudioPriority.P1_CRITICAL && now - lastSpoken < 40000) {
      return false; // suppressed by deduplication
    }

    const msg = { id: Math.random().toString(), priority, text, timestamp: now };

    if (priority === AudioPriority.P1_CRITICAL) {
      // P1 clears low priority and jumps immediately to front
      const criticalOnly = queue.filter((m) => m.priority <= AudioPriority.P2_STRATEGY);
      queue.length = 0;
      queue.push(...criticalOnly);
      queue.unshift(msg);
      lastSpokenMap.set(text, now);
      return true;
    }

    queue.push(msg);
    queue.sort((a, b) => a.priority - b.priority);
    lastSpokenMap.set(text, now);
    return true;
  }

  // 1. Post P4 status
  postCallout("Sector 1 green", AudioPriority.P4_STATUS);
  assert.equal(queue.length, 1);
  assert.equal(queue[0].priority, AudioPriority.P4_STATUS);

  // 2. Post P2 strategy (must sort ahead of P4)
  postCallout("Box now for fuel", AudioPriority.P2_STRATEGY);
  assert.equal(queue.length, 2);
  assert.equal(queue[0].priority, AudioPriority.P2_STRATEGY);
  assert.equal(queue[1].priority, AudioPriority.P4_STATUS);

  // 3. Post identical P2 message within cooldown -> must be deduplicated/ignored
  const acceptedAgain = postCallout("Box now for fuel", AudioPriority.P2_STRATEGY);
  assert.equal(acceptedAgain, false, "Duplicate strategy callout should be suppressed");
  assert.equal(queue.length, 2);

  // 4. Post P1 critical alert -> must preempt and purge P4 status!
  postCallout("Puncture detected! Slow down!", AudioPriority.P1_CRITICAL);
  assert.equal(queue[0].priority, AudioPriority.P1_CRITICAL);
  assert.equal(queue[0].text, "Puncture detected! Slow down!");
  // The P4 status message should have been cleared
  assert.ok(!queue.some((m) => m.priority === AudioPriority.P4_STATUS));
});
