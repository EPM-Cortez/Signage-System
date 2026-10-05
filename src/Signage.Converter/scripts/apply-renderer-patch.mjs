// Compatibility fix for the pinned renderer. Never silently apply this to another version.
import { createHash } from 'node:crypto';
import { readFile, readdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { additionalTextPatches } from './text-layout-patches.mjs';

const packageRoot = resolve(import.meta.dirname, '../node_modules/pptx-glimpse');
const metadata = JSON.parse(await readFile(resolve(packageRoot, 'package.json'), 'utf8'));
if (metadata.version !== '3.2.8') throw new Error('Revalidate the renderer compatibility patches before upgrading pptx-glimpse.');
const marker = '// school-signage: signed-line-chart-v1';
const expectedHash = '9044a6546ebd0550694f3b056d0d29527b893c5779c4c5e3924f7afe37014511';
const textPatches = [
  {
    name: 'computeTextBody',
    marker: '// school-signage: inherited-text-body-v1',
    hash: 'f0ccee32bc0a22ef042f4a448816d526608ae82fe875912a91982c53d9bf6fde',
    apply: source => source.replace('  const properties = mergeTextBodyProperties2(void 0, textBody.properties);',
      `  // school-signage: inherited-text-body-v1
  // Inherited bodies are ordered nearest first (layout, master). Local values win.
  const inheritedProperties = inheritedBodies.reduceRight(
    (merged, body) => mergeTextBodyProperties2(merged, body?.properties), void 0
  );
  const properties = mergeTextBodyProperties2(inheritedProperties, textBody.properties);`)
  },
  {
    name: 'computeParagraphProperties',
    marker: '// school-signage: inherited-paragraph-spacing-v1',
    hash: '968df23e1adeb8db3d2f6852c1be3a870ddf440e28ade45277bd493a57480e03',
    apply: source => source
      .replace('  const merged = {', '  // school-signage: inherited-paragraph-spacing-v1\n  const merged = {')
      .replace('...local?.lineSpacing !== void 0 ? { lineSpacing: local.lineSpacing } : {},', '...local?.lineSpacing !== void 0 ? { lineSpacing: local.lineSpacing } : pickInherited(inherited, "lineSpacing"),')
      .replace('...local?.spaceBefore !== void 0 ? { spaceBefore: local.spaceBefore } : {},', '...local?.spaceBefore !== void 0 ? { spaceBefore: local.spaceBefore } : pickInherited(inherited, "spaceBefore"),')
      .replace('...local?.spaceAfter !== void 0 ? { spaceAfter: local.spaceAfter } : {},', '...local?.spaceAfter !== void 0 ? { spaceAfter: local.spaceAfter } : pickInherited(inherited, "spaceAfter"),')
  },
  ...additionalTextPatches
];
const pending = [];
let checked = 0;
const textChecked = new Map(textPatches.map(patch => [patch.name, 0]));
for (const file of await readdir(resolve(packageRoot, 'dist'))) {
  if (!/\.(c?js)$/.test(file)) continue;
  const path = resolve(packageRoot, 'dist', file);
  let source = await readFile(path, 'utf8');
  const initial = source;
  for (const patch of textPatches) {
    const start = source.indexOf(`function ${patch.name}(`);
    if (start < 0) continue;
    const end = source.indexOf('\nfunction ', start + 1);
    if (end < 0) throw new Error(`Unknown ${patch.name} function boundary in ${file}`);
    const original = source.slice(start, end);
    textChecked.set(patch.name, textChecked.get(patch.name) + 1);
    if (original.includes(patch.marker)) continue;
    const hashes = Array.isArray(patch.hash) ? patch.hash : [patch.hash];
    if (!hashes.includes(createHash('sha256').update(original).digest('hex'))) throw new Error(`${patch.name} patch checksum mismatch in ${file}`);
    const patched = patch.apply(original);
    if (!patched.includes(patch.marker)) throw new Error(`Incomplete ${patch.name} patch in ${file}`);
    source = source.slice(0, start) + patched + source.slice(end);
  }
  const start = source.indexOf('function renderLineChart(');
  if (start < 0) {
    if (source !== initial) pending.push({ path, content: source });
    continue;
  }
  const end = source.indexOf('\nfunction renderAreaChart(', start);
  if (end < 0) throw new Error(`Unknown renderer function boundary in ${file}`);
  const original = source.slice(start, end);
  checked++;
  if (original.includes(marker)) {
    if (source !== initial) pending.push({ path, content: source });
    continue;
  }
  if (createHash('sha256').update(original).digest('hex') !== expectedHash) throw new Error(`Renderer patch checksum mismatch in ${file}`);
  const patched = original
    .replace('  const maxVal = getMaxValue(series);\n  if (maxVal === 0) {\n    debugChart(context, "chart.line", "max value is 0");',
      `  ${marker}\n  const values = series.flatMap((s) => s.values).filter(Number.isFinite);\n  const minVal = Math.min(0, ...values);\n  const maxVal = Math.max(0, ...values);\n  if (values.length === 0) {\n    debugChart(context, "chart.line", "no finite values");`)
    .replace('  const ticks = computeNiceTicks(0, maxVal);\n  const scaleMax = ticks[ticks.length - 1];',
      '  const ticks = computeNiceTicks(minVal, maxVal === minVal ? maxVal + 1 : maxVal);\n  const scaleMin = ticks[0];\n  const scaleMax = ticks[ticks.length - 1];')
    .replace('renderValueAxisLabels(ticks, 0, scaleMax, x, y, h)', 'renderValueAxisLabels(ticks, scaleMin, scaleMax, x, y, h)')
    .replace('y + h - val / scaleMax * h', 'y + h - (val - scaleMin) / (scaleMax - scaleMin) * h');
  if (!patched.includes(marker) || patched.includes('val / scaleMax') || !patched.includes('renderValueAxisLabels(ticks, scaleMin')) throw new Error(`Incomplete renderer patch in ${file}`);
  pending.push({ path, content: source.slice(0, start) + patched + source.slice(end) });
}
if (checked !== 4 || [...textChecked.values()].some(count => count !== 4)) throw new Error('Could not locate all pinned renderer bundles');
for (const file of pending) await writeFile(file.path, file.content);
console.log(`Line-chart and text-layout patches verified (${checked} renderer bundles).`);
