// Golden for Ledger.cs, from the original game/ledger.js: the PICKS table and plan lines, every plan at home and
// at the shelter on a well-stocked and on a bare home, and seeded random households (stock at home and in the
// bag, radio charge, phone, stats near their caps, an earlier ledger to carry open water and gas over) simulated
// and then applied.
//
//   node make-golden-ledger.mjs [golden-ledger.json]
//
// SAFE_ROUTE_GAME overrides where the original dist/game/ modules are (an absolute file:/// URL ending in '/').
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const GAME = process.env.SAFE_ROUTE_GAME
  ?? 'file:///C:/Users/김태양/AppData/Local/Temp/claude/80eae06ac10fc83e/082ba9f6-2e32-43aa-b236-612ace9a2adc/scratchpad/aion-src/safe-route/dist/game/';
const mod = (name) => import(new URL(name, GAME).href);
const { simulateDays, applyLedger, planLine, ledgerSummary, PICKS } = await mod('ledger.js');
const st = await mod('state.js');
const { CATALOG } = await mod('items.js');

const here = dirname(fileURLToPath(import.meta.url));
const out = resolve(process.argv[2] ?? resolve(here, '../../Tests/EditMode/Fixtures/golden-ledger.json'));

let a = 13579; const rnd = () => { a = (a * 1103515245 + 12345) % 2147483648; return a / 2147483648; };
const pick = (list) => list[Math.floor(rnd() * list.length)];
const int = (lo, hi) => lo + Math.floor(rnd() * (hi - lo + 1));
const save = (G) => JSON.parse(st.save(G));
const prices = Object.fromEntries(Object.entries(CATALOG).map(([id, c]) => [id, c.price]));
const PLANS = [];
for (const water of [1, 2, 3]) for (const meals of [1, 2, 3]) for (const power of ['phone', 'powerbank', 'radio']) PLANS.push({ water, meals, power });

function game(seed, cart = {}) {
  const G = st.createGame({ seed });
  G.money = 1e7; G.day = 3; G.minute = 10 * 60;
  st.buy(G, cart, prices);
  return G;
}

// What applyLedger may touch, so a case does not carry the whole save twice.
const touched = (G, eventsBefore) => ({ items: G.items, stats: G.stats, phone: G.phone, know: G.know, ledger: G.ledger, events: G.events.slice(eventsBefore) });

function simulateOnly(G, plan, days, place) {
  const before = st.save(G);
  const result = place === undefined ? simulateDays(G, plan, days) : simulateDays(G, plan, days, { place });
  if (st.save(G) !== before) throw new Error('simulateDays changed G');
  return { plan, days, place: place ?? null, result };
}

// ---------- every plan, both places ----------
const matrices = [];
{
  const rich = game(8, { water_500: 6, water_2l: 4, canned: 3, bars: 4, ramen: 3, gas_stove: 1, butane: 1, water_jug: 1, radio: 1, batteries_aa: 1, powerbank: 1 });
  st.check(rich, 'radio');
  for (const [id, n] of Object.entries({ water_500: 3, bars: 2, canned: 1, old_bars: 1, powerbank: 1 })) st.pack(rich, id, n);
  rich.phone.battery = 38;
  const bare = game(9);
  for (const id of ['water_2l', 'ramen', 'rice_instant', 'canned']) bare.items[id].home = 0;
  for (const [label, G, days] of [['well stocked', rich, 4], ['bare home', bare, 3]])
    matrices.push({ label, save: save(G), cases: PLANS.flatMap(plan => ['home', 'shelter'].map(place => simulateOnly(G, plan, days, place))) });
  // No place given: home.
  matrices.push({ label: 'default place', save: save(rich), cases: [simulateOnly(rich, PLANS[4], 2, undefined)] });
}

// ---------- random households ----------
const cases = [];
for (let n = 0; n < 96; n++) {
  const cart = {};
  const stock = { water_500: 8, water_2l: 5, canned: 4, rice_instant: 3, bars: 5, ramen: 8, butane: 2, batteries_aa: 2 };
  for (const [id, max] of Object.entries(stock)) if (rnd() < 0.55) cart[id] = int(1, max);
  for (const id of ['gas_stove', 'water_jug', 'radio', 'powerbank']) if (rnd() < 0.5) cart[id] = 1;
  const G = game(700 + n, cart);
  G.day = int(-3, 12); G.minute = int(0, 1439);
  // Thin out what was at home to begin with, so trips, old bars and skipped meals happen.
  for (const id of ['water_2l', 'ramen', 'rice_instant', 'canned', 'old_bars']) if (rnd() < 0.4) G.items[id].home = int(0, G.items[id].home);
  // Part of the stock goes in the bag: the shelter only sees the bag, home eats from home first.
  for (const id of ['water_500', 'water_2l', 'canned', 'rice_instant', 'bars', 'ramen', 'old_bars', 'butane', 'gas_stove', 'water_jug', 'radio', 'powerbank']) {
    const e = G.items[id];
    if (e && e.home > 0 && rnd() < 0.4) st.pack(G, id, int(1, e.home));
  }
  if (G.items.radio && rnd() < 0.75 && st.check(G, 'radio').ok) {
    const charge = pick([100, 76, 30, 10, 8, 7.5, 41.3, 87.5, 3, 24, 16.1, undefined]);
    if (charge === undefined) delete G.items.radio.charge; // checked before charges existed: counts as full
    else G.items.radio.charge = charge;
  }
  if (G.items.powerbank && rnd() < 0.3) st.check(G, 'powerbank');
  G.phone.battery = pick([64, 100, 40, 26, 20, 19.5, 5, 0, 33.3, 72]);
  G.stats.thirst = pick([10, 50, 78, 80, 85, 99.5, 0]);
  G.stats.fatigue = pick([20, 60, 84, 85, 90, 12.5]);
  G.stats.health = pick([100, 50, 22, 20, 15, 4, 61.5]);
  G.stats.tension = pick([25, 0, 96, 100, 3]);
  // Sometimes days have already been played out: open water and gas carry over from the last row.
  if (rnd() < 0.35) applyLedger(G, simulateDays(G, pick(PLANS), int(1, 3), { place: pick(['home', 'home', 'shelter']) }));

  const plan = pick(PLANS), days = pick([1, 1, 2, 2, 3, 3, 4, 5, 6, 7, 9, 12, 30]), place = pick(['home', 'home', 'shelter']);
  const before = save(G), eventsBefore = G.events.length;
  const { result } = simulateOnly(G, plan, days, place);
  const rows = applyLedger(G, result);
  if (JSON.stringify(rows) !== JSON.stringify(result.rows)) throw new Error('applyLedger returns the rows');
  cases.push({ before, plan, days, place, result, after: touched(G, eventsBefore), summary: ledgerSummary(G), planLine: planLine(plan) });
}

// ---------- the radio's charge, whole and fractional ----------
// One household; only the radio's charge and the number of days change (the test sets the charge after loading).
const radio = { save: null, runs: [] };
{
  const base = () => { const G = game(21, { radio: 1, batteries_aa: 1, water_2l: 12, canned: 8 }); st.check(G, 'radio'); return G; };
  radio.save = save(base());
  for (const charge of [100, null, 87.5, 41.3, 16.1, 24, 8, 7.5, 3, 0.4])
    for (const days of [1, 2, 3, 5]) {
      const G = base();
      if (charge === null) delete G.items.radio.charge; else G.items.radio.charge = charge;
      const eventsBefore = G.events.length;
      const { result } = simulateOnly(G, { water: 2, meals: 2, power: 'radio' }, days, 'home');
      applyLedger(G, result);
      radio.runs.push({ charge, days, result, after: touched(G, eventsBefore) });
    }
}

// ---------- bad input ----------
const bad = [
  { plan: { water: 4, meals: 2, power: 'phone' }, days: 3, place: 'home' },
  { plan: { water: 0, meals: 2, power: 'phone' }, days: 3, place: 'home' },
  { plan: { water: 2, meals: 4, power: 'phone' }, days: 3, place: 'home' },
  { plan: { water: 2, meals: 2, power: 'tv' }, days: 3, place: 'home' },
  { plan: { water: 2, meals: 2, power: null }, days: 3, place: 'home' },
  { plan: null, days: 3, place: 'home' },
  { plan: { water: 2, meals: 2, power: 'phone' }, days: 0, place: 'home' },
  { plan: { water: 2, meals: 2, power: 'phone' }, days: -1, place: 'home' },
  { plan: { water: 2, meals: 2, power: 'phone' }, days: 31, place: 'home' },
  { plan: { water: 2, meals: 2, power: 'phone' }, days: 2, place: 'car' },
  { plan: { water: 2, meals: 2, power: 'phone' }, days: 2, place: '' },
];
{
  const G = game(8);
  for (const b of bad) {
    let threw = false;
    try { simulateDays(G, b.plan, b.days, { place: b.place }); } catch { threw = true; }
    if (!threw) throw new Error(`expected a throw: ${JSON.stringify(b)}`);
  }
  let threw = false;
  try { applyLedger(G, {}); } catch { threw = true; }
  if (!threw) throw new Error('applyLedger({}) should throw');
}

const golden = {
  picks: PICKS,
  planLines: PLANS.map(plan => ({ plan, line: planLine(plan) })),
  emptySummary: ledgerSummary(game(8)),
  matrices, cases, radio, bad,
};
mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, JSON.stringify(golden, null, 0));
const all = [...matrices.flatMap(m => m.cases), ...cases, ...radio.runs];
const lines = new Set(all.flatMap(c => c.result.lines.map(l => l.replace(/\d+(\.\d+)?/g, '#'))));
const sum = (f) => all.reduce((s, c) => s + f(c.result), 0);
console.log(`${all.length} simulations (${cases.length} applied); trips ${sum(r => r.summary.trips)}, cooked ${sum(r => r.summary.cooked)}, skipped ${sum(r => r.summary.skipped)}, old bars ${sum(r => r.summary.oldBars)}`);
console.log(`radio daily ${all.filter(c => c.result.radioDaily).length}, radio flat ${all.filter(c => c.result.lines.some(l => l.includes('다 닳았다'))).length}, bank charged ${all.filter(c => c.result.powerbankCharged).length}, topped ${all.filter(c => c.result.usedBank).length}, stove ${all.filter(c => c.result.usedStove).length}, jug ${all.filter(c => c.result.usedJug).length}, five lines ${all.filter(c => c.result.lines.length === 5).length}`);
console.log(`${lines.size} distinct line shapes; odd numbers in lines: ${[...new Set(all.flatMap(c => c.result.lines.join(' ').match(/\d+\.\d{3,}/g) ?? []))].join(' ') || '(none)'}`);
console.log(`battery_dead events ${cases.filter(c => c.after.events.some(e => e.type === 'battery_dead')).length}; carried-over ledgers ${cases.filter(c => c.before.ledger.length).length}; stat deltas hitting a cap ${cases.filter(c => Object.values(c.result.stats).some(d => !Number.isInteger(d))).length} fractional`);
console.log('->', out);
