export {};
import { cachedRangeResponse } from './ranges';
const worker = self as unknown as ServiceWorkerGlobalScope;

const cacheName = 'school-signage-packages-v1';
const shellCacheName = 'school-signage-shell-v2-__PLAYER_SHELL_VERSION__';
const shellUrls: string[] = JSON.parse('__PLAYER_SHELL_URLS__');
worker.addEventListener('install', event => event.waitUntil((async () => {
  await (await caches.open(shellCacheName)).addAll(shellUrls);
  await worker.skipWaiting();
})()));
worker.addEventListener('activate', event => event.waitUntil((async () => {
  await Promise.all((await caches.keys()).filter(name => name.startsWith('school-signage-shell-') && name !== shellCacheName).map(name => caches.delete(name)));
  await worker.clients.claim();
})()));
worker.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET') return;
  if (url.pathname.startsWith('/player/')) {
    event.respondWith((async () => (await caches.match(event.request)) ?? fetch(event.request))());
    return;
  }
  if (!url.pathname.startsWith('/content/')) return;
  event.respondWith((async () => {
    const cache = await caches.open(cacheName);
    const cached = await cache.match(event.request.url);
    if (cached) return cachedRangeResponse(cached, event.request.headers.get('Range'));
    return fetch(event.request);
  })());
});
