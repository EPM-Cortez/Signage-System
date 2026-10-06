import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { zipSync, unzipSync, strToU8, strFromU8 } from 'fflate';
import { PNG } from 'pngjs';
import { convertPptxToPng, convertPptxToSvg } from 'pptx-glimpse';
import { encode } from 'jpeg-js';
import { createCmykImageNormalizer } from './images.js';
import { PptxGlimpseAdapter, type RenderSettings } from './adapter.js';
import { loadRenderFonts } from './fonts.js';

// Internally authored OOXML test data. No private school presentations are test fixtures.
const ns = 'xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"';
const drawing = 'http://schemas.openxmlformats.org/drawingml/2006';
const relationship = 'http://schemas.openxmlformats.org/package/2006/relationships';
const officeRelationship = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships';
const settings: RenderSettings = {
  outputWidth: 960, fontDirectories: [resolve(import.meta.dirname, '../../../fonts')], useSystemFonts: false,
  fontMapping: { Aptos: 'Carlito' }, requestedSourceSlideNumbers: [1], timeoutSeconds: 30, diagnosticVerbosity: 'normal'
};
// Original 8x4 red/blue test image encoded as an Adobe CMYK JPEG.
// This contains no school presentation data or third-party artwork.
const cmykJpeg = Buffer.from('/9j/7gAOQWRvYmUAZAAAAAAA/9sAQwABAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB/8AAFAgABAAIBEMRAE0RAFkRAEsRAP/EAB8AAAEFAQEBAQEBAAAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscEVUtHwJDNicoIJChYXGBkaJSYnKCkqNDU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6g4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2drh4uPk5ebn6Onq8fLz9PX29/j5+v/aAA4EQwBNAFkASwAAPwCP/g+c/wCcXX/d7P8A76PX+f8A1H/wYx/85Rf+7Jv/AH7iv7+K/9k=', 'base64');
const escapeXml = (text: string) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
const textBody = (text: string, font: string, size = 4_000) => `<a:p><a:r><a:rPr lang="en-GB" sz="${size}"><a:solidFill><a:srgbClr val="000000"/></a:solidFill><a:latin typeface="${escapeXml(font)}"/></a:rPr><a:t>${escapeXml(text)}</a:t></a:r><a:endParaRPr lang="en-GB"/></a:p>`;
const textShape = (font: string) => `<p:sp><p:nvSpPr><p:cNvPr id="2" name="Regression text"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="914400" y="914400"/><a:ext cx="9144000" cy="1828800"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/>${textBody('Visible text 123', font)}</p:txBody></p:sp>`;

function fixture(content: string, chartXml?: string, ratio43 = false): Uint8Array {
  const files: Record<string, string> = {
    '[Content_Types].xml': `<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/><Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>${chartXml ? '<Override PartName="/ppt/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>' : ''}</Types>`,
    '_rels/.rels': `<Relationships xmlns="${relationship}"><Relationship Id="rId1" Type="${officeRelationship}/officeDocument" Target="ppt/presentation.xml"/></Relationships>`,
    'ppt/presentation.xml': `<p:presentation ${ns}><p:sldIdLst><p:sldId id="256" r:id="rId1"/></p:sldIdLst><p:sldSz cx="${ratio43 ? '9144000' : '12192000'}" cy="6858000"/></p:presentation>`,
    'ppt/_rels/presentation.xml.rels': `<Relationships xmlns="${relationship}"><Relationship Id="rId1" Type="${officeRelationship}/slide" Target="slides/slide1.xml"/></Relationships>`,
    'ppt/slides/slide1.xml': `<p:sld ${ns}><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill></p:bgPr></p:bg><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>${content}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>`
  };
  if (chartXml) {
    files['ppt/charts/chart1.xml'] = chartXml;
    files['ppt/slides/_rels/slide1.xml.rels'] = `<Relationships xmlns="${relationship}"><Relationship Id="rIdChart" Type="${officeRelationship}/chart" Target="../charts/chart1.xml"/></Relationships>`;
  }
  return zipSync(Object.fromEntries(Object.entries(files).map(([path, xml]) => [path, strToU8(xml)])));
}

function pictureFixture(bytes: Uint8Array): Uint8Array {
  const picture = `<p:pic><p:nvPicPr><p:cNvPr id="2" name="Colour test"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed="rIdImage"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="12192000" cy="6858000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>`;
  const files = unzipSync(fixture(picture));
  files['[Content_Types].xml'] = strToU8(strFromU8(files['[Content_Types].xml']!).replace('</Types>', '<Default Extension="JPG" ContentType="image/jpeg"/></Types>'));
  files['ppt/slides/_rels/slide1.xml.rels'] = strToU8(`<Relationships xmlns="${relationship}"><Relationship Id="rIdImage" Type="${officeRelationship}/image" Target="../media/colour.JPG"/></Relationships>`);
  files['ppt/media/colour.JPG'] = new Uint8Array(bytes);
  return zipSync(files);
}

test('CMYK pictures render their colours instead of silently disappearing, without changing the PPTX', async () => {
  const input = pictureFixture(cmykJpeg);
  const before = new Uint8Array(input);
  const result = await new PptxGlimpseAdapter().render(input, settings);
  const png = PNG.sync.read(Buffer.from(result.slides[0]!.png));
  const sample = (x: number) => [...png.data.subarray((270 * png.width + x) * 4, (270 * png.width + x) * 4 + 4)];
  assert.deepEqual(sample(120), [255, 0, 0, 255]);
  assert.deepEqual(sample(840), [0, 0, 255, 255]);
  assert.ok(result.diagnostics.some(d => d.code === 'renderer.image.cmykConverted' && d.slideNumber === 1));
  assert.deepEqual(input, before);
});

test('ordinary RGB JPEG pictures retain the exact upstream PNG output', async () => {
  const jpeg = encode({ width: 8, height: 4, data: Buffer.from(Array.from({ length: 32 }, () => [30, 180, 60, 255]).flat()) }, 100).data;
  const input = pictureFixture(jpeg);
  const fonts = await loadRenderFonts(settings.fontDirectories, settings.useSystemFonts, settings.fontMapping);
  const upstream = await convertPptxToPng(input, { slides: [1], width: 960, fonts, fontMapping: settings.fontMapping });
  const result = await new PptxGlimpseAdapter().render(input, settings);
  assert.deepEqual(result.slides[0]!.png, upstream.slides[0]!.png);
  assert.ok(!result.diagnostics.some(d => d.code === 'renderer.image.cmykConverted'));
});

test('CMYK normalization covers shared pictures, fills and backgrounds without touching other SVG', () => {
  const uri = `data:image/jpeg;base64,${cmykJpeg.toString('base64')}`;
  const normalize = createCmykImageNormalizer();
  const svg = `<svg><image href="${uri}"/><pattern><image href="${uri}"/></pattern></svg>`;
  const first = normalize(svg);
  assert.equal(first.convertedImages, 1);
  assert.equal((first.svg.match(/data:image\/png;base64,/g) ?? []).length, 2);
  assert.deepEqual(normalize(svg), first, 'Shared images must remain visible on later slides');
  const ordinary = '<svg><image href="data:image/jpeg;base64,/9j/2Q=="/><image href="data:image/png;base64,AAAA"/></svg>';
  assert.equal(normalize(ordinary).svg, ordinary);
});

test('corrupt or oversized CMYK JPEGs fail conversion instead of publishing a blank picture', () => {
  const frame = cmykJpeg.indexOf(Buffer.from([0xff, 0xc0]));
  const truncated = cmykJpeg.subarray(0, frame + 22);
  const oversized = Buffer.from(cmykJpeg);
  oversized.writeUInt16BE(50_000, frame + 5);
  oversized.writeUInt16BE(50_000, frame + 7);
  for (const bytes of [truncated, oversized]) {
    assert.throws(() => createCmykImageNormalizer()(`<image href="data:image/jpeg;base64,${bytes.toString('base64')}"/>`), /CMYK_IMAGE_CONVERSION_FAILED/);
  }
});

const chartFrame = `<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id="3" name="Regression chart"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x="914400" y="914400"/><a:ext cx="9144000" cy="4572000"/></p:xfrm><a:graphic><a:graphicData uri="${drawing}/chart"><c:chart xmlns:c="${drawing}/chart" r:id="rIdChart"/></a:graphicData></a:graphic></p:graphicFrame>`;
const chart = (values: number[], type = 'line', cache = 'literal') => {
  const points = values.map((value, index) => `<c:pt idx="${index}"><c:v>${value}</c:v></c:pt>`).join('');
  const numericData = cache === 'literal' ? `<c:numLit><c:formatCode>General</c:formatCode><c:ptCount val="${values.length}"/>${points}</c:numLit>` : `<c:numRef><c:f>Sheet1!$B$2:$B$4</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="${values.length}"/>${points}</c:numCache></c:numRef>`;
  return `<c:chartSpace xmlns:c="${drawing}/chart" xmlns:a="${drawing}/main"><c:chart><c:plotArea><c:${type}Chart>${type === 'bar' ? '<c:barDir val="col"/>' : ''}<c:ser><c:idx val="0"/><c:order val="0"/><c:tx><c:v>Test series</c:v></c:tx><c:cat><c:strLit><c:ptCount val="3"/><c:pt idx="0"><c:v>One</c:v></c:pt><c:pt idx="1"><c:v>Two</c:v></c:pt><c:pt idx="2"><c:v>Three</c:v></c:pt></c:strLit></c:cat><c:val>${numericData}</c:val></c:ser></c:${type}Chart></c:plotArea></c:chart></c:chartSpace>`;
};

function pixels(png: Uint8Array, kind: 'text' | 'chart' | 'labels'): number {
  const decoded = PNG.sync.read(Buffer.from(png));
  let count = 0;
  for (let index = 0; index < decoded.data.length; index += 4) {
    const r = decoded.data[index]!; const g = decoded.data[index + 1]!; const b = decoded.data[index + 2]!; const a = decoded.data[index + 3]!;
    const x = (index / 4) % decoded.width; const y = Math.floor(index / 4 / decoded.width);
    const matches = kind === 'text' ? r < 100 && g < 100 && b < 100
      : kind === 'chart' ? b > r + 30 && b > g + 10
      // Small grey chart labels are anti-aliased. Only examine the category-label strip,
      // excluding axes, chart borders and coloured data points.
      : x >= 90 && x <= 805 && y >= 412 && y <= 430 && r < 220 && g < 220 && b < 220 && Math.max(r, g, b) - Math.min(r, g, b) < 10;
    if (a > 128 && matches) count++;
  }
  return count;
}

for (const font of ['Arial', 'Calibri', 'Aptos', 'Times New Roman', 'Cambria', 'Courier New', 'Font That Is Not Installed']) {
  test(`text pixels survive conversion with ${font} using only bundled fonts`, async () => {
    const result = await new PptxGlimpseAdapter().render(fixture(textShape(font)), settings);
    assert.equal(result.slides.length, 1);
    assert.equal(result.slides[0]!.width, 960);
    assert.equal(result.slides[0]!.height, 540);
    // Broad glyph-pixel threshold tolerates anti-aliasing, but not an empty text box.
    assert.ok(pixels(result.slides[0]!.png, 'text') > 1_000);
    if (font === 'Font That Is Not Installed') assert.ok(result.diagnostics.some(d => d.code.includes('font.notFound')));
  });
}

for (const [label, values] of Object.entries({ negative: [-1, -2, -0.5], mixed: [-1, 1, 0], positive: [1, 2, 0.5], zero: [0, 0, 0] })) {
  for (const cache of ['literal', 'reference']) {
    test(`${label} line chart with ${cache} data has visible series and bounded geometry`, async () => {
      const input = fixture(chartFrame, chart(values, 'line', cache));
      const fonts = await loadRenderFonts(settings.fontDirectories);
      const svg = (await convertPptxToSvg(input, { fonts })).slides[0]!.svg;
      const polyline = /<polyline points="([^"]+)"/.exec(svg);
      assert.ok(polyline, 'Chart data must produce a polyline');
      assert.doesNotMatch(svg, /NaN|Infinity/);
      const coordinates = polyline[1]!.split(' ').map(p => p.split(',').map(Number));
      assert.equal(coordinates.length, 3);
      for (const [x, y] of coordinates) assert.ok(x! >= 0 && x! <= 960 && y! >= 0 && y! <= 480, 'Chart point must remain inside its frame');
      if (label === 'negative') {
        assert.ok(coordinates[1]![1]! > coordinates[0]![1]! && coordinates[0]![1]! > coordinates[2]![1]!, 'More negative values must appear lower on the chart');
        assert.match(svg, />-/, 'Negative axis labels must remain negative');
      }
      const result = await new PptxGlimpseAdapter().render(input, settings);
      assert.ok(pixels(result.slides[0]!.png, 'chart') > 200, 'Chart must also survive PNG rasterisation');
      assert.ok(pixels(result.slides[0]!.png, 'labels') > 20, 'Chart category labels must also be visible');
    });
  }
}

for (const type of ['bar', 'pie']) test(`${type} chart remains visible`, async () => {
  const result = await new PptxGlimpseAdapter().render(fixture(chartFrame, chart([1, 2, 3], type)), settings);
  assert.ok(pixels(result.slides[0]!.png, 'chart') > 200);
});

test('4:3 slides preserve dimensions and visible text', async () => {
  const result = await new PptxGlimpseAdapter().render(fixture(textShape('Arial'), undefined, true), settings);
  assert.equal(result.slides[0]!.width, 960);
  assert.equal(result.slides[0]!.height, 720);
  assert.ok(pixels(result.slides[0]!.png, 'text') > 1_000);
});

test('native table cell text survives rasterisation', async () => {
  const cell = (text: string) => `<a:tc><a:txBody><a:bodyPr/><a:lstStyle/>${textBody(text, 'Calibri', 3_000)}</a:txBody><a:tcPr/></a:tc>`;
  const table = `<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id="4" name="Regression table"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x="914400" y="914400"/><a:ext cx="7315200" cy="1828800"/></p:xfrm><a:graphic><a:graphicData uri="${drawing}/table"><a:tbl><a:tblPr/><a:tblGrid><a:gridCol w="3657600"/><a:gridCol w="3657600"/></a:tblGrid><a:tr h="914400">${cell('Header one')}${cell('Header two')}</a:tr><a:tr h="914400">${cell('Table data')}${cell('12345')}</a:tr></a:tbl></a:graphicData></a:graphic></p:graphicFrame>`;
  const result = await new PptxGlimpseAdapter().render(fixture(table), settings);
  assert.ok(pixels(result.slides[0]!.png, 'text') > 1_000);
});

test('empty, missing and corrupt font directories fail instead of publishing textless slides', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'signage-font-test-'));
  t.after(async () => {
    assert.equal(resolve(dirname(directory)), resolve(tmpdir()));
    await rm(directory, { recursive: true, force: true });
  });
  for (const directories of [[], [directory], [join(directory, 'missing')]]) await assert.rejects(loadRenderFonts(directories), /NO_USABLE_FONTS/);
  await writeFile(join(directory, 'corrupt.ttf'), 'not a font');
  await assert.rejects(loadRenderFonts([directory]), /NO_USABLE_FONTS/);
  const result = await new PptxGlimpseAdapter().render(fixture(textShape('Arial')), { ...settings, fontDirectories: [directory, ...settings.fontDirectories] });
  assert.ok(pixels(result.slides[0]!.png, 'text') > 1_000);
});

const longHeading = 'Staff supporting interviews for the new school year';
const titleTextShape = (text = longHeading, body = '<a:noAutofit/>', style = 'b="1"', size = 6_000, paragraphProperties = '') =>
  `<p:sp><p:nvSpPr><p:cNvPr id="2" name="Layout regression text"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="914400" y="365760"/><a:ext cx="9144000" cy="1325563"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></p:spPr><p:txBody><a:bodyPr anchor="ctr">${body}</a:bodyPr><a:lstStyle/><a:p>${paragraphProperties}<a:r><a:rPr lang="en-GB" sz="${size}" ${style}><a:solidFill><a:srgbClr val="000000"/></a:solidFill><a:latin typeface="Calibri"/></a:rPr><a:t>${escapeXml(text)}</a:t></a:r></a:p></p:txBody></p:sp>`;

function textBounds(png: Uint8Array) {
  const decoded = PNG.sync.read(Buffer.from(png));
  let left = Infinity; let top = Infinity; let right = -Infinity; let bottom = -Infinity; let count = 0;
  for (let index = 0; index < decoded.data.length; index += 4) {
    if (decoded.data[index]! >= 100 || decoded.data[index + 1]! >= 100 || decoded.data[index + 2]! >= 100 || decoded.data[index + 3]! <= 128) continue;
    const x = index / 4 % decoded.width; const y = Math.floor(index / 4 / decoded.width);
    left = Math.min(left, x); top = Math.min(top, y); right = Math.max(right, x); bottom = Math.max(bottom, y); count++;
  }
  return { left, top, right, bottom, count };
}

// The title box above is x=72, y=28.8, w=720, h=104.375 at the test PNG width.
const assertTitleContained = (png: Uint8Array) => {
  const bounds = textBounds(png);
  assert.ok(bounds.count > 1_000, 'All fitting tests must still render visible text');
  assert.ok(bounds.left >= 72 && bounds.right < 792 && bounds.top >= 28 && bounds.bottom < 134,
    `Text must stay inside the original title box: ${JSON.stringify(bounds)}`);
};

test('long fixed-height headings fit their original boxes without clipping', async () => {
  const input = fixture(titleTextShape());
  const before = new Uint8Array(input);
  const result = await new PptxGlimpseAdapter().render(input, settings);
  assertTitleContained(result.slides[0]!.png);
  assert.ok(result.diagnostics.some(d => d.code === 'renderer.text.fitToBox'));
  assert.deepEqual(input, before, 'Rendering must not mutate the uploaded presentation');
});

test('text-fit opt-out preserves intentional source overflow', async () => {
  const result = await new PptxGlimpseAdapter().render(fixture(titleTextShape()), { ...settings, fitTextToBox: false });
  assert.ok(textBounds(result.slides[0]!.png).bottom >= 134, 'The original fixed-height overflow must remain when fitting is disabled');
  assert.ok(!result.diagnostics.some(d => d.code === 'renderer.text.fitToBox'));
});

test('non-wrapping text also fits horizontally', async () => {
  const input = fixture(titleTextShape().replace('anchor="ctr"', 'anchor="ctr" wrap="none"'));
  const result = await new PptxGlimpseAdapter().render(input, settings);
  assertTitleContained(result.slides[0]!.png);
});

test('paragraph indentation is included in text-fit measurements', async () => {
  const input = fixture(titleTextShape(longHeading + ' and school performance', '<a:noAutofit/>', 'b="1"', 6_000, '<a:pPr marL="1828800"/>'));
  const result = await new PptxGlimpseAdapter().render(input, settings);
  assertTitleContained(result.slides[0]!.png);
  assert.ok(textBounds(result.slides[0]!.png).left >= 216, 'Indentation must be preserved, not removed to fit text');
});

test('saved normal-autofit scale is honoured without growing text', async () => {
  const scaled = await new PptxGlimpseAdapter().render(fixture(titleTextShape('Saved scale', '<a:normAutofit fontScale="50000"/>', '', 6_000)), settings);
  const explicit = await new PptxGlimpseAdapter().render(fixture(titleTextShape('Saved scale', '<a:noAutofit/>', '', 3_000)), settings);
  assert.deepEqual(scaled.slides[0]!.png, explicit.slides[0]!.png);
});

test('shape-autofit retains the source request to grow rather than shrink', async () => {
  const result = await new PptxGlimpseAdapter().render(fixture(titleTextShape(longHeading, '<a:spAutoFit/>')), settings);
  assert.ok(textBounds(result.slides[0]!.png).bottom >= 134);
  assert.ok(!result.diagnostics.some(d => d.code === 'renderer.text.fitToBox'));
});

test('already fitting text is unchanged when the safeguard is enabled', async () => {
  const input = fixture(titleTextShape('Short heading', '<a:noAutofit/>', 'b="1"', 3_000));
  const fitted = await new PptxGlimpseAdapter().render(input, settings);
  const source = await new PptxGlimpseAdapter().render(input, { ...settings, fitTextToBox: false });
  assert.deepEqual(fitted.slides[0]!.png, source.slides[0]!.png);
  assert.ok(!fitted.diagnostics.some(d => d.code === 'renderer.text.fitToBox'));
});

test('regular, bold, italic and bold-italic use different glyph outlines', async () => {
  const fonts = await loadRenderFonts(settings.fontDirectories);
  const outlines = [];
  for (const style of ['', 'b="1"', 'i="1"', 'b="1" i="1"']) {
    const svg = (await convertPptxToSvg(fixture(titleTextShape('Visible styles', '<a:noAutofit/>', style, 3_000)), { fonts })).slides[0]!.svg;
    outlines.push([...svg.matchAll(/<path d="([^"]+)"/g)].map(match => match[1]).join('|'));
  }
  assert.equal(new Set(outlines).size, 4, 'Bold and italic flags must select their actual bundled font faces');
});

function masterFixture(body: string, masterBody: string, layoutBody: string, masterStyle: string): Uint8Array {
  const local = titleTextShape('Inherited layout', body, '', 3_000)
    .replace('<p:cNvSpPr txBox="1"/><p:nvPr/>', '<p:cNvSpPr/><p:nvPr><p:ph type="title"/></p:nvPr>');
  const files = unzipSync(fixture(local));
  const placeholder = (properties: string) => `<p:sp><p:nvSpPr><p:cNvPr id="2" name="Title placeholder"/><p:cNvSpPr/><p:nvPr><p:ph type="title"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr ${properties}/><a:lstStyle/><a:p/></p:txBody></p:sp>`;
  const tree = (shape: string) => `<p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>${shape}</p:spTree>`;
  const extra = {
    'ppt/slides/_rels/slide1.xml.rels': `<Relationships xmlns="${relationship}"><Relationship Id="rIdLayout" Type="${officeRelationship}/slideLayout" Target="../slideLayouts/slideLayout1.xml"/></Relationships>`,
    'ppt/slideLayouts/slideLayout1.xml': `<p:sldLayout ${ns} type="title"><p:cSld>${tree(placeholder(layoutBody))}</p:cSld></p:sldLayout>`,
    'ppt/slideLayouts/_rels/slideLayout1.xml.rels': `<Relationships xmlns="${relationship}"><Relationship Id="rIdMaster" Type="${officeRelationship}/slideMaster" Target="../slideMasters/slideMaster1.xml"/></Relationships>`,
    'ppt/slideMasters/slideMaster1.xml': `<p:sldMaster ${ns}><p:cSld>${tree(placeholder(masterBody))}</p:cSld><p:txStyles><p:titleStyle><a:lvl1pPr>${masterStyle}</a:lvl1pPr></p:titleStyle></p:txStyles></p:sldMaster>`
  };
  for (const [path, xml] of Object.entries(extra)) files[path] = strToU8(xml);
  files['[Content_Types].xml'] = strToU8(strFromU8(files['[Content_Types].xml']!).replace('</Types>', '<Override PartName="/ppt/slideLayouts/slideLayout1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/><Override PartName="/ppt/slideMasters/slideMaster1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/></Types>'));
  return zipSync(files);
}

test('master insets, vertical alignment and paragraph spacing reach the rendered slide', async () => {
  const spacing = '<a:lnSpc><a:spcPct val="90000"/></a:lnSpc><a:spcBef><a:spcPts val="800"/></a:spcBef><a:spcAft><a:spcPts val="1000"/></a:spcAft>';
  const inherited = masterFixture('', 'lIns="365760" tIns="91440" rIns="182880" bIns="182880" anchor="b"', '', spacing);
  // Remove the helper's explicit local anchor so the master's value can inherit.
  const parts = unzipSync(inherited);
  parts['ppt/slides/slide1.xml'] = strToU8(strFromU8(parts['ppt/slides/slide1.xml']!).replace('anchor="ctr"', '')
    .replace('</p:txBody>', `${textBody('Second paragraph', 'Calibri', 3_000)}</p:txBody>`));
  const result = await new PptxGlimpseAdapter().render(zipSync(parts), settings);
  const explicitXml = titleTextShape('Inherited layout', '', '', 3_000, `<a:pPr>${spacing}</a:pPr>`)
    .replace('anchor="ctr"', 'lIns="365760" tIns="91440" rIns="182880" bIns="182880" anchor="b"')
    .replace('</p:txBody>', `${textBody('Second paragraph', 'Calibri', 3_000).replace('<a:p>', `<a:p><a:pPr>${spacing}</a:pPr>`)}</p:txBody>`);
  const explicit = await new PptxGlimpseAdapter().render(fixture(explicitXml), settings);
  assert.deepEqual(result.slides[0]!.png, explicit.slides[0]!.png, 'Inherited and explicit formatting must render identically');
});

test('local and layout text-body properties override the master independently', async () => {
  const input = masterFixture('', 'lIns="365760" tIns="91440" rIns="182880" bIns="182880" anchor="b"', 'lIns="548640" tIns="45720" anchor="t"', '');
  const parts = unzipSync(input);
  parts['ppt/slides/slide1.xml'] = strToU8(strFromU8(parts['ppt/slides/slide1.xml']!).replace('anchor="ctr"', 'rIns="365760"'));
  const result = await new PptxGlimpseAdapter().render(zipSync(parts), settings);
  const explicit = await new PptxGlimpseAdapter().render(fixture(titleTextShape('Inherited layout', '', '', 3_000)
    .replace('anchor="ctr"', 'lIns="548640" tIns="45720" rIns="365760" bIns="182880" anchor="t"')), settings);
  assert.deepEqual(result.slides[0]!.png, explicit.slides[0]!.png);
});

test('explicit source no-autofit overrides an inherited shrink scale', async () => {
  const input = masterFixture('<a:noAutofit/>', 'anchor="ctr"', '', '');
  const parts = unzipSync(input);
  parts['ppt/slideMasters/slideMaster1.xml'] = strToU8(strFromU8(parts['ppt/slideMasters/slideMaster1.xml']!)
    .replace('<a:bodyPr anchor="ctr"/>', '<a:bodyPr anchor="ctr"><a:normAutofit fontScale="50000" lnSpcReduction="20000"/></a:bodyPr>'));
  const result = await new PptxGlimpseAdapter().render(zipSync(parts), { ...settings, fitTextToBox: false });
  const explicit = await new PptxGlimpseAdapter().render(fixture(titleTextShape('Inherited layout', '<a:noAutofit/>', '', 3_000)), { ...settings, fitTextToBox: false });
  assert.deepEqual(result.slides[0]!.png, explicit.slides[0]!.png);
});
