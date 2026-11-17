// Golden for Relay.cs, from the original game/relay.js: the times, every chip note and how it parses back, free
// texts read by rule (word boundaries, digits and white space as JavaScript sees them), and makeHandover with the
// pins and claim rows that follow from it.
//
//   node make-golden-relay.mjs [golden-relay.json]
//
// SAFE_ROUTE_GAME overrides where the original dist/game/ modules are (an absolute file:/// URL ending in '/').
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const GAME = process.env.SAFE_ROUTE_GAME
  ?? 'file:///C:/Users/김태양/AppData/Local/Temp/claude/80eae06ac10fc83e/082ba9f6-2e32-43aa-b236-612ace9a2adc/scratchpad/aion-src/safe-route/dist/game/';
const mod = (name) => import(new URL(name, GAME).href);
const { hhmm, ENTRANCES, MAX_NOTE, relayTimes, noteContext, composeNote, parseNote, noteKind, makeHandover, buildPins, closingLines, readClaims } = await mod('relay.js');
const st = await mod('state.js');

const here = dirname(fileURLToPath(import.meta.url));
const out = resolve(process.argv[2] ?? resolve(here, '../../Tests/EditMode/Fixtures/golden-relay.json'));

const GATE = 20 * 60 + 9;
const save = (G) => JSON.parse(st.save(G));

// 정민 sees the east gate, enters A or B; the D-day chapter then sets G.relay (story/seoyeon.js).
function played(route = 'stairs', via = 'sign', { gate = GATE, sent = GATE + 4, seed = 1 } = {}) {
  const G = st.createGame({ seed });
  st.recordEvent(G, 'gate_observed', { who: 'jeongmin', via, minute: gate });
  st.recordEvent(G, 'route_attempt', { who: 'jeongmin', route, minute: gate + 2 });
  G.relay = relayTimes(sent, gate);
  return G;
}

// Every combination of chips the compose screen allows (ui/compose.js always sends all five keys).
function* allChips() {
  for (const what of ['gate_closed', 'all_closed'])
    for (const time of [false, true]) for (const route of [false, true]) for (const unknowns of [false, true])
      for (const advice of [null, 'follow_me', 'direction', 'check_notice']) yield { what, time, route, unknowns, advice };
}

// ---------- times ----------
const times = [];
for (let sent = 20 * 60; sent <= 21 * 60 + 30; sent += 7) {
  times.push({ sent, gate: null, times: relayTimes(sent) });
  for (let gate = sent - 20; gate <= sent + 5; gate++) times.push({ sent, gate, times: relayTimes(sent, gate) });
}
times.push({ sent: 23 * 60 + 58, gate: null, times: relayTimes(23 * 60 + 58) }, { sent: 5, gate: 2, times: relayTimes(5, 2) });
const clock = [0, 1, 59, 60, 61, 599, 600, 1209, 1216, 1439, 1440, 1501].map(m => ({ minute: m, text: hhmm(m) }));

// ---------- what 정민 really did ----------
const contexts = [];
{
  const add = (label, G) => contexts.push({ label, save: save(G), context: noteContext(G) });
  for (const route of ['stairs', 'ramp']) for (const via of ['sign', 'notice_after']) add(`${route}/${via}`, played(route, via));
  const G = played('stairs');
  st.recordEvent(G, 'route_attempt', { who: 'seoyeon', route: 'ramp' });
  st.recordEvent(G, 'route_attempt', { who: 'jeongmin', route: 'access' });
  st.recordEvent(G, 'gate_observed', { who: 'jeongmin', via: 'notice_after' });
  add("서연's moves, C and later notices do not count", G);
  st.recordEvent(G, 'route_attempt', { who: 'jeongmin', route: 'ramp' });
  add('the last entrance 정민 used', G);
  add('nothing happened', st.createGame({ seed: 2 }));
  const H = st.createGame({ seed: 2 });
  st.recordEvent(H, 'gate_observed', { who: 'seoyeon', via: 'notice_after' });
  st.recordEvent(H, 'gate_observed', {});
  st.recordEvent(H, 'route_attempt', { who: 'jeongmin' });
  add('events with other people or missing fields', H);
}

// ---------- chip notes ----------
const composed = [];
for (const gateSeenAt of ['20:09', relayTimes(20 * 60 + 14).gateSeenAt])
  for (const route of ['stairs', 'ramp']) for (const observedVia of ['sign', 'notice_after']) for (const chips of allChips()) {
    const text = composeNote(chips, { route, observedVia, gateSeenAt });
    const claims = parseNote(text);
    composed.push({ chips, route, observedVia, gateSeenAt, text, claims, kind: noteKind(claims) });
  }
const composeDefaults = { empty: composeNote(), route: composeNote({ route: true }), unknownRoute: composeNote({ route: true, advice: 'direction' }, { route: 'access' }) };

// ---------- free text ----------
const FREE = [
  // tests/relay.test.mjs
  '동쪽문 막힘ㅠ 난 계단으로 옴 이쪽으로 ㄱㄱ', '대피소 전부 닫힘', '다른 입구는 모두 확인 못 했어', 'C로 와', 'ㅋㅋㅋ', '2시 10분에 동쪽 출입구 통제 중',
  '20:09 대피소 동쪽 출입구 닫힘. 다른 입구는 몰라', '나 대피소에 막 도착했어', '대피시설이 닫혀 있었어', '20:09 동쪽 닫힘. 나머지 다 닫혔는지 확인 못 함',
  '한빛 생활문화센터 지하주차장으로 와', '계단 말고 다른 입구로 가', '1시간 전에 동쪽 문 닫혀 있었어', '20:09 동쪽 폐쇄. B 진출입로 운영. C는 몰라. 안내 다시 확인!',
  '동쪽 출입구가 닫혀 있었어', '20:09에 동쪽 출입구가 닫혀 있었어. 다른 입구 상태는 확인 못 했어.', '20:09 동쪽 폐쇄. B 진출입로 운영. C 경사로는 몰라. 안내 다시 확인',
  '', '   ', '\n\n', '...', '?!',
  // entrances: a lone latin letter is a word of its own next to Hangul (JS \b knows only ASCII word characters)
  'A로 와', 'a로 와', 'A', 'b쪽이야', 'B진출입로', 'ABC', 'A1 입구', 'cafe 앞', 'C', 'c로', 'c_1', '경사로로 와', '입구 A, B 둘 다 열림', 'A-B 사이',
  'KA로 와', 'K A로 와', '가A나', '１A', 'Ａ로 와', 'ａ', '계단A', 'A계단 말고 B로', 'B 빼고', '계단은 안 돼', '계단은 안돼', '계단 쓰지 마', '계단 쓰지마',
  '경사로 가지 마', '경사로로 가지마. 계단으로', '계단 피해서 와', '진출입로', '지하주차장 진출입로 말고. 계단!',
  // what was seen
  '동쪽 문 막혀 있음', '동문 막힌 상태', '동쪽 막힘', '동쪽 막혔어', '동쪽 막아 놨어', '동쪽 막았어', '동쪽에 막 도착', '동쪽 통제', '동문 폐쇄',
  '동쪽 출입구는 지금 사람이 많아서 통제', '동쪽 출입구 지금 통제', '동쪽, 닫힘', '동쪽. 닫힘', '동쪽12345678닫힘', '동쪽123456789닫힘',
  '전체 폐쇄', '모두 닫힘', '전부 닫았어', '다 닫혔어', '다닫힘', '다  \t닫힘', '대피소 문 닫힘', '대피시설 폐쇄', '동쪽 대피소 닫힘', '대피소 닫혔는지 몰라',
  '대피소12345678닫힘', '대피소123456789닫힘', '대피소, 닫힘', '대피소는 열려 있어', '모두 무사해', '다 왔어', '전체적으로 막 붐빔',
  'A 닫힘. 대피소 전부 닫힘!', '대피소 전부 닫힘? 동쪽만 닫힘', '동쪽 닫힘\n대피소 다 닫힘', '동문 통제. 전부 폐쇄는 확인 못 함. 대피소 폐쇄',
  // time
  '8시', '8시간 뒤', '8시간', '20시 9분', '20 시 09 분', '20시9분에 봄', '9:5', '09:05', '9:05', '24:00', '23:59', '7:60', '120:09', '12:345', 'x20:09y',
  '20:09분', '가20:09나', '20:09:30', '2:5 3:07', '８시', '２０：０９', '٨시', '오후 8시쯤 동쪽 닫힘', '100시', '3 시', '3\t시 5\n분', '시간 없음', '10시10분10초',
  '어제 7시. 오늘 20:09', '20:09. 7시',
  // not knowing
  '모르겠어', '몰라', '몰랐어', '미확인', '확인 못 함', '확인못함', '확인 안 함', '확인  안 해 봄', '확인하지 않음', '확인했어', '확인 완료',
  // advice
  '나처럼 와', '따라와', '따라  와', '따라 가', '똑같이 해', '같은길', '같은 길로', '여기로', '오른쪽으로', '왼쪽', '쭉 가', '건너서', '꺾어',
  '안내 확인', '안내문 꼭 다시 확인해', '확인은 현장 안내로', '안내. 확인', '안내123456789확인', '안내12345678확인', '확인12345678안내', '나처럼 쭉 와', '쭉 와. 나처럼',
  // white space as JS \s and trim see it: NBSP, ideographic space and BOM count; NEL, zero-width space and U+180E do not
  '확인\u00a0못 했어', '확인\u3000안 함', '확인\ufeff못', '확인\u0085못', '확인\u200b못', '확인\u180e못', '확인\u2028못', '따라\u00a0와', '같은\u2003길',
  '8\u00a0시 5\u3000분', '8\u0085시', '안\ufeff돼 계단', ' \u00a0동쪽 닫힘\ufeff ', '\u0085동쪽 닫힘\u0085', '\u200b계단\u200b', '\u3000A\u3000',
  // clause splitting
  '동쪽 닫힘。계단으로 옴', '동쪽 닫힘…계단', '동쪽 닫힘 - 계단으로 - 나처럼', '계단!!나처럼??쭉', 'a.b.c', '20.09 동쪽 닫힘', '동쪽 출입구 닫힘, 계단으로 옴',
  // long and mixed
  `동쪽 닫힘 ${'가'.repeat(200)}`, '20:09 동쪽 폐쇄 A 계단 이용 나처럼 왼쪽 안내 확인 다른 곳 몰라',
];
const free = FREE.map(text => { const claims = parseNote(text); return { text, claims, kind: noteKind(claims) }; });
const kinds = [
  { what: null, time: null, route: null, unknowns: false, advice: null, quotes: {} },
  { what: 'gate_closed', time: '20:09', route: null, unknowns: false, advice: null, quotes: {} },
  { what: 'gate_closed', time: null, route: null, unknowns: true, advice: null, quotes: {} },
  { what: 'gate_closed', time: '20:09', route: null, unknowns: true, advice: null, quotes: {} },
  { what: 'all_closed', time: '20:09', route: null, unknowns: true, advice: null, quotes: {} },
  { what: null, time: null, route: null, unknowns: false, advice: 'direction', quotes: {} },
  { what: null, time: null, route: 'access', unknowns: false, advice: null, quotes: {} },
  { what: null, time: '', route: null, unknowns: false, advice: null, quotes: {} },
].map(claims => ({ claims, kind: noteKind(claims) }));

// ---------- handover ----------
const games = [];
const handovers = [];
// Many messages lead to the same pins and claim rows: each distinct set is stored once and cases point at it.
const pinSets = [], claimSets = [];
const intern = (table) => { const seen = new Map(); return (v) => { const k = JSON.stringify(v); if (!seen.has(k)) { seen.set(k, table.length); table.push(v); } return seen.get(k); }; };
const pinSet = intern(pinSets), claimSet = intern(claimSets);
function run(gi, input, { keepAfter = false, jsOnly = false } = {}) {
  const G = st.load(games[gi].save);
  const args = {};
  if (input.chips !== undefined) args.chips = input.chips;
  if (input.text !== undefined) args.text = input.text;
  if (input.edited !== undefined) args.edited = input.edited;
  const h = input.none ? makeHandover(G) : makeHandover(G, args);
  const c = { game: gi, input: { chips: input.chips ?? null, text: input.text ?? null, edited: input.edited ?? false, none: !!input.none },
    handover: h, event: G.events.at(-1), pins: pinSet(buildPins(G)), claims: claimSet(readClaims(G)) };
  if (keepAfter) c.after = save(G);
  if (jsOnly) c.jsOnly = true; // a shape only the original can write (partial chips): the port must still read it from the save
  handovers.push(c);
}
const addGame = (label, G) => games.push({ label, save: save(G) }) - 1;
const chipGames = [addGame('stairs/sign', played('stairs', 'sign')), addGame('ramp/sign', played('ramp', 'sign')),
  addGame('stairs/notice_after', played('stairs', 'notice_after', { gate: GATE + 2, sent: GATE + 3 })), addGame('ramp/notice_after', played('ramp', 'notice_after', { sent: GATE + 30, seed: 9 }))];
const noRelay = addGame('no relay, nothing happened', st.createGame({ seed: 3 }));
const relayOnly = addGame('relay only', (() => { const G = st.createGame({ seed: 4 }); G.relay = relayTimes(20 * 60 + 40); return G; })());
const cleared = addGame('handover cleared (story/seoyeon.js sets null before composing)', (() => { const G = played('ramp'); G.handover = null; return G; })());

let n = 0;
for (const gi of chipGames) for (const chips of allChips()) run(gi, { chips }, { keepAfter: n++ % 32 === 0 });
const EDITS = [...FREE.filter(Boolean), `  동쪽\u0007 막힘 ${'가'.repeat(200)}`, '동쪽\t닫힘\r\n계단으로\u007f옴', '\u0001\u001f20:09 동쪽 폐쇄\u0000', `${'A'.repeat(119)} B`, `${' '.repeat(130)}동쪽 닫힘`, `${'가'.repeat(118)} \u00a0 나`];
// Every edit as 정민 who took the stairs; every third one again as 정민 who took the ramp and only read the notice.
for (const gi of [chipGames[0], chipGames[3]]) EDITS.forEach((text, i) => {
  if (gi === chipGames[3] && i % 3) return;
  run(gi, { chips: { what: 'gate_closed', time: true, route: true, unknowns: false, advice: null }, text, edited: true }, { keepAfter: i % 32 === 0 });
});
// Blank edits fall back to the chips; text counts only when edited.
for (const text of ['   ', '\u0000\u0007\n', undefined, '\u00a0\ufeff']) run(chipGames[0], { chips: { what: 'gate_closed', time: true, route: true, unknowns: true, advice: null }, text, edited: true });
run(chipGames[1], { chips: { what: 'all_closed', time: false, route: false, unknowns: false, advice: 'follow_me' }, text: '무시됨' });
run(chipGames[1], { chips: { what: null, time: false, route: false, unknowns: false, advice: null }, text: 'C로 와', edited: true }, { keepAfter: true });
for (const gi of [noRelay, relayOnly, cleared]) {
  run(gi, { chips: { what: 'gate_closed', time: true, route: true, unknowns: true, advice: 'check_notice' } }, { keepAfter: true });
  run(gi, { none: true }, { keepAfter: true });
  run(gi, { edited: true, text: '대피소 전부 닫힘. 나처럼 와' });
}
// Shapes the port never writes but must read back from an original save.
run(chipGames[0], { chips: { what: 'all_closed' } }, { keepAfter: true, jsOnly: true });
run(chipGames[3], { chips: { what: 'gate_closed', route: true, advice: 'follow_me' } }, { keepAfter: true, jsOnly: true });
run(chipGames[2], { chips: { what: 'gate_closed', time: true, unknowns: true, advice: 'check_notice' } }, { keepAfter: true, jsOnly: true });

// Pins and claim rows when no message was written.
const bare = games.map((g, gi) => { const G = st.load(g.save); return { game: gi, pins: buildPins(G), claims: readClaims(G) }; });
const nothing = { pins: buildPins(undefined), claims: readClaims(undefined) };

const golden = {
  maxNote: MAX_NOTE, entrances: ENTRANCES, clock, times, contexts, composed, composeDefaults, free, kinds,
  closing: Object.fromEntries(['precise', 'partial', 'generalized', 'unparsed', 'something else'].map(k => [k, closingLines(k)])),
  closingDefault: closingLines(undefined),
  games, handovers, pinSets, claimSets, bare, nothing,
};
mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, JSON.stringify(golden, null, 0));
const tally = (list, f) => Object.entries(list.reduce((m, x) => { const k = String(f(x)); m[k] = (m[k] ?? 0) + 1; return m; }, {})).map(([k, v]) => `${k}:${v}`).join(' ');
console.log(`${times.length} times, ${composed.length} chip notes, ${free.length} free texts (kind ${tally(free, x => x.kind)})`);
console.log(`free claims: what ${tally(free, x => x.claims.what)} | route ${tally(free, x => x.claims.route)} | advice ${tally(free, x => x.claims.advice)} | time ${free.filter(x => x.claims.time).length} | unknowns ${free.filter(x => x.claims.unknowns).length}`);
console.log(`${handovers.length} handovers (kind ${tally(handovers, x => x.handover.kind)}; edited ${handovers.filter(x => x.handover.edited).length}; with save ${handovers.filter(x => x.after).length})`);
console.log(`${pinSets.length} pin sets: third ${tally(handovers, x => pinSets[x.pins][2].factor)} | time anchor ${tally(handovers, x => pinSets[x.pins][1].anchor)} | body ${tally(handovers, x => pinSets[x.pins][0].anchor)}`);
console.log(`${claimSets.length} claim sets: ${tally(handovers.flatMap(x => claimSets[x.claims]), r => `${r.label}/${r.status}`)}`);
console.log('->', out);
