// Golden for 4장/5장 (story/evac.js): runs the ORIGINAL crossroads / evacuation under node with a fake engine and
// scripted answers (by speaker), and writes the G.events each script produced (type, day, minute, details), the
// toasts, captions, talk speakers, objectives and the final path to Tests/EditMode/Fixtures/story-evac-golden.json.
// EvacGoldenTests replays the same scripts on Evac + FakeStoryHost and compares.
//   node make-golden-story-evac.mjs <path to safe-route/dist> <out json>
import { writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const dist = process.argv[2], out = process.argv[3];
const imp = (p) => import(pathToFileURL(path.join(dist, p)).href);
const { createRuntime } = await imp('story/runtime.js');
const { crossroads, evacuation } = await imp('story/evac.js');
const { createGame } = await imp('game/state.js');
const { rumor } = await imp('game/rumors.js');

const tick = () => new Promise(r => setTimeout(r, 0));
const isLabels = (o) => o.choices?.some(c => c.id === 'neighbor') && o.choices?.some(c => c.id === 'official');

// script: { labels, speakers: { name: [ids...] }, cards: [ids...], opens: { screen: [results...] }, presses: [ids...] }
async function play(seed, setup, chapters, script) {
  let G = createGame({ seed });
  setup(G);
  const toasts = [], captions = [], talks = [], objectives = [], frames = new Set(), inter = new Set(), built = {};
  const speakers = Object.fromEntries(Object.entries(script.speakers ?? {}).map(([k, v]) => [k, [...v]]));
  const cards = [...(script.cards ?? [])], opens = Object.fromEntries(Object.entries(script.opens ?? {}).map(([k, v]) => [k, [...v]]));
  const level = (id) => ({ id, points: [], npcs: {}, state: {}, setState(k, v) { this.state[k] = v; } });
  const engine = {
    level: null, controlsEnabled: true, player: { x: 0, y: 0, z: 0, yaw: 0, speedMul: 1, staminaUse: 1 },
    onNearest() {}, onFrame(f) { frames.add(f); return () => frames.delete(f); }, onInteract(f) { inter.add(f); return () => inter.delete(f); },
    setPower() {}, setEnv() {}, setControls(v) { engine.controlsEnabled = v; },
    async enter(id, spawn, { prepare }) { engine.level = built[id] ||= level(id); await prepare(engine.level); return engine.level; },
  };
  const hud = { prompt() {}, setClock() {}, setStats() {}, setBattery() {}, setBag() {}, setTension() {}, setChapter() {},
    setObjective: (title, body, steps) => objectives.push([title, body, (steps ?? []).map(s => [s.text, !!s.done])]),
    toast: (t) => toasts.push(t), caption: async (t) => { captions.push(t); } };
  const pickTalk = (o) => {
    if (!o.choices?.length) return undefined;
    if (isLabels(o)) return script.labels;
    const q = speakers[o.speaker];
    if (q?.length) return q.shift();
    return o.choices.find(c => !c.disabled)?.id;
  };
  const open = (screen) => async () => (opens[screen]?.length ? opens[screen].shift() : {});
  const ui = {
    talk: async (o) => { const id = pickTalk(o); talks.push([o.speaker, id ?? null]); return id; },
    card: async (o) => (o.actions?.length ? (cards.length ? cards.shift() : (o.actions.find(a => a.primary) ?? o.actions[0]).id) : undefined),
    timeSkip: async () => {}, chapterCard: open('chapterCard'), extinguisher: open('extinguisher'), radio: open('radio'),
    emergencyAlert: open('emergencyAlert'), bag: open('bag'),
  };
  const ctx = createRuntime({ engine, hud, ui, audio: { ambience() {}, radio() {}, phoneAlert() {} }, settings: {}, persist: { checkpoint() {} }, getG: () => G, setG: (g) => { G = g; } });
  let fail = null;
  const run = (async () => { for (const c of chapters) await { crossroads, evacuation }[c](ctx); })().catch(e => { fail = { type: e.type, detail: e.detail }; });
  for (const id of script.presses ?? []) {
    for (let i = 0; i < 50 && !(ctx.roaming && engine.controlsEnabled); i++) await tick();
    const pid = id === 'closure_ROAD' ? `closure_${rumor('evac_shortcut').road(G)}` : id;
    await Promise.all([...inter].map(f => f({ id: pid, kind: 'npc', label: pid })));
    for (let i = 0; i < 5; i++) await tick();
  }
  if (script.at1400) { for (let i = 0; i < 50 && !ctx.roaming; i++) await tick(); G.minute = 14 * 60; for (const f of [...frames]) f(1 / 30); }
  await run;
  return { fail, path: G.know.path ?? null, events: G.events.map(e => [e.type, e.day, e.minute, e.details]), toasts, captions, talks, objectives: objectives.slice(-1) };
}

const fresh = (G) => {
  G.chapter = 'endure'; G.day = 3; G.minute = 18 * 60;
  G.items.water_2l = { home: 6, bag: 0, bought: 0, checked: false, used: 0 };
  G.items.canned = { home: 4, bag: 0, bought: 0, checked: false, used: 0 };
  G.items.rice_instant = { home: 2, bag: 0, bought: 0, checked: false, used: 0 };
};
const kit = (G) => {
  fresh(G);
  G.chapter = 'crossroads'; G.day = 7; G.minute = 23 * 60;
  G.know.stayMove = 'stay'; G.know.bagPacked = true;
  for (const id of ['id_card', 'contact_card', 'doc_copies', 'water_500', 'bars', 'flashlight', 'meds']) G.items[id] = { home: 2, bag: 0, bought: 0, checked: false, used: 0 };
};
const FULL_BAG = { bag: { id_card: 1, contact_card: 1, doc_copies: 1, water_500: 2, bars: 2, flashlight: 1, meds: 1 }, pocket: { contact_card: 1 } };

const cases = {
  crossroads_stay: await play(7, fresh, ['crossroads'], { labels: 'rumor', speakers: { 정민: ['go'], 판단: ['handle', 'stay'], '윤 팀장': ['yes'], '박영수 소장': ['go'] }, cards: ['add', 'stay'] }),
  crossroads_move: await play(7, fresh, ['crossroads'], { labels: 'neighbor', speakers: { 정민: ['go'], '대피소 관리자': ['precise', 'help'] }, cards: ['add', 'move'],
    opens: { extinguisher: [{ sequence: ['shout', 'retreat'] }] } }),
  crossroads_fight: await play(7, fresh, ['crossroads'], { labels: 'rumor', speakers: { 정민: ['go'], 판단: ['fight'] }, cards: ['read', 'stay'] }),
  evac_bus: await play(7, kit, ['evacuation'], { labels: 'rumor', speakers: { 정민: ['go'], 라디오: ['no'], 현관: ['note'], 집결지: ['ask'] }, opens: { bag: [FULL_BAG] },
    presses: ['homeExitDoor', 'lobbyExit', 'assembly'] }),
  evac_seoyeon: await play(7, kit, ['evacuation'], { labels: 'rumor', speakers: { 정민: ['go'], 라디오: ['no'], 오서연: ['card', 'wait'] }, opens: { bag: [FULL_BAG] },
    presses: ['homeExitDoor', 'seoyeonDoor', 'seoyeonDoor'] }),
  evac_shortcut_lost: await play(7, kit, ['evacuation'], { labels: 'neighbor', speakers: { 정민: ['go'], 라디오: ['no'] }, opens: { bag: [FULL_BAG] },
    presses: ['homeExitDoor', 'lobbyExit', 'closure_ROAD'], at1400: true }),
};
writeFileSync(out, JSON.stringify(cases, null, 1));
console.log('wrote', out, Object.keys(cases).map(k => `${k}: ${cases[k].events.length} events${cases[k].fail ? ' fail ' + cases[k].fail.type : ''} path ${cases[k].path}`).join('; '));
