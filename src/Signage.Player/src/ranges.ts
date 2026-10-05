// A fully cached media response must still answer single byte-range requests
// (including suffix ranges) for offline seeking in native video elements.
export const cachedRangeResponse = async (response: Response, range: string | null): Promise<Response> => {
  if (!range) return response;
  const match = /^bytes=(\d*)-(\d*)$/.exec(range);
  const blob = await response.blob();
  const size = blob.size;
  const invalid = () => new Response(null, { status: 416, headers: { 'Content-Range': `bytes */${size}` } });
  if (!match || (!match[1] && !match[2]) || !size) return invalid();
  const start = match[1] ? Number(match[1]) : Math.max(0, size - Number(match[2]));
  const end = match[1] ? (match[2] ? Math.min(Number(match[2]), size - 1) : size - 1) : size - 1;
  if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start < 0 || start >= size || end < start || (!match[1] && Number(match[2]) <= 0)) return invalid();
  const headers = new Headers(response.headers);
  headers.set('Content-Range', `bytes ${start}-${end}/${size}`);
  headers.set('Accept-Ranges', 'bytes');
  headers.set('Content-Length', String(end - start + 1));
  return new Response(blob.slice(start, end + 1, headers.get('Content-Type') ?? 'application/octet-stream'), { status: 206, headers });
};
