// What the ORIGINAL game/fates.js answers for the runs of make-golden-runs.mjs (saves: golden-runs.json): the
// neighbours' and the town's lines for endings other than the one each run reaches, the path, and the bus
// ending's letters.   node make-golden-fates.mjs [out.json]
import { readFileSync } from 'node:fs';
import { buildRuns, lib, write, SRC } from './make-golden-runs.mjs';

const { PATHS, LEAVE, TOWN, pathOf, letterOf, peopleLines, townLines } = await lib('dist/game/fates.js');
const { pickEnding } = await lib('dist/game/endings.js');
const { NEIGHBOURS, axes, deeds } = await lib('dist/game/deeds.js');
const runs = await buildRuns();

const WORLD = ['commons', 'reopen', 'relief', 'board'];
// The ending each run reaches is in golden-endings.json (fate.people, fate.town); here every run that holds more
// than one hand-built deed is also asked about endings it did not reach.
const cases = runs.filter((x) => !x.name.startsWith('raw:')).map(({ name, G }, i) => {
  const picked = pickEnding(G).id;
  const ids = [...new Set(['bus', WORLD[i % 4], i % 7 === 0 ? 'shut' : 'bus'])].filter((id) => id !== picked);
  return { name, path: pathOf(G), picked, lines: ids.map((id) => ({ id, people: peopleLines(G, id), town: townLines(G, id) })) };
});

const letters = [];
for (const { name, G } of runs.filter((x) => /^(bus-letters|fix:commons|rnd:(1|7|13|22|30)$)/.test(x.name))) {
  const a = axes(G, { final: true }), done = new Set(deeds(G).map((d) => d.id));
  letters.push({ name, letters: Object.fromEntries(Object.keys(NEIGHBOURS).map((who) => [who, letterOf(who, a, (id) => done.has(id))])) });
}

// Coverage: every Korean line written in fates.js should be said by some run.
const said = new Set(cases.flatMap((c) => c.lines.flatMap((l) => [...l.people.flatMap((p) => [p.line, p.letter]), ...l.town])));
const src = readFileSync(new URL('dist/game/fates.js', SRC), 'utf8');
const literals = [...src.matchAll(/'((?:[^'\\\n]|\\.)*[가-힣](?:[^'\\\n]|\\.)*)'/g)].map((m) => m[1].replace(/\\'/g, "'")).filter((s) => s.length > 12);
const unsaid = literals.filter((s) => !said.has(s));
if (unsaid.length) throw new Error(`fates.js lines no run says:\n${unsaid.join('\n')}`);
console.log(write('golden-fates.json', { paths: PATHS, leave: [...LEAVE], town: [...TOWN], cases, letters }), `(${cases.length} runs, all ${literals.length} lines said)`);
