import { readFile, readdir } from 'node:fs/promises';
import { homedir } from 'node:os';
import { extname, join, resolve } from 'node:path';
import { createOpentypeSetupFromBuffers, type FontBuffer, type FontMapping } from 'pptx-glimpse';

const fontExtensions = new Set(['.ttf', '.otf', '.ttc']);

const systemFontDirectories = (): string[] => {
  if (process.platform === 'win32') return [
    join(process.env.WINDIR ?? 'C:\\Windows', 'Fonts'),
    join(homedir(), 'AppData', 'Local', 'Microsoft', 'Windows', 'Fonts')
  ];
  if (process.platform === 'darwin') return ['/System/Library/Fonts', '/Library/Fonts', join(homedir(), 'Library/Fonts')];
  return ['/usr/share/fonts', '/usr/local/share/fonts', join(homedir(), '.local', 'share', 'fonts')];
};

/** Load local font bytes explicitly: an empty directory must never produce textless PNGs. */
export async function loadRenderFonts(directories: string[], useSystemFonts = false, fontMapping?: FontMapping): Promise<FontBuffer[]> {
  const paths = new Set<string>();
  const visit = async (directory: string): Promise<void> => {
    let entries;
    try {
      entries = await readdir(directory, { withFileTypes: true });
    } catch (error) {
      if (['ENOENT', 'EACCES', 'EPERM'].includes((error as NodeJS.ErrnoException).code ?? '')) return;
      throw error;
    }
    // Do not follow symlinks. Fonts are administrator-controlled, never extracted from a PPTX.
    for (const entry of entries.sort((left, right) => left.name.localeCompare(right.name))) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) await visit(path);
      else if (entry.isFile() && fontExtensions.has(extname(entry.name).toLowerCase())) paths.add(resolve(path));
    }
  };
  // Register configured fonts first so they provide a predictable fallback family.
  for (const directory of directories) await visit(resolve(directory));
  if (useSystemFonts) for (const directory of systemFontDirectories()) await visit(directory);
  if (paths.size > 2_000) throw new Error('Too many configured font files');
  const fonts: FontBuffer[] = [];
  const orderedPaths = [...paths].sort((left, right) => {
    const priority = (path: string) => /[-_](Regular|Roman|Book)\.[^.]+$/i.test(path) ? 0 : 1;
    return priority(left) - priority(right) || left.localeCompare(right);
  });
  for (const path of orderedPaths) {
    try {
      fonts.push({ data: new Uint8Array(await readFile(path)) });
    } catch (error) {
      if (!['EACCES', 'EPERM'].includes((error as NodeJS.ErrnoException).code ?? '')) throw error;
    }
  }
  if (await createOpentypeSetupFromBuffers(fonts, fontMapping) === null) {
    throw new Error('NO_USABLE_FONTS: No readable TTF/OTF/TTC fonts could be loaded. Install the bundled fonts or enable approved system fonts. Existing screen content has not been changed.');
  }
  return fonts;
}
