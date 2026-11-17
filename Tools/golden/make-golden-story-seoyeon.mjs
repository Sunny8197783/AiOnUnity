// Golden for the 서연 relay interlude (story/seoyeon.js): runs the ORIGINAL scene under node with the original runtime, a
// fake engine that loads town-level.json, and scripted answers; ui/compose.js and notecheck.js are replaced (loader)
// by stand-ins that answer compose()/rereadCaption() from a script and record showNote/hideNote and the 3D calls.
// Writes Tests/EditMode/Fixtures/story-seoyeon-golden.json; SeoyeonRelayTests replays the scripts on SeoyeonRelay.Run.
//   node --import <register.mjs: stubs three, ui/compose.js, notecheck.js, document> make-golden-story-seoyeon.mjs <dist> <level dir> <out>
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const [dist, levelDir, out] = process.argv.slice(2);
const imp = (p) => import(pathToFileURL(path.join(dist, p)).href);
const { createRuntime } = await imp('story/runtime.js');
const { seoyeonRelay } = await imp('story/seoyeon.js');
const { createGame, recordEvent } = await imp('game/state.js');

const timers = [];
globalThis.setTimeout = (fn, ms = 0) => { timers.push({ left: ms / 1000, fn }); return timers.length; };
const settle = async () => { for (let i = 0; i < 20; i++) await new Promise((r) => setImmediate(r)); };

function loadLevel(id) {
  const f = path.join(levelDir, `${id}-level.json`);
  const j = existsSync(f) ? JSON.parse(readFileSync(f, 'utf8')) : { points: [], spawns: {} };
  return { id, points: (j.points || []).map((p) => ({ ...p })), spawns: j.spawns || {}, anchors: j.anchors || {}, npcs: {}, state: {}, setState(k, v) { this.state[k] = v; } };
}

function rig(seed) {
  let G = createGame({ seed });
  const frames = new Set(), inter = new Set(), built = {};
  const rec = { toasts: [], talks: [], cards: [], chapters: [], players: [], views: [], cinematic: [] };
  const engine = {
    level: null, controlsEnabled: true, crouching: false, player: { x: 0, y: 0, z: 0 },
    onNearest() {}, onFrame(f) { frames.add(f); return () => frames.delete(f); }, onInteract(f) { inter.add(f); return () => inter.delete(f); },
    setPower() {}, setEnv() {}, setControls(v) { engine.controlsEnabled = v; }, setView(v) { rec.views.push(v); },
    async enter(id, spawn, { prepare }) {
      engine.level = built[id] ||= loadLevel(id);
      const s = typeof spawn === 'string' ? engine.level.spawns[spawn] : spawn;
      if (s) Object.assign(engine.player, { x: s.x, y: s.y ?? 0, z: s.z });
      await prepare(engine.level); return engine.level;
    },
    frame(dt = 1 / 30) {
      for (const t of [...timers]) { t.left -= dt; if (t.left <= 0) { timers.splice(timers.indexOf(t), 1); t.fn(); } }
      for (const f of [...frames]) f(dt);
    },
    press(id) { const p = engine.level.points.find((x) => x.id === id); return Promise.all([...inter].map((f) => f(p))); },
  };
  const hud = new Proxy({
    toast: (text) => rec.toasts.push(text), caption: async () => {}, setObjective() {},
    setChapter: (a, b) => rec.chapters.push([a, b]), setCinematic: (v) => rec.cinematic.push(v),
  }, { get: (t, k) => (k in t ? t[k] : () => {}) });
  const answers = { talk: [], card: [] };
  const ui = {
    talk: async (o) => {
      rec.talks.push([o.speaker, o.role ?? '', o.lines]);
      if (!o.choices?.length) return undefined;
      const id = answers.talk.shift(); if (!o.choices.some((c) => c.id === id)) throw new Error(`answer ${id} not offered by ${o.speaker}`);
      return id;
    },
    card: async (o) => { rec.cards.push([o.title, o.meta, o.body]); if (!o.actions?.length) return undefined; const id = answers.card.shift(); if (!o.actions.some((a) => a.id === id)) throw new Error(`card answer ${id} not offered by ${o.title}`); return id; },
  };
  const ctx = createRuntime({ engine, hud, ui, audio: new Proxy({}, { get: () => () => {} }), settings: { view: 'third' }, persist: { checkpoint() {} }, getG: () => G, setG: (g) => { G = g; } });
  ctx.setPlayer = (who) => rec.players.push(who);
  return { ctx, engine, rec, answers, get G() { return G; } };
}

async function play(r, steps) {
  let finished = false, error = null;
  const run = seoyeonRelay(r.ctx).then(() => { finished = true; }, (e) => { error = e; finished = true; });
  let idle = 0;
  while (!finished) {
    await settle();
    if (finished) break;
    if (steps.length && r.ctx.roaming && r.engine.controlsEnabled) { const id = steps.shift(); idle = 0; r.engine.press(id); await settle(); r.engine.frame(); continue; }
    if (++idle > 20000) throw new Error(`stuck at ${steps[0] ?? '(no steps)'}`);
    r.engine.frame();
  }
  await run;
  if (error) throw error;
}

// The scripts (mirrored in SeoyeonRelayTests.Scripts). Every one starts at 20:12 in the shelter, 정민 having
// come in by the stairs (A) after seeing the east gate closed at 20:09.
const SCRIPTS = {
  // contact, chips (gate + time + route + follow me), every caption read, the notice -> C, then C.
  chips: {
    contact: true, battery: 40,
    compose: [{ chips: { what: 'gate_closed', time: true, route: true, unknowns: false, advice: 'follow_me' }, text: '', edited: false }],
    rereads: [], card: ['ok', 'access', 'ok'], talk: [], steps: ['shelterNotice', 'shelterC'],
  },
  // no contact: no message; 서연 tries A first and asks the guide to walk her to C.
  nocontact: {
    contact: false, battery: 40, compose: [], rereads: [], card: ['ok', 'ok', 'ok', 'ok'], talk: ['escort'], steps: ['shelterEast', 'shelterA', 'shelterC'],
  },
  // a dying phone saved by the powerbank, a free-text message, Esc on the second caption, B then C.
  edited: {
    contact: true, battery: 2, powerbank: true,
    compose: [{ chips: { what: 'all_closed', time: false, route: false, unknowns: false, advice: null }, text: '다 막혔어. 나 따라 계단으로 와', edited: true }],
    rereads: ['next', 'skip'], card: ['ok', 'stairs', 'ok'], talk: ['self'], steps: ['shelterNotice', 'shelterB', 'shelterC'],
  },
  // the phone is dead and there is no powerbank: no message either.
  deadphone: {
    contact: true, battery: 0, compose: [], rereads: [], card: ['ok', 'ok', 'ok', 'ok'], talk: [], steps: ['shelterC'],
  },
};

const cases = {};
for (const [name, s] of Object.entries(SCRIPTS)) {
  const r = rig(7);
  const G = r.G;
  G.chapter = 'dday'; G.day = 0; G.minute = 20 * 60 + 9;
  recordEvent(G, 'gate_observed', { who: 'jeongmin', minute: 20 * 60 + 9, via: 'sign' });
  recordEvent(G, 'route_attempt', { who: 'jeongmin', route: 'stairs', minute: 20 * 60 + 11 });
  G.minute = 20 * 60 + 12;
  if (s.contact) G.rel.seoyeon.contact = 'phone';
  G.phone.battery = s.battery;
  if (s.powerbank) G.items.powerbank = { home: 0, bag: 1, bought: 1, checked: true, used: 0 };
  globalThis.__relay = { compose: [...s.compose], captions: [], answers: [...s.rereads], notes: [], fx: [] };
  r.answers.talk.push(...s.talk); r.answers.card.push(...s.card);
  await play(r, [...s.steps]);
  const R = globalThis.__relay;
  cases[name] = {
    events: G.events.map((e) => [e.type, e.details]), relay: G.relay, handover: G.handover, minute: G.minute, battery: G.phone.battery,
    powerbank: G.items.powerbank ?? null, info: G.info.map((i) => [i.text, i.label]),
    toasts: r.rec.toasts, talks: r.rec.talks, cards: r.rec.cards, chapters: r.rec.chapters, players: r.rec.players, views: r.rec.views, cinematic: r.rec.cinematic,
    composeOpened: R.opened ?? null, captions: R.captions, notes: R.notes, fx: R.fx, calm: r.ctx.calm, speedOverride: r.ctx.speedOverride ?? null,
    leftTalk: r.answers.talk.length, leftCard: r.answers.card.length,
  };
}
writeFileSync(out, JSON.stringify(cases, null, 1));
console.log('wrote', out, Object.fromEntries(Object.entries(cases).map(([k, v]) => [k, [v.events.length, v.leftTalk, v.leftCard]])));
