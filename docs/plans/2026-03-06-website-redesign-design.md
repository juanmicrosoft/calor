# Calor.dev Landing Page Redesign

**Date**: 2026-03-06
**Status**: Approved

## Aesthetic Direction: "Refined Heat"

Dark navy foundation (#002257) with warm gradients (pink to salmon). Editorial typography. Controlled glow effects. Heat as atmosphere, not spectacle.

## Brand Palette (unchanged)

| Color | Hex | Role |
|-------|-----|------|
| Deep Navy | #002257 | Primary bg, code blocks, footer, hero overlay |
| Bubblegum Pink | #FA3D6F | CTAs, badges, gradient start, accent glows |
| Salmon | #FF8E77 | Gradient endpoints, warm accents |
| Cerulean | #006D87 | Secondary accents, C# indicators |
| Strong Cyan | #3DDFE7 | Code highlights, terminal glow, interactive states |

## Changes

1. **Typography**: Inter -> Instrument Serif (headings) + DM Sans (body). Keep JetBrains Mono + VT323.
2. **Hero**: Gradient overlay on lava video, frosted glass card, radial logo glow, shaped bottom divider.
3. **Scroll Animations**: CSS + IntersectionObserver hook. Fade-up sections, staggered cards, animated bars.
4. **Brand Colors**: Navy code blocks, gradient mesh blobs, gradient borders on hover, pink-to-salmon CTAs.
5. **Terminal CRT**: VT323 font, scanlines, terminal glow, brand navy bg.
6. **Section Variety**: Full-width code, floating annotations, pulsing error output, gradient CTA band.
7. **Atmosphere**: CSS noise texture, gradient mesh shapes, navy footer with heat gradient.
8. **Mobile**: Video poster fallback, reduced animation distances, stacked layouts.

## Technical Approach

- Google Fonts only (next/font/google)
- CSS + Intersection Observer (no Framer Motion)
- Respect prefers-reduced-motion
