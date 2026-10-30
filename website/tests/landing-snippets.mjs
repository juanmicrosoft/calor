import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const snippets = JSON.parse(await readFile('src/components/landing/snippets.json', 'utf8'));
const html = await readFile('out/index.html', 'utf8');
const page = await readFile('src/app/page.tsx', 'utf8');
const entities = { amp: '&', lt: '<', gt: '>', quot: '"', '#x27': "'" };
const decode = text => text.replace(/&(amp|lt|gt|quot|#x27);/g, (_, entity) => entities[entity]);
const blocks = [...html.matchAll(/<code\b[^>]*>([\s\S]*?)<\/code>/g)].map(match => decode(match[1]));
const mounted = Object.entries(snippets).filter(([, snippet]) =>
  new RegExp(`<${snippet.component}\\b`).test(page));
const codes = mounted.map(([, snippet]) => snippet.code);
assert.ok(codes.length > 0, 'Landing page must mount checked snippets');

function check(displayed) {
  for (const [id, snippet] of mounted) {
    assert.equal(displayed.filter(code => code === snippet.code).length, 1,
      `${id}: exported page must display exactly the shared checked source once`);
  }
  for (const code of displayed.filter(code => code.includes('§'))) {
    assert.ok(codes.includes(code), `Unregistered displayed Calor code: ${code}`);
  }
}

check(blocks);
assert.throws(() => check(blocks.filter(code => code !== codes[0])), /must display/);
assert.throws(() => check([...blocks, '§P "unchecked"']), /Unregistered/);
assert.throws(() => check(blocks.map(code => code === codes[0] ? `${code}\n§R missing` : code)),
  /must display/);
console.log(`Exported landing page displays all ${codes.length} mounted checked Calor snippets; mutation controls passed.`);
