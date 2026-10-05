export type Placement = { x: number; y: number; width: number; height: number; rotation?: number; flipHorizontal?: boolean; flipVertical?: boolean };
export type Playback = { startupTimeoutMs: number; stallTimeoutMs: number; maximumDurationMs: number; startSeconds?: number; endSeconds?: number | null };
export type Asset = { asset: string; sha256: string };
export type ManifestItem = Asset & {
  type: 'image' | 'video' | 'youtube';
  sourceSlideNumber: number;
  durationMs: number;
  transition: { type: 'cut' | 'fade'; durationMs: number };
  background?: Asset | null;
  placement?: Placement | null;
  playback?: Playback | null;
  youTubeVideoId?: string | null;
};
export type Manifest = {
  schemaVersion: number;
  contentId: string;
  presentationVersionId: string;
  loop: boolean;
  canvas?: { width: number; height: number };
  items: ManifestItem[];
};

const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object';
const finite = (value: unknown, minimum: number, maximum: number) => typeof value === 'number' && Number.isFinite(value) && value >= minimum && value <= maximum;
const validAsset = (value: unknown): value is Asset & Record<string, unknown> => object(value) && typeof value.asset === 'string' &&
  /^(slides\/slide-\d{4,}\.png|media\/[a-f0-9]{64}\.(mp4|webm))$/.test(value.asset) &&
  typeof value.sha256 === 'string' && /^[a-fA-F0-9]{64}$/.test(value.sha256);

export const parseManifest = (value: unknown, contentId?: string): Manifest => {
  if (!object(value) || ![1, 2].includes(value.schemaVersion as number) || typeof value.contentId !== 'string' ||
    !/^[a-f0-9]{64}$/.test(value.contentId) || (contentId && value.contentId !== contentId) ||
    typeof value.presentationVersionId !== 'string' || typeof value.loop !== 'boolean' || !Array.isArray(value.items) || !value.items.length || value.items.length > 5000) {
    throw new Error('Unknown or invalid manifest');
  }
  if (value.canvas !== undefined && (!object(value.canvas) || !finite(value.canvas.width, 1, 100000) || !finite(value.canvas.height, 1, 100000))) throw new Error('Invalid canvas');
  for (const item of value.items) {
    if (!validAsset(item) || !object(item) || !['image', 'video', 'youtube'].includes(item.type as string) ||
      !finite(item.sourceSlideNumber, 1, 5000) || !finite(item.durationMs, 250, 600000) || !object(item.transition) ||
      !['cut', 'fade'].includes(item.transition.type as string) || !finite(item.transition.durationMs, 0, 10000)) throw new Error('Invalid manifest item');
    if (item.type === 'youtube' && (value.schemaVersion !== 2 || typeof item.youTubeVideoId !== 'string' || !/^[A-Za-z0-9_-]{11}$/.test(item.youTubeVideoId))) throw new Error('Invalid YouTube video ID');
    if (item.type !== 'video' && !item.asset.endsWith('.png')) throw new Error('Invalid slide background');
    if (item.type === 'video' && !/\.(mp4|webm)$/.test(item.asset)) throw new Error('Invalid video asset');
    if (item.background != null && (!validAsset(item.background) || !item.background.asset.endsWith('.png'))) throw new Error('Invalid media background');
    if (item.placement != null) {
      const p = item.placement;
      if (!object(p) || !finite(p.x, -10, 10) || !finite(p.y, -10, 10) || !finite(p.width, 0.000001, 20) || !finite(p.height, 0.000001, 20) ||
        (p.rotation != null && !finite(p.rotation, -36000, 36000)) || (p.flipHorizontal != null && typeof p.flipHorizontal !== 'boolean') ||
        (p.flipVertical != null && typeof p.flipVertical !== 'boolean')) throw new Error('Invalid video placement');
    }
    if (item.playback != null) {
      const p = item.playback;
      if (!object(p) || !finite(p.startupTimeoutMs, 2000, 120000) || !finite(p.stallTimeoutMs, 5000, 300000) || !finite(p.maximumDurationMs, 10000, 14400000) ||
        (p.startSeconds != null && !finite(p.startSeconds, 0, 86400)) || (p.endSeconds != null && (!finite(p.endSeconds, 0, 86400) || (p.endSeconds as number) <= (typeof p.startSeconds === 'number' ? p.startSeconds : 0)))) throw new Error('Invalid video timing');
    }
  }
  return value as unknown as Manifest;
};

export const packageAssets = (manifest: Manifest): Asset[] => {
  const assets = new Map<string, Asset>();
  for (const item of manifest.items) {
    for (const asset of [item, item.background].filter((a): a is Asset => a != null)) {
      if (assets.has(asset.asset) && assets.get(asset.asset)!.sha256.toLowerCase() !== asset.sha256.toLowerCase()) throw new Error('Conflicting asset hashes');
      assets.set(asset.asset, { asset: asset.asset, sha256: asset.sha256 });
    }
  }
  return [...assets.values()];
};

export const playbackSettings = (item: ManifestItem): Playback => item.playback ?? { startupTimeoutMs: 20000, stallTimeoutMs: 30000, maximumDurationMs: 3600000, startSeconds: 0 };
