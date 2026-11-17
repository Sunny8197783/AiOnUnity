import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const names = new Set();
function scan(dir) {
  for (const f of readdirSync(dir)) {
    const p = path.join(dir, f);
    if (statSync(p).isDirectory()) scan(p);
    else if (p.endsWith('.js')) {
      const s = readFileSync(p, 'utf8');
      for (const m of s.matchAll(/THREE\.(\w+)/g)) names.add(m[1]);
      for (const m of s.matchAll(/import\s*\{([^}]*)\}\s*from\s*'three[^']*'/g)) for (const n of m[1].split(',')) { const k = n.trim().split(/\s+as\s+/)[0]; if (k) names.add(k); }
    }
  }
}
let scanned = false;
const STUB = (list) => `const h = { get: (t, k) => k === Symbol.toPrimitive ? () => 0 : k === 'then' ? undefined : P, apply: () => P, construct: () => P };
const P = new Proxy(function () {}, h);
export default P;
${[...list].filter(n => /^[A-Za-z_$][\w$]*$/.test(n) && n !== 'default').map(n => `export const ${n} = P;`).join('\n')}`;

export async function resolve(spec, ctx, next) {
  if (spec === 'three' || spec.startsWith('three/')) {
    if (!scanned) { scanned = true; const dist = process.env.AION_DIST; if (dist) scan(dist); }
    return { url: 'stub:three?' + encodeURIComponent(spec), shortCircuit: true };
  }
  return next(spec, ctx);
}
export async function load(url, ctx, next) {
  if (url.startsWith('stub:three')) return { format: 'module', source: STUB(names), shortCircuit: true };
  return next(url, ctx);
}
