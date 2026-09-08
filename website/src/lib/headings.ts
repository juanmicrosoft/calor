import { toString } from 'mdast-util-to-string';
import type { Root, Heading as MarkdownHeading } from 'mdast';

export interface Heading {
  id: string;
  text: string;
  level: number;
}

// One AST pass assigns both rendered IDs and TOC destinations. Fenced code is
// not a heading, and inline markup contributes its text rather than its syntax.
export function remarkHeadings(headings: Heading[]) {
  return () => (tree: Root) => {
    const used = new Set<string>();
    const visit = (node: Root | Root['children'][number]) => {
      if (node.type === 'heading') {
        const heading = node as MarkdownHeading;
        const text = toString(heading);
        const base = text.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '') || 'section';
        let id = base;
        for (let suffix = 1; used.has(id); suffix++) id = `${base}-${suffix}`;
        used.add(id);
        heading.data = { ...heading.data, hProperties: { ...heading.data?.hProperties, id } };
        if (heading.depth >= 2 && heading.depth <= 4) {
          headings.push({ id, text, level: heading.depth });
        }
      }
      if ('children' in node) {
        for (const child of node.children) visit(child as Root['children'][number]);
      }
    };
    visit(tree);
  };
}
