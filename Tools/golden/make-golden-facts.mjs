// Golden for Facts.cs, from the original game/facts.js: every fact and source with all its fields, and cite(id).
// It also checks a Unity data file (Data/Source/facts.json) against the original and, when fields are missing
// (the first export dropped sources[].checkedAt), writes a corrected copy.
//
//   node make-golden-facts.mjs [golden-facts.json] [facts.json to check] [corrected facts.json to write]
//
// SAFE_ROUTE_GAME overrides where the original dist/game/ modules are (an absolute file:/// URL ending in '/').
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const GAME = process.env.SAFE_ROUTE_GAME
  ?? 'file:///C:/Users/김태양/AppData/Local/Temp/claude/80eae06ac10fc83e/082ba9f6-2e32-43aa-b236-612ace9a2adc/scratchpad/aion-src/safe-route/dist/game/';
const { FACTS, SOURCES, fact, source, cite } = await import(new URL('facts.js', GAME).href);

const here = dirname(fileURLToPath(import.meta.url));
const out = resolve(process.argv[2] ?? resolve(here, '../../Tests/EditMode/Fixtures/golden-facts.json'));
const dataIn = resolve(process.argv[3] ?? 'C:/Users/김태양/Desktop/AiOnUnityProject/Assets/_AiOn/Data/Source/facts.json');
const dataOut = resolve(process.argv[4] ?? resolve(here, '../../Data/Source/facts.json'));

const golden = {
  facts: FACTS,
  sources: SOURCES,
  // cite(id) for every fact, in FACTS order, plus the lookups of an id that does not exist.
  cites: FACTS.map(f => ({ id: f.id, cite: cite(f.id) })),
  unknown: { fact: fact('nope'), source: source('nope'), cite: cite('nope') },
  topics: [...new Set(FACTS.map(f => f.topic))],
};
mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, JSON.stringify(golden, null, 1));
console.log(`golden: ${FACTS.length} facts, ${SOURCES.length} sources -> ${out}`);

// --- the Unity data file: same entries, same order, same fields as the original ---
const want = { facts: FACTS, sources: SOURCES };
const problems = [];
if (!existsSync(dataIn)) problems.push(`${dataIn} does not exist`);
else {
  const have = JSON.parse(readFileSync(dataIn, 'utf8'));
  for (const list of ['facts', 'sources']) {
    const h = have[list] ?? [], w = want[list];
    if (h.length !== w.length) problems.push(`${list}: ${h.length} entries, the original has ${w.length}`);
    w.forEach((entry, i) => {
      const got = h[i];
      if (!got || got.id !== entry.id) { problems.push(`${list}[${i}]: expected id ${entry.id}, found ${got?.id}`); return; }
      for (const k of Object.keys(entry)) {
        if (!(k in got)) problems.push(`${list}.${entry.id}: field '${k}' is missing`);
        else if (got[k] !== entry[k]) problems.push(`${list}.${entry.id}: field '${k}' differs`);
      }
      for (const k of Object.keys(got)) if (!(k in entry)) problems.push(`${list}.${entry.id}: extra field '${k}'`);
    });
  }
}
if (!problems.length) console.log(`data ok: ${dataIn} has every field of the original`);
else {
  const kinds = new Map();
  for (const p of problems) { const k = p.replace(/^[^:]*\.[^:]*: /, '').replace(/^\w+\[\d+\]: .*/, 'entry order'); kinds.set(k, (kinds.get(k) ?? 0) + 1); }
  console.log(`data differs (${problems.length} problems): ${[...kinds].map(([k, n]) => `${n} x ${k}`).join('; ')}`);
  for (const p of problems.slice(0, 5)) console.log('  ' + p);
  mkdirSync(dirname(dataOut), { recursive: true });
  writeFileSync(dataOut, JSON.stringify(want, null, 1)); // same layout as the first export
  console.log(`corrected data written -> ${dataOut}`);
}
