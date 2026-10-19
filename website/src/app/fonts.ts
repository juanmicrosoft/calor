// Self-hosted Google Fonts (#1548). The build used to download these from the Google Fonts API
// through next/font's Google loader, which could change the site's bytes or fail the build. The
// woff2 files are the exact bytes that loader served (same families, weights, and per-subset unicode ranges), and
// their SIL Open Font License texts sit beside them. website/src/fonts/fonts.json records each
// file's source URL and sha256; scripts/check_website_fonts.py enforces it.
//
// next/font/local cannot give each file of one family its own unicode-range, so each subset is its
// own family, and the CSS variable lists them, latin first. The last entry is the metric-adjusted
// Arial fallback from ./fonts.css: the exact values next/font's Google loader generated.
// next/font/local would compute slightly different ones, which changes characters no subset covers
// (such as the arrow U+2192). Only the latin faces are preloaded, as before.
import type { CSSProperties } from 'react';
import localFont from 'next/font/local';
import './fonts.css';

type LoadedFont = { style: { fontFamily: string } };

/** The subsets' generated family names, latin first, then the shared fallback. */
function family(fallback: string, ...subsets: LoadedFont[]): string {
  return [...subsets.map((f) => f.style.fontFamily.split(', ')[0]), `'${fallback}'`].join(', ');
}

const dmSansLatin = localFont({
  src: [{ path: '../fonts/dm-sans/dm-sans-latin.woff2', weight: '100 1000', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD' }],
  adjustFontFallback: false,
});

const dmSansLatinExt = localFont({
  src: [{ path: '../fonts/dm-sans/dm-sans-latin-ext.woff2', weight: '100 1000', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0100-02BA, U+02BD-02C5, U+02C7-02CC, U+02CE-02D7, U+02DD-02FF, U+0304, U+0308, U+0329, U+1D00-1DBF, U+1E00-1E9F, U+1EF2-1EFF, U+2020, U+20A0-20AB, U+20AD-20C4, U+2113, U+2C60-2C7F, U+A720-A7FF' }],
  preload: false,
  adjustFontFallback: false,
});

const dmSansFamily = family('DM Sans Fallback', dmSansLatin, dmSansLatinExt);

const ralewayLatin = localFont({
  src: [
    { path: '../fonts/raleway/raleway-latin.woff2', weight: '400', style: 'normal' },
    { path: '../fonts/raleway/raleway-latin.woff2', weight: '500', style: 'normal' },
    { path: '../fonts/raleway/raleway-latin.woff2', weight: '600', style: 'normal' },
    { path: '../fonts/raleway/raleway-latin.woff2', weight: '700', style: 'normal' },
  ],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD' }],
  adjustFontFallback: false,
});

const ralewayLatinExt = localFont({
  src: [
    { path: '../fonts/raleway/raleway-latin-ext.woff2', weight: '400', style: 'normal' },
    { path: '../fonts/raleway/raleway-latin-ext.woff2', weight: '500', style: 'normal' },
    { path: '../fonts/raleway/raleway-latin-ext.woff2', weight: '600', style: 'normal' },
    { path: '../fonts/raleway/raleway-latin-ext.woff2', weight: '700', style: 'normal' },
  ],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0100-02BA, U+02BD-02C5, U+02C7-02CC, U+02CE-02D7, U+02DD-02FF, U+0304, U+0308, U+0329, U+1D00-1DBF, U+1E00-1E9F, U+1EF2-1EFF, U+2020, U+20A0-20AB, U+20AD-20C4, U+2113, U+2C60-2C7F, U+A720-A7FF' }],
  preload: false,
  adjustFontFallback: false,
});

const ralewayVietnamese = localFont({
  src: [
    { path: '../fonts/raleway/raleway-vietnamese.woff2', weight: '400', style: 'normal' },
    { path: '../fonts/raleway/raleway-vietnamese.woff2', weight: '500', style: 'normal' },
    { path: '../fonts/raleway/raleway-vietnamese.woff2', weight: '600', style: 'normal' },
    { path: '../fonts/raleway/raleway-vietnamese.woff2', weight: '700', style: 'normal' },
  ],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0102-0103, U+0110-0111, U+0128-0129, U+0168-0169, U+01A0-01A1, U+01AF-01B0, U+0300-0301, U+0303-0304, U+0308-0309, U+0323, U+0329, U+1EA0-1EF9, U+20AB' }],
  preload: false,
  adjustFontFallback: false,
});

const ralewayCyrillic = localFont({
  src: [
    { path: '../fonts/raleway/raleway-cyrillic.woff2', weight: '400', style: 'normal' },
    { path: '../fonts/raleway/raleway-cyrillic.woff2', weight: '500', style: 'normal' },
    { path: '../fonts/raleway/raleway-cyrillic.woff2', weight: '600', style: 'normal' },
    { path: '../fonts/raleway/raleway-cyrillic.woff2', weight: '700', style: 'normal' },
  ],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0301, U+0400-045F, U+0490-0491, U+04B0-04B1, U+2116' }],
  preload: false,
  adjustFontFallback: false,
});

const ralewayCyrillicExt = localFont({
  src: [
    { path: '../fonts/raleway/raleway-cyrillic-ext.woff2', weight: '400', style: 'normal' },
    { path: '../fonts/raleway/raleway-cyrillic-ext.woff2', weight: '500', style: 'normal' },
    { path: '../fonts/raleway/raleway-cyrillic-ext.woff2', weight: '600', style: 'normal' },
    { path: '../fonts/raleway/raleway-cyrillic-ext.woff2', weight: '700', style: 'normal' },
  ],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0460-052F, U+1C80-1C8A, U+20B4, U+2DE0-2DFF, U+A640-A69F, U+FE2E-FE2F' }],
  preload: false,
  adjustFontFallback: false,
});

const ralewayFamily = family('Raleway Fallback', ralewayLatin, ralewayLatinExt, ralewayVietnamese, ralewayCyrillic, ralewayCyrillicExt);

const jetbrainsMonoLatin = localFont({
  src: [{ path: '../fonts/jetbrains-mono/jetbrains-mono-latin.woff2', weight: '100 800', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD' }],
  adjustFontFallback: false,
});

const jetbrainsMonoLatinExt = localFont({
  src: [{ path: '../fonts/jetbrains-mono/jetbrains-mono-latin-ext.woff2', weight: '100 800', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0100-02BA, U+02BD-02C5, U+02C7-02CC, U+02CE-02D7, U+02DD-02FF, U+0304, U+0308, U+0329, U+1D00-1DBF, U+1E00-1E9F, U+1EF2-1EFF, U+2020, U+20A0-20AB, U+20AD-20C4, U+2113, U+2C60-2C7F, U+A720-A7FF' }],
  preload: false,
  adjustFontFallback: false,
});

const jetbrainsMonoVietnamese = localFont({
  src: [{ path: '../fonts/jetbrains-mono/jetbrains-mono-vietnamese.woff2', weight: '100 800', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0102-0103, U+0110-0111, U+0128-0129, U+0168-0169, U+01A0-01A1, U+01AF-01B0, U+0300-0301, U+0303-0304, U+0308-0309, U+0323, U+0329, U+1EA0-1EF9, U+20AB' }],
  preload: false,
  adjustFontFallback: false,
});

const jetbrainsMonoGreek = localFont({
  src: [{ path: '../fonts/jetbrains-mono/jetbrains-mono-greek.woff2', weight: '100 800', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0370-0377, U+037A-037F, U+0384-038A, U+038C, U+038E-03A1, U+03A3-03FF' }],
  preload: false,
  adjustFontFallback: false,
});

const jetbrainsMonoCyrillic = localFont({
  src: [{ path: '../fonts/jetbrains-mono/jetbrains-mono-cyrillic.woff2', weight: '100 800', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0301, U+0400-045F, U+0490-0491, U+04B0-04B1, U+2116' }],
  preload: false,
  adjustFontFallback: false,
});

const jetbrainsMonoCyrillicExt = localFont({
  src: [{ path: '../fonts/jetbrains-mono/jetbrains-mono-cyrillic-ext.woff2', weight: '100 800', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0460-052F, U+1C80-1C8A, U+20B4, U+2DE0-2DFF, U+A640-A69F, U+FE2E-FE2F' }],
  preload: false,
  adjustFontFallback: false,
});

const jetbrainsMonoFamily = family('JetBrains Mono Fallback', jetbrainsMonoLatin, jetbrainsMonoLatinExt, jetbrainsMonoVietnamese, jetbrainsMonoGreek, jetbrainsMonoCyrillic, jetbrainsMonoCyrillicExt);

const vt323Latin = localFont({
  src: [{ path: '../fonts/vt323/vt323-latin.woff2', weight: '400', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD' }],
  adjustFontFallback: false,
});

const vt323LatinExt = localFont({
  src: [{ path: '../fonts/vt323/vt323-latin-ext.woff2', weight: '400', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0100-02BA, U+02BD-02C5, U+02C7-02CC, U+02CE-02D7, U+02DD-02FF, U+0304, U+0308, U+0329, U+1D00-1DBF, U+1E00-1E9F, U+1EF2-1EFF, U+2020, U+20A0-20AB, U+20AD-20C4, U+2113, U+2C60-2C7F, U+A720-A7FF' }],
  preload: false,
  adjustFontFallback: false,
});

const vt323Vietnamese = localFont({
  src: [{ path: '../fonts/vt323/vt323-vietnamese.woff2', weight: '400', style: 'normal' }],
  display: 'swap',
  declarations: [{ prop: 'unicode-range', value: 'U+0102-0103, U+0110-0111, U+0128-0129, U+0168-0169, U+01A0-01A1, U+01AF-01B0, U+0300-0301, U+0303-0304, U+0308-0309, U+0323, U+0329, U+1EA0-1EF9, U+20AB' }],
  preload: false,
  adjustFontFallback: false,
});

const vt323Family = family('VT323 Fallback', vt323Latin, vt323LatinExt, vt323Vietnamese);

export const fontVariables = {
  '--font-body': dmSansFamily,
  '--font-display': ralewayFamily,
  '--font-mono': jetbrainsMonoFamily,
  '--font-terminal': vt323Family,
} as CSSProperties;
