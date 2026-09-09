'use client';

import Link from 'next/link';
import { useId, useMemo, useRef, useState } from 'react';
import { getBasePath } from '@/lib/utils';
import { SearchDocument, searchDocs } from '@/lib/search';

export function DocSearch() {
  const id = useId();
  const [query, setQuery] = useState('');
  const [documents, setDocuments] = useState<SearchDocument[] | null>(null);
  const [error, setError] = useState(false);
  const loading = useRef(false);
  const firstResult = useRef<HTMLAnchorElement>(null);
  const results = useMemo(() => searchDocs(documents || [], query), [documents, query]);
  const load = async () => {
    if (documents || loading.current) return;
    loading.current = true;
    setError(false);
    try {
      const response = await fetch(`${getBasePath()}/search-index.json`);
      if (!response.ok) throw new Error(`Search index HTTP ${response.status}`);
      setDocuments(await response.json());
    } catch {
      setError(true);
    } finally {
      loading.current = false;
    }
  };

  return (
    <form role="search" aria-label="Documentation" className="mb-6 rounded-lg border bg-background p-3"
      onSubmit={event => { event.preventDefault(); firstResult.current?.focus(); }}>
      <label htmlFor={id} className="block text-sm font-medium">Search documentation</label>
      <input id={id} type="search" value={query} placeholder="Diagnostic code, CLI flag, or syntax"
        className="mt-2 w-full rounded-md border bg-background px-3 py-2 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary"
        onFocus={load} onChange={event => { setQuery(event.target.value); void load(); }}
        onKeyDown={event => {
          if (event.key === 'Escape') setQuery('');
          if (event.key === 'ArrowDown' && results.length) { event.preventDefault(); firstResult.current?.focus(); }
        }} aria-describedby={`${id}-status`} />
      <p id={`${id}-status`} role="status" className="mt-2 text-xs text-muted-foreground">
        {error ? 'Search could not load. Refocus the field to retry, or browse the sidebar.'
          : !query.trim() ? 'Search runs locally in your browser. No account required.'
          : !documents ? 'Loading documentation index…'
          : results.length ? `${results.length} matching pages${results.length > 10 ? '; showing the first 10' : ''}.`
          : 'No matching pages. Try another diagnostic, flag, or keyword.'}
      </p>
      {query.trim() && results.length > 0 && (
        <ul aria-label="Search results" className="mt-3 max-h-80 space-y-3 overflow-y-auto">
          {results.slice(0, 10).map((result, index) => (
            <li key={result.slug}>
              <Link ref={index === 0 ? firstResult : undefined}
                href={`/docs/${result.slug}${result.slug ? '/' : ''}`}
                onClick={() => setQuery('')}
                className="block rounded p-2 hover:bg-accent focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">
                <span className="font-medium text-primary underline">{result.title}</span>
                <span className="mt-1 block break-words text-xs text-muted-foreground">{result.snippet}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </form>
  );
}
