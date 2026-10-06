import { decode } from 'jpeg-js';
import { PNG } from 'pngjs';

// resvg-wasm 2.6.2 silently omits four-component CMYK/YCCK JPEGs.
// Read the JPEG frame header without decoding ordinary RGB/grayscale images.
function jpegComponents(bytes: Uint8Array): number | undefined {
  if (bytes[0] !== 0xff || bytes[1] !== 0xd8) return undefined;
  let offset = 2;
  while (offset < bytes.length) {
    if (bytes[offset++] !== 0xff) return undefined;
    while (bytes[offset] === 0xff) offset++;
    const marker = bytes[offset++];
    if (marker === undefined || marker === 0xda || marker === 0xd9) return undefined;
    if (marker === 0x01 || (marker >= 0xd0 && marker <= 0xd8)) continue;
    if (offset + 2 > bytes.length) return undefined;
    const length = bytes[offset]! * 256 + bytes[offset + 1]!;
    if (length < 2 || offset + length > bytes.length) return undefined;
    if (marker >= 0xc0 && marker <= 0xcf && ![0xc4, 0xc8, 0xcc].includes(marker)) {
      return length >= 8 ? bytes[offset + 7] : undefined;
    }
    offset += length;
  }
  return undefined;
}

/** Normalize only embedded JPEGs in renderer-produced SVG; never alter the PPTX. */
export function createCmykImageNormalizer() {
  const cache = new Map<string, string>();
  return (svg: string): { svg: string; convertedImages: number } => {
    const converted = new Set<string>();
    const normalized = svg.replace(/data:image\/jpeg;base64,([A-Za-z0-9+/=]+)/g, (uri: string, base64: string) => {
      let replacement = cache.get(uri);
      if (replacement === undefined) {
        const bytes = Buffer.from(base64, 'base64');
        replacement = uri;
        if (jpegComponents(bytes) === 4) {
          try {
            const image = decode(bytes, { maxResolutionInMP: 40, maxMemoryUsageInMB: 256 });
            const png = new PNG({ width: image.width, height: image.height });
            png.data = image.data;
            replacement = `data:image/png;base64,${PNG.sync.write(png).toString('base64')}`;
          } catch (error) {
            throw new Error('CMYK_IMAGE_CONVERSION_FAILED: Could not decode an embedded CMYK/YCCK JPEG.', { cause: error });
          }
        }
        cache.set(uri, replacement);
      }
      if (replacement !== uri) converted.add(uri);
      return replacement;
    });
    return { svg: normalized, convertedImages: converted.size };
  };
}
