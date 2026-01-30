import fs from 'fs';
import path from 'path';
import matter from 'gray-matter';

const DOCS_DIR = path.join(process.cwd(), 'content');

export interface DocMeta {
  title: string;
  description?: string;
  section?: string;
  order: number;
  slug: string;
  hasChildren?: boolean;
}

export interface Doc extends DocMeta {
  content: string;
}

export interface DocSection {
  title: string;
  slug: string;
  order: number;
  docs: DocMeta[];
}

// Map section slugs to display titles
const sectionTitles: Record<string, string> = {
  'getting-started': 'Getting Started',
  'syntax-reference': 'Syntax Reference',
  'benchmarking': 'Benchmarking',
  'philosophy': 'Philosophy',
  'contributing': 'Contributing',
  'cli': 'CLI Reference',
};

// Map section slugs to nav order
const sectionOrder: Record<string, number> = {
  'getting-started': 1,
  'syntax-reference': 2,
  'cli': 3,
  'benchmarking': 4,
  'philosophy': 5,
  'contributing': 6,
};

function getSlugFromPath(filePath: string): string {
  const relativePath = path.relative(DOCS_DIR, filePath);
  let slug = relativePath.replace(/\.mdx?$/, '');

  // Handle index files
  if (slug.endsWith('/index')) {
    slug = slug.replace(/\/index$/, '');
  }
  if (slug === 'index') {
    slug = '';
  }

  return slug;
}

function getAllDocFiles(dir: string = DOCS_DIR): string[] {
  if (!fs.existsSync(dir)) {
    return [];
  }

  const files: string[] = [];
  const entries = fs.readdirSync(dir, { withFileTypes: true });

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      files.push(...getAllDocFiles(fullPath));
    } else if (entry.name.endsWith('.mdx') || entry.name.endsWith('.md')) {
      files.push(fullPath);
    }
  }

  return files;
}

export function getDocBySlug(slug: string): Doc | null {
  const slugPath = slug || 'index';

  // Try .mdx first, then .md
  const possiblePaths = [
    path.join(DOCS_DIR, `${slugPath}.mdx`),
    path.join(DOCS_DIR, `${slugPath}.md`),
    path.join(DOCS_DIR, slugPath, 'index.mdx'),
    path.join(DOCS_DIR, slugPath, 'index.md'),
  ];

  for (const filePath of possiblePaths) {
    if (fs.existsSync(filePath)) {
      const fileContent = fs.readFileSync(filePath, 'utf-8');
      const { data, content } = matter(fileContent);

      return {
        title: data.title || 'Untitled',
        description: data.description,
        section: data.section,
        order: data.order ?? 999,
        slug,
        hasChildren: data.hasChildren,
        content,
      };
    }
  }

  return null;
}

export function getAllDocs(): DocMeta[] {
  const files = getAllDocFiles();

  return files.map((filePath) => {
    const fileContent = fs.readFileSync(filePath, 'utf-8');
    const { data } = matter(fileContent);
    const slug = getSlugFromPath(filePath);

    return {
      title: data.title || 'Untitled',
      description: data.description,
      section: data.section,
      order: data.order ?? 999,
      slug,
      hasChildren: data.hasChildren,
    };
  });
}

export function getDocSlugs(): string[] {
  const files = getAllDocFiles();
  return files.map((filePath) => getSlugFromPath(filePath));
}

export function getDocSections(): DocSection[] {
  const allDocs = getAllDocs();
  const sections: Map<string, DocSection> = new Map();

  // Group docs by section
  for (const doc of allDocs) {
    // Skip the root index
    if (doc.slug === '') continue;

    const parts = doc.slug.split('/');
    const sectionSlug = parts[0];

    if (!sections.has(sectionSlug)) {
      sections.set(sectionSlug, {
        title: sectionTitles[sectionSlug] || sectionSlug,
        slug: sectionSlug,
        order: sectionOrder[sectionSlug] ?? 999,
        docs: [],
      });
    }

    sections.get(sectionSlug)!.docs.push(doc);
  }

  // Sort docs within each section
  for (const section of sections.values()) {
    section.docs.sort((a, b) => {
      // Index pages come first
      if (a.slug === section.slug) return -1;
      if (b.slug === section.slug) return 1;
      return a.order - b.order;
    });
  }

  // Convert to array and sort sections
  return Array.from(sections.values()).sort((a, b) => a.order - b.order);
}

export function getAdjacentDocs(currentSlug: string): {
  prev: DocMeta | null;
  next: DocMeta | null;
} {
  const sections = getDocSections();
  const flatDocs: DocMeta[] = [];

  for (const section of sections) {
    flatDocs.push(...section.docs);
  }

  const currentIndex = flatDocs.findIndex((doc) => doc.slug === currentSlug);

  return {
    prev: currentIndex > 0 ? flatDocs[currentIndex - 1] : null,
    next: currentIndex < flatDocs.length - 1 ? flatDocs[currentIndex + 1] : null,
  };
}

export function extractHeadings(content: string): { id: string; text: string; level: number }[] {
  const headingRegex = /^(#{2,4})\s+(.+)$/gm;
  const headings: { id: string; text: string; level: number }[] = [];
  let match;

  while ((match = headingRegex.exec(content)) !== null) {
    const level = match[1].length;
    const text = match[2].trim();
    const id = text
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/(^-|-$)/g, '');

    headings.push({ id, text, level });
  }

  return headings;
}
