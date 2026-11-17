// Golden for Drills.cs (radio, extinguisher, contact card), from the original game/drills.js.
// The bag part of drills.js has its own golden (make-golden-bag.mjs -> golden-bag.json, BagDrill.cs).
//
//   node make-golden-drills.mjs [golden-drills.json]
//
// SAFE_ROUTE_GAME overrides where the original dist/game/ modules are (an absolute file:/// URL ending in '/').
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const GAME = process.env.SAFE_ROUTE_GAME
  ?? 'file:///C:/Users/김태양/AppData/Local/Temp/claude/80eae06ac10fc83e/082ba9f6-2e32-43aa-b236-612ace9a2adc/scratchpad/aion-src/safe-route/dist/game/';
const mod = (name) => import(new URL(name, GAME).href);
const { RADIO_WIDTH, radioSignal, radioNeeds, EXT_STEPS, EXT_CHOICES, evalExtinguisher, DOC_FIELDS, evalDocs } = await mod('drills.js');
const st = await mod('state.js');
const { CATALOG } = await mod('items.js');

const here = dirname(fileURLToPath(import.meta.url));
const out = resolve(process.argv[2] ?? resolve(here, '../../Tests/EditMode/Fixtures/golden-drills.json'));

// Small seeded generator (the same LCG as make-golden-bag.mjs), so the file is the same on every run.
let a = 24680; const rnd = () => { a = (a * 1103515245 + 12345) % 2147483648; return a / 2147483648; };
const r1 = (x) => Math.round(x * 10) / 10;

// ---------- radio signal ----------
const stationSets = [
  [],
  [{ freq: 89.1, name: 'A' }],
  [{ freq: 89.1, name: 'A' }, { freq: 90.0, name: 'B' }],
  // The stations of the story (story/common.js): music, an unsourced voice, the official broadcast.
  [{ freq: 95.1, name: '음악 방송', kind: 'music' }, { freq: 99.3, name: '알 수 없는 방송', kind: 'rumor' }, { freq: 102.1, name: '재난방송', kind: 'official' }],
  // Two stations on one frequency (the first wins), and two close enough to overlap.
  [{ freq: 91.9, name: 'first' }, { freq: 91.9, name: 'second' }, { freq: 92.4, name: 'near' }],
];
// Each case tunes to freq + plus. Both are short decimals and the test adds them itself, so the exact double at
// the edge of a station's band (89.1 + 0.6) does not depend on how a JSON parser rounds 17 digits.
const signals = [];
stationSets.forEach((set, si) => {
  const tune = [];
  for (let f = 87.5; f <= 108.001; f += 0.1) tune.push([r1(f), 0]);                           // what the dial sends: one decimal
  for (let i = 0; i < 40; i++) tune.push([Math.round((87.5 + rnd() * 20.5) * 1e4) / 1e4, 0]); // and anything in between
  for (const s of set) for (const d of [-0.61, -0.6, -0.5999, -0.3, -0.05, 0, 0.05, 0.3, 0.5999, 0.6, 0.61]) tune.push([s.freq, d]);
  for (const [freq, plus] of tune) {
    const r = radioSignal(freq + plus, set);
    signals.push({ set: si, freq, plus, station: r.station ? set.indexOf(r.station) : null, clarity: r.clarity });
  }
});

// ---------- radio needs ----------
const prices = Object.fromEntries(Object.entries(CATALOG).map(([id, c]) => [id, c.price]));
const needs = [];
const snap = (label, G) => needs.push({ label, save: JSON.parse(st.save(G)), needs: radioNeeds(G) });
{
  const G = st.createGame({ seed: 4 }); G.money = 1e7;
  snap('no radio', G);
  st.buy(G, { radio: 1 }, prices); snap('radio, no batteries', G);
  st.buy(G, { batteries_aa: 1 }, prices); st.pack(G, 'radio'); snap('radio in the bag, new pack at home', G);
  st.pack(G, 'batteries_aa'); snap('both in the bag', G);
  st.check(G, 'radio'); snap('checked', G);
  st.drainCharge(G, 'radio', 100); snap('run flat, no pack left', G);
  st.trade(G, { ramen: 1 }, { batteries_old: 1 }, 'clerk'); snap('run flat, a half-used pack', G);
  st.check(G, 'radio'); snap('checked again with the old pack', G);
  st.trade(G, { radio: 1 }, { bars: 1 }, 'clerk'); snap('radio traded away (entry stays, none owned)', G);
}
{
  const G = st.createGame({ seed: 5 }); G.money = 1e7;
  st.trade(G, { ramen: 1 }, { batteries_old: 1 }, 'clerk'); snap('old pack only, no radio', G);
  st.buy(G, { radio: 2 }, prices); snap('two radios and an old pack', G);
}

// ---------- extinguisher ----------
const TOKENS = ['fight', 'retreat', 'shout', 'pin', 'aim', 'squeeze', 'sweep'];
const seqs = [[]];
for (const x of TOKENS) { seqs.push([x]); for (const y of TOKENS) { seqs.push([x, y]); for (const z of TOKENS) seqs.push([x, y, z]); } }
for (let i = 0; i < 500; i++) {
  const n = 4 + Math.floor(rnd() * 6), s = [];
  // Mostly the official order with one slip, so the 'out' and firstError branches are well covered.
  if (rnd() < 0.5) {
    const base = ['fight', 'shout', 'pin', 'aim', 'squeeze', 'sweep'].filter(() => rnd() < 0.85);
    if (rnd() < 0.5 && base.length > 1) { const i = Math.floor(rnd() * (base.length - 1)); [base[i], base[i + 1]] = [base[i + 1], base[i]]; }
    if (rnd() < 0.3) base.splice(Math.floor(rnd() * (base.length + 1)), 0, TOKENS[Math.floor(rnd() * TOKENS.length)]);
    s.push(...base);
  } else for (let k = 0; k < n; k++) s.push(TOKENS[2 + Math.floor(rnd() * 5)]);
  seqs.push(s);
}
const extinguisher = [];
for (const fire of ['small', 'large']) for (const sequence of seqs) extinguisher.push({ fire, sequence, ...evalExtinguisher(sequence, { fire }) });

// ---------- contact card ----------
const full = Object.fromEntries(DOC_FIELDS.map(f => [f.id, '적음']));
const cards = [
  null, {}, full,
  { contacts: '   ', meds: false, shelter: null, meet: '' },
  { contacts: '엄마 010', meds: 0 },
  { contacts: true, meds: 1.5, shelter: '\t\n', meet: ' x ', idCopy: false, cash: '0' },
  { ...full, cash: '' }, { ...full, meet: null }, { ...full, extra: '무시됨' },
  { idCopy: '사본', cash: true },
  { contacts: [], meds: ['x'], shelter: [''], meet: [null], idCopy: {}, cash: [1, 2] },
  { contacts: '\u00a0\u3000', meds: '\ufeff', shelter: '\u200b' }, // JS trim strips NBSP, ideographic space and BOM, not the zero-width space
];
const docs = [];
for (const card of cards) for (const where of [undefined, 'bag', 'home', 'desk']) docs.push({ card, where: where ?? null, ...evalDocs(card ?? undefined, where) });

const golden = {
  radioWidth: RADIO_WIDTH,
  stationSets, signals, needs,
  extSteps: EXT_STEPS, extChoices: EXT_CHOICES, extinguisher,
  docFields: DOC_FIELDS, docs,
};
mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, JSON.stringify(golden, null, 0));
const count = (k) => Object.entries(extinguisher.reduce((m, x) => (m[x[k]] = (m[x[k]] ?? 0) + 1, m), {})).map(([v, n]) => `${v}:${n}`).join(' ');
console.log(`${signals.length} signals (${signals.filter(s => s.station !== null).length} heard), ${needs.length} radio states, ${docs.length} cards (${docs.filter(d => d.ok).length} ok)`);
console.log(`${extinguisher.length} extinguisher runs; result ${count('result')}; firstError ${count('firstError')}; correct ${count('correct')}`);
console.log('->', out);
