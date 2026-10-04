/**
 * #1422 (0.24 B2): marks a number published before the 0.24 benchmark method. These figures
 * stay as historical records. They were not computed over pairs that a registered oracle found
 * equivalent, so they cannot be compared with a 0.24 result.
 */
export function HistoricalMethodLabel({ className = '' }: { className?: string }) {
  return (
    <p
      role="note"
      aria-label="Historical result"
      className={`rounded border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm text-foreground ${className}`}
    >
      <strong>Historical, not comparable under the 0.24 method.</strong>{' '}
      These numbers were published before the 0.24 benchmark rules. They were not computed only over
      pairs that a registered oracle found behaviorally equivalent, so no 0.24 result may be compared
      with them.{' '}
      <a className="text-primary underline" href="https://github.com/juanmicrosoft/calor/issues/1422">
        Why (#1422)
      </a>
    </p>
  );
}
