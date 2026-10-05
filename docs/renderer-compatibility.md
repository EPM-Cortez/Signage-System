# Renderer compatibility

The bundled renderer adapter targets `pptx-glimpse` 3.2.8 with the local chart and text-layout compatibility patches described below. It runs on Node.js 22 and produces PNG slides without Office, PowerPoint, or LibreOffice. The adapter interface and CLI boundary are deliberate so a future renderer can replace it.

Expected strengths are ordinary text boxes, images, basic shapes, tables, common charts, masters/layouts, 16:9, 4:3, and custom slide sizes supported by the pinned renderer. Hidden slides are filtered from the Open XML metadata before render. Valid automatic-advance timings are clamped to configured limits; missing timings use the default.

Known limitations:

- PowerPoint animations and transitions are flattened; only player-side cut/fade is used.
- SmartArt, unusual charts, groups, image crop, theme inheritance, equations, and advanced effects may differ.
- Embedded MP4/WebM video is copied into the immutable package and played in the browser, muted, until completion. The browser must support the video codecs; WMV/AVI and invalid containers retain the static preview with a warning. There is no transcoding or Office automation.
- Recognised YouTube video relationships become online YouTube player items. No YouTube audiovisual content is downloaded, proxied, or cached. Other external video URLs and arbitrary embed HTML are not executed.
- PowerPoint media triggers/animations, simultaneous videos, and audio mixing are not executed. Multiple videos on one slide play sequentially in shape order. Video is an overlay above the flattened slide, so foreground objects cannot cover it. Simple position/size, individual rotation/flips, and unrotated group scaling are supported; complex group transforms use full-slide video with a warning.
- Macros are rejected. External relationships produce warnings and are not fetched by the server.
- Font metrics affect wrapping and pagination. The release bundles approved open-source replacements for common Office fonts; see `fonts/README.md`. Administrators can add licensed school fonts in configured local directories. Unavailable fonts fall back with diagnostics, so wrapping and symbols can differ. CJK fonts are not bundled.
- The system does not claim pixel-perfect or complete PowerPoint fidelity.

Before rollout, compare a representative private corpus against PowerPoint screenshots using `docs/manual-renderer-corpus.md`. Record the renderer/package versions with the result. A renderer upgrade requires rerunning converter tests and the corpus before release.

## Text and chart safeguards

Font bytes are loaded explicitly from approved local directories. System fonts are opt-in through `Rendering:UseSystemFonts`; merely configuring a directory never implicitly switches the font policy. The adapter checks that the pinned renderer can parse at least one font before rendering. An empty, missing or corrupt-only font set is a conversion failure, not a successful blank-text package.

The pinned renderer clamps line-chart maxima to zero, which makes negative-only series disappear and clips mixed-sign series. `src/Signage.Converter/scripts/apply-renderer-patch.mjs` corrects the line chart's axis range and coordinate scaling without changing chart data or labels. It checks the package version and original function checksum, patches the installed ESM/CommonJS bundles during `npm ci` and before builds, and fails on unrecognised upstream code. Release scripts include this installer hook. Revalidate or remove the patch before any renderer upgrade; do not bypass a checksum failure.

The text-layout patches preserve master/layout text-body properties (insets, anchoring, wrapping and autofit) with local properties taking precedence, and inherit paragraph spacing from the applicable style. Font registration keeps full face names so bold, italic and bold-italic glyph paths select their actual bundled faces instead of silently drawing the regular face.

`Rendering:FitTextToBox` defaults to `true`. Fixed-height text that overflows after font substitution is reduced in the output to fit its existing box. A bounded search chooses a fitting scale rather than a single proportional reduction, and accounts for indentation and non-wrapping horizontal overflow. Chart/image positions and the original PPTX remain unchanged. This intentionally overrides source `noAutofit` for overflowing boxes; set the option to `false` for presentations that intentionally overflow text. Normal-autofit scales remain honoured and shape-autofit boxes still grow according to the source. Reductions are reported as `renderer.text.fitToBox`; text that cannot fit at the 10% lower scale limit fails conversion rather than publishing a clipped package. This safeguard is not a claim of pixel-perfect PowerPoint typography.

Converter tests assert actual text and chart pixels, chart geometry, negative axis labels, ordinary Office-font replacements, an unavailable-font fallback, positive/mixed/negative/zero line data with both literal and reference caches, positive bar/pie charts, 4:3 dimensions and failure for unusable font sets. Signed bar/area and unusual chart variants still require corpus validation.

Text-layout regressions also check glyph pixels stay within a fixed title box, the source bytes are unchanged, fitting can be disabled, non-wrapping/indented text fits, saved normal-autofit scales do not grow, font styles produce different glyph outlines, and master/layout/local formatting precedence produces equivalent rendered output.

Existing immutable PNG packages do not change when the converter is updated. Upload the original presentation again to create and publish a newly rendered package; existing content stays available during that conversion.

## Video playback

New packages containing video use manifest schema 2. Schema 1 image packages remain supported. Local media and slide backgrounds are downloaded, hash-checked and cached before activation. The service worker supports single HTTP byte ranges for offline media seeking. The player advances on `ended` (or a source trim endpoint), not on the fallback slide timer. Failures show the cached poster briefly and continue to the next item. Startup, stalled playback, and never-ending media have bounded recovery; configure `Media:StartupTimeoutSeconds`, `StallTimeoutSeconds`, and `MaximumPlaybackSeconds` (defaults 20s, 30s, 1h). `MaximumEmbeddedVideoBytes` defaults to 250 MiB.

Set `Media:EnableYouTube` to false to keep online videos static and disallow YouTube scripts/frames. When enabled, the player builds a privacy-enhanced YouTube iframe from a validated video ID using the official IFrame API. It preserves numeric start/end URL parameters, uses muted autoplay, and handles ended, errors, blocked autoplay and offline playback. Small/off-slide/transformed YouTube shapes use a full-slide player to satisfy visibility/minimum-size requirements. No player controls or branding are obscured. YouTube availability, school filtering, embedding restrictions, sign-in/age restrictions, consent, and browser autoplay policy can still prevent playback. Third-party players require internet; an offline display skips them and continues cached local content.

YouTube loads only when a YouTube item plays. The player route's CSP narrowly permits the official API script and privacy-enhanced frame; its referrer policy supplies the origin without the page path. Administration pages retain their restrictive CSP. Site administrators should review YouTube's terms/privacy requirements, including child-directed site designation where applicable, before enabling this on school displays. See [IFrame API](https://developers.google.com/youtube/iframe_api_reference), [minimum functionality](https://developers.google.com/youtube/terms/required-minimum-functionality) and [embedding guidance](https://support.google.com/youtube/answer/171780).

After upgrading, reload existing display browsers to obtain the new player, then re-upload media-containing presentations. Existing PNG-only packages do not gain video automatically. The admin preview offers native controls for extracted local clips; YouTube previews stay static and are labelled as internet-dependent. Total presentation duration remains an estimate based on slide fallback timings; actual video duration is determined by playback.
