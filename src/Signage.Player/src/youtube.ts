import type { Playback } from './manifest';
import { abortError } from './media.ts';

type YouTubePlayer = { mute(): void; playVideo(): void; getCurrentTime(): number; destroy(): void };
type PlayerEvent = { target: YouTubePlayer; data?: number };
type YouTubeApi = { Player: new (element: HTMLIFrameElement, options: { events: Record<string, (event: PlayerEvent) => void> }) => YouTubePlayer };
declare global { interface Window { YT?: YouTubeApi; onYouTubeIframeAPIReady?: () => void } }
let apiPromise: Promise<YouTubeApi> | undefined;

const loadApi = (): Promise<YouTubeApi> => {
  if (window.YT?.Player) return Promise.resolve(window.YT);
  if (apiPromise) return apiPromise;
  apiPromise = new Promise((resolve, reject) => {
    const script = document.createElement('script');
    const timer = window.setTimeout(() => { script.remove(); apiPromise = undefined; reject(new Error('YouTube API timed out')); }, 20000);
    window.onYouTubeIframeAPIReady = () => { clearTimeout(timer); window.YT ? resolve(window.YT) : reject(new Error('YouTube API unavailable')); };
    script.src = 'https://www.youtube.com/iframe_api';
    script.onerror = () => { clearTimeout(timer); script.remove(); apiPromise = undefined; reject(new Error('YouTube API could not load')); };
    document.head.append(script);
  });
  return apiPromise;
};

export const playYouTube = async (host: HTMLElement, id: string, settings: Playback, signal: AbortSignal): Promise<void> => {
  if (!/^[A-Za-z0-9_-]{11}$/.test(id)) throw new Error('Invalid YouTube ID');
  if (!navigator.onLine) throw new Error('YouTube unavailable offline');
  const began = performance.now();
  const api = await new Promise<YouTubeApi>((resolve, reject) => {
    const cleanup = () => { clearTimeout(timer); signal.removeEventListener('abort', abort); };
    const abort = () => { cleanup(); reject(abortError()); };
    const timer = setTimeout(() => { cleanup(); reject(new Error('YouTube API startup timed out')); }, settings.startupTimeoutMs);
    if (signal.aborted) { abort(); return; }
    signal.addEventListener('abort', abort, { once: true });
    void loadApi().then(value => { cleanup(); resolve(value); }, error => { cleanup(); reject(error); });
  });
  if (signal.aborted) throw abortError();
  return new Promise((resolve, reject) => {
    let player: YouTubePlayer | undefined;
    let settled = false;
    let started = false;
    let lastTime = -1;
    let lastProgress = began;
    const iframe = document.createElement('iframe');
    const parameters = new URLSearchParams({ enablejsapi: '1', playsinline: '1', autoplay: '0', origin: location.origin, rel: '0' });
    if (settings.startSeconds) parameters.set('start', String(Math.floor(settings.startSeconds)));
    if (settings.endSeconds != null) parameters.set('end', String(Math.ceil(settings.endSeconds)));
    iframe.src = `https://www.youtube-nocookie.com/embed/${id}?${parameters}`;
    iframe.title = 'YouTube video';
    iframe.allow = 'autoplay; encrypted-media; picture-in-picture; fullscreen';
    iframe.referrerPolicy = 'strict-origin-when-cross-origin';
    iframe.className = 'youtube-frame';
    host.replaceChildren(iframe);
    const finish = (error?: Error) => {
      if (settled) return;
      settled = true;
      clearInterval(watchdog);
      signal.removeEventListener('abort', abort);
      try { player?.destroy(); } catch { /* frame can already be removed by a package switch */ }
      host.replaceChildren();
      error ? reject(error) : resolve();
    };
    const abort = () => finish(abortError());
    const watchdog = window.setInterval(() => {
      const now = performance.now();
      try {
        const time = player?.getCurrentTime() ?? 0;
        if (settings.endSeconds != null && time >= settings.endSeconds) { finish(); return; }
        if (time > lastTime + 0.01) { lastTime = time; lastProgress = now; }
      } catch { /* loading API state is transient; startup timeout still applies */ }
      if (!started && now - began > settings.startupTimeoutMs) finish(new Error('YouTube startup timed out'));
      else if (started && now - lastProgress > settings.stallTimeoutMs) finish(new Error('YouTube playback stalled'));
      else if (now - began > settings.maximumDurationMs) finish(new Error('YouTube exceeded the configured playback limit'));
    }, 250);
    signal.addEventListener('abort', abort, { once: true });
    try {
      player = new api.Player(iframe, { events: {
        onReady: event => { if (!settled) { event.target.mute(); event.target.playVideo(); } },
        onStateChange: event => {
          if (event.data === 0) finish();
          else if (event.data === 1) { if (!started) lastProgress = performance.now(); started = true; }
        },
        onError: event => finish(new Error(`YouTube playback unavailable (${event.data ?? 'unknown'})`)),
        onAutoplayBlocked: () => finish(new Error('YouTube autoplay blocked'))
      } });
    } catch (error) { finish(new Error(`YouTube player failed: ${String(error)}`)); }
  });
};
