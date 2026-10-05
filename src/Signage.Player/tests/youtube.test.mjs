import test from 'node:test';
import assert from 'node:assert/strict';
import { playYouTube } from '../src/youtube.ts';

let events;
let destroyed = 0;
let muted = false;
let played = false;
const player = { mute() { muted = true; }, playVideo() { played = true; }, getCurrentTime() { return 1; }, destroy() { destroyed++; } };
globalThis.window = { setInterval, setTimeout, YT: { Player: class {
  constructor(_iframe, options) { events = options.events; queueMicrotask(() => events.onReady({ target: player })); return player; }
} } };
globalThis.location = { origin: 'https://signage.test' };
globalThis.document = { createElement: () => ({}) };
Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { onLine: true } });
const host = { children: [], replaceChildren(...children) { this.children = children; } };
const settings = { startupTimeoutMs: 1000, stallTimeoutMs: 1000, maximumDurationMs: 5000 };

test('YouTube uses a rebuilt privacy-enhanced URL and completes on ENDED', async () => {
  const promise = playYouTube(host, 'M7lc1UVf-VE', settings, new AbortController().signal);
  await new Promise(resolve => setTimeout(resolve, 10));
  assert.match(host.children[0].src, /^https:\/\/www.youtube-nocookie.com\/embed\/M7lc1UVf-VE\?/);
  assert.equal(host.children[0].referrerPolicy, 'strict-origin-when-cross-origin');
  assert.equal(muted, true);
  assert.equal(played, true);
  events.onStateChange({ target: player, data: 1 });
  events.onStateChange({ target: player, data: 0 });
  await promise;
  assert.equal(host.children.length, 0);
  assert.equal(destroyed, 1);
});

test('YouTube errors and blocked autoplay are recoverable', async () => {
  for (const [name, event, message] of [['onError', { data: 150 }, /unavailable/], ['onAutoplayBlocked', {}, /autoplay blocked/]]) {
    const promise = playYouTube(host, 'M7lc1UVf-VE', settings, new AbortController().signal);
    await new Promise(resolve => setTimeout(resolve, 10));
    events[name]({ target: player, ...event });
    await assert.rejects(promise, message);
  }
});

test('YouTube is skipped offline and rejects malformed IDs without creating a frame', async () => {
  navigator.onLine = false;
  await assert.rejects(playYouTube(host, 'M7lc1UVf-VE', settings, new AbortController().signal), /offline/);
  navigator.onLine = true;
  await assert.rejects(playYouTube(host, '../malicious', settings, new AbortController().signal), /Invalid YouTube ID/);
});

test('YouTube package replacement destroys the old player', async () => {
  const controller = new AbortController();
  const promise = playYouTube(host, 'M7lc1UVf-VE', settings, controller.signal);
  await new Promise(resolve => setTimeout(resolve, 10));
  controller.abort();
  await assert.rejects(promise, { name: 'AbortError' });
  assert.equal(host.children.length, 0);
});
