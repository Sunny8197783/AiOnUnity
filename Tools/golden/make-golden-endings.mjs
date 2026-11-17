// What the ORIGINAL game/endings.js answers for the runs of make-golden-runs.mjs (saves: golden-runs.json): the
// whole fate (pickEnding), traces and near misses for endings the run did not reach, stock in litres, the static
// endings table and the ending gallery.   node make-golden-endings.mjs [out.json]
import { readFileSync } from 'node:fs';
import { buildRuns, lib, write, SRC } from './make-golden-runs.mjs';

const E = await lib('dist/game/endings.js');
const { PATHS, LEAVE, TOWN, ORDER, ENDINGS, STOCK_OK, waterLitres, pickEnding, trace, nearMisses, readGallery, addToGallery, galleryView } = E;
const runs = await buildRuns();

const endings = ENDINGS.map(({ lines, ...e }) => ({ ...e, facts: e.facts ?? [] }));
const OTHER = ['bus', 'walk', 'seoyeon', 'missed', 'stay', 'volunteer', 'commons', 'reopen', 'relief', 'board'];
const cases = runs.map(({ name, G }, i) => {
  const fate = pickEnding(G), c = { name, fate, waterLitres: waterLitres(G) };
  if (!name.startsWith('raw:') && i % 2 === 0) {
    const ids = [...new Set(['shut', OTHER[(i >> 1) % OTHER.length], i % 9 === 0 ? 'nope' : 'shut'])].filter((id) => id !== fate.id);
    c.traces = ids.map((id) => ({ id, trace: trace(G, id) }));
  }
  if (!name.startsWith('raw:') && i % 3 === 0) {
    const other = fate.id === 'commons' ? 'board' : 'commons';
    c.missedFor = { id: other, list: nearMisses(G, other) };
  }
  return c;
});

// ---------- gallery ----------
const raws = [null, undefined, 42, 'not json', '{"seen":[1]}', '', '42', 'null', '{"seen":{"bus":{"at":"2026-10-30"}}}', '{"seen":{"bus":{"at":"2026-10-30\\n"}}}',
  { seen: { nope: { at: '2026-10-30', runs: 1, trace: [] } } }, { seen: { bus: { at: 'yesterday' } } }, { seen: { bus: null, walk: 7, stay: 'x', shut: [] } },
  { seen: { bus: { at: '2026-10-30', runs: -2, trace: ['stairs_asked', 'evil', 7, 'path'] } } },
  { seen: { walk: { at: '2026-10-31', runs: 2.5, trace: 'stairs_asked' }, stay: { at: '2026-11-01', runs: 4, trace: ['hoard', 'violence', 'door_note', 'card_meet', 'mart_share', 'stayed', 'store_paid'] } }, extra: 1 },
  { seen: { board: { at: '2026-1-1', runs: 1 }, reopen: { at: 20261030, runs: 1 }, relief: { at: '2026-10-30', runs: '3', trace: [null, 'yoon_reply'] } } },
  [], { seen: null }, { notSeen: {} }];
const reads = raws.map((raw) => ({ kind: raw === undefined ? 'undefined' : typeof raw === 'string' ? 'string' : 'json', raw: raw === undefined ? null : raw, out: readGallery(raw) }));

let g = readGallery(null);
const adds = [];
for (const [id, traceIds, at] of [['reopen', ['path', 'clerk_contact', 'store_paid', 'store_shuttered'], '2026-10-30'], ['reopen', ['store_paid'], '2026-11-02'], ['nope', [], '2026-11-02'],
  ['bus', ['stairs_asked', 'water_for_seoyeon', 'door_note', 'card_meet'], '2026-11-03'], ['walk', null, '2026-11-04'], ['stay', ['stayed'], 'someday'],
  ['shut', ['hoard', 'violence', 'rumor_spread', 'store_took', 'candle_left', 'looter_fought', 'path'], '2026-11-05']]) {
  g = addToGallery(adds.length % 2 ? JSON.stringify(g) : g, id, traceIds, at);
  adds.push({ id, trace: traceIds, at, out: structuredClone(g), view: galleryView(g) });
}
const gallery = { reads, adds, emptyView: galleryView(null) };

// Coverage: every Korean line written in endings.js should be said by some run.
const said = cases.flatMap((c) => [...c.fate.lines, ...c.fate.trace.map((t) => t.text), ...(c.traces ?? []).flatMap((t) => t.trace.map((x) => x.text))]).join('\n');
const src = readFileSync(new URL('dist/game/endings.js', SRC), 'utf8');
const quoted = [...src.matchAll(/'((?:[^'\\\n]|\\.)*)'|"((?:[^"\\\n]|\\.)*)"/g)].map((m) => (m[1] ?? m[2]).replace(/\\(['"])/g, '$1'));
const fixed = new Set(ENDINGS.flatMap((e) => [e.title, e.place, e.hint]));
const literals = quoted.filter((s) => /[가-힣]/.test(s) && s.length > 14 && !fixed.has(s));
const unsaid = literals.filter((s) => !said.includes(s));
if (unsaid.length) throw new Error(`endings.js lines no run says:\n${unsaid.join('\n')}`);
const reached = new Set(cases.map((c) => c.fate.id));
if (reached.size !== ENDINGS.length) throw new Error('an ending is never reached');

console.log(write('golden-endings.json', { paths: PATHS, leave: [...LEAVE], town: [...TOWN], order: ORDER, stockOk: STOCK_OK, endings, cases, gallery }),
  `(${cases.length} runs, ${reached.size} endings, all ${literals.length} lines said)`);
