// Golden for Ripples.cs, from the original game/ripples.js: the PREP and CAUSES tables, whenText, and ripples() /
// causeOf() over seeded random event histories (every cause rule, matching and not, payoffs with and without a cause).
//
//   node make-golden-ripples.mjs [golden-ripples.json]
//
// SAFE_ROUTE_GAME overrides where the original dist/game/ modules are (an absolute file:/// URL ending in '/').
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const GAME = process.env.SAFE_ROUTE_GAME
  ?? 'file:///C:/Users/김태양/AppData/Local/Temp/claude/80eae06ac10fc83e/082ba9f6-2e32-43aa-b236-612ace9a2adc/scratchpad/aion-src/safe-route/dist/game/';
const mod = (name) => import(new URL(name, GAME).href);
const { ripples, causeOf, CAUSES, PREP, whenText } = await mod('ripples.js');
const st = await mod('state.js');

const here = dirname(fileURLToPath(import.meta.url));
const out = resolve(process.argv[2] ?? resolve(here, '../../Tests/EditMode/Fixtures/golden-ripples.json'));

let a = 97531; const rnd = () => { a = (a * 1103515245 + 12345) % 2147483648; return a / 2147483648; };
const pick = (list) => list[Math.floor(rnd() * list.length)];

// Events that are a cause for some prep, next to look-alikes that are not (wrong id, wrong person, empty cart).
const POOL = [
  ['bag_packed', {}], ['docs_done', { fields: ['family'], where: 'bag' }], ['shelter_checked', {}],
  ['encounter', { tool: 'route', result: 'planned', stage: 'guided' }], ['encounter', { tool: 'radio', result: 'tuned', stage: 'guided' }],
  ['encounter', { tool: 'extinguisher', result: 'practice', stage: 'guided' }], ['encounter', { tool: 'extinguisher', result: 'risky', stage: 'hint' }],
  ['check', { id: 'radio', charge: 100 }], ['check', { id: 'flashlight', charge: 30 }], ['check', { id: 'powerbank' }], ['check', {}],
  ['help', { who: 'seoyeon', what: '보조배터리를 나눠 썼다' }], ['help', { who: 'seoyeon', what: '길을 알려 줬다' }], ['help', { who: 'seoyeon' }],
  ['help', { who: 'elder', what: '함께 내려갔다' }], ['help', { who: 'manager', what: '304호 어르신을 알렸다' }], ['help', { who: 'manager', what: '순찰을 도왔다' }],
  ['help', { who: 'clerk', what: '304호 보조배터리' }],
  ['tub_filled', {}], ['buy', { cart: { water_jug: 1 }, total: 7000 }], ['buy', { cart: { cash: 2, bars: 1 }, total: 101500 }],
  ['buy', { cart: { bars: 3 }, total: 4500 }], ['buy', { cart: { water_jug: 0, cash: 0 }, total: 0 }], ['buy', {}],
  ['cash', { amount: 50000 }], ['home_ready', {}], ['gas_checked', {}], ['slot', { actionId: 'mart', cost: 1 }], ['battery_dead', { id: 'radio' }],
];
const PREPS = [...Object.keys(PREP), 'mystery'];
const MOMENTS = ['바로 나섰다', '받아 둔 물로 세수했다', '어둠 속에서 손전등을 켰다', '순간', ''];

function scenario(seed, n, { backwards = false, bare = false } = {}) {
  const G = st.createGame({ seed });
  for (let i = 0; i < n; i++) {
    // The clock mostly stands still or moves on; now and then a whole day passes (and, when asked, it jumps back).
    const r = rnd();
    if (r < 0.35) st.advance(G, Math.floor(rnd() * 240));
    else if (r < 0.45) st.advance(G, 1440 + Math.floor(rnd() * 600));
    else if (backwards && r < 0.5) { G.day -= 1; G.minute = Math.floor(rnd() * 1440); }
    if (rnd() < 0.3) {
      const d = {};
      if (rnd() < 0.95) d.prep = pick(PREPS);
      if (rnd() < 0.9) d.moment = pick(MOMENTS);
      st.recordEvent(G, 'payoff', d);
    } else {
      const [type, details] = pick(POOL);
      st.recordEvent(G, type, structuredClone(details));
    }
  }
  // An event written without details (the save keeps none): rules with a `when` see {} and do not match.
  if (bare) for (const e of G.events) if (e.type !== 'payoff' && rnd() < 0.3) delete e.details;
  const seqs = [0, 1, Math.ceil(n / 3), Math.ceil(n / 2), n, n + 1];
  return {
    save: JSON.parse(st.save(G)),
    ripples: ripples(G),
    causes: Object.fromEntries(PREPS.map(p => [p, causeOf(G, p)])),
    // causeOf(G, prep, beforeSeq) for a few event numbers; preps without a cause before that event are left out (null).
    before: seqs.map(beforeSeq => ({ beforeSeq, causes: Object.fromEntries(PREPS.map(p => [p, causeOf(G, p, beforeSeq)]).filter(([, c]) => c)) })),
  };
}

const scenarios = [];
for (let i = 0; i < 24; i++) scenarios.push(scenario(300 + i, 10 + Math.floor(rnd() * 70)));
for (let i = 0; i < 8; i++) scenarios.push(scenario(400 + i, 60, { backwards: true }));
for (let i = 0; i < 4; i++) scenarios.push(scenario(500 + i, 50, { bare: true }));
scenarios.push(scenario(600, 0));

const days = [-3, -2, -1, 0, 1, 2, 12, 14], minutes = [0, 1, 359, 360, 719, 720, 1079, 1080, 1259, 1260, 1439];
const golden = {
  prep: PREP,
  // The `when` tests are code: the scenarios above exercise them; here only whether a rule has one.
  causes: Object.fromEntries(Object.entries(CAUSES).map(([prep, rules]) => [prep, rules.map(r => ({ type: r.type, text: r.text, when: !!r.when }))])),
  whenText: days.flatMap(day => minutes.map(minute => ({ day, minute, text: whenText(day, minute) }))),
  scenarios,
};
mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, JSON.stringify(golden, null, 0));
const rows = scenarios.flatMap(s => s.ripples), seen = new Set(rows.map(r => r.prep)), texts = new Set(rows.map(r => r.cause.text));
const allTexts = Object.values(CAUSES).flat().map(r => r.text);
console.log(`${scenarios.length} scenarios, ${rows.length} ripple rows over ${seen.size}/${Object.keys(CAUSES).length} preps; cause texts seen ${texts.size}/${new Set(allTexts).size}`);
console.log('preps never rippled:', Object.keys(CAUSES).filter(p => !seen.has(p)).join(',') || '(none)', '| texts never seen:', [...new Set(allTexts)].filter(t => !texts.has(t)).join(' / ') || '(none)');
console.log('negative gaps:', rows.filter(r => r.gap < 0).length, '| ties:', scenarios.filter(s => s.ripples.some((r, i) => i && r.gap === s.ripples[i - 1].gap)).length, 'scenarios');
console.log('->', out);
