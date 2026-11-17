// Local export tool: serves the original game's dist/ (read-only) with this folder's overlay/ on top, and saves
// PUT /__save/<name> bodies into the export folder. Binds to 127.0.0.1 only.
import { createServer } from 'node:http';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve, extname, sep, basename, join } from 'node:path';
const [dist, overlay, out] = process.argv.slice(2).map((p) => resolve(p));
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.json': 'application/json', '.png': 'image/png', '.svg': 'image/svg+xml' };
const inside = (root, file) => file.startsWith(root + sep);
createServer(async (req, res) => {
  try {
    const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    if (req.method === 'PUT' && pathname.startsWith('/__save/')) {
      const chunks = []; for await (const c of req) chunks.push(c);
      const name = basename(pathname.slice(8)); const buf = Buffer.concat(chunks);
      await writeFile(join(out, name), buf); console.log('saved', name, buf.length);
      return res.writeHead(200).end('ok');
    }
    const rel = '.' + (pathname === '/' ? '/index.html' : pathname);
    let data;
    for (const root of [overlay, dist]) {
      const file = resolve(root, rel);
      if (!inside(root, file)) continue;
      try { data = await readFile(file); break; } catch { /* next root */ }
    }
    if (!data) return res.writeHead(404).end('Not found');
    res.writeHead(200, { 'Content-Type': types[extname(rel)] || 'application/octet-stream', 'Cache-Control': 'no-cache' });
    res.end(data);
  } catch (e) { res.writeHead(500).end(String(e)); }
}).listen(4173, '127.0.0.1', () => console.log('export server on http://127.0.0.1:4173'));
