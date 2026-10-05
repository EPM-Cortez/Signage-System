import './style.css';
import { deleteValue, getValue, setValues } from './store';
import { parseManifest, packageAssets, playbackSettings } from './manifest';
import type { Manifest, ManifestItem } from './manifest';
import { abortError, delay, playVideo } from './media';
import { playYouTube } from './youtube';

type Assignment = {
  publication: { id: string; presentationVersionId: string; startsUtc: string } | null;
  contentId?: string;
  manifestUrl?: string;
  manifestEtag?: string;
  pollIntervalSeconds: number;
};

const cacheName = 'school-signage-packages-v1';
const message = document.querySelector<HTMLElement>('#message')!;
const messageLabel = document.querySelector<HTMLElement>('#message-label')!;
const messageDetail = document.querySelector<HTMLElement>('#message-detail')!;
const pairingCode = document.querySelector<HTMLElement>('#pairing-code')!;
const imageElements = [document.querySelector<HTMLImageElement>('#image-a')!, document.querySelector<HTMLImageElement>('#image-b')!];
const video = document.querySelector<HTMLVideoElement>('#video')!;
const stage = document.querySelector<HTMLElement>('#slide-stage')!;
const youtubeHost = document.querySelector<HTMLElement>('#youtube-host')!;
let currentManifest: Manifest | undefined;
let currentItemIndex = 0;
let playbackController: AbortController | undefined;
let lastError: string | null = null;
let deviceToken: string | undefined;
let assignmentEtag: string | undefined;
let activeImage = 0;
let wakeLock: WakeLockSentinel | undefined;

const showMessage = (label: string, detail = '') => {
  message.classList.remove('hidden');
  messageLabel.textContent = label;
  messageDetail.textContent = detail;
};

const hideMessage = () => message.classList.add('hidden');

const authenticatedFetch = (url: string, init: RequestInit = {}) => fetch(url, {
  ...init,
  headers: { ...init.headers, Authorization: `Bearer ${deviceToken}` },
  cache: 'no-store'
});

const hashHex = async (buffer: ArrayBuffer): Promise<string> => {
  const digest = await crypto.subtle.digest('SHA-256', buffer);
  return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('');
};

const pair = async (): Promise<string> => {
  const response = await fetch('/api/player/pairing-sessions', { method: 'POST' });
  if (!response.ok) throw new Error('Could not start pairing');
  const session = await response.json() as { sessionId: string; temporaryToken: string; code: string };
  showMessage('Enter this code in School Signage administration');
  pairingCode.textContent = session.code;
  pairingCode.hidden = false;
  while (true) {
    await new Promise(resolve => window.setTimeout(resolve, 2000));
    const poll = await fetch(`/api/player/pairing-sessions/${session.sessionId}`, { headers: { Authorization: `Bearer ${session.temporaryToken}` }, cache: 'no-store' });
    if (poll.status === 401) throw new Error('Pairing code expired');
    if (!poll.ok) continue;
    const result = await poll.json() as { paired: boolean; deviceToken?: string; deviceName?: string };
    if (result.paired && result.deviceToken) {
      pairingCode.hidden = true;
      showMessage('Device paired', result.deviceName ?? 'Downloading assigned content…');
      await setValues({ deviceToken: result.deviceToken });
      return result.deviceToken;
    }
  }
};

const removePackage = async (contentId: string) => {
  const manifest = await getValue<Manifest>(`package:${contentId}`);
  const cache = await caches.open(cacheName);
  if (manifest) {
    await Promise.all([`/content/${contentId}/manifest.json`, ...packageAssets(manifest).map(item => `/content/${contentId}/${item.asset}`)].map(url => cache.delete(url)));
  }
  await deleteValue(`package:${contentId}`);
};

const downloadPackage = async (assignment: Assignment): Promise<Manifest> => {
  if (!assignment.contentId || !assignment.manifestUrl) throw new Error('Assignment is incomplete');
  const manifestResponse = await authenticatedFetch(assignment.manifestUrl);
  if (!manifestResponse.ok) throw new Error(`Manifest download failed (${manifestResponse.status})`);
  const manifest = parseManifest(await manifestResponse.clone().json(), assignment.contentId);
  const cache = await caches.open(cacheName);
  const cachedUrls: string[] = [];
  try {
    await cache.put(assignment.manifestUrl, manifestResponse.clone());
    cachedUrls.push(assignment.manifestUrl);
    for (const item of packageAssets(manifest)) {
      const url = `/content/${manifest.contentId}/${item.asset}`;
      const response = await authenticatedFetch(url);
      if (!response.ok) throw new Error(`Asset download failed (${response.status})`);
      const bytes = await response.clone().arrayBuffer();
      if (await hashHex(bytes) !== item.sha256.toLowerCase()) throw new Error(`Asset hash failed for ${item.asset}`);
      await cache.put(url, response);
      cachedUrls.push(url);
    }
    const previousContentId = await getValue<string>('activeContentId');
    const olderContentId = await getValue<string>('previousContentId');
    await setValues({
      [`package:${manifest.contentId}`]: manifest,
      packageComplete: manifest.contentId,
      activeContentId: manifest.contentId,
      previousContentId: previousContentId ?? null,
      assignmentEtag: assignment.manifestEtag ?? null
    });
    if (olderContentId && olderContentId !== previousContentId && olderContentId !== manifest.contentId) await removePackage(olderContentId);
    return manifest;
  } catch (error) {
    await Promise.all(cachedUrls.map(url => cache.delete(url)));
    throw error;
  }
};

const resizeStage = () => {
  const canvas = currentManifest?.canvas ?? { width: 16, height: 9 };
  const scale = Math.min(window.innerWidth / canvas.width, window.innerHeight / canvas.height);
  stage.style.width = `${canvas.width * scale}px`;
  stage.style.height = `${canvas.height * scale}px`;
};
window.addEventListener('resize', resizeStage);

const placeMedia = (element: HTMLElement, item: ManifestItem) => {
  let p = item.placement ?? { x: 0, y: 0, width: 1, height: 1 };
  // Respect YouTube's minimum viewport and visibility requirements. Small,
  // off-slide or transformed source shapes use an unobscured full-slide player.
  if (item.type === 'youtube' && (p.width * stage.clientWidth < 200 || p.height * stage.clientHeight < 200 || p.x < 0 || p.y < 0 ||
    p.x + p.width > 1 || p.y + p.height > 1 || p.rotation || p.flipHorizontal || p.flipVertical)) p = { x: 0, y: 0, width: 1, height: 1 };
  element.style.left = `${p.x * 100}%`;
  element.style.top = `${p.y * 100}%`;
  element.style.width = `${p.width * 100}%`;
  element.style.height = `${p.height * 100}%`;
  element.style.transform = `rotate(${p.rotation ?? 0}deg) scale(${p.flipHorizontal ? -1 : 1},${p.flipVertical ? -1 : 1})`;
};

const loadImage = (image: HTMLImageElement, url: string, signal: AbortSignal): Promise<void> => new Promise((resolve, reject) => {
  if (signal.aborted) { reject(abortError()); return; }
  const cleanup = () => { clearTimeout(timer); image.onload = null; image.onerror = null; signal.removeEventListener('abort', abort); };
  const abort = () => { cleanup(); reject(abortError()); };
  const timer = window.setTimeout(() => { cleanup(); reject(new Error('Slide image load timed out')); }, 20000);
  image.onload = () => { cleanup(); resolve(); };
  image.onerror = () => { cleanup(); reject(new Error('Slide image could not load')); };
  signal.addEventListener('abort', abort, { once: true });
  image.src = url;
});

const runPlaylist = async (manifest: Manifest, signal: AbortSignal) => {
  for (let index = 0; !signal.aborted; index = (index + 1) % manifest.items.length) {
    const item = manifest.items[index]!;
    currentItemIndex = index;
    video.classList.remove('active');
    youtubeHost.classList.remove('active');
    try {
      const nextImage = imageElements[1 - activeImage]!;
      const background = item.background?.asset ?? (item.type === 'video' ? undefined : item.asset);
      if (background) await loadImage(nextImage, `/content/${manifest.contentId}/${background}`, signal);
      if (signal.aborted) return;
      imageElements[activeImage]!.classList.remove('active');
      if (background) {
        nextImage.style.transitionDuration = item.transition.type === 'fade' ? `${item.transition.durationMs}ms` : '0ms';
        nextImage.classList.add('active');
        activeImage = 1 - activeImage;
      }
      hideMessage();
      if (item.type === 'image') {
        await delay(item.durationMs, signal);
      } else if (item.type === 'video') {
        placeMedia(video, item);
        video.classList.add('active');
        await playVideo(video, `/content/${manifest.contentId}/${item.asset}`, playbackSettings(item), signal);
      } else {
        placeMedia(youtubeHost, item);
        youtubeHost.classList.add('active');
        if (youtubeHost.clientWidth < 200 || youtubeHost.clientHeight < 200) throw new Error('Display too small for YouTube playback');
        await playYouTube(youtubeHost, item.youTubeVideoId!, playbackSettings(item), signal);
      }
    } catch (error) {
      if (signal.aborted) return;
      lastError = `Slide ${item.sourceSlideNumber}: ${String(error)}`;
      video.pause();
      video.classList.remove('active');
      youtubeHost.classList.remove('active');
      // Leave its cached poster visible briefly, then continue. Never let one
      // broken video stall every display or spin a one-item package in a loop.
      await delay(Math.min(2000, item.durationMs), signal);
    }
    if (!manifest.loop && index === manifest.items.length - 1) return;
  }
};

const activate = (manifest: Manifest) => {
  playbackController?.abort();
  currentManifest = manifest;
  resizeStage();
  playbackController = new AbortController();
  void runPlaylist(manifest, playbackController.signal).catch(error => {
    if (error instanceof DOMException && error.name === 'AbortError') return;
    lastError = String(error);
    showMessage('Playback error', 'The player will keep trying.');
  });
  void heartbeat();
};

const pollAssignment = async (): Promise<void> => {
  try {
    const headers: Record<string, string> = {};
    if (assignmentEtag) headers['If-None-Match'] = assignmentEtag;
    const response = await authenticatedFetch('/api/player/assignment', { headers });
    if (response.status === 401) {
      await deleteValue('deviceToken');
      location.reload();
      return;
    }
    if (response.status === 304) return;
    if (!response.ok) throw new Error(`Assignment failed (${response.status})`);
    const assignment = await response.json() as Assignment;
    assignmentEtag = response.headers.get('ETag') ?? undefined;
    if (!assignment.publication || !assignment.contentId) {
      if (!currentManifest) showMessage('No content assigned', 'An administrator can assign this display to a screen group.');
      return;
    }
    if (currentManifest?.contentId !== assignment.contentId) {
      const manifest = await downloadPackage(assignment);
      activate(manifest);
    }
  } catch (error) {
    lastError = String(error);
    if (!currentManifest) showMessage('Waiting for the server', 'Playback will resume automatically when the connection is available.');
  }
};

const heartbeat = async (): Promise<void> => {
  if (!deviceToken) return;
  try {
    const estimate = await navigator.storage?.estimate();
    await authenticatedFetch('/api/player/heartbeat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        contentId: currentManifest?.contentId ?? null,
        presentationVersionId: currentManifest?.presentationVersionId ?? null,
        currentItemIndex,
        playerVersion: '1.1.0-media',
        browserSummary: navigator.userAgent,
        lastError,
        storageEstimate: { usageBytes: estimate?.usage ?? null, quotaBytes: estimate?.quota ?? null }
      })
    });
  } catch { /* Offline is an expected player state. */ }
};

const requestWakeLock = async () => {
  try { wakeLock = await navigator.wakeLock?.request('screen'); } catch { wakeLock = undefined; }
};

const start = async () => {
  if ('serviceWorker' in navigator) await navigator.serviceWorker.register('/player/sw.js', { scope: '/' });
  await requestWakeLock();
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible' && (!wakeLock || wakeLock.released)) void requestWakeLock(); });
  deviceToken = await getValue<string>('deviceToken');
  assignmentEtag = await getValue<string>('assignmentEtag');
  const activeContentId = await getValue<string>('activeContentId');
  if (activeContentId) {
    const cached = await getValue<Manifest>(`package:${activeContentId}`);
    if (cached) {
      try { activate(parseManifest(cached, activeContentId)); } catch (error) { lastError = String(error); }
    }
  }
  if (!deviceToken) deviceToken = await pair();
  await pollAssignment();
  window.setInterval(() => void pollAssignment(), 60_000);
  window.setInterval(() => void heartbeat(), 60_000);
};

void start().catch(error => {
  lastError = String(error);
  showMessage('Player could not start', 'Reload the browser or contact IT.');
});
