// Random bags evaluated by the original game/drills.js evalBag, for the C# parity test.
import * as st from './game/state.js';
import { evalBag, BAG_GOALS } from './game/drills.js';
import { CATALOG } from './game/items.js';
import { writeFileSync } from 'node:fs';
let a = 12345; const r = () => { a = (a * 1103515245 + 12345) % 2147483648; return a / 2147483648; };
const prices = Object.fromEntries(Object.entries(CATALOG).filter(([, c]) => !c.fixture).map(([id, c]) => [id, c.price]));
const ids = Object.keys(prices);
const cases = [];
for (let n = 0; n < 40; n++) {
  const G = st.createGame({ seed: 100 + n }); G.money = 1e8;
  const cart = {}; for (const id of ids) if (r() < 0.3) cart[id] = 1 + Math.floor(r() * 2);
  if (n % 3 === 0) Object.assign(cart, { water_500: 4, bars: 3, radio: 1, batteries_aa: 2, blanket: 1, contact_card: 1, cash: 1, hygiene_kit: 0, wipes: 1, water_jug: 1 });
  st.buy(G, cart, prices);
  for (const id of Object.keys(G.items)) { if (CATALOG[id].needsCheck && r() < 0.5) st.check(G, id); }
  for (const [id, e] of Object.entries(G.items)) { if (CATALOG[id].fixture) continue; const heavy = CATALOG[id].weightKg > 1; const k = heavy ? (r() < 0.15 ? 1 : 0) : Math.floor(r() * (e.home + 1)); if (k > 0 && CATALOG[id].weightKg * k < 3) st.pack(G, id, Math.min(k, e.home)); }
  const evals = Object.fromEntries(Object.keys(BAG_GOALS).map(c => [c, evalBag(G, c)]));
  cases.push({ save: JSON.parse(st.save(G)), evals });
}
// The passing bags of tests/drills.test.mjs: a full alert bag with checked gear, and the water trip's empty jug.
{
  const G = st.createGame({ seed: 3 }); G.money = 1e7;
  st.buy(G, { water_500: 2, bars: 2, radio: 1, batteries_aa: 2, blanket: 1, contact_card: 1 }, prices);
  st.check(G, 'flashlight'); st.check(G, 'radio');
  for (const [id, k] of Object.entries({ water_2l: 1, bars: 1, flashlight: 1, radio: 1, meds: 1, contact_card: 1, blanket: 1 })) st.pack(G, id, k);
  cases.push({ save: JSON.parse(st.save(G)), evals: Object.fromEntries(Object.keys(BAG_GOALS).map(c => [c, evalBag(G, c)])) });
  const W = st.createGame({ seed: 3 }); W.money = 1e7; st.buy(W, { water_jug: 1 }, prices); st.pack(W, 'water_jug');
  cases.push({ save: JSON.parse(st.save(W)), evals: Object.fromEntries(Object.keys(BAG_GOALS).map(c => [c, evalBag(W, c)])) });
}
writeFileSync(process.argv[2], JSON.stringify({ cases }, null, 0));
console.log(cases.length, 'cases; ok counts', Object.keys(BAG_GOALS).map(c => c + ':' + cases.filter(x => x.evals[c].ok).length).join(' '));
