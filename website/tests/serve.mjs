import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import path from 'node:path';

const root = path.resolve('out');
const basePath = process.env.NEXT_PUBLIC_BASE_PATH || '';
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css',
  '.json': 'application/json', '.png': 'image/png', '.webp': 'image/webp',
  '.svg': 'image/svg+xml', '.ico': 'image/x-icon', '.woff2': 'font/woff2',
  '.mp4': 'video/mp4', '.jpg': 'image/jpeg', '.xml': 'application/xml', '.txt': 'text/plain' };

createServer(async (req, res) => {
  const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
  if (basePath && pathname !== '/' && !pathname.startsWith(`${basePath}/`)) {
    res.writeHead(404).end();
    return;
  }
  const relative = pathname.slice(basePath.length) || '/';
  const file = path.resolve(root, `.${relative.endsWith('/') ? `${relative}index.html` : relative}`);
  if (!file.startsWith(`${root}${path.sep}`)) {
    res.writeHead(403).end();
    return;
  }
  try {
    const content = await readFile(file);
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
    res.end(content);
  } catch (error) {
    if (error.code !== 'ENOENT' && error.code !== 'ENOTDIR') console.error(error);
    res.writeHead(404).end();
  }
}).listen(4173, '127.0.0.1');
