import test from 'node:test';
import assert from 'node:assert/strict';
import { playVideo } from '../src/media.ts';
import { cachedRangeResponse } from '../src/ranges.ts';
import { parseManifest, packageAssets } from '../src/manifest.ts';

class FakeVideo extends EventTarget {
  currentTime = 0;
  duration = 20;
  loop = true;
  muted = false;
  paused = false;
  error = null;
  src = '';
  load() { this.dispatchEvent(new Event('loadedmetadata')); }
  play() { this.dispatchEvent(new Event('playing')); return Promise.resolve(); }
  pause() { this.paused = true; }
}
const settings = { startupTimeoutMs: 1000, stallTimeoutMs: 1000, maximumDurationMs: 5000 };

test('video waits for ended rather than a slide timer and pauses after completion', async () => {
  const video = new FakeVideo();
  let finished = false;
  const promise = playVideo(video, '/clip.mp4', settings, new AbortController().signal).then(() => { finished = true; });
  await new Promise(resolve => setTimeout(resolve, 30));
  assert.equal(finished, false);
  assert.equal(video.loop, false);
  assert.equal(video.muted, true);
  video.dispatchEvent(new Event('ended'));
  await promise;
  assert.equal(finished, true);
  assert.equal(video.paused, true);
});

test('replacing the package cancels playback and removes event listeners', async () => {
  const controller = new AbortController();
  const video = new FakeVideo();
  const promise = playVideo(video, '/clip.mp4', settings, controller.signal);
  controller.abort();
  await assert.rejects(promise, { name: 'AbortError' });
  video.dispatchEvent(new Event('ended'));
  assert.equal(video.paused, true);
});

test('decode errors and autoplay rejection recover instead of waiting for completion', async () => {
  const video = new FakeVideo();
  const promise = playVideo(video, '/clip.mp4', settings, new AbortController().signal);
  video.dispatchEvent(new Event('error'));
  await assert.rejects(promise, /decode\/load/);
  video.play = () => Promise.reject(new Error('blocked'));
  await assert.rejects(playVideo(video, '/clip.mp4', settings, new AbortController().signal), /autoplay failed/);
});

test('trimmed videos seek to start and complete at the trim end', async () => {
  const video = new FakeVideo();
  const promise = playVideo(video, '/clip.mp4', { ...settings, startSeconds: 2, endSeconds: 4 }, new AbortController().signal);
  assert.equal(video.currentTime, 2);
  video.currentTime = 4;
  video.dispatchEvent(new Event('timeupdate'));
  await promise;
});

test('stalled playback is bounded by the watchdog', async () => {
  await assert.rejects(playVideo(new FakeVideo(), '/clip.mp4', { ...settings, stallTimeoutMs: 10 }, new AbortController().signal), /stalled/);
});

test('cached media responds to normal, open ended, suffix, and invalid ranges', async () => {
  const response = () => new Response('abcdefghij', { headers: { 'Content-Type': 'video/mp4' } });
  for (const [range, expected, contentRange] of [['bytes=2-5', 'cdef', 'bytes 2-5/10'], ['bytes=7-', 'hij', 'bytes 7-9/10'], ['bytes=-3', 'hij', 'bytes 7-9/10'], ['bytes=0-999', 'abcdefghij', 'bytes 0-9/10']]) {
    const r = await cachedRangeResponse(response(), range);
    assert.equal(r.status, 206);
    assert.equal(r.headers.get('Content-Range'), contentRange);
    assert.equal(await r.text(), expected);
  }
  for (const range of ['bytes=20-', 'bytes=5-2', 'bytes=-0', 'bytes=0-1,4-5', 'invalid']) assert.equal((await cachedRangeResponse(response(), range)).status, 416);
});

const hash = 'a'.repeat(64);
const background = { asset: 'slides/slide-0001.png', sha256: hash };
const item = { ...background, type: 'image', sourceSlideNumber: 1, durationMs: 10000, transition: { type: 'cut', durationMs: 0 } };
const manifest = { schemaVersion: 1, contentId: hash, presentationVersionId: 'test', loop: true, items: [item] };

test('old image packages remain playable and media backgrounds are deduplicated', () => {
  assert.equal(parseManifest(manifest).schemaVersion, 1);
  const video = { ...item, type: 'video', asset: `media/${hash}.mp4`, background };
  const youtube = { ...item, type: 'youtube', youTubeVideoId: 'M7lc1UVf-VE', background };
  assert.equal(packageAssets(parseManifest({ ...manifest, schemaVersion: 2, items: [video, youtube] })).length, 2);
});

test('manifest rejects unsupported versions, unsafe paths, YouTube IDs and asset conflicts', () => {
  assert.throws(() => parseManifest({ ...manifest, schemaVersion: 3 }));
  assert.throws(() => parseManifest({ ...manifest, items: [{ ...item, asset: '../../private.png' }] }));
  assert.throws(() => parseManifest({ ...manifest, schemaVersion: 2, items: [{ ...item, type: 'youtube', youTubeVideoId: 'javascript:alert(1)' }] }));
  assert.throws(() => packageAssets({ ...manifest, items: [item, { ...item, sha256: 'b'.repeat(64) }] }));
});
