import type { Playback } from './manifest';

export const abortError = () => new DOMException('Playback replaced', 'AbortError');
export const delay = (ms: number, signal: AbortSignal): Promise<void> => new Promise((resolve, reject) => {
  if (signal.aborted) { reject(abortError()); return; }
  const cleanup = () => { clearTimeout(timer); signal.removeEventListener('abort', abort); };
  const abort = () => { cleanup(); reject(abortError()); };
  const timer = setTimeout(() => { cleanup(); resolve(); }, Math.max(0, ms));
  signal.addEventListener('abort', abort, { once: true });
});

// Completion is event-driven, not durationMs-driven. The watchdog only handles
// startup failure, stalled/paused media and never-ending content.
export const playVideo = (video: HTMLVideoElement, url: string, settings: Playback, signal: AbortSignal): Promise<void> => new Promise((resolve, reject) => {
  if (signal.aborted) { reject(abortError()); return; }
  let settled = false;
  let started = false;
  let lastTime = -1;
  const began = performance.now();
  let lastProgress = began;
  const handlers: [string, EventListener][] = [];
  const finish = (error?: Error) => {
    if (settled) return;
    settled = true;
    clearInterval(watchdog);
    handlers.forEach(([name, handler]) => video.removeEventListener(name, handler));
    signal.removeEventListener('abort', abort);
    video.pause();
    error ? reject(error) : resolve();
  };
  const abort = () => finish(abortError());
  const on = (name: string, handler: EventListener) => { handlers.push([name, handler]); video.addEventListener(name, handler); };
  const progress = () => {
    if (settings.endSeconds != null && video.currentTime >= settings.endSeconds) { finish(); return; }
    if (video.currentTime > lastTime + 0.01) { lastTime = video.currentTime; lastProgress = performance.now(); }
  };
  on('ended', () => finish());
  on('error', () => finish(new Error(`Video decode/load failed (${video.error?.code ?? 'unknown'})`)));
  on('playing', () => { if (!started) lastProgress = performance.now(); started = true; });
  on('timeupdate', progress);
  on('loadedmetadata', () => {
    const start = settings.startSeconds ?? 0;
    if (Number.isFinite(video.duration) && start >= video.duration) { finish(new Error('Video trim starts after the video ends')); return; }
    if (start > 0) video.currentTime = start;
  });
  const watchdog = setInterval(() => {
    progress();
    const now = performance.now();
    if (!started && now - began > settings.startupTimeoutMs) finish(new Error('Video startup timed out'));
    else if (started && now - lastProgress > settings.stallTimeoutMs) finish(new Error('Video playback stalled'));
    else if (now - began > settings.maximumDurationMs) finish(new Error('Video exceeded the configured playback limit'));
  }, 250);
  signal.addEventListener('abort', abort, { once: true });
  video.loop = false;
  video.muted = true;
  video.src = url;
  video.load();
  if (!settled) void video.play().catch(error => finish(new Error(`Video autoplay failed: ${String(error)}`)));
});
