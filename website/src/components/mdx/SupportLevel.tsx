import { supportLevelKey, supportLevelName, supportWarning } from '@/lib/feature-support';

interface SupportLevelProps {
  /** A FeatureSupport registry name, as `calor feature-check <feature>` takes it. */
  feature: string;
  /** "warning" renders the converter's warning text for the feature instead of the level name. */
  form?: 'warning';
}

/**
 * #1664: the only way a page states a converter support level. The level is read from the
 * registry export at build time, so a page cannot drift from `calor feature-check`.
 */
export function SupportLevel({ feature, form }: SupportLevelProps) {
  const level = supportLevelKey(feature);
  if (form === 'warning') {
    return (
      <code className="bg-muted px-1.5 py-0.5 rounded text-sm font-mono" data-feature={feature} data-support-level={level}>
        {supportWarning(feature)}
      </code>
    );
  }
  return (
    <span data-feature={feature} data-support-level={level}>
      {supportLevelName(feature)}
    </span>
  );
}
