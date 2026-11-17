// Golden data for RoutesTests: the original game/routes.js shortestPath, routeOptions and explain over every
// closure set conditions can pick (CLOSURE_SETS dday + evac), no closure and all closures, several gate states.
//   node make-golden-routes.mjs [out.json]
// AION_SRC = the original safe-route folder (default: the copy this port was made from).
// Explicit results are kept as the original's objects; the exhaustive sweeps (every start/goal pair) are kept as
// one FNV-1a digest per graph so the fixture stays small. A digest that differs names the graph to look at.
import { writeFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const SRC = process.env.AION_SRC ?? 'C:/Users/김태양/AppData/Local/Temp/claude/80eae06ac10fc83e/082ba9f6-2e32-43aa-b236-612ace9a2adc/scratchpad/aion-src/safe-route';
const game = (name) => import(pathToFileURL(path.join(SRC, 'dist/game', name)).href); // absolute file:/// URL
const out = process.argv[2] ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '../../Tests/EditMode/Fixtures/golden-routes.json');

const tm = await game('townmap.js');
const st = await game('state.js');
const rt = await game('routes.js');
const { CATALOG } = await game('items.js');

// FNV-1a over UTF-16 code units; its twin in the C# tests is TownGolden.Fnv.
const fnv = (s) => { let h = 0x811c9dc5; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193) >>> 0; } return h.toString(16).padStart(8, '0'); };
const uniq = (lists) => [...new Map(lists.map(l => [JSON.stringify(l), l])).values()];
const NODE_IDS = Object.keys(tm.NODES);
const CLOSURE_SETS = uniq([[], ...st.CLOSURE_SETS.dday, ...st.CLOSURE_SETS.evac, Object.keys(tm.CLOSURES)]);
const WORST = { east: false, stairs: true, ramp: true, access: false }; // any night: east gate closed, C not open yet
const graphOf = (closures, gates) => tm.graph(gates ? { closures, gates } : { closures });

// ---------- shortestPath: explicit results ----------
const PAIRS = [['home', 'shelter'], ['home', 'assembly'], ['home', 'water'], ['cafe', 'shelter'], ['store', 'water'], ['shelter', 'home'], ['office', 'B'], ['mart', 'east'], ['home', 'home']];
const paths = [];
for (const closures of CLOSURE_SETS) for (const gates of [null, WORST]) {
  const adj = graphOf(closures, gates);
  paths.push({ closures, gates, results: PAIRS.map(([from, to]) => ({ from, to, path: rt.shortestPath(adj, from, to) })) });
}

// Options. JSON has no Infinity: it is written as the string 'Infinity'.
const VARIANTS = [
  {},
  { avoidTags: ['stairs', 'steep'] },
  { avoidTags: ['crossing'] },
  { tagCost: { stairs: 100 } },
  { tagCost: { stairs: 2.5, steep: 1.7, ramp: 1.2, crossing: 3, level: 0.5 } },
  { tagCost: { stairs: Infinity, ramp: Infinity, level: Infinity } },
  { tagCost: { crossing: 0 } },
  { avoidTags: ['stairs'], tagCost: { ramp: 4 } },
];
const plain = (v) => ({ ...v, ...(v.tagCost ? { tagCost: Object.fromEntries(Object.entries(v.tagCost).map(([k, c]) => [k, c === Infinity ? 'Infinity' : c])) } : {}) });
const variants = [];
for (const [closures, gates] of [[[], null], [['yeonmuCenter'], WORST], [['danji', 'munhwaNorth'], { east: true, stairs: false, ramp: true, access: true }]]) {
  const adj = graphOf(closures, gates);
  for (const v of VARIANTS) for (const [from, to] of [['I3', 'shelter'], ['A', 'shelter'], ['home', 'shelter'], ['cafe', 'shelter'], ['W', 'I5']]) {
    variants.push({ closures, gates, from, to, options: plain(v), path: rt.shortestPath(adj, from, to, v) });
  }
}

// ---------- shortestPath: every start/goal pair, one digest per graph and variant ----------
const pathLine = (p) => (p ? `${p.nodes.join(' ')}|${p.length}|${p.segments.join(' ')}|${p.tags.join(' ')}` : '-');
const SWEEP_GATES = [null, WORST, { east: false, stairs: false, ramp: false, access: true }, { east: true, stairs: false, ramp: true, access: true }];
const SWEEP_VARIANTS = [0, 1, 2, 4, 5];
const sweep = [];
for (const closures of CLOSURE_SETS) for (const gates of SWEEP_GATES) {
  const adj = graphOf(closures, gates);
  for (const vi of SWEEP_VARIANTS) {
    let found = 0;
    const lines = [];
    for (const from of NODE_IDS) for (const to of NODE_IDS) {
      const p = rt.shortestPath(adj, from, to, VARIANTS[vi]);
      if (p) found++;
      lines.push(`${from}>${to}=${pathLine(p)}`);
    }
    sweep.push({ closures, gates, options: plain(VARIANTS[vi]), found, digest: fnv(lines.join('\n')) });
  }
}

// ---------- shortestPath: ties ----------
// The town has no two ways of exactly the same cost, so these hand-made graphs pin down which of several equal
// ways the original takes (the node reached first is settled first; a later way of the same cost does not replace
// an earlier one). Node ids are names, never numbers: a JS object lists number-like keys first.
const link = (adj, a, b, len, tags = []) => {
  const seg = `${a}-${b}`;
  (adj[a] ||= []).push({ to: b, seg, tags, len });
  (adj[b] ||= []).push({ to: a, seg, tags, len });
};
const grid = (w, h, tagOf = () => []) => {
  const adj = {}, id = (x, y) => `n${x}_${y}`;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    if (x + 1 < w) link(adj, id(x, y), id(x + 1, y), 1, tagOf(x, y, 'e'));
    if (y + 1 < h) link(adj, id(x, y), id(x, y + 1), 1, tagOf(x, y, 's'));
  }
  return adj;
};
const diamond = {};
link(diamond, 'a', 'c', 1, ['steep']); link(diamond, 'a', 'b', 1, ['stairs']); link(diamond, 'b', 'd', 1); link(diamond, 'c', 'd', 1, ['ramp']);
link(diamond, 'a', 'd', 2, ['level']); link(diamond, 'd', 'e', 0, ['crossing']); link(diamond, 'e', 'f', 0); link(diamond, 'a', 'f', 2);
const lone = { ...grid(2, 2), island: [] };
const TIE_GRAPHS = [
  ['grid 3x3, every edge 1 m', grid(3, 3), [{}]],
  ['grid 4x4, every edge 1 m', grid(4, 4), [{}]],
  ['grid 3x5 with stairs on the middle row', grid(3, 5, (x, y, d) => (y === 2 && d === 'e' ? ['stairs'] : y === 1 && d === 's' ? ['ramp', 'crossing'] : [])),
    [{}, { avoidTags: ['stairs'] }, { tagCost: { stairs: 2 } }, { tagCost: { stairs: 0, ramp: 0.5 } }, { tagCost: { ramp: 2, crossing: 0.5 } }, { tagCost: { ramp: Infinity, crossing: 0 } }]],
  ['diamond with a direct way and free edges', diamond, [{}, { tagCost: { level: 0.5 } }, { avoidTags: ['stairs', 'steep'] }, { tagCost: { stairs: 0, steep: 0, ramp: 0, level: 0 } }]],
  ['a node with no way out', lone, [{}]],
];
const ties = [];
for (const [name, adj, list] of TIE_GRAPHS) for (const v of list) {
  const ids = Object.keys(adj);
  const lines = ids.flatMap(from => ids.map(to => `${from}>${to}=${pathLine(rt.shortestPath(adj, from, to, v))}`));
  // Small graphs keep their lines (readable when a test fails); the bigger ones only the digest.
  ties.push({ name, options: plain(v), adj: Object.entries(adj).map(([node, links]) => ({ node, links })),
    digest: fnv(lines.join('\n')), ...(lines.length <= 81 ? { lines } : {}) });
}

// ---------- routeOptions ----------
// Gates as the game passes them: booleans or the town level's 'open' | 'closed'; a gate not mentioned is open.
const OPT_GATES = [
  { east: false },
  { east: 'closed', access: 'closed' },
  {},
  { east: false, stairs: false, ramp: false, access: true },
  { east: 'closed', stairs: 'open', ramp: 'open', access: 'open' },
  { east: false, stairs: false, ramp: false, access: false },
];
const withSaid = (opts) => opts.map(o => ({ ...o, said: rt.explain(o) }));
const options = [];
CLOSURE_SETS.forEach((closures, ci) => {
  const main = closures.length === 0 || (closures.length === 1 && closures[0] === 'yeonmuCenter');
  for (const gates of main ? OPT_GATES : [OPT_GATES[ci % 2]]) for (const mobility of ['walk', 'walker']) {
    for (const [from, to] of [['home', 'shelter'], ['cafe', 'shelter'], ['home', 'assembly']]) {
      options.push({ from, to, closures, gates, mobility, result: withSaid(rt.routeOptions(null, from, to, { closures, gates, mobility })) });
    }
  }
});

const optLine = (o) => `${o.kind}/${o.entrance}/${o.length}/${o.seconds}/${o.nodes.join(' ')}/${o.segments.join(' ')}/${o.tags.join(' ')}/${rt.explain(o)}`;
const SWEEP_TO = ['shelter', 'assembly', 'water', 'home', 'cafe'];
const optionSweep = [];
// OPT_GATES[4] opens and closes the same gates as OPT_GATES[0]; the explicit results above cover its spelling.
for (const closures of CLOSURE_SETS) for (const gates of OPT_GATES.filter((_, i) => i !== 4)) for (const [mobility, carryingKg] of [['walk', undefined], ['walker', undefined], ['walk', 10]]) {
  let count = 0;
  const lines = [];
  for (const from of NODE_IDS) for (const to of SWEEP_TO) {
    const opts = rt.routeOptions(null, from, to, { closures, gates, mobility, carryingKg });
    count += opts.length;
    lines.push(`${from}>${to}=${opts.map(optLine).join(';')}`);
  }
  optionSweep.push({ closures, gates, mobility, ...(carryingKg === undefined ? {} : { carryingKg }), count, digest: fnv(lines.join('\n')) });
}

// ---------- routeOptions with a game: the bag's weight slows walking above 8 kg ----------
const prices = Object.fromEntries(Object.entries(CATALOG).filter(([, c]) => !c.fixture).map(([id, c]) => [id, c.price]));
const bag = (seed, cart, packed) => {
  const G = st.createGame({ seed }); G.money = 1e7;
  if (Object.keys(cart).length) st.buy(G, cart, prices);
  for (const [id, n] of Object.entries(packed)) st.pack(G, id, n);
  return G;
};
const GAMES = [
  ['empty bag', bag(1, {}, {})],
  ['light bag', bag(2, { bars: 2 }, { water_2l: 1, bars: 2, meds: 1 })],
  ['exactly 8 kg: not heavy', bag(3, { gas_stove: 1, butane: 1, first_aid: 1 }, { water_2l: 2, gas_stove: 1, butane: 1, first_aid: 1, clothes: 1 })],
  ['8.01 kg: heavy', bag(4, { gas_stove: 1, butane: 1, first_aid: 1, cash: 1 }, { water_2l: 2, gas_stove: 1, butane: 1, first_aid: 1, clothes: 1, cash: 1 })],
  ['five 2 L bottles (tests/routes.test.mjs)', bag(1, { water_2l: 3 }, { water_2l: 5 })],
];
const CALLS = [
  { from: 'home', to: 'water', options: {} },
  { from: 'home', to: 'shelter', options: { gates: { east: false } } },
  { from: 'cafe', to: 'shelter', options: { closures: ['yeonmuCenter'], gates: { east: false }, mobility: 'walker' } },
  { from: 'home', to: 'water', options: { carryingKg: 0 } },
  { from: 'home', to: 'water', options: { carryingKg: 8 } },
  { from: 'home', to: 'water', options: { carryingKg: 8.01 } },
  { from: 'home', to: 'water', options: { carryingKg: 10, mobility: 'walker' } },
];
const games = GAMES.map(([name, G]) => ({
  name, bagKg: st.bagLoad(G).weightKg, save: JSON.parse(st.save(G)),
  calls: CALLS.map(c => ({ ...c, result: withSaid(rt.routeOptions(G, c.from, c.to, c.options)) })),
}));
const noGame = CALLS.map(c => ({ ...c, result: withSaid(rt.routeOptions(null, c.from, c.to, c.options)) }));

// ---------- explain ----------
const EXPLAIN = [
  null, {}, { nodes: [] },
  { nodes: ['home', 'gate'], length: 22, tags: [] },
  { nodes: ['home', 'gate'], length: 22 },                                                  // no tags at all
  { nodes: ['I1', 'I2'], length: 100, tags: [] },                                           // junctions have no name
  { nodes: ['home', 'home', 'gate', 'moon', 'gate'], length: 5, tags: ['level', 'nope'] },  // a name once in a row; unknown node and tag skipped
  { nodes: ['A', 'shelter'], length: 20, seconds: 0, tags: ['stairs'] },                    // never under 1 minute
  { nodes: ['A', 'shelter'], length: 20.5, seconds: 89, tags: ['stairs', 'steep', 'ramp', 'crossing'] },
  { nodes: ['A', 'shelter'], length: 2000, seconds: 90, tags: ['crossing', 'stairs'] },     // 1.5 minutes rounds up
  { nodes: ['A', 'shelter'], length: 195, seconds: 150, tags: [] },                         // and so does 2.5 (Math.round, not banker's rounding)
  { nodes: ['A', 'shelter'], length: 13, seconds: 30, tags: [] },
  { nodes: ['C', 'shelter'], length: 1234, tags: ['ramp'] },                                // no seconds: timed at walking speed
  rt.shortestPath(tm.graph(), 'home', 'shelter'),
  rt.shortestPath(tm.graph({ closures: ['yeonmuCenter', 'danji'], gates: WORST }), 'home', 'shelter'),
  rt.shortestPath(tm.graph(), 'cafe', 'office'),
];
const explain = EXPLAIN.map(option => ({ option, said: rt.explain(option) }));

// gateOpen as routeOptions applies it: does a route to the shelter still enter by the east gate?
const gateOpen = [true, false, 'open', 'closed', 'unknown', null, 0, 1, ''].map(value => ({
  value, open: rt.routeOptions(null, 'east', 'shelter', { gates: { east: value, stairs: false, ramp: false, access: false } }).length > 0,
}));

writeFileSync(out, JSON.stringify({ closureSets: CLOSURE_SETS, paths, variants, sweep, ties, options, optionSweep, games, noGame, explain, gateOpen }, null, 0));
console.log(`golden-routes: ${paths.length * PAIRS.length} paths, ${variants.length} variants, ${sweep.length} sweep digests (${NODE_IDS.length ** 2} pairs each), ${ties.length} tie graphs, `
  + `${options.length} option sets, ${optionSweep.length} option digests, ${games.length} games -> ${out}`);
