// Golden for 3장 버티기 (story/endure.js with neighbors.js, escape.js, strain.js): runs the ORIGINAL chapter under node
// with the original runtime, a fake engine that loads the level json, and a scripted player (the same scripts as
// EndureTests). Writes events, items, know, rel, stats, info notes, talk speakers and fail of each script to
// Tests/EditMode/Fixtures/story-endure-golden.json; EndureTests replays them on Endure.Run + FakeStoryHost.
//   node --import <register.mjs that stubs 'three' and document> make-golden-story-endure.mjs <safe-route/dist> <level json dir> <out json>
// The 'three' stub is needed only because endure.js imports cutscene/level modules that import three at load time.
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const [dist, levelDir, out] = process.argv.slice(2);
const imp = (p) => import(pathToFileURL(path.join(dist, p)).href);
const { createRuntime, Fail } = await imp('story/runtime.js');
const { endure } = await imp('story/endure.js');
const { createGame } = await imp('game/state.js');

// setTimeout driven by frames (factCard toasts), so a run does not depend on wall-clock time.
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
  const rec = { toasts: [], talks: [], cards: [], opens: [], captions: [], checkpoints: [], timeSkips: [] };
  const engine = {
    level: null, controlsEnabled: true, crouching: false,
    player: { x: 0, y: 0, z: 0, speedMul: 1, staminaUse: 1, carrying: null },
    onNearest() {}, onFrame(f) { frames.add(f); return () => frames.delete(f); }, onInteract(f) { inter.add(f); return () => inter.delete(f); },
    setPower() {}, setEnv() {}, setControls(v) { engine.controlsEnabled = v; }, setView() {}, setGrade() {},
    async enter(id, spawn, { prepare }) {
      engine.level = built[id] ||= loadLevel(id);
      const s = typeof spawn === 'string' ? engine.level.spawns[spawn] ?? engine.level.anchors[spawn] : spawn;
      if (s) Object.assign(engine.player, { x: s.x, y: s.y ?? 0, z: s.z });
      await prepare(engine.level); return engine.level;
    },
    frame(dt = 1 / 30) {
      for (const t of [...timers]) { t.left -= dt; if (t.left <= 0) { timers.splice(timers.indexOf(t), 1); t.fn(); } }
      for (const f of [...frames]) f(dt);
    },
    press(id) { const p = engine.level.points.find((x) => x.id === id); if (!p) throw new Error(`no point ${id} in ${engine.level.id}`); return Promise.all([...inter].map((f) => f(p))); },
  };
  const quiet = new Proxy({}, { get: (t, k) => (k in t ? t[k] : () => {}) });
  const hud = new Proxy({
    toast: (text, o) => rec.toasts.push(text),
    caption: async (text) => { rec.captions.push(text); },
    setObjective() {},
  }, { get: (t, k) => (k in t ? t[k] : () => {}) });
  const answers = { talk: [], card: [], open: {} };
  const ui = {
    talk: async (o) => {
      rec.talks.push([o.speaker, o.role ?? '', o.lines[0] ?? '']);
      if (!o.choices?.length) return undefined;
      if (!answers.talk.length) throw new Error(`unscripted talk ${o.speaker}: ${o.choices.map((c) => c.id)}`);
      const id = answers.talk.shift(), c = o.choices.find((x) => x.id === id);
      if (!c || c.disabled) throw new Error(`answer ${id} not offered/enabled by ${o.speaker}`);
      return id;
    },
    card: async (o) => { rec.cards.push(o.title); if (!o.actions?.length) return undefined; if (!answers.card.length) throw new Error(`unscripted card ${o.title}`); return answers.card.shift(); },
    timeSkip: async (o) => { rec.timeSkips.push([o.from, o.to]); },
  };
  for (const screen of ['radio', 'extinguisher', 'chapterCard']) ui[screen] = async (o) => { rec.opens.push(screen); const q = answers.open[screen] || []; if (!q.length) throw new Error(`unscripted screen ${screen}`); return q.shift(); };
  const ctx = createRuntime({ engine, hud, ui, audio: quiet, settings: {}, persist: { checkpoint: (k) => rec.checkpoints.push(k) }, getG: () => G, setG: (g) => { G = g; } });
  return { ctx, engine, rec, answers, get G() { return G; } };
}

// Steps: 'press level:point' (E), 'door level:point' (walk through a door: here E, the runtime follows DOORS),
// 'y <n>' (stand at that height: the stairs), each run once a roam waits in that level with controls on.
async function play(r, steps) {
  let finished = false, error = null;
  const run = endure(r.ctx).then(() => { finished = true; }, (e) => { error = e; finished = true; });
  let idle = 0;
  while (!finished) {
    await settle();
    if (finished) break;
    const s = steps[0];
    if (s) {
      const [verb, target] = s.split(' ');
      const [lv, id] = target.includes(':') ? target.split(':') : [null, target];
      const ready = r.ctx.roaming && r.engine.controlsEnabled && (!lv || r.engine.level?.id === lv);
      if (ready) {
        steps.shift(); idle = 0;
        if (verb === 'y') { r.engine.player.y = Number(target); r.engine.frame(); }
        else if (verb === 'stairs1F') { Object.assign(r.engine.player, { x: 5, y: 0, z: 0.9 }); r.engine.frame(); }
        else { r.engine.press(id); await settle(); r.engine.frame(); } // a frame after each step, as walking onto a door gives one
        continue;
      }
    }
    if (++idle > 20000) throw new Error(`stuck at ${steps[0] ?? "(no steps)"} level=${r.engine.level?.id} roaming=${r.ctx.roaming} talks=${JSON.stringify(r.rec.talks.slice(-3))} events=${r.G.events.map(e=>e.type)} left=${r.answers.talk}`);
    r.engine.frame();
  }
  await run;
  return error;
}

const items = (G) => Object.fromEntries(Object.entries(G.items).filter(([, v]) => v.home || v.bag || v.used).map(([k, v]) => [k, [v.home, v.bag, v.used, !!v.checked]]));
const dump = (r, err) => ({
  fail: err ? (err instanceof Fail ? err.type : `error: ${err.message}`) : null,
  events: r.G.events.map((e) => [e.type, e.details]),
  items: items(r.G), know: r.G.know, rel: r.G.rel, stats: r.G.stats, day: r.G.day, minute: r.G.minute,
  info: r.G.info.map((i) => [i.text, i.label]), karma: r.G.karma ?? 0,
  talks: r.rec.talks, cards: r.rec.cards, opens: r.rec.opens, captions: r.rec.captions, checkpoints: r.rec.checkpoints, timeSkips: r.rec.timeSkips,
  phone: r.G.phone.battery,
});

// The scripts (mirrored in EndureTests.Scripts).
const SCRIPTS = {
  // A: the helper. 서연 trusts 정민, a jug and cash at home, 어르신 came down with 정민 on D-day.
  helper: {
    seed: 7,
    setup(G) { G.rel.seoyeon.trust = 1; G.items.water_jug = { home: 1, bag: 0, bought: 1, checked: false, used: 0 }; G.items.cash = { home: 1, bag: 0, bought: 1, checked: false, used: 0 }; G.know.elderPick = 'help'; G.know.extinguisherLocated = true; },
    talk: ['toss', 'toss', 'yes', 'add', 'ask', 'buy', 'rumor', 'check', 'tell', 'take', 'shut', 'info', 'thanks', 'visit'],
    card: ['ok', 'ok'],
    open: { extinguisher: [{ sequence: ['fight', 'shout', 'pin', 'aim', 'squeeze', 'sweep'] }], chapterCard: [{}] },
    steps: ['press apartment:homeFridge', 'press apartment:homeStock', 'press apartment:seoyeonDoor', 'press apartment:lobbyNotice', 'press apartment:homePlanner',
      'press apartment:homeExitDoor', 'door apartment:lobbyExit', 'press town:water', 'door town:home', 'y 5.6', 'press apartment:seoyeonDoor',
      'door apartment:lobbyExit', 'door town:store', 'press store:storeCounter', 'door store:storeExit', 'door town:home', 'press apartment:homeBed',
      'press apartment:extinguisher'],
  },
  // B: on my own. No jug, no cash, nobody to ask; eats from the fridge, listens on the phone FM, believes the rumour first.
  alone: {
    seed: 11,
    setup(G) { G.rel.seoyeon.trust = 0; },
    talk: ['eat', 'eat', 'phone', 'go', 'take', 'official', 'spread', 'fix', 'old', 'no', 'go', 'none', 'shout', 'water', 'retreat', 'ok', 'later'],
    card: ['ok'],
    open: { radio: [{ official: true, noted: [{ text: '급수소 09:30~17:00', kind: 'official' }] }], chapterCard: [{}] },
    steps: ['press apartment:homeFridge', 'press apartment:homeRadio', 'press apartment:seoyeonDoor', 'press apartment:homePlanner',
      'press apartment:homeExitDoor', 'door apartment:lobbyExit', 'press town:water', 'door town:home', 'y 5.6',
      'door apartment:lobbyExit', 'door town:store', 'press store:storeCounter', 'door store:storeExit', 'door town:home', 'press apartment:homeBed',
      'press apartment:hallFire', 'press apartment:hallFire', 'press apartment:hallFire', 'stairs1F apartment:stairs1F'],
  },
  // C: retry from the fire checkpoint; 박 소장 was told on D-day (she knocks); the drill says retreat; a wet towel first.
  fireRetreat: {
    seed: 7, resume: 'fire',
    setup(G) { G.know.elderPick = 'manager'; G.know.elderTold = true; G.chapter = 'endure'; },
    talk: ['ok', 'later'],
    card: [],
    open: { extinguisher: [{ sequence: ['retreat'] }], chapterCard: [{}] },
    steps: ['press apartment:extinguisher', 'press apartment:homeSink', 'stairs1F apartment:stairs1F'],
  },
  // D: retry from the fire; fighting it once it reached the ceiling ends in the fire fail.
  fireEscalated: {
    seed: 7, resume: 'fire',
    setup(G) { G.chapter = 'endure'; },
    talk: ['water'],
    card: [],
    open: { extinguisher: [{ sequence: ['fight'] }] },
    steps: ['press apartment:hallFire', 'press apartment:extinguisher'],
  },
};

const cases = {};
for (const [name, s] of Object.entries(SCRIPTS)) {
  const r = rig(s.seed);
  s.setup(r.G);
  if (s.resume) r.ctx.resume = s.resume;
  r.answers.talk.push(...s.talk); r.answers.card.push(...s.card);
  for (const [k, v] of Object.entries(s.open)) r.answers.open[k] = [...v];
  const err = await play(r, [...s.steps]);
  cases[name] = { ...dump(r, err), leftTalk: r.answers.talk.length };
}
writeFileSync(out, JSON.stringify(cases, null, 1));
console.log('wrote', out, Object.fromEntries(Object.entries(cases).map(([k, v]) => [k, [v.fail, v.events.length, v.leftTalk]])));
