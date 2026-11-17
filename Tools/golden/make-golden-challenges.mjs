// What the ORIGINAL game/challenges.js answers for the runs of make-golden-runs.mjs (saves: golden-runs.json): run
// stats and earned challenges per run, a records board built run by run, finished runs (gallery + records),
// share texts, sanitised reads and time formatting.   node make-golden-challenges.mjs [out.json]
import { buildRuns, lib, write } from './make-golden-runs.mjs';

const { CHALLENGES, TIMED, PUBLIC_URL, runStats, runChallenges, readRecords, addRun, finishRun, shareText, fmtMs } = await lib('dist/game/challenges.js');
const { pickEnding } = await lib('dist/game/endings.js');
const runs = await buildRuns();
const byName = Object.fromEntries(runs.map((x) => [x.name, x.G]));
// How the C# test should hand a raw value over: as text (a stored string), as JSON (an object) or as nothing.
const rawOf = (raw) => ({ kind: raw === undefined ? 'undefined' : typeof raw === 'string' ? 'string' : 'json', raw: raw === undefined ? null : raw });

const cases = runs.map(({ name, G }) => ({ name, stats: runStats(G), done0: runChallenges(G, 0), done3: runChallenges(G, 3) }));

// ---------- sanitised reads ----------
const reads = [null, undefined, '', '{', '[]', 7, 'null', '"text"',
  { done: { clean: 'x', nope: '2026-11-02' }, best: { dday: -1, nope: 5 }, runs: [{ at: '2026-11-02', ending: 'nope', n: 1, ms: 5, retries: 0 }] },
  { done: { clean: '2026-11-02', share: '2026-11-02\n', elder: 20261102, fire: '2026-11-02' }, best: { dday: 1500, prep: 1.5, evac: 1e9, endure: 999999999, prologue: '5' },
    runs: [{ at: '2026-11-02', ending: 'stay', n: 2, ms: 5000, retries: 1, extra: 'dropped' }, { at: '2026-11-03', ending: 'bus', n: 5, ms: 9000, retries: 0 }, null, 'x', 7,
      { at: '2026-11-03', ending: 'bus', n: 14, ms: 9000, retries: 0 }, { at: '2026-11-03', ending: 'bus', n: 2, ms: 9000, retries: -1 }, { at: '2026-11-03', ending: 'bus', n: 2.5, ms: 9000, retries: 0 },
      { at: '2026-11-04', ending: 'walk', n: 2, ms: 4000, retries: 1 }, { at: '2026-11-05', ending: 'shut', n: 2, ms: 4000, retries: 0 }, { at: '2026-11-06', ending: 'board', n: 5, ms: 9000, retries: 0 },
      { at: '2026-11-07', ending: 'relief', n: 0, ms: 1, retries: 0 }, { at: '2026-11-08', ending: 'reopen', n: 13, ms: 999999999, retries: 9 }] },
  { done: [], best: 'x', runs: {} },
  JSON.stringify({ done: { precise: '2026-11-09' }, best: { evac: 777 }, runs: [{ at: '2026-11-09', ending: 'commons', n: 3, ms: 123456, retries: 2 }] }),
].map((raw) => ({ ...rawOf(raw), out: readRecords(raw) }));

// ---------- a records board built run by run (tests/challenges.test.mjs and more) ----------
let rec = null;
const board = [];
const steps = [['ch-slow', 'stay', 1, '2026-11-01'], ['ch-fast', 'stay', 1, '2026-11-02'], ['ch-fewer', 'bus', 1, '2026-11-02'],
  ['ch-retry2-fast', 'stay', 1, '2026-11-02'], ['ch-retry1-slow', 'stay', 1, '2026-11-02'],
  ['ch-board-0', 'stay', 1, '2026-11-03'], ['ch-board-1', 'stay', 1, '2026-11-03'], ['ch-board-2', 'stay', 1, '2026-11-03'], ['ch-board-3', 'stay', 1, '2026-11-03'],
  ['fix:stay', 'stay', 1, '2026-11-03'], ['ch-partial', 'bus', 2, '2026-11-04'], ['ch-network-timed', 'stay', 3, '2026-11-05'], ['ch-ask-share-elder', 'bus', 4, '2026-11-06'],
  ['ch-odd-times', 'stay', 4, '2026-11-07'], ['ch-fast', 'nope', 4, '2026-11-08'], ['ch-fast', 'stay', 4, 'tomorrow'], ['ch-bus-timed-retry', 'bus', 0, '2026-11-09'],
  ...runs.filter((x) => x.name.startsWith('rnd:')).map((x, i) => [x.name, pickEnding(x.G).id, i % 5, `2026-12-${String(1 + (i % 28)).padStart(2, '0')}`])];
steps.forEach(([name, ending, seen, at], i) => {
  const raw = i === 0 ? null : i % 2 ? JSON.stringify(rec) : rec; // stored text and live objects both go in
  const r = addRun(raw, byName[name], ending, seen, at);
  rec = r.rec;
  board.push({ run: name, ending, seen, at, asText: typeof raw === 'string', rec: structuredClone(r.rec), news: r.news });
});

// ---------- finished runs: the gallery and the records together ----------
let gal = null, recs = null;
const finished = [];
['ch-fast', 'ch-fast', 'fix:commons', 'ch-ask-share-elder', 'fix:bus', 'rnd:3', 'rnd:11', 'rnd:20', 'fix:shut', 'ch-network-timed'].forEach((name, i) => {
  const at = `2026-11-${String(6 + i).padStart(2, '0')}`;
  const r = finishRun(byName[name], i % 2 ? JSON.stringify(gal) : gal, i % 3 ? JSON.stringify(recs) : recs, at);
  finished.push({ run: name, at, galAsText: i % 2 === 1, recAsText: i % 3 !== 0, gal: structuredClone(r.gal), rec: structuredClone(r.rec), news: r.news });
  gal = r.gal; recs = r.rec;
});

// ---------- share texts ----------
const shares = ['ch-fast', 'ch-partial', 'fix:bus', 'ch-ask-share-elder', 'rnd:5', 'rnd:17'].map((name) => {
  const G = byName[name], { news } = addRun(null, G, pickEnding(G).id, 1, '2026-11-02');
  return { run: name, news, text: shareText(pickEnding(G), news, runStats(G)), noNews: shareText(pickEnding(G), null, runStats(G)) };
});

const times = [0, 1, 499, 500, 999, 1000, 59499, 59500, 60000, 65000, 599999, 3599499, 3599500, 3600000, 3725000, 86399999, 90061000, 999999999].map((ms) => ({ ms, text: fmtMs(ms) }));

const earned = new Set(cases.flatMap((c) => [...c.done0, ...c.done3]));
const never = CHALLENGES.map((c) => c.id).filter((id) => !earned.has(id));
if (never.length) throw new Error(`challenges no run earns: ${never.join(', ')}`);
console.log(write('golden-challenges.json', { challenges: CHALLENGES.map(({ id, title, hint }) => ({ id, title, hint })), timed: TIMED, publicUrl: PUBLIC_URL,
  cases, reads, board, finished, shares, times }), `(${cases.length} runs, ${board.length} board steps, all ${CHALLENGES.length} challenges earned)`);
