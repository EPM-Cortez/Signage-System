# Machine API and converter contract

The canonical route summary is in `docs/openapi.yaml`. Human endpoints use the secure authentication cookie and antiforgery validation on mutations. Player endpoints use `Authorization: Bearer <device-token>` except pairing-session creation. Pairing and player routes are rate limited.

## Player lifecycle

1. `POST /api/player/pairing-sessions` returns a session ID, temporary polling token, six-digit code, and 15-minute expiry.
2. An administrator approves the code with `POST /api/admin/pairing/{code}/approve`.
3. `GET /api/player/pairing-sessions/{id}` returns the long-lived device token exactly once.
4. `GET /api/player/assignment` accepts `If-None-Match` and returns `304` when the content ID has not changed.
5. The player downloads `/content/{contentId}/manifest.json` and every referenced asset with its bearer token.
6. `POST /api/player/heartbeat` reports playback and error state.

Content URLs are immutable and return a one-year cache policy, but still require a non-revoked device assigned to a group with an enabled publication for that content.

## Media manifest extension

Image-only packages continue to use schema 1. Packages with embedded local or YouTube video use schema 2 and retain the same content and assignment endpoints. A `video` item references a hash-verified `media/<sha256>.mp4` or `.webm` asset. A `youtube` item references its cached slide PNG and a validated `youTubeVideoId`, never an arbitrary URL or embed HTML. Both may include `background: { asset, sha256 }`, normalized `placement: { x, y, width, height, rotation, flipHorizontal, flipVertical }`, and `playback: { startupTimeoutMs, stallTimeoutMs, maximumDurationMs, startSeconds, endSeconds }`. `durationMs` is the fallback static-slide duration, not a video cut-off; completion is driven by media events. Multiple videos on one slide are sequential items with the same source slide number and background.

Download and verify all local assets, including backgrounds, before activation; deduplicate repeated assets. YouTube content is streamed directly by the official iframe player and is never cached for offline playback. Server and service-worker media responses support single HTTP byte ranges. The converter CLI remains a static renderer; the host extracts video after validated PPTX inspection and before committing the complete immutable package.

## Converter CLI

The web process invokes the converter directly without a shell:

```text
node <absolute-cli-path> health
node <absolute-cli-path> render --input <absolute-pptx> --output <absolute-directory> --settings <absolute-json>
```

Settings schema:

```json
{
  "outputWidth": 1920,
  "fontDirectories": ["/opt/school-signage/fonts"],
  "useSystemFonts": false,
  "fitTextToBox": true,
  "fontMapping": { "Calibri": "Carlito", "Arial": "Arimo", "Aptos": "Carlito" },
  "requestedSourceSlideNumbers": [1, 2, 4],
  "timeoutSeconds": 300,
  "diagnosticVerbosity": "normal"
}
```

The output directory contains `slides/slide-0001.png`, sequential slide assets, `render-report.json`, and `diagnostics.json`. Standard output is newline-delimited JSON progress (`render-started`, `slide-rendered`, `render-completed`, or `render-failed`). Exit code zero means the complete requested set was rendered; anything else is failure. The host separately verifies PNG signature, dimensions, slide count, and hashes before publication. Captured standard streams are bounded and the process tree is terminated on timeout.

The two optional font fields default to approved-directory-only loading (`useSystemFonts: false`) and the renderer's built-in font mappings. The web application passes its configured `Rendering:FontMapping`. No font bytes are fetched from the PPTX or network. A font set with no parseable fonts produces a non-zero exit and `NO_USABLE_FONTS` in bounded stderr, preserving the existing publication.

The optional `fitTextToBox` setting defaults to `true` and is passed from `Rendering:FitTextToBox`. It reduces overflowing fixed-height text in the signage output, including text with source autofit disabled, without moving shapes or modifying the original PPTX. A reduction produces an administrator diagnostic (`renderer.text.fitToBox`). Set it to `false` to preserve intentional source overflow; source normal-autofit settings still apply. Text that cannot fit even at 10% of its source size fails conversion with `TEXT_DOES_NOT_FIT` instead of publishing clipped text.
