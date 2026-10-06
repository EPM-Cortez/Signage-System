#!/usr/bin/env node
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { isAbsolute, join, resolve } from 'node:path';
import process from 'node:process';
import { PptxGlimpseAdapter, validateSettings } from './adapter.js';

type ParsedArguments = { command: 'health' } | { command: 'render'; input: string; output: string; settings: string };

export const parseArguments = (values: string[]): ParsedArguments => {
  const [command, ...rest] = values;
  if (command === 'health') return { command: 'health' };
  if (command !== 'render') throw new Error('Usage: cli.js health | render --input <pptx> --output <directory> --settings <json>');
  const option = (name: string): string => {
    const index = rest.indexOf(name);
    if (index < 0 || !rest[index + 1]) throw new Error(`Missing ${name}`);
    return resolve(rest[index + 1]!);
  };
  return { command: 'render', input: option('--input'), output: option('--output'), settings: option('--settings') };
};

const progress = (event: string, detail: Record<string, unknown> = {}) => process.stdout.write(`${JSON.stringify({ event, utc: new Date().toISOString(), ...detail })}\n`);

const run = async (): Promise<number> => {
  const args = parseArguments(process.argv.slice(2));
  const adapter = new PptxGlimpseAdapter();
  if (args.command === 'health') {
    progress('healthy', { nodeVersion: process.version, converterVersion: '1.0.0', renderer: adapter.name, rendererVersion: adapter.version });
    return 0;
  }

  if (![args.input, args.output, args.settings].every(isAbsolute)) throw new Error('All converter paths must be absolute');
  const settings = validateSettings(JSON.parse(await readFile(args.settings, 'utf8')) as unknown);
  await mkdir(join(args.output, 'slides'), { recursive: true });
  progress('render-started', { slideCount: settings.requestedSourceSlideNumbers.length });
  const started = performance.now();
  const input = new Uint8Array(await readFile(args.input));
  const result = await adapter.render(input, settings, (rendered, total) => progress('slide-rasterized', { index: rendered, total }));
  if (result.slides.length !== settings.requestedSourceSlideNumbers.length) throw new Error(`Renderer returned ${result.slides.length} of ${settings.requestedSourceSlideNumbers.length} requested slides`);

  for (let index = 0; index < result.slides.length; index++) {
    const slide = result.slides[index]!;
    await writeFile(join(args.output, 'slides', `slide-${String(index + 1).padStart(4, '0')}.png`), slide.png, { flag: 'wx' });
    progress('slide-rendered', { index: index + 1, sourceSlideNumber: slide.slideNumber, width: slide.width, height: slide.height });
  }
  const elapsedMs = Math.round(performance.now() - started);
  const report = {
    converterVersion: '1.0.0', nodeVersion: process.version, renderer: adapter.name, rendererVersion: adapter.version, elapsedMs,
    slides: result.slides.map((slide, index) => ({ outputIndex: index + 1, sourceSlideNumber: slide.slideNumber, width: slide.width, height: slide.height })),
    supportCoverage: result.supportCoverage
  };
  await writeFile(join(args.output, 'render-report.json'), JSON.stringify(report, null, 2), { flag: 'wx' });
  await writeFile(join(args.output, 'diagnostics.json'), JSON.stringify({ diagnostics: result.diagnostics }, null, 2), { flag: 'wx' });
  progress('render-completed', { slideCount: result.slides.length, elapsedMs, warningCount: result.diagnostics.filter(item => item.severity === 'warning').length });
  return 0;
};

if (process.env.NODE_ENV !== 'test') {
  run().then(code => { process.exitCode = code; }).catch(error => {
    const message = error instanceof Error ? error.message : String(error);
    process.stderr.write(`${message.slice(0, 16_384)}\n`);
    progress('render-failed', { code: 'RENDER_FAILED' });
    process.exitCode = 1;
  });
}
