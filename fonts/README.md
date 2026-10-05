# Approved fonts

This directory includes regular, bold, italic and bold-italic TTF files for Carlito, Arimo, Tinos, Cousine and Caladea. Each family has its original `OFL.txt` licence. The fonts ship in release packages and require no runtime download or Office installation.

The default mappings are Calibri/Aptos to Carlito, Arial to Arimo, Times New Roman to Tinos, Courier New to Cousine and Cambria to Caladea. Configure replacements with `Rendering:FontMapping`. The renderer also provides built-in mappings for common CJK fonts, but the relevant Noto font files must be added separately for those writing systems.

Administrator-added, properly licensed `.ttf`, `.otf` and `.ttc` files can live in this directory or other `Rendering:FontDirectories`. The pinned renderer does not load WOFF/WOFF2 files through this adapter. No proprietary font files are redistributed.

`Rendering:UseSystemFonts` defaults to `false`, so production output uses the approved directories. Set it to `true` only when the host's installed fonts are also approved for rendering. Empty or corrupt-only font sets fail conversion instead of generating PNGs without text. Restart after changing the font configuration.

## Bundled font provenance

Files are unmodified upstream assets, downloaded on 5 October 2026. The revisions below pin both the TTF files and licences:

| Family | Repository and revision | Files |
| --- | --- | --- |
| Carlito | `google/fonts` at `7085eb89a950e85db5b166b7a58d414544b4140c` | `ofl/carlito/Carlito-{Regular,Bold,Italic,BoldItalic}.ttf` and `OFL.txt` |
| Cousine | `google/fonts` at `7085eb89a950e85db5b166b7a58d414544b4140c` | `ofl/cousine/Cousine-{Regular,Bold,Italic,BoldItalic}.ttf` and `OFL.txt` |
| Caladea | `google/fonts` at `7085eb89a950e85db5b166b7a58d414544b4140c` | `ofl/caladea/Caladea-{Regular,Bold,Italic,BoldItalic}.ttf` and `OFL.txt` |
| Arimo | `googlefonts/Arimo` at `4a6255f269916ae7ad3fc2706b0935e7621396b8` | `fonts/ttf/Arimo-{Regular,Bold,Italic,BoldItalic}.ttf` and root `OFL.txt` |
| Tinos | `googlefonts/tinos` at `3b4482a99b80ea5fc75f187b1be3120a3f5905b3` | `fonts/ttf/Tinos-{Regular,Bold,Italic,BoldItalic}.ttf` and root `OFL.txt` |
