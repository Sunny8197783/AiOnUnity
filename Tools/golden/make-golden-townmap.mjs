// Golden data for TownMapTests: the original game/townmap.js tables, its distances bit for bit, and graph()
// under every closure set and shelter-gate combination.
//   node make-golden-townmap.mjs [out.json]
// AION_SRC = the original safe-route folder (default: the copy this port was made from).
import { writeFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const SRC = process.env.AION_SRC ?? 'C:/Users/김태양/AppData/Local/Temp/claude/80eae06ac10fc83e/082ba9f6-2e32-43aa-b236-612ace9a2adc/scratchpad/aion-src/safe-route';
const game = (name) => import(pathToFileURL(path.join(SRC, 'dist/game', name)).href); // absolute file:/// URL
const out = process.argv[2] ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '../../Tests/EditMode/Fixtures/golden-townmap.json');

const tm = await game('townmap.js');
const st = await game('state.js');

// The 64 bits of a double as hex: JSON text could hide a last-bit difference, this cannot.
const bits = (x) => { const b = Buffer.alloc(8); b.writeDoubleBE(x); return b.toString('hex'); };
// FNV-1a over UTF-16 code units; its twin in the C# tests is TownGolden.Fnv.
const fnv = (s) => { let h = 0x811c9dc5; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193) >>> 0; } return h.toString(16).padStart(8, '0'); };
// Keyed tables as rows in declaration order (JSON object order is not compared by the tests, array order is).
const rows = (o) => Object.entries(o).map(([id, v]) => ({ id, ...v }));

const tables = {
  bounds: tm.BOUNDS, sidewalk: tm.SIDEWALK, roads: tm.ROADS, crosswalks: tm.CROSSWALKS, buildings: tm.BUILDINGS, areas: tm.AREAS,
  places: rows(tm.PLACES), spawns: rows(tm.SPAWNS), nodes: rows(tm.NODES),
  edges: tm.EDGES.map(([from, to, seg, tags]) => ({ from, to, seg, tags })),
  shelterEntrances: rows(tm.SHELTER_ENTRANCES), closures: rows(tm.CLOSURES),
};

// dist(): every pair (i < j) of nodes, then places, then spawns, in declaration order.
const points = [...Object.values(tm.NODES), ...Object.values(tm.PLACES), ...Object.values(tm.SPAWNS)];
const distBits = [];
for (let i = 0; i < points.length; i++) for (let j = i + 1; j < points.length; j++) distBits.push(bits(tm.dist(points[i], points[j])));
const edgeLengths = tm.EDGES.map(([a, b, seg]) => { const len = tm.dist(tm.NODES[a], tm.NODES[b]); return { seg, len, bits: bits(len) }; });

// graph(): one line per node, 'node:to/seg/len-bits/tags ...', in the order graph() lists them.
const line = (adj) => Object.entries(adj).map(([n, es]) => `${n}:${es.map(e => `${e.to}/${e.seg}/${bits(e.len)}/${e.tags.join('+')}`).join(' ')}`).join('\n');
const reach = (adj, from) => {
  const seen = new Set([from]), todo = [from];
  while (todo.length) for (const e of adj[todo.pop()]) if (!seen.has(e.to)) { seen.add(e.to); todo.push(e.to); }
  return seen;
};
const uniq = (lists) => [...new Map(lists.map(l => [JSON.stringify(l), l])).values()];
const closureSets = uniq([
  [], ...st.CLOSURE_SETS.dday, ...st.CLOSURE_SETS.evac, ...Object.keys(tm.CLOSURES).map(id => [id]), Object.keys(tm.CLOSURES),
  ['nope'], ['danji', 'nope', 'danji'], ['yeonmuCenter', 'danji', 'sijang'],
]);
const KEYS = Object.keys(tm.SHELTER_ENTRANCES);
const allGates = Array.from({ length: 16 }, (_, m) => Object.fromEntries(KEYS.map((k, i) => [k, !!(m >> i & 1)])));
// null = graph()'s default (all open); a gates object that leaves an entrance out closes it.
const partialGates = [null, {}, { east: true }, { stairs: true, ramp: true }, { access: true, east: false }];
const fewGates = [null, { east: false, stairs: true, ramp: true, access: false }, { east: false, stairs: false, ramp: false, access: false }, { stairs: true }];
const graphs = [];
for (const closures of closureSets) {
  const wide = closures.length === 0 || closures.length === Object.keys(tm.CLOSURES).length;
  for (const gates of wide ? [...partialGates, ...allGates] : fewGates) {
    const adj = tm.graph(gates ? { closures, gates } : { closures });
    const open = [...new Set(Object.values(adj).flat().map(e => e.seg))];
    const seen = reach(adj, 'home');
    graphs.push({ closures, gates, open: open.join(' '), cut: tm.EDGES.map(e => e[2]).filter(s => !open.includes(s)).join(' '),
      unreachable: Object.keys(tm.NODES).filter(n => !seen.has(n)).join(' '), digest: fnv(line(adj)) });
  }
}
const full = tm.graph();
const fullAdj = Object.entries(full).map(([node, es]) => ({ node, links: es.map(e => ({ to: e.to, seg: e.seg, tags: e.tags, len: e.len })) }));

writeFileSync(out, JSON.stringify({ tables, points: points.length, distBits, edgeLengths, fullAdj, fullLine: line(full), graphs }, null, 0));
console.log(`golden-townmap: ${Object.keys(tm.NODES).length} nodes, ${tm.EDGES.length} edges, ${distBits.length} distances, ${graphs.length} graphs -> ${out}`);
