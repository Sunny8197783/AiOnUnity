// What the ORIGINAL game/lounge.js answers: neighbour names, message bodies, and which relay messages are let
// through.   node make-golden-lounge.mjs [out.json]
import { lib, write, rng } from './make-golden-runs.mjs';

const { RELAY, TOPIC, PHRASES, isName, newName, phraseBody, runBody, readMessage, readHistory } = await lib('dist/game/lounge.js');
const { ENDINGS } = await lib('dist/game/endings.js');
const { CHALLENGES } = await lib('dist/game/challenges.js');

// newName(rnd): two draws per name.
const r = rng(99);
const draws = [[0, 0], [0.5, 0.5], [0.9999, 0.9999], [0.999999999, 0], [0.1666, 0.1111], [0.1667, 0.99999], ...Array.from({ length: 40 }, () => [r(), r()])];
const names = draws.map(([a, b]) => { const q = [a, b]; return { draws: [a, b], name: newName(() => q.shift()) }; });

const nameChecks = ['', '107동 이웃 1234', '103동 이웃 12', '103동 이웃 1234<b>', '103동 이웃 4821', '100동 이웃 4821', '101동 이웃 0000', '106동 이웃 9999', ' 103동 이웃 4821',
  '103동 이웃 4821\n', '103동 이웃 ４８２１', '103동  이웃 4821', '103동 이웃 48210', null, 7, ['103동 이웃 4821'], { who: '103동 이웃 4821' }, true]
  .map((v) => ({ v, ok: isName(v) }));

const who = '103동 이웃 4821';
const bodies = [
  ...Object.keys(PHRASES).map((p) => ({ fn: 'phrase', who, p, body: phraseBody(who, p) })),
  { fn: 'phrase', who: '101동 이웃 1000', p: 'free "text"\n', body: phraseBody('101동 이웃 1000', 'free "text"\n') },
  ...ENDINGS.map((e, i) => ({ fn: 'run', who, ending: e.id, n: i, body: runBody(who, { ending: e.id, n: i }) })),
  { fn: 'run', who, ending: 'commons', n: CHALLENGES.length, body: runBody(who, { ending: 'commons', n: CHALLENGES.length }) },
];

const msg = (message, extra = {}) => ({ id: 'a1', time: 1762050000, event: 'message', message, ...extra });
const S = JSON.stringify;
const messages = [
  ...bodies.map((b, i) => msg(b.body, { id: `m${i}`, time: 1762050000 + i })),
  msg('hi'), msg('{}'), msg('null'), msg('[]'), msg('7'), msg(''), msg(S({ v: 1, who, p: 'free text' })), msg(S({ v: 1, who, p: 'toString' })), msg(S({ v: 1, who, p: 'constructor' })),
  msg(S({ v: 1, who: 'evil<script>', p: 'radio' })), msg(S({ v: 2, who, p: 'radio' })), msg(S({ v: '1', who, p: 'radio' })), msg(S({ v: 1.0, who, p: 'radio' })), msg(S({ who, p: 'radio' })),
  msg(S({ v: 1, who, run: { e: 'nope', c: 1 } })), msg(S({ v: 1, who, run: { e: 'bus', c: 99 } })), msg(S({ v: 1, who, run: { e: 'bus', c: -1 } })), msg(S({ v: 1, who, run: { e: 'bus', c: 1.5 } })),
  msg(S({ v: 1, who, run: { e: 'bus', c: '3' } })), msg(S({ v: 1, who, run: { e: 'bus' } })), msg(S({ v: 1, who, run: 'bus' })), msg(S({ v: 1, who, run: null })), msg(S({ v: 1, who })),
  msg(S({ v: 1, who, p: 7, run: { e: 'walk', c: 2 } })), msg(S({ v: 1, who, p: 'nope', run: { e: 'walk', c: 2 } })), msg(S({ v: 1, who, p: 'hello', run: { e: 'walk', c: 2 } })),
  msg(S({ v: 1, who, run: { e: 'shut', c: 0 } })), msg(S({ v: 1, who, run: { e: 'board', c: 13 } })), msg(S({ v: 1, who, run: { e: 'board', c: 13.0 }, more: [1, 2] })),
  msg(phraseBody(who, 'radio'), { event: 'open' }), msg(phraseBody(who, 'radio'), { event: 'keepalive' }), msg(phraseBody(who, 'radio'), { id: 7 }), msg(phraseBody(who, 'radio'), { id: '' }),
  msg(phraseBody(who, 'radio'), { time: '1762050000' }), msg(phraseBody(who, 'radio'), { time: 1762050000.5 }), msg(phraseBody(who, 'radio'), { time: null }), msg(7), msg(null), msg({ v: 1, who, p: 'radio' }),
  { event: 'message' }, { id: 'a2', time: 5, message: phraseBody(who, 'cheer') }, null, 7, 'text', [], {},
].map((m) => ({ m, out: readMessage(m) }));

const histories = [
  [S(msg(phraseBody('102동 이웃 2000', 'hello'))), 'garbage', S({ event: 'keepalive' }), ''].join('\n'),
  messages.slice(0, 30).map((x) => S(x.m)).join('\n'),
  messages.map((x) => S(x.m)).join('\r\n'),
  '', '\n\n', 'null\n7\n[]',
  [S(msg(runBody('104동 이웃 1234', { ending: 'relief', n: 4 }), { id: 'r1', time: 10 })), S(msg(phraseBody('105동 이웃 5555', 'thanks'), { id: 'r2', time: 11 }))].join('\n') + '\n',
].map((text) => ({ text, out: readHistory(text) }));
histories.push({ text: null, out: readHistory(null) });

console.log(write('golden-lounge.json', { relay: RELAY, topic: TOPIC, phrases: PHRASES, names, nameChecks, bodies, messages, histories }),
  `(${names.length} names, ${messages.length} messages, ${messages.filter((x) => x.out).length} let through)`);
