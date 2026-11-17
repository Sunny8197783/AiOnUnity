// Golden for the prologue and chapter 1 (prep): runs the ORIGINAL story/prologue.js and story/prep.js (with
// prep-actions.js, common.js, runtime.js) under node on a fake engine built from the level json, and plays the
// scripted playthroughs below. Each case stores its own script (presses, talk rules, screen answers) and what the
// original did (events, toasts, talks, items, stats, notes, level states), so PrepGoldenTests replays the same
// script on the C# port with FakeStoryHost and compares.
//   node make-golden-story-prep.mjs <path to safe-route/dist> <path to Data/Source> <out json>
// prologue.js and prep.js import three.js through engine/kit.js and cutscenes.js; they are copied next to this
// script's temp folder with those two imports replaced (FONT is a string; townTour is never reached: toured is set).
import { writeFileSync, readFileSync, mkdtempSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import os from 'node:os';

const dist = path.resolve(process.argv[2]), data = path.resolve(process.argv[3]), out = process.argv[4];
const url = (p) => pathToFileURL(path.join(dist, p)).href;
const imp = (p) => import(url(p));

// ---- the two chapter modules, with their three.js imports cut
const tmp = mkdtempSync(path.join(os.tmpdir(), 'golden-prep-'));
function port(file, extra) {
  let src = readFileSync(path.join(dist, 'story', file), 'utf8');
  src = src.replace(/from '\.\.\/([^']+)'/g, (_, p) => `from '${url(p)}'`).replace(/from '\.\/([^']+)'/g, (_, p) => `from '${url('story/' + p)}'`);
  for (const [a, b] of extra) { if (!src.includes(a)) throw new Error(`${file}: '${a}' not found`); src = src.replace(a, b); }
  const p = path.join(tmp, file);
  writeFileSync(p, src);
  return import(pathToFileURL(p).href);
}
const { prologue } = await port('prologue.js', [[`import { FONT } from '${url('engine/kit.js')}';`, `const FONT = 'sans-serif';`]]);
const { prep } = await port('prep.js', [[`import { townTour } from '${url('story/cutscenes.js')}';`, `const townTour = async () => { throw new Error('no cine in the golden'); };`]]);
const { createRuntime } = await imp('story/runtime.js');
const { createGame } = await imp('game/state.js');

// dashboardCanvas draws on a canvas: a do-nothing 2D context is enough.
const ctx2d = new Proxy({}, { get: () => () => {}, set: () => true });
globalThis.document = { createElement: () => ({ width: 0, height: 0, getContext: () => ctx2d }) };

const levelJson = {};
function level(id) {
  levelJson[id] ??= (() => { try { return JSON.parse(readFileSync(path.join(data, `${id}-level.json`), 'utf8')); } catch { return { points: [] }; } })();
  const j = levelJson[id];
  return {
    id, points: (j.points || []).map(p => ({ ...p, enabled: p.enabled ?? true })), spawns: j.spawns || {}, anchors: j.anchors || {}, npcs: {}, states: j.states || {},
    state: {}, setState(k, v) { this.state[k] = v instanceof Object && v.constructor?.name === 'Object' ? JSON.parse(JSON.stringify(v)) : (v && v.getContext ? '(canvas)' : v); },
  };
}

// A case: { seed, chapter, setup, steps: ['level:point' | 'point' | 'frame'], talk: [{ speaker, offers?, answer }], open: { screen: [result...] } }
async function play(c) {
  let G = createGame({ seed: c.seed });
  if (c.setup?.toured) G.know.toured = true;
  if (c.setup?.soldOutEarly) G.cond.soldOut.early = c.setup.soldOutEarly;
  if (c.setup?.soldOutLate) G.cond.soldOut.late = c.setup.soldOutLate;
  if (c.setup?.clock) { G.day = c.setup.clock[0]; G.minute = c.setup.clock[1]; }
  const frames = new Set(), inter = new Set(), built = {};
  const engine = {
    level: null, controlsEnabled: true, player: { x: 0, y: 0, z: 0, yaw: 0, speedMul: 1, staminaUse: 1 },
    onNearest() {}, onFrame(f) { frames.add(f); return () => frames.delete(f); }, onInteract(f) { inter.add(f); return () => inter.delete(f); },
    setPower() {}, setEnv() {}, setControls(v) { engine.controlsEnabled = v; },
    async enter(id, spawn, { prepare }) {
      engine.level = built[id] ||= level(id);
      const s = engine.level.spawns[spawn] || engine.level.anchors[spawn];
      if (s) Object.assign(engine.player, { x: s.x, y: s.y ?? 0, z: s.z, yaw: s.yaw ?? 0 });
      await prepare(engine.level);
      return engine.level;
    },
  };
  const toasts = [], talks = [], cards = [], opens = [], skips = [], objectives = [];
  const hud = {
    prompt() {}, setClock() {}, setStats() {}, setBattery() {}, setBag() {}, setTension() {}, setChapter() {},
    setObjective: (t) => objectives.push(t), toast: (t, o) => toasts.push([t, o.label, o.ms]), caption: async () => {},
  };
  const rules = (c.talk || []).map(r => ({ ...r }));
  const queues = Object.fromEntries(Object.entries(c.open || {}).map(([k, v]) => [k, [...v]]));
  const screen = (name) => async (o) => { opens.push([name, JSON.parse(JSON.stringify(o))]); const q = queues[name]; return q && q.length ? q.shift() : {}; };
  const ui = {
    talk: async (o) => {
      talks.push({ speaker: o.speaker, role: o.role, lines: o.lines, choices: o.choices?.map(x => x.id) ?? null });
      if (!o.choices?.length) return undefined;
      const i = rules.findIndex(r => r.speaker === o.speaker && (!r.offers || o.choices.some(x => x.id === r.offers)));
      if (i >= 0) { const r = rules.splice(i, 1)[0]; return r.answer; }
      return o.choices.find(x => !x.disabled)?.id;
    },
    card: async (o) => { cards.push(o.title); return (o.actions?.find(a => a.primary) ?? o.actions?.[0])?.id; },
    timeSkip: async (o) => { skips.push([o.from, o.to, o.text]); },
    planner: screen('planner'), shop: screen('shop'), radio: screen('radio'), extinguisher: screen('extinguisher'), docs: screen('docs'), routemap: screen('routemap'), bag: screen('bag'),
  };
  const ctx = createRuntime({ engine, hud, ui, audio: { ambience() {}, radio() {} }, settings: {}, persist: { checkpoint() {} }, getG: () => G, setG: (g) => { G = g; } });
  const steps = [...c.steps];
  let finished = false, err = null;
  (c.chapter === 'prologue' ? prologue(ctx) : prep(ctx)).then(() => { finished = true; }, (e) => { err = e; finished = true; });
  const tick = () => new Promise(r => setTimeout(r, 0));
  const frame = () => { for (const f of [...frames]) f(1 / 30); };
  let idle = 0;
  while (!finished) {
    await tick();
    if (finished) break;
    const s = steps[0];
    const [lv, id] = s && s !== 'frame' && s.includes(':') ? s.split(':') : [null, s];
    if (s && ctx.roaming && engine.controlsEnabled && (!lv || engine.level?.id === lv)) {
      steps.shift(); idle = 0;
      if (s === 'frame') frame();
      else {
        const p = engine.level.points.find(x => x.id === id);
        if (!p) throw new Error(`${c.name}: no point ${id} in ${engine.level.id}`);
        for (const f of [...inter]) f(p);
      }
    } else { frame(); if (++idle > 20000) throw new Error(`${c.name}: stuck at ${s} in ${engine.level?.id}`); }
  }
  if (err) throw err;
  if (steps.length) throw new Error(`${c.name}: steps left: ${steps.join(', ')}`);
  const lvStates = Object.fromEntries(Object.entries(built).map(([k, L]) => [k, JSON.parse(JSON.stringify(L.state))]));
  return {
    script: c,
    events: G.events.map(e => [e.type, e.details]),
    toasts, talks, cards, skips, objectives,
    opens: opens.map(([n, o]) => [n, n === 'planner' ? { day: o.day, slots: o.slots, hint: o.hint, actions: o.actions.map(a => [a.id, a.done, a.disabled, a.reason]) } : n]),
    clock: [G.day, G.minute], chapter: G.chapter, stats: G.stats, money: G.money, battery: G.phone.battery, slots: G.slots,
    items: Object.fromEntries(Object.entries(G.items).map(([k, v]) => [k, [v.home, v.bag, v.used, v.checked, v.charge ?? null, v.pocket ?? null, v.bought]])),
    know: G.know, rel: G.rel, skill: G.skill,
    info: G.info.map(i => [i.text, i.label, i.place]),
    levels: lvStates,
  };
}

const cases = [
  // Prologue: every signal right, the driver texted, only what is known; out-of-order presses first.
  { name: 'prologue_right', seed: 7, chapter: 'prologue',
    talk: [{ speaker: '운영 대시보드', answer: 'fact' }, { speaker: '협력사 공지', answer: 'fact' }, { speaker: '옆자리 동료', answer: 'guess' }, { speaker: '동네 단톡방', answer: 'rumor' },
      { speaker: '거래처 담당자', answer: 'rumor' }, { speaker: '창밖', answer: 'text' }, { speaker: '윤 팀장', offers: 'ok', answer: 'ok' }],
    steps: ['office:coworker', 'window', 'officeExit', 'desk', 'desk', 'officeExit', 'coworker', 'coworker', 'officeExit'] },
  // Prologue: everything called a fact (two right), the war question, just looking out of the window.
  { name: 'prologue_facts', seed: 9, chapter: 'prologue', talk: [{ speaker: '창밖', answer: 'look' }],
    steps: ['desk', 'coworker', 'window', 'officeExit'] },
  // Prep A: equip tonight; mart, shelter check, neighbours (through the lobby, the town and the store), bag tomorrow; water and the album.
  { name: 'prep_a', seed: 7, chapter: 'prep', setup: { toured: true, soldOutEarly: [], soldOutLate: ['water'], clock: [-2, 1110] },
    talk: [{ speaker: '휴대폰', offers: 'yes', answer: 'yes' }],
    open: {
      planner: ['equip', 'mart', 'shelter', 'neighbors', 'bag'],
      shop: [{ cart: { batteries_aa: 2, radio: 1 }, total: 33000 }, { cart: { water_2l: 4, bars: 3 }, total: 8900 }],
      routemap: [{ choice: 1 }],
      bag: [{ bag: { water_2l: 1, bars: 3, flashlight: 1, radio: 1, meds: 1, clothes: 1 }, pocket: { meds: 1, bars: 2 } }],
    },
    steps: ['homeStock', 'homeGas', 'homeBreaker', 'homePlanner',
      'homeFridge', 'homeDesk', 'homeWindow', 'homePlanner', 'homeExitDoor', 'town:mart', 'mart:shelfElec', 'mart:shelfWater', 'mart:checkout',
      'homePlanner', 'homeExitDoor', 'town:shelterEast', 'town:shelterA', 'town:shelterB', 'town:shelterNotice',
      'homePlanner', 'homeExitDoor', 'seoyeonDoor', 'elevator', 'managerWindow', 'apartment:lobbyExit', 'town:store', 'store:storeCounter', 'store:storeExit', 'town:home', 'apartment:elevator', 'homeDoor',
      'homePlanner', 'homeBag',
      'homeStock', 'homeSink', 'homeBed'] },
  // Prep B: documents tonight; the planner closed once, home readiness, rest, 서연 only (her spare batteries), rest again, an empty bag.
  { name: 'prep_b', seed: 7, chapter: 'prep', setup: { toured: true, clock: [-2, 1110] },
    talk: [{ speaker: '오서연', offers: 'both', answer: 'both' }, { speaker: '오서연', offers: 'yes', answer: 'yes' }, { speaker: '침대', answer: 'sleep' }],
    open: {
      planner: ['docs', null, 'home', 'rest', 'neighbors', 'rest', 'bag'],
      docs: [{ card: { family: '부모님', meet: '대피소', idcopy: true }, where: 'bag' }],
      bag: [{ bag: {}, pocket: {} }],
    },
    steps: ['homeDesk',
      'homePlanner', 'homePlanner', 'homeWindow', 'homeStock', 'homeSink',
      'homePlanner',
      'homePlanner', 'homeExitDoor', 'seoyeonDoor', 'apartment:homeDoor', 'homePlanner',
      'homePlanner',
      'homePlanner', 'homeBag',
      'homeSink', 'homeBed'] },
];

const res = {};
for (const c of cases) res[c.name] = await play(c);
writeFileSync(out, JSON.stringify(res, null, 1));
console.log('wrote', out, Object.keys(res).join(', '));
