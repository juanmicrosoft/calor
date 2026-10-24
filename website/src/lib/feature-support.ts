// #1664: support levels on the website come from the converter's FeatureSupport registry, never
// from hand-written prose. public/data/feature-support.json is the verbatim output of
// `calor feature-check --list` (regenerate it with that command; WebsiteFeatureSupportParityTests
// fails when it differs from the registry). Pages state a level only through <SupportLevel>.
import registry from '../../public/data/feature-support.json';

export type SupportLevelKey = 'full' | 'partial' | 'notsupported' | 'manualrequired';

/** The registry's enum names, as the docs tables print them. */
export const LEVEL_NAMES: Record<SupportLevelKey, string> = {
  full: 'Full',
  partial: 'Partial',
  notsupported: 'NotSupported',
  manualrequired: 'ManualRequired',
};

/** The converter's warning text per level (ConversionContext); Full produces no warning. */
const WARNING_PHRASES: Record<SupportLevelKey, string | null> = {
  full: null,
  partial: 'is partially supported',
  notsupported: 'is not supported',
  manualrequired: 'requires manual intervention',
};

const levels = new Map<string, SupportLevelKey>();
for (const [level, features] of Object.entries(registry.data.features)) {
  if (!(level in LEVEL_NAMES)) throw new Error(`feature-support.json: unknown support level '${level}'`);
  for (const feature of features as { name: string }[]) levels.set(feature.name, level as SupportLevelKey);
}

/** The registry level of a feature. An unknown name fails the build. */
export function supportLevelKey(feature: string): SupportLevelKey {
  const level = levels.get(feature);
  if (!level) throw new Error(`<SupportLevel feature="${feature}">: not a FeatureSupport registry name`);
  return level;
}

export function supportLevelName(feature: string): string {
  return LEVEL_NAMES[supportLevelKey(feature)];
}

/** The converter's Calor1343 message prefix, e.g. "[out-var] Feature 'out-var' is partially supported…". */
export function supportWarning(feature: string): string {
  const phrase = WARNING_PHRASES[supportLevelKey(feature)];
  if (!phrase) throw new Error(`<SupportLevel feature="${feature}" form="warning">: a Full feature has no converter warning`);
  return `[${feature}] Feature '${feature}' ${phrase}…`;
}

const COMPONENT = /<SupportLevel\s+feature="([a-z0-9-]+)"(\s+form="warning")?\s*\/>/g;

function expandTags(text: string): string {
  // The search index never fails the build: a tag the page itself would reject (an unknown
  // feature) is left as written, and next build reports it when it renders the page.
  return text.replace(COMPONENT, (tag, feature: string, warning?: string) => {
    if (!levels.has(feature)) return tag;
    const level = levels.get(feature)!;
    return warning ? (WARNING_PHRASES[level] ? supportWarning(feature) : tag) : LEVEL_NAMES[level];
  });
}

// Fenced code (``` or ~~~, any indentation) and code spans of any backtick run show a tag literally.
const FENCE = /(^[ \t]*(`{3,}|~{3,})[^\n]*\n[\s\S]*?^[ \t]*\2[`~]*[ \t]*$)/m;
const CODE_SPAN = /((`+)[\s\S]*?[^`]\2(?!`))/;

/**
 * Replaces each <SupportLevel> tag in raw page text with what it renders (for the search index).
 * A tag inside fenced code or a code span is shown literally on the page, so it is left as written.
 */
export function expandSupportLevels(text: string): string {
  return text
    .split(new RegExp(FENCE.source, 'm'))
    .map((part, i) => {
      // split() with two capture groups yields [text, fence, fenceMarker, text, ...].
      if (i % 3 !== 0) return i % 3 === 1 ? part : '';
      return part.split(CODE_SPAN).map((piece, k) => (k % 3 === 0 ? expandTags(piece) : k % 3 === 1 ? piece : '')).join('');
    })
    .join('');
}
