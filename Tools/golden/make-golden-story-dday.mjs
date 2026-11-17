// Golden for 2장 (story/dday.js): runs the ORIGINAL chapter under node on a fake engine (levels' points and spawns from
// the level json) with the same player scripts as DDayPlaythroughTests, and writes what it recorded (events with their
// details, payoffs, deeds, a few know flags, item counts) to Tests/EditMode/Fixtures/story-dday-golden.json.
// seoyeon.js, elder.js and cutscenes.js (other agents' modules) are swapped for the same stubs the C# harness uses
// (module.registerHooks), so the chapter's own logic is what is compared.
//   node make-golden-story-dday.mjs <path to safe-route/dist> <path to Data/Source> <out json>
import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { registerHooks } from 'node:module';
import path from 'node:path';

const [dist, dataDir, out] = process.argv.slice(2);
const url = (p) => pathToFileURL(path.join(dist, p)).href;
const STUBS = {
  'story/seoyeon.js': `import { relayTimes } from '${url('game/relay.js')}'; import { recordEvent } from '${url('game/state.js')}';
    export async function seoyeonRelay(ctx) { const G = ctx.G; const gate = G.events.find(e => e.type === 'gate_observed' && e.details.who === 'jeongmin');
      G.relay = relayTimes(G.minute + 2, gate ? gate.details.minute : G.minute - 3); G.handover = null; recordEvent(G, 'stub_seoyeon_relay', {}); }`,
  'story/elder.js': `export function createElderDilemma(ctx) { return { following: false, tick() {}, async managerLine() { return false; } }; }`,
  'story/cutscenes.js': `export async function strikeScene(ctx, onImpact) { onImpact(); return 'done'; }
    export async function blackoutScene(ctx, cut) { cut(); return 'done'; } export async function sirenScene(ctx) { return 'done'; }`,
};
registerHooks({
  load(u, context, next) {
    for (const [k, src] of Object.entries(STUBS)) if (u === url(k)) return { format: 'module', source: src, shortCircuit: true };
    return next(u, context);
  },
});
globalThis.document ??= { getElementById: () => null, createElement: () => ({}) };
const { createRuntime } = await import(url('story/runtime.js'));
const { dday } = await import(url('story/dday.js'));
const { createGame, pack } = await import(url('game/state.js'));
Math.random = (() => { let s = 5; return () => ((s = (s * 16807) % 2147483647) / 2147483647); })(); // far blasts only

function levelOf(id) {
  let j = {};
  try { j = JSON.parse(readFileSync(path.join(dataDir, `${id}-level.json`), 'utf8')); } catch { /* no json: empty level */ }
  return { id, points: (j.points || []).map(p => ({ ...p })), spawns: j.spawns || {}, npcs: {}, state: {}, setState(k, v) { this.state[k] = v; }, defaults: {} };
}

function rig(setup) {
  let G = createGame({ seed: 7 });
  G.chapter = 'prep'; G.day = -1;
  setup?.(G);
  const frames = new Set(), inter = new Set(), built = {}, opens = [], talks = [], toasts = [];
  const engine = {
    level: null, controlsEnabled: true, crouching: false,
    player: { x: 0, y: 0, z: 0, yaw: 0, speedMul: 1, staminaUse: 1 },
    onNearest() {}, onFrame(f) { frames.add(f); return () => frames.delete(f); }, onInteract(f) { inter.add(f); return () => inter.delete(f); },
    setPower() {}, setEnv() {}, setControls(v) { engine.controlsEnabled = v; }, shake() {}, setFlashlight() {},
    async enter(id, spawn, { prepare }) {
      engine.level = built[id] ||= levelOf(id);
      const s = engine.level.spawns[spawn]; if (s) Object.assign(engine.player, { x: s.x, y: s.y, z: s.z, yaw: s.yaw });
      await prepare(engine.level); return engine.level;
    },
  };
  const answers = { byKey: {}, open: {} };
  const pick = (o) => {
    const q = answers.byKey[o.speaker];
    if (o.choices?.length && q?.length) return q.shift();
    return o.choices?.find(c => !c.disabled)?.id ?? null;
  };
  const hud = { prompt() {}, setClock() {}, setStats() {}, setBattery() {}, setBag() {}, setTension() {}, setObjective() {}, setChapter() {}, hurt() {},
    toast: (t) => toasts.push(t), caption: async () => {} };
  const openRes = (screen, o) => { opens.push(screen); return answers.open[screen]?.shift() ?? {}; };
  const ui = {
    talk: async (o) => { talks.push({ speaker: o.speaker, lines: o.lines, choices: o.choices?.map(c => c.id) ?? null }); return pick(o); },
    card: async (o) => (o.actions?.find(a => a.primary) ?? o.actions?.[0])?.id ?? null,
    phone: async (o) => openRes('phone', o), bag: async (o) => openRes('bag', o), radio: async (o) => openRes('radio', o),
    emergencyAlert: async (o) => openRes('emergencyAlert', o), chapterCard: async (o) => openRes('chapterCard', o),
  };
  const audio = { ambience() {}, siren() {}, boom() {}, hit() {}, phoneAlert() {}, radio() {} };
  const ctx = createRuntime({ engine, hud, ui, audio, settings: {}, persist: { checkpoint() {} }, getG: () => G, setG: (g) => { G = g; } });
  const flush = async () => { for (let i = 0; i < 20; i++) await new Promise(r => setImmediate(r)); };
  const frame = async () => { for (const f of [...frames]) f(1 / 30); await flush(); };
  const until = async (cond, max = 40000) => { for (let i = 0; i < max && !cond(); i++) await frame(); if (!cond()) throw new Error('stuck'); };
  const press = async (id) => { const p = engine.level.points.find(q => q.id === id); if (!p) throw new Error(`no point ${id} in ${engine.level.id}`); await Promise.all([...inter].map(f => f(p))); await flush(); };
  const ready = (lv) => ctx.roaming && engine.controlsEnabled && engine.level?.id === lv;
  return { ctx, engine, answers, opens, talks, toasts, frame, until, press, ready, get G() { return G; } };
}

const know = ['bagOn', 'closureHeard', 'closureSeen', 'gateClosedSeen', 'strikeSeen', 'candleLit', 'candleOut'];
async function play(r, script) {
  let done = false, err = null;
  const run = dday(r.ctx).then(() => { done = true; }, (e) => { done = true; err = e; });
  await script(r);
  await r.until(() => done);
  await run;
  const G = r.G;
  return {
    fail: err ? (err.type ?? String(err)) : null,
    events: G.events.map(e => [e.type, e.details]),
    payoffs: G.events.filter(e => e.type === 'payoff').map(e => e.details.prep),
    deeds: G.events.filter(e => e.type === 'deed').map(e => e.details.id),
    know: Object.fromEntries(know.map(k => [k, G.know[k] ?? null])),
    items: Object.fromEntries(Object.entries(G.items).map(([k, v]) => [k, [v.home, v.bag, v.used]])),
    minute: G.minute, trust: G.rel.seoyeon.trust, opens: r.opens,
  };
}
const alertRoam = (r) => r.until(() => r.ctx.roaming && r.G.events.some(e => e.type === 'alert'));
const downstairs = async (r) => { await r.until(() => r.ready('apartment') && r.engine.player.y > 4); Object.assign(r.engine.player, { x: 5, y: 0, z: 0.9 }); await r.frame(); };

const cases = {};
cases.prepared = await play(rig((G) => {
  G.rel.seoyeon.contact = true; G.rel.manager.contact = true;
  G.items.flashlight.checked = true;
  G.items.radio = { home: 1, bag: 0, bought: 0, checked: true, used: 0, charge: 100 };
  Object.assign(G.know, { radioChecked: true, routeChecked: true, idTaken: true, bagPacked: true });
  for (const [id, home, bag] of [['water_500', 3, 2], ['contact_card', 1, 1], ['blanket', 1, 1]]) { G.items[id] = { home, bag: 0, bought: 0, checked: false, used: 0 }; pack(G, id, bag); }
}), async (r) => {
  r.answers.byKey['정전'] = ['hall', 'radio']; r.answers.byKey['3층 복도'] = ['tip'];
  r.answers.open.radio = [{ official: true, noted: [] }];
  r.answers.byKey['접수대'] = ['ask']; r.answers.byKey['옆자리'] = ['give']; r.answers.byKey['오서연'] = ['ask'];
  await alertRoam(r); await r.press('homeExitDoor');
  await downstairs(r);
  await r.until(() => r.ready('apartment')); await r.press('managerWindow');
  await r.until(() => r.ready('apartment')); await r.press('lobbyExit');
  await r.until(() => r.ready('town') && r.G.know.strikeSeen); await r.press('shelterA');
  await r.until(() => r.ready('shelter')); await r.press('reception');
  await r.until(() => r.ready('shelter')); await r.press('spot');
});

cases.unprepared = await play(rig((G) => {
  G.items.candles = { home: 1, bag: 0, bought: 0, checked: false, used: 0 };
  G.items.water_500 = { home: 1, bag: 0, bought: 0, checked: false, used: 0 };
  delete G.items.radio;
}), async (r) => {
  r.answers.byKey['정전'] = ['candle', 'phone'];
  r.answers.byKey['현관'] = ['pack'];
  r.answers.open.bag = [{ bag: { water_500: 1, water_2l: 1, flashlight: 1 }, pocket: {} }];
  r.answers.byKey['거실'] = ['leave']; r.answers.byKey['접수대'] = ['go']; r.answers.byKey['옆자리'] = ['keep']; r.answers.byKey['오서연'] = ['go'];
  await alertRoam(r);
  for (const id of ['homeBag', 'homeExitDoor']) { await r.until(() => r.ready('apartment')); await r.press(id); }
  await downstairs(r);
  for (const id of ['elevator', 'managerWindow', 'lobbyExit']) { await r.until(() => r.ready('apartment')); await r.press(id); }
  for (const id of ['closure_yeonmuCenter', 'aptNotice', 'shelterEast', 'shelterC']) { await r.until(() => r.ready('town')); await r.press(id); }
  await r.until(() => r.ready('town') && r.G.know.strikeSeen); await r.press('shelterB');
  for (const id of ['spot', 'reception', 'spot']) { await r.until(() => r.ready('shelter')); await r.press(id); }
});

writeFileSync(out, JSON.stringify(cases, null, 1));
console.log(Object.entries(cases).map(([k, v]) => `${k}: ${v.events.length} events, fail ${v.fail}`).join('\n'));
