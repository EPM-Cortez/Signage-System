import { convertPptxToPng, type ConversionDiagnostic, type ConvertOptions, type PngConversionReport } from 'pptx-glimpse';
import { loadRenderFonts } from './fonts.js';

export type RenderSettings = {
  outputWidth: number;
  fontDirectories: string[];
  useSystemFonts?: boolean;
  fontMapping?: Record<string, string>;
  fitTextToBox?: boolean;
  requestedSourceSlideNumbers: number[];
  timeoutSeconds: number;
  diagnosticVerbosity: 'quiet' | 'normal' | 'debug';
};

export type AdapterResult = {
  slides: PngConversionReport['slides'];
  diagnostics: readonly ConversionDiagnostic[];
  supportCoverage: PngConversionReport['supportCoverage'];
};

export interface RendererAdapter {
  readonly name: string;
  readonly version: string;
  render(input: Uint8Array, settings: RenderSettings): Promise<AdapterResult>;
}

export class PptxGlimpseAdapter implements RendererAdapter {
  readonly name = 'pptx-glimpse';
  readonly version = '3.2.8';

  async render(input: Uint8Array, settings: RenderSettings): Promise<AdapterResult> {
    const fonts = await loadRenderFonts(settings.fontDirectories, settings.useSystemFonts, settings.fontMapping);
    // This extension is implemented by the version-checked compatibility patch.
    const options: ConvertOptions & { fitTextToBox: boolean } = {
      slides: settings.requestedSourceSlideNumbers,
      width: settings.outputWidth,
      fonts,
      fontMapping: settings.fontMapping,
      fitTextToBox: settings.fitTextToBox ?? true,
      logLevel: settings.diagnosticVerbosity === 'debug' ? 'debug' : 'off'
    };
    const report = await convertPptxToPng(input, options);
    return report;
  }
}

export const validateSettings = (value: unknown): RenderSettings => {
  if (!value || typeof value !== 'object') throw new Error('Settings must be an object');
  const candidate = value as Partial<RenderSettings>;
  if (!Number.isInteger(candidate.outputWidth) || candidate.outputWidth! < 320 || candidate.outputWidth! > 7680) throw new Error('Invalid output width');
  if (!Array.isArray(candidate.fontDirectories) || candidate.fontDirectories.some(item => typeof item !== 'string')) throw new Error('Invalid font directories');
  if (candidate.useSystemFonts !== undefined && typeof candidate.useSystemFonts !== 'boolean') throw new Error('Invalid system font setting');
  if (candidate.fitTextToBox !== undefined && typeof candidate.fitTextToBox !== 'boolean') throw new Error('Invalid text fitting setting');
  if (candidate.fontMapping !== undefined && (!candidate.fontMapping || typeof candidate.fontMapping !== 'object' || Array.isArray(candidate.fontMapping) || Object.entries(candidate.fontMapping).some(([name, replacement]) => !name.trim() || typeof replacement !== 'string' || !replacement.trim()))) throw new Error('Invalid font mapping');
  if (!Array.isArray(candidate.requestedSourceSlideNumbers) || candidate.requestedSourceSlideNumbers.length === 0 || candidate.requestedSourceSlideNumbers.some(item => !Number.isInteger(item) || item < 1)) throw new Error('Invalid requested slides');
  if (!Number.isInteger(candidate.timeoutSeconds) || candidate.timeoutSeconds! < 1) throw new Error('Invalid timeout');
  if (!['quiet', 'normal', 'debug'].includes(candidate.diagnosticVerbosity ?? '')) throw new Error('Invalid diagnostic verbosity');
  return candidate as RenderSettings;
};
