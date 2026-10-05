// Hash-guarded fixes for pptx-glimpse 3.2.8. Applied to every installed bundle.
const fitBody = source => source
  .replace('  let fontScale = bodyProperties.fontScale;',
    '  // school-signage: bounded-text-fit-v1\n  let fontScale = bodyProperties.fontScale;')
  .replace('if (bodyProperties.autoFit === "normAutofit" && shouldWrap)',
    'if (bodyProperties.autoFit === "normAutofit" || (bodyProperties.autoFit === "noAutofit" && context.fitTextToBox))')
  .replace('      availableHeight,\n      context\n    );',
    `      availableHeight,
      context,
      shouldWrap
    );
    if (bodyProperties.autoFit === "noAutofit" && fontScale < bodyProperties.fontScale - 0.0001) {
      context.warningLogger.warn("text.fitToBox", "Text was reduced to fit its original text box in the signage output.");
    }`);

export const additionalTextPatches = [
  {
    name: 'collectFontNames',
    marker: '// school-signage: font-style-names-v1',
    hash: 'f6428ff0d4f5038ac61bdd6749bbaf5f67e1bc5e99149c4ff3bc5f1e0b61333f',
    apply: () => `function collectFontNames(font) {
  // school-signage: font-style-names-v1
  const names = new Set(Object.values(font.names.fullName ?? {}));
  const families = new Set([
    ...Object.values(font.names.fontFamily ?? {}),
    ...Object.values(font.names.preferredFamily ?? {})
  ]);
  const styles = Object.values(font.names.preferredSubfamily ?? font.names.fontSubfamily ?? {});
  const regular = styles.length === 0 || styles.some(style => /^(regular|normal|roman|book)$/i.test(style));
  for (const family of families) {
    if (regular) names.add(family);
    for (const style of styles) names.add(family + " " + style);
  }
  return names;
}`
  },
  {
    name: 'measureLineWidth',
    marker: '// school-signage: styled-path-width-v1',
    hash: ['94fdf2980f67927a2e01584c7dd532a187707ed6dc0efdda0574de682b7a04c7', '10806a93ec38c604b81ae6e52e1734fdbfa7ad8443a0e990b876b2d3c3b2968f'],
    apply: source => source
      .replace('  let totalWidth = 0;', '  // school-signage: styled-path-width-v1\n  let totalWidth = 0;')
      .replace('const font = fontResolver.resolveFont(', 'const font = resolveStyledPathFont(fontResolver,')
      .replace('        jpanFallback,\n        context', '        jpanFallback,\n        seg.properties,\n        context')
      + `
function resolveStyledPathFont(fontResolver, family, eastAsianFamily, fallback, properties, context) {
  const style = properties.bold && properties.italic ? "Bold Italic" : properties.bold ? "Bold" : properties.italic ? "Italic" : "";
  if (style && typeof fontResolver.findFont === "function") {
    for (const name of [family, eastAsianFamily, fallback]) {
      if (!name) continue;
      const mapped = context.fontMapping[name] ?? Object.entries(context.fontMapping).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1];
      for (const base of [name, mapped]) {
        if (!base) continue;
        for (const variant of [base + " " + style, base + "-" + style.replace(/ /g, "")]) {
          const font = fontResolver.findFont(variant, context);
          if (font) return font;
        }
      }
    }
  }
  return fontResolver.resolveFont(family, eastAsianFamily, fallback, context);
}`
  },
  {
    name: 'renderSegmentAsPath',
    marker: '// school-signage: styled-glyph-paths-v1',
    hash: ['dd943cee7e203663f0f4d77d16884908c59f16cc00f20a05ece33b6c802125f9', '7b70e0063fb2b70b1118b112b9eb19ea449104dfdbd9875e5e5c3ffa1a408103'],
    apply: source => source
      .replace('  const fontSize =', '  // school-signage: styled-glyph-paths-v1\n  const fontSize =')
      .replaceAll('fontResolver.resolveFont(fontFamily, fontFamilyEa, jpanFallback, context)',
        'resolveStyledPathFont(fontResolver, fontFamily, fontFamilyEa, jpanFallback, props, context)')
  },
  {
    name: 'renderPptxSourceModelToSvg',
    marker: '// school-signage: fit-text-option-v1',
    hash: ['cc324a88af6d23ff43364d9fcdf4a46659ad7e6cfd08c65c5b4da512201924c0', 'adb176fb84971f1ee0d4537ce7aadc6d046a6ab0ee433f3d0781c691caa0e171'],
    apply: source => source.replace('  if (source.presentation.slidePartPaths.length === 0)',
      '  // school-signage: fit-text-option-v1\n  context.fitTextToBox = options?.fitTextToBox !== false;\n  if (source.presentation.slidePartPaths.length === 0)')
  },
  {
    name: 'renderTextBody',
    marker: '// school-signage: bounded-text-fit-v1',
    hash: ['f509c9a163d7993ef9a819cc984ee83b03249257cb27caf963b0f9891fcf58f7', '10106a429cd2f747122d370f4d7d4b179024387ec79cd1a2f5f0488ca7ca1f60'],
    apply: fitBody
  },
  {
    name: 'renderTextBodyAsPath',
    marker: '// school-signage: bounded-text-fit-v1',
    hash: ['f20ccdf69cfb68671fa78e073255b4cbc264aa69d902e0f98abee419df5f1275', '01f340912b54b1c75bae7975cbf9c2d8c16fc042b703a6553b254741d7bb9cbd'],
    apply: fitBody
  },
  {
    name: 'computeShrinkToFitScale',
    marker: '// school-signage: maximum-fitting-scale-v1',
    hash: '3dc56154840cd8931077e2a4418923426e9043194130a2cae785d1ed281a1206',
    apply: () => `function computeShrinkToFitScale(paragraphs, defaultFontSize, fontScale, lnSpcReduction, textWidth, availableHeight, context = createLegacyRendererContext(), shouldWrap = true) {
  // school-signage: maximum-fitting-scale-v1
  if (availableHeight <= 0 || textWidth <= 0) return fontScale;
  const fits = scale => {
    const height = estimateTextHeight(paragraphs, defaultFontSize, shouldWrap, textWidth, lnSpcReduction, scale, context);
    if (height > availableHeight) return false;
    for (const para of paragraphs) {
      const width = textWidth - emuToPixels(para.properties.marginLeft ?? 0);
      const lines = shouldWrap
        ? wrapParagraph(para, width, defaultFontSize * scale, scale, context.textMeasurer, context)
        : [{ segments: para.runs }];
      for (const line of lines) {
        if (measureLineWidth(line.segments, defaultFontSize, scale, context.textPathFontResolver, context) > width + 0.01) return false;
      }
    }
    return true;
  };
  if (fits(fontScale)) return fontScale;
  let lower = fontScale * 0.1;
  let upper = fontScale;
  if (!fits(lower)) throw new Error("TEXT_DOES_NOT_FIT: Text cannot fit its box without reducing it below 10% of the source size.");
  // A ratio-only step over-shrinks when two lines become one. Search for the
  // largest fitting size, including horizontal overflow in non-wrapping boxes.
  for (let iteration = 0; iteration < 18; iteration++) {
    const middle = (lower + upper) / 2;
    if (fits(middle)) lower = middle;
    else upper = middle;
  }
  return lower;
}`
  },
  {
    name: 'estimateTextHeight',
    marker: '// school-signage: indented-text-height-v1',
    hash: '70005d66dc7fb31672d261a36ba3758299030c32fef78f961b21ba894522ca24',
    apply: source => source
      .replace('  let totalHeight = 0;', '  // school-signage: indented-text-height-v1\n  let totalHeight = 0;')
      .replace('        textWidth,\n        scaledDefaultForWrap,', '        textWidth - emuToPixels(para.properties.marginLeft ?? 0),\n        scaledDefaultForWrap,')
  }
];
