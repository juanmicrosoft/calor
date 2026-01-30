/**
 * Migrate Jekyll docs to MDX format for Next.js website
 *
 * Run with: npx ts-node scripts/migrate-docs.ts
 */

import * as fs from 'fs';
import * as path from 'path';

const SOURCE_DIR = path.join(__dirname, '..', 'docs');
const TARGET_DIR = path.join(__dirname, '..', 'website', 'content');

// Map parent to section slug
const parentToSection: Record<string, string> = {
  'Getting Started': 'getting-started',
  'Syntax Reference': 'syntax-reference',
  'Benchmarking': 'benchmarking',
  'Philosophy': 'philosophy',
  'Contributing': 'contributing',
  'CLI Reference': 'cli',
};

// Map nav_order to order within sections
function getOrder(navOrder: number | undefined, isIndex: boolean): number {
  if (isIndex) return 0;
  return navOrder ?? 999;
}

function getSectionFromPath(filePath: string): string | undefined {
  const relativePath = path.relative(SOURCE_DIR, filePath);
  const parts = relativePath.split(path.sep);
  if (parts.length > 1) {
    return parts[0];
  }
  return undefined;
}

interface FrontMatter {
  layout?: string;
  title?: string;
  nav_order?: number;
  has_children?: boolean;
  parent?: string;
  permalink?: string;
  description?: string;
}

function parseFrontMatter(content: string): { data: FrontMatter; body: string } {
  const match = content.match(/^---\n([\s\S]*?)\n---\n([\s\S]*)$/);
  if (!match) {
    return { data: {}, body: content };
  }

  const frontMatterStr = match[1];
  const body = match[2];

  const data: FrontMatter = {};
  const lines = frontMatterStr.split('\n');

  for (const line of lines) {
    const colonIndex = line.indexOf(':');
    if (colonIndex === -1) continue;

    const key = line.slice(0, colonIndex).trim();
    let value = line.slice(colonIndex + 1).trim();

    // Remove quotes
    if ((value.startsWith('"') && value.endsWith('"')) ||
        (value.startsWith("'") && value.endsWith("'"))) {
      value = value.slice(1, -1);
    }

    switch (key) {
      case 'layout':
        data.layout = value;
        break;
      case 'title':
        data.title = value;
        break;
      case 'nav_order':
        data.nav_order = parseInt(value, 10);
        break;
      case 'has_children':
        data.has_children = value === 'true';
        break;
      case 'parent':
        data.parent = value;
        break;
      case 'permalink':
        data.permalink = value;
        break;
      case 'description':
        data.description = value;
        break;
    }
  }

  return { data, body };
}

function transformContent(content: string): string {
  let result = content;

  // Remove Jekyll-style classes like {: .fs-9 } and {: .btn .btn-primary ... }
  result = result.replace(/\{:\s*\.[^}]+\}/g, '');

  // Transform Jekyll buttons to regular links (they're already markdown links)
  // The {: .btn ...} classes are already removed above

  // Keep /opal/ links as-is - they'll be transformed at render time

  return result;
}

function generateNewFrontMatter(
  data: FrontMatter,
  section: string | undefined,
  isIndex: boolean
): string {
  const lines: string[] = ['---'];

  lines.push(`title: "${data.title || 'Untitled'}"`);

  if (data.description) {
    lines.push(`description: "${data.description}"`);
  }

  if (section) {
    lines.push(`section: ${section}`);
  }

  const order = getOrder(data.nav_order, isIndex);
  lines.push(`order: ${order}`);

  if (data.has_children) {
    lines.push('hasChildren: true');
  }

  lines.push('---');

  return lines.join('\n');
}

function processFile(filePath: string): void {
  const content = fs.readFileSync(filePath, 'utf-8');
  const { data, body } = parseFrontMatter(content);

  const relativePath = path.relative(SOURCE_DIR, filePath);
  const isIndex = path.basename(filePath, path.extname(filePath)) === 'index';

  // Determine section
  let section = getSectionFromPath(filePath);
  if (!section && data.parent) {
    section = parentToSection[data.parent];
  }

  // Transform content
  const transformedBody = transformContent(body);

  // Generate new front matter
  const newFrontMatter = generateNewFrontMatter(data, section, isIndex);

  // Combine
  const newContent = `${newFrontMatter}\n${transformedBody}`;

  // Determine target path (change .md to .mdx)
  const targetRelativePath = relativePath.replace(/\.md$/, '.mdx');
  const targetPath = path.join(TARGET_DIR, targetRelativePath);

  // Ensure directory exists
  const targetDir = path.dirname(targetPath);
  fs.mkdirSync(targetDir, { recursive: true });

  // Write file
  fs.writeFileSync(targetPath, newContent);
  console.log(`Migrated: ${relativePath} -> ${targetRelativePath}`);
}

function processDirectory(dir: string): void {
  const entries = fs.readdirSync(dir, { withFileTypes: true });

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);

    if (entry.isDirectory()) {
      // Skip Jekyll-specific directories
      if (entry.name.startsWith('_') || entry.name === 'node_modules') {
        continue;
      }
      processDirectory(fullPath);
    } else if (entry.name.endsWith('.md')) {
      processFile(fullPath);
    }
  }
}

// Main
console.log('Migrating docs from Jekyll to MDX...\n');

// Clean target directory
if (fs.existsSync(TARGET_DIR)) {
  fs.rmSync(TARGET_DIR, { recursive: true });
}
fs.mkdirSync(TARGET_DIR, { recursive: true });

// Process all docs
processDirectory(SOURCE_DIR);

console.log('\nMigration complete!');
