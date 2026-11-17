// What the ORIGINAL game/deeds.js answers for every run of make-golden-runs.mjs (saves: golden-runs.json),
// plus its tables and a scripted remember() sequence.   node make-golden-deeds.mjs [out.json]
import { buildRuns, lib, write } from './make-golden-runs.mjs';

const { createGame, recordEvent, save } = await lib('dist/game/state.js');
const { AXES, CH, DEEDS, NEW_DEEDS, NEIGHBOURS, remember, deeds, axes, promises, newDeeds, cueText } = await lib('dist/game/deeds.js');
const runs = await buildRuns();

const table = Object.entries(DEEDS).map(([id, d]) => ({ id, ...d, each: !!d.each, cueText: cueText(id) }));

const cases = runs.map(({ name, G }) => {
  const after = G.events.length ? G.events[Math.floor(G.events.length / 2)].seq : 0;
  return {
    name, deeds: deeds(G), axes: axes(G), axesFinal: axes(G, { final: true }),
    promises: promises(G), promisesFinal: promises(G, { final: true }),
    after, newDeeds: newDeeds(G, after),
  };
});

// remember(): once per id (violence-style 'each' ids aside), only new-choice ids, details kept, id wins.
const G = createGame({ seed: 21 });
const steps = [['driver_text'], ['driver_text'], ['nope'], ['hoard'], ['violence'], ['rumor_corrected', { after: 'check', id: 'x' }], ['mart_share', { note: '생수', n: 2 }],
  ['rumor_corrected', { after: 'spread' }], ['store_iou', {}], [null], ['signals_clear']];
const log = steps.map(([id, details]) => {
  try { const e = details === undefined ? remember(G, id) : remember(G, id, details); return e ? { seq: e.seq, type: e.type, details: e.details } : null; }
  catch { return 'throws'; }
});
recordEvent(G, 'deed', { id: 'promise_note' }); // a deed event written by hand counts like a remembered one
const rememberCase = { seed: 21, steps: steps.map(([id, details]) => ({ id, details: details ?? null, hasDetails: details !== undefined })), log, save: JSON.parse(save(G)), deeds: deeds(G) };

const seen = new Set(cases.flatMap((c) => c.deeds.map((d) => d.id)));
const unseen = Object.keys(DEEDS).filter((id) => !seen.has(id));
if (unseen.length) throw new Error(`deeds never reached by any run: ${unseen.join(', ')}`);
console.log(write('golden-deeds.json', { axes: AXES, ch: CH, neighbours: NEIGHBOURS, newDeeds: NEW_DEEDS, table, cases, remember: rememberCase }),
  `(${cases.length} runs, all ${table.length} deeds reached)`);
