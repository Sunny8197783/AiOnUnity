// What the ORIGINAL game/debrief.js answers for the runs of make-golden-runs.mjs (saves: golden-runs.json): every
// fail card, the chapter summaries and the ending's record.   node make-golden-debrief.mjs [out.json]
// The record's fate is the whole pickEnding() result; it is in golden-endings.json, so only its id is kept here.
import { readFileSync } from 'node:fs';
import { buildRuns, lib, write, SRC } from './make-golden-runs.mjs';

const { FAIL_TYPES, PEOPLE, PREP, failDebrief, chapterSummary, endingRecord } = await lib('dist/game/debrief.js');
const { CH } = await lib('dist/game/deeds.js');
const runs = await buildRuns();

// Every card the story can ask for: type x detail. An unknown why / scene / via falls back like the original.
const CARDS = [
  ...['fall', 'fight', 'blast', 'debris', 'fire', 'smoke', 'hurt', 'other'].map((why) => ['dead', { why }]), ['dead', {}], ['dead', undefined],
  ['late', undefined], ['late', {}], ['exhausted', undefined], ['exhausted', { why: 'carry' }], ['exhausted', { why: 'thirst' }],
  ['fire', undefined], ['fire', { scene: 'corridor' }], ['fire', { scene: 'upstairs' }], ['fire', { scene: 'shelter' }], ['fire', { scene: 'elsewhere' }],
  ['lostWay', undefined], ['lostWay', { via: 'rumor' }], ['lostWay', { via: 'map' }],
];
// The cards whose lines read the game state (events, know flags, stats, the bag); the others say the same for every run.
const SOME = CARDS.filter(([type, detail]) => detail === undefined || ['smoke', 'carry'].includes(detail.why));
const cards = (G, list) => list.map(([type, detail]) => ({ type, detail: detail ?? null, out: detail === undefined ? failDebrief(G, type) : failDebrief(G, type, detail) }));

const cases = runs.map(({ name, G, tags }) => {
  const c = { name, chapters: {} };
  for (const ch of new Set([...Object.keys(CH), 'c', 'nowhere', ...G.events.map((e) => e.chapter)])) {
    if (ch !== 'nowhere' && !G.events.some((e) => e.chapter === ch) && !G.info.some((i) => i.chapter === ch)) continue; // nothing happened there: same as 'nowhere'
    c.chapters[ch] = chapterSummary(G, ch);
  }
  if (tags.includes('fail')) c.fails = cards(G, CARDS);
  else if (tags.includes('somefails')) c.fails = cards(G, SOME);
  if (tags.includes('record') || !name.startsWith('raw:')) {
    const { fate, ...rec } = endingRecord(G);
    c.record = { fateId: fate.id, ...rec };
  }
  return c;
});

// Coverage: every Korean sentence written in debrief.js should reach some card, summary or record.
const said = JSON.stringify(cases);
const src = readFileSync(new URL('dist/game/debrief.js', SRC), 'utf8');
const quoted = [...src.matchAll(/'((?:[^'\\\n]|\\.)*)'|"((?:[^"\\\n]|\\.)*)"/g)].map((m) => (m[1] ?? m[2]).replace(/\\(['"])/g, '$1'));
const literals = quoted.filter((s) => /[가-힣]/.test(s) && s.length > 8);
const unsaid = literals.filter((s) => !said.includes(JSON.stringify(s).slice(1, -1)));
if (unsaid.length) throw new Error(`debrief.js lines no run says:\n${unsaid.join('\n')}`);

console.log(write('golden-debrief.json', { failTypes: FAIL_TYPES, people: PEOPLE, prep: PREP, cases }),
  `(${cases.length} runs, ${cases.filter((c) => c.fails).length} with fail cards, ${cases.filter((c) => c.record).length} records, all ${literals.length} lines said)`);
