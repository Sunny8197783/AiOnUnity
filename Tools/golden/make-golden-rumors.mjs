// Golden data for RumorsTests: the original game/rumors.js sets, the road evac_shortcut names for many seeds,
// every text it can produce, and scripted runs (judge / believe / verify / follow in random order, with the
// queries after every step and the events and know flags they leave behind).
//   node make-golden-rumors.mjs [out.json]
// AION_SRC = the original safe-route folder (default: the copy this port was made from).
import { writeFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const SRC = process.env.AION_SRC ?? 'C:/Users/김태양/AppData/Local/Temp/claude/80eae06ac10fc83e/082ba9f6-2e32-43aa-b236-612ace9a2adc/scratchpad/aion-src/safe-route';
const game = (name) => import(pathToFileURL(path.join(SRC, 'dist/game', name)).href); // absolute file:/// URL
const out = process.argv[2] ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '../../Tests/EditMode/Fixtures/golden-rumors.json');

const tm = await game('townmap.js');
const st = await game('state.js');
const ru = await game('rumors.js');

// The static part of every set (the text functions are covered by 'posts' below).
const sets = ru.RUMORS.map(r => ({ id: r.id, chapter: r.chapter, facts: r.facts, namesRoad: typeof r.road === 'function', items: r.items.map(({ text, ...it }) => it) }));
const facts = Object.fromEntries(ru.RUMORS.map(r => [r.id, ru.rumorFacts(r.id).map(f => f.text)]));

// evac_shortcut: the road it names, per seed.
const SEEDS = [...Array.from({ length: 500 }, (_, i) => i + 1), 0, 12345, 99991, 2147483647, 4294967295];
const roads = SEEDS.map(seed => [seed, ru.rumor('evac_shortcut').road(st.createGame({ seed }))]);

// Every set's posts for each road the evacuation can close, and for conditions the game never makes
// (no closure, an unknown one): the text then falls back to '통제 구간'.
const posts = [...Object.keys(tm.CLOSURES).map(id => [id]), ['danji', 'sijang'], [], ['nope'], ['nope', 'danji']].map(evac => {
  const G = st.createGame({ seed: 1 });
  G.cond.closures.evac = evac;
  return { evac, road: ru.rumor('evac_shortcut').road(G) ?? null, posts: Object.fromEntries(ru.RUMORS.map(r => [r.id, ru.posts(G, r.id)])) };
});

// ---------- scripted runs ----------
let a = 20261116;
const rnd = () => { a = (a * 1103515245 + 12345) % 2147483648; return a / 2147483648; };
const pick = (l) => l[Math.floor(rnd() * l.length)];
const IDS = ru.RUMORS.map(r => r.id);
const LABELS = ['official', 'site', 'neighbor', 'rumor', 'self'];
const someId = () => (rnd() < 0.07 ? 'nope' : pick(IDS));

function run(seed, n, evac) {
  const G = st.createGame({ seed });
  if (evac) G.cond.closures.evac = evac;
  const steps = [];
  // After every step: what the story would ask next.
  const ask = () => ({ misled: ru.misled(G), lost: ru.lostByRumor(G), seen: ru.seenRumors(G).map(s => s.id) });
  const step = (s, fn) => {
    let r;
    try { r = fn(); } catch { steps.push({ ...s, throws: true, q: ask() }); return; }
    steps.push({ ...s, ...(r === undefined ? {} : { r }), q: ask() });
  };
  for (let i = 0; i < n; i++) {
    const x = rnd();
    if (x < 0.30) {
      const id = someId();
      const keys = ru.RUMORS.find(r => r.id === id)?.items.map(it => it.key) ?? ['claim'];
      const item = rnd() < 0.4 ? undefined : rnd() < 0.08 ? 'nope' : pick(keys);
      const label = rnd() < 0.06 ? 'gossip' : pick(LABELS);
      step({ op: 'judge', id, pick: label, ...(item === undefined ? {} : { item }) }, () => ru.judge(G, id, label, item === undefined ? undefined : { item }));
    } else if (x < 0.46) {
      const id = someId();
      step({ op: 'believe', id }, () => ru.believe(G, id));
    } else if (x < 0.60) {
      const id = someId();
      step({ op: 'verify', id }, () => ru.verify(G, id));
    } else if (x < 0.72) { // the story reached the closed road
      const id = someId();
      step({ op: 'event', type: 'rumor_followed', details: { id } }, () => { st.recordEvent(G, 'rumor_followed', { id }); });
    } else if (x < 0.79) {
      const minutes = 1 + Math.floor(rnd() * 400);
      step({ op: 'advance', minutes }, () => { st.advance(G, minutes); });
    } else if (x < 0.84) {
      const chapter = pick(['prep', 'dday', 'endure', 'crossroads', 'evac']);
      step({ op: 'chapter', chapter }, () => { G.chapter = chapter; });
    } else if (x < 0.90) { // a know flag that is not a rumor id must read as "not misled"
      const value = pick([5, true, 'nope', null, '', ...IDS]);
      step({ op: 'know', value }, () => { G.know.misled = value; });
    } else { // events that look like rumor events but carry no usable id
      const type = pick(['rumor_judged', 'rumor_believed', 'rumor_followed']);
      const details = pick([{}, { id: 7 }, { id: null }, { id: 'nope' }, { item: 'claim' }]);
      step({ op: 'event', type, details }, () => { st.recordEvent(G, type, details); });
    }
  }
  return { seed, ...(evac ? { evac } : {}), steps, seen: ru.seenRumors(G), know: G.know, events: G.events };
}
const runs = [];
for (let i = 0; i < 14; i++) runs.push(run(1 + i * 37, 30));
runs.push(run(7, 30, ['munhwaSouth', 'sijang']), run(8, 30, []));

writeFileSync(out, JSON.stringify({ sets, facts, roads, posts, runs }, null, 0));
const ops = runs.flatMap(r => r.steps);
const count = (f) => ops.filter(f).length;
console.log(`golden-rumors: ${roads.length} seeds (${[...new Set(roads.map(r => r[1]))].join(', ')}), ${posts.length} road cases, ${runs.length} runs, ${ops.length} steps: `
  + `${count(s => s.throws)} throw, judge ${['sorted', 'doubted', 'misled'].map(k => `${k} ${count(s => s.r?.result === k)}`).join(' / ')}, `
  + `believe ${count(s => s.op === 'believe' && s.r === true)}, misled seen ${count(s => s.q.misled)}, lost seen ${count(s => s.q.lost)} -> ${out}`);
