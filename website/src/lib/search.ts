export interface SearchDocument {
  slug: string;
  title: string;
  text: string;
}

export function searchDocs(documents: SearchDocument[], query: string) {
  const tokens = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (tokens.length === 0) return [];
  return documents.filter(doc => tokens.every(token => `${doc.title} ${doc.text}`.toLowerCase().includes(token)))
    .map(doc => {
      const start = Math.max(0, doc.text.toLowerCase().indexOf(tokens[0]) - 40);
      return { ...doc, snippet: `${start ? '…' : ''}${doc.text.slice(start, start + 180)}…`,
        score: tokens.filter(token => doc.title.toLowerCase().includes(token)).length };
    })
    .sort((a, b) => b.score - a.score || a.title.localeCompare(b.title));
}
