// Golden for the story runtime + common scenes: runs the ORIGINAL story/runtime.js and story/common.js under node
// with a fake engine and scripted answers, and writes what they did (toasts, talk lines, events, ledger rows,
// item counts) to Tests/EditMode/Fixtures/story-runtime-golden.json. StoryRuntimeGoldenTests replays the same
// scripts on StoryContext + FakeStoryHost and compares.
//   node make-golden-story-runtime.mjs <path to safe-route/dist> <out json>
import { writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const dist = process.argv[2], out = process.argv[3];
const imp = (p) => import(pathToFileURL(path.join(dist, p)).href);
const { createRuntime } = await imp('story/runtime.js');
const { dayLedger, stockLook, lookAlbum, radioScene, eatOldBars } = await imp('story/common.js');
const { createGame, recordEvent, check } = await imp('game/state.js');

function rig(seed, answer, radio) {
  let G = createGame({ seed });
  const toasts = [], talks = [], frames = new Set(), inter = new Set();
  const level = (id) => ({ id, points: [], npcs: {}, state: {}, setState(k, v) { this.state[k] = v; } });
  const built = {};
  const engine = {
    level: null, controlsEnabled: true, player: { x: 0, y: 0, z: 0, speedMul: 1, staminaUse: 1 },
    onNearest() {}, onFrame(f) { frames.add(f); return () => frames.delete(f); }, onInteract(f) { inter.add(f); return () => inter.delete(f); },
    setPower() {}, setEnv() {}, setControls(v) { engine.controlsEnabled = v; },
    async enter(id, spawn, { prepare }) { engine.level = built[id] ||= level(id); await prepare(engine.level); return engine.level; },
  };
  const hud = { prompt() {}, setClock() {}, setStats() {}, setBattery() {}, setBag() {}, setTension() {}, setObjective() {}, toast: (t, o) => toasts.push([t, o.label, o.ms]), caption: async () => {} };
  const ui = { talk: async (o) => { talks.push({ speaker: o.speaker, role: o.role, lines: o.lines, choices: o.choices?.map(c => c.id) ?? null }); return answer(o); }, card: async () => 'ok', radio: async () => radio() };
  const ctx = createRuntime({ engine, hud, ui, audio: { ambience() {}, radio() {} }, settings: {}, persist: { checkpoint() {} }, getG: () => G, setG: (g) => { G = g; } });
  return { ctx, toasts, talks, get G() { return G; }, press: (p) => Promise.all([...inter].map(f => f(p))) };
}
const items = (G) => Object.fromEntries(Object.entries(G.items).map(([k, v]) => [k, [v.home, v.bag, v.used, v.checked, v.charge ?? null]]));
const dump = (r) => ({ toasts: r.toasts, talks: r.talks, events: r.G.events.map(e => [e.type, e.details]), stats: r.G.stats, items: items(r.G), ledger: r.G.ledger, battery: r.G.phone.battery, money: r.G.money, info: r.G.info.map(i => [i.text, i.label]) });

const cases = {};
// 1. day ledger, the second choice of each pick, three days at home from D+3 (seed 11)
for (const [name, pick, place] of [['ledger_home_1', 1, 'home'], ['ledger_home_0', 0, 'home'], ['ledger_shelter_2', 2, 'shelter']]) {
  const r = rig(11, (o) => o.choices?.at(pick)?.id);
  r.G.day = 3;
  const lines = await dayLedger(r.ctx, 3, place);
  cases[name] = { lines, ...dump(r) };
}
// 2. the home stock (eat one) and the album twice (seed 11)
{
  const r = rig(11, (o) => o.choices?.[0]?.id);
  await stockLook(r.ctx); await lookAlbum(r.ctx); await lookAlbum(r.ctx); eatOldBars(r.ctx);
  cases.stock_album = dump(r);
}
// 3. radio: three listens, the last on a used pack (seed 11)
{
  const r = rig(11, (o) => o.choices?.[0]?.id, () => ({ official: true, noted: [{ text: '라디오 안내 하나', kind: 'official' }] }));
  const G = r.G;
  G.items.radio = { home: 1, bag: 0, bought: 1, checked: false, used: 0 };
  G.items.batteries_aa = { home: 1, bag: 0, bought: 1, checked: false, used: 0 };
  check(G, 'radio');
  const a = await radioScene(r.ctx, 'normal');
  G.items.radio.charge = 4;
  const b = await radioScene(r.ctx, 'blackout');
  G.items.batteries_old = { home: 1, bag: 0, bought: 0, checked: false, used: 0 };
  G.chapter = 'evac';
  const c = await radioScene(r.ctx, 'evac');
  cases.radio = { heard: [a, b, c], ...dump(r) };
}
// 4. payoff toasts and the memory cue
{
  const r = rig(7, () => undefined);
  const G = r.G;
  G.day = -1; G.minute = 600; recordEvent(G, 'bag_packed', {});
  G.day = 0; G.minute = 1206; r.ctx.payoff('bag', '바로 나섰다'); r.ctx.payoff('judgement', '판단했다');
  r.ctx.stat('tension', 30); r.ctx.pass(45); r.ctx.note('메모 하나', 'rumor');
  cases.payoff = dump(r);
}
// 5. roam over the everyday points in town (ATM 10만 원, vending, bus stop, a description, an idle line), seed 7
{
  const r = rig(7, (o) => (o.speaker === 'ATM' ? '2' : o.choices?.[0]?.id));
  await r.ctx.goto('town', 'home');
  const done = r.ctx.roam({ on: { aptNotice: async () => 'end' } });
  for (const p of [{ id: 'atm1', kind: 'atm', label: 'ATM' }, { id: 'vending1', kind: 'vending', label: '자판기' }, { id: 'busStop', kind: 'busStop', label: '정류장' },
    { id: 'center', kind: 'place', label: '행정복지센터' }, { id: 'x', kind: 'npc', label: '누군가' }, { id: 'home', kind: 'door', label: '103동' }]) await r.press(p);
  await r.press({ id: 'aptNotice', kind: 'notice', label: 'b' });
  cases.town_common = { result: await done, level: 'apartment', ...dump(r) };
}
writeFileSync(out, JSON.stringify(cases, null, 1));
console.log('wrote', out, Object.keys(cases).join(', '));
