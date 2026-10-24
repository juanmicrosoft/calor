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

/** Replaces each <SupportLevel> tag in raw page text with what it renders (for the search index). */
export function expandSupportLevels(text: string): string {
  return text.replace(COMPONENT, (_tag, feature: string, warning?: string) =>
    warning ? supportWarning(feature) : supportLevelName(feature));
}
