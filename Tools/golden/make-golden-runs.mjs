// Shared scenarios for the deeds / fates / endings / challenges / debrief parity fixtures: games built with the
// ORIGINAL state.js, drills.js, ledger.js and deeds.js. Run directly it writes Fixtures/golden-runs.json
// ({ runs: [{ name, save }] }); the other make-golden-*.mjs import buildRuns() and write what the original
// answers for each run, keyed by the same names.
//
//   node make-golden-runs.mjs [out.json]          AION_SRC=file:///.../safe-route/ overrides the source folder
import { writeFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const SRC = process.env.AION_SRC
  ?? 'file:///C:/Users/김태양/AppData/Local/Temp/claude/80eae06ac10fc83e/082ba9f6-2e32-43aa-b236-612ace9a2adc/scratchpad/aion-src/safe-route/';
export const lib = (path) => import(new URL(path, SRC).href);
export const outPath = (name) => process.argv[2] ?? fileURLToPath(new URL(`../../Tests/EditMode/Fixtures/${name}`, import.meta.url));
export const isMain = (meta) => !!process.argv[1] && meta.url === pathToFileURL(process.argv[1]).href;
export const write = (name, data) => {
  const json = JSON.stringify(data);
  writeFileSync(outPath(name), json);
  return `${name}: ${(Buffer.byteLength(json) / 1024).toFixed(0)} KB`;
};
// mulberry32, as in the original state.js
export const rng = (a) => () => {
  a |= 0; a = (a + 0x6d2b79f5) | 0;
  let t = Math.imul(a ^ (a >>> 15), 1 | a);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
};

export const COMMONS = ['manager_contact', 'manager_detour', 'rounds_with_manager', 'elder_met', 'elder_alert_knock', 'board_offer', 'mart_share'];
// tests/endings.test.mjs FIX: the minimal run that reaches each ending.
export const FIX = {
  bus: ['bus'], walk: ['walk'], seoyeon: ['seoyeon'], missed: ['missed'], stay: ['stay'], volunteer: ['volunteer'],
  commons: ['stay', COMMONS],
  reopen: ['missed', ['clerk_contact', 'promise_store', 'store_paid', 'store_shuttered']],
  relief: ['volunteer', ['driver_text', 'yoon_reply', 'sort_supplies']],
  board: ['stay', ['signals_clear', 'rumor_checked', 'handover_precise', 'card_meet', 'blackout_radio']],
  shut: ['stay', ['hoard'], [['violence', { how: 'punch', ko: true }]]],
};
export const N_RANDOM = 36;

export async function buildRuns() {
  const st = await lib('dist/game/state.js');
  const { CATALOG } = await lib('dist/game/items.js');
  const dr = await lib('dist/game/drills.js');
  const led = await lib('dist/game/ledger.js');
  const { DEEDS, remember } = await lib('dist/game/deeds.js');
  const { PREP } = await lib('dist/game/ripples.js');
  const { CHALLENGES, TIMED } = await lib('dist/game/challenges.js');
  const { RAW, run } = await lib('tests/deed-fixtures.mjs');
  const { createGame, recordEvent } = st;
  const prices = Object.fromEntries(Object.entries(CATALOG).map(([id, c]) => [id, c.price]));
  const runs = [];
  // tags: what the debrief fixture asks of a run ('fail' every fail card, 'somefails' the ones that read the state, 'record').
  const add = (name, G, tags = '') => { runs.push({ name, G, tags: tags.split(' ').filter(Boolean) }); return G; };
  const fresh = (seed = 4) => createGame({ seed });
  const rich = (cart = {}, seed = 5) => { const G = createGame({ seed }); G.money = 1e7; st.buy(G, cart, prices); return G; };
  const timed = (G, ms = 60000, retries = 0) => { for (const id of TIMED) recordEvent(G, 'chapter_time', { id, ms, retries }); return G; };
  // An empty flat: only what the scenario lays in (tests/r6-fix1-endings.test.mjs).
  const flat = (path, items = {}) => {
    const G = createGame({ seed: 3 }); if (path) G.know.path = path;
    for (const id of Object.keys(G.items)) G.items[id].home = G.items[id].bag = 0;
    for (const [id, n] of Object.entries(items)) G.items[id] = { home: n, bag: 0, bought: 0, checked: false, used: 0 };
    return G;
  };
  const ledger = (G, plan, days, opts) => { led.applyLedger(G, led.simulateDays(G, plan, days, opts)); return G; };

  // ---------- deeds ----------
  add('fresh', fresh());
  for (const [id, evs] of Object.entries(RAW)) {
    const G = fresh();
    for (const [type, d, ch] of evs) { if (ch) G.chapter = ch; recordEvent(G, type, structuredClone(d)); }
    add(`raw:${id}`, G);
  }
  for (const id of Object.keys(FIX)) add(`fix:${id}`, run(FIX[id][0], FIX[id][1], FIX[id][2]));
  {
    const G = fresh();
    for (const [who, what] of [['seoyeon', '어떻게 도울지 먼저 묻고, 계단에서 보행기를 옆에서 들어 주었다'], ['seoyeon', '라디오 메모와 급수소 소식을 서로 나누기로 했다'],
      ['seoyeon', '떠나기 전 서로의 연락처를 다시 확인했다'], ['manager', '마지막까지 건물을 지키며 길을 안내했다'],
      ['clerk', '가게를 닫으며 쪽지와 현금 통을 남겨 이웃이 필요한 물건을 가져가게 했다']]) recordEvent(G, 'help', { who, what });
    add('legacy-help', G);
    const C = fresh(); recordEvent(C, 'help', { who: 'seoyeon', what: '계단에서 보행기를 옆에서 들어 주었다' });
    add('legacy-carry', C);
    const T = fresh(); recordEvent(T, 'help', { who: 'seoyeon', kind: 'water', what: '어떻게 도울지 먼저 묻고' });
    add('help-tag-wins', T);
  }
  {
    const G = fresh();
    for (let i = 0; i < 3; i++) recordEvent(G, 'signal_sorted', { id: `s${i}`, ok: true });
    recordEvent(G, 'signal_sorted', { id: 'x', ok: false });
    add('signals-3', structuredClone(G));
    recordEvent(G, 'signal_sorted', { id: 's3', ok: true });
    recordEvent(G, 'signal_sorted', { id: 's4', ok: true });
    recordEvent(G, 'signal_sorted', { id: 's5', ok: 1 }); // truthy is not true
    add('signals-5', G);
  }
  {
    const G = fresh();
    recordEvent(G, 'violence', { how: 'punch', ko: false });
    recordEvent(G, 'violence', { how: 'kick', ko: false });
    recordEvent(G, 'violence', { how: 'punch', ko: true });
    recordEvent(G, 'looter', { result: 'fought' });
    add('violence-3', G);
  }
  for (const [name, fn] of [
    ['fire-out-object', (G) => dr.recordEncounter(G, 'extinguisher', { result: 'out' })],
    ['fire-retreat-string', (G) => dr.recordEncounter(G, 'extinguisher', 'retreat-ok')],
    ['fire-retreat-event', (G) => recordEvent(G, 'fire_retreat', { size: 'large' })],
    ['fire-escalated', (G) => dr.recordEncounter(G, 'extinguisher', 'escalated')],
    ['fire-eval-out', (G) => dr.recordEncounter(G, 'extinguisher', dr.evalExtinguisher(['shout', 'pin', 'aim', 'squeeze', 'sweep'], { fire: 'small' }))],
    ['fire-eval-pin', (G) => dr.recordEncounter(G, 'extinguisher', dr.evalExtinguisher(['shout', 'aim', 'pin', 'squeeze', 'sweep'], { fire: 'small' }))],
    ['fire-null-result', (G) => dr.recordEncounter(G, 'extinguisher', null)],
    ['fire-other-tool', (G) => dr.recordEncounter(G, 'radio', 'out')],
  ]) { const G = fresh(); G.chapter = 'endure'; fn(G); add(name, G, 'somefails'); }
  {
    // shelter_fire_judged: the last decision wins over know.stayMove; an escalated fire is no judgement
    const A = fresh(); A.know.stayMove = 'move'; A.chapter = 'crossroads'; dr.recordEncounter(A, 'extinguisher', 'retreat-ok');
    add('shelter-fire-know', A);
    const B = fresh(); B.know.stayMove = 'move'; recordEvent(B, 'decision', { stayMove: 'stay' }); B.chapter = 'crossroads'; dr.recordEncounter(B, 'extinguisher', 'out');
    add('shelter-fire-decided-stay', B);
    const C = fresh(); recordEvent(C, 'decision', { stayMove: 'stay' }); recordEvent(C, 'decision', { stayMove: 'move' }); C.chapter = 'crossroads';
    dr.recordEncounter(C, 'extinguisher', { result: 'escalated' }); add('shelter-fire-escalated', structuredClone(C));
    dr.recordEncounter(C, 'extinguisher', { ok: true }); add('shelter-fire-object', C);
    const D = fresh(); recordEvent(D, 'decision', {}); D.know.stayMove = 'move'; D.chapter = 'crossroads'; dr.recordEncounter(D, 'extinguisher', 'practice');
    add('shelter-fire-decision-empty', D);
  }
  {
    const G = fresh(); remember(G, 'promise_note'); add('promise-open', structuredClone(G));
    recordEvent(G, 'note_left', {}); remember(G, 'promise_store'); add('promise-store-open', G);
  }
  {
    const G = fresh(); remember(G, 'promise_note'); recordEvent(G, 'note_left', {}); remember(G, 'promise_store'); remember(G, 'store_shuttered');
    recordEvent(G, 'help', { who: 'seoyeon', kind: 'memo', what: '?' });
    add('promises-kept-and-news-open', G);
  }
  {
    const G = fresh();
    recordEvent(G, 'elder_choice', { pick: 'go' }); recordEvent(G, 'knock', { pick: 'none' });
    recordEvent(G, 'rumor_checked', { id: 'camp_food' }); recordEvent(G, 'rumor_spread', { id: 'evac_shortcut' });
    recordEvent(G, 'help', { who: 'manager', what: '박 소장과 함께 남아 건물을 지키기로 했다' });
    add('final-build-none', structuredClone(G));
    recordEvent(G, 'help', { who: 'elder', what: '경보 속 캄캄한 계단에서 304호 어르신을 부축해 1층까지 함께 내려갔다' });
    recordEvent(G, 'knock', { pick: 'info' }); recordEvent(G, 'rumor_checked', {});
    recordEvent(G, 'rumor_spread', { id: null }); recordEvent(G, 'docs_done', { fields: 'neighbors' }); recordEvent(G, 'docs_done', { fields: ['family', 'meet', 7] });
    recordEvent(G, 'buy', { cart: { water_500: 1 }, total: 700, paid: 'iou' }); recordEvent(G, 'path', { path: 'teleport' });
    add('final-build-some', G);
  }
  {
    const G = fresh(); remember(G, 'driver_text'); const mid = recordEvent(G, 'blackout', {}).seq; remember(G, 'mart_share'); recordEvent(G, 'hoard', {});
    add('new-deeds-after', G); void mid;
  }

  // ---------- endings and fates ----------
  const NEW = Object.keys(DEEDS).filter((id) => DEEDS[id].src === 'new');
  add('bus-all-new', run('bus', NEW));
  add('stay-mixed', run('stay', ['elder_candle_tip', 'fire_warned', 'water_declined', 'store_iou', 'looter_escaped', 'looter_fought', 'shelter_water_received', 'lead_honest', 'yoon_reply']));
  const ALL = [...COMMONS, 'clerk_contact', 'promise_store', 'store_paid', 'store_shuttered', 'driver_text', 'yoon_reply', 'sort_supplies', 'signals_clear', 'rumor_checked'];
  for (const p of ['bus', 'walk', 'seoyeon']) {
    add(`leave-all:${p}`, run(p, ALL));
    add(`leave-harm:${p}`, run(p, ['hoard'], [['violence', { how: 'punch', ko: true }]]));
  }
  add('tie-commons', run('stay', [...COMMONS, 'clerk_contact', 'store_paid', 'store_shuttered']));
  add('reopen-higher', run('stay', [...COMMONS, 'clerk_contact', 'store_paid', 'store_shuttered', 'promise_store']));
  add('harm-first', run('stay', [...COMMONS, 'hoard'], [['violence', { how: 'punch', ko: true }]]));
  add('harm3-stay', run('stay', ['hoard', 'rumor_spread', 'store_took']));
  { const G = createGame({ seed: 3 }); G.know.path = 'teleport'; add('path-teleport', G); }
  add('walk-driver-family', run('walk', ['driver_text', 'card_family']));
  { const G = run('walk'); G.injury = ['leg']; add('walk-leg', G); }
  { const G = run('walk', ['driver_text']); G.injury = ['leg']; add('walk-leg-driver', G); }
  { const G = run('walk'); G.stats.health = 30; add('walk-weak', G); }
  add('bus-letters-seoyeon', run('bus', ['seoyeon_contact', 'stairs_asked', 'water_for_seoyeon', 'bus_helped']));
  add('bus-letters-all', run('bus', ['seoyeon_contact', 'stairs_asked', 'manager_contact', 'rounds_with_manager', 'clerk_contact', 'store_paid', 'store_shuttered', 'elder_met', 'elder_alert_knock']));
  add('bus-logistics', run('bus', ['driver_text', 'yoon_reply', 'sort_supplies', 'lead_honest']));
  add('missed-manager', run('missed', ['manager_contact', 'manager_detour', 'rounds_with_manager']));
  add('missed-note', run('missed', ['door_note']));
  add('missed-card', run('missed', ['card_meet']));
  add('seoyeon-asked', run('seoyeon', ['stairs_asked']));
  add('commons-stock', run('stay', [...COMMONS, 'stock_to_manager']));
  add('commons-big', run('stay', [...COMMONS, 'card_neighbors', 'door_note', 'water_for_seoyeon']));
  add('reopen-iou', run('volunteer', ['clerk_contact', 'store_paid', 'store_iou']));
  add('reopen-shuttered-iou', run('stay', ['clerk_contact', 'store_shuttered', 'store_iou']));
  { const G = run('volunteer'); G.stats.health = 30; add('volunteer-weak', G); }
  { const G = run('stay'); G.injury = ['leg']; add('stay-leg', G); }
  // people lines
  add('seoyeon-promise-broken', run('bus', ['seoyeon_contact', 'promise_note']));
  add('seoyeon-promise-kept', run('bus', ['seoyeon_contact', 'promise_note'], [['note_left', {}]]));
  add('seoyeon-news-broken', run('bus', ['seoyeon_contact', 'notes_exchange']));
  add('seoyeon-handover', run('stay', ['seoyeon_contact', 'handover_precise']));
  add('seoyeon-only-contact', run('stay', ['seoyeon_contact']));
  add('manager-candle', run('bus', ['manager_contact', 'candle_left']));
  add('manager-water-declined', run('bus', ['manager_contact', 'water_declined']));
  add('manager-fire', run('bus', ['manager_contact', 'fire_handled']));
  add('manager-default', run('bus', ['manager_contact']));
  add('clerk-took', run('bus', ['clerk_contact', 'store_took']));
  add('clerk-shuttered', run('bus', ['store_shuttered']));
  add('clerk-promise-broken', run('bus', ['promise_store']));
  add('clerk-contact', run('bus', ['clerk_contact']));
  add('elder-knock-stay', run('stay', ['elder_alert_knock']));
  add('elder-knock-bus', run('bus', ['elder_alert_knock']));
  add('elder-told-bus', run('bus', ['elder_alert_told']));
  add('elder-told-missed', run('missed', ['elder_alert_told']));
  add('elder-candle-fire', run('stay', ['elder_candle_tip', 'fire_warned']));
  add('elder-fire', run('stay', ['fire_warned']));
  add('elder-candle', run('stay', ['elder_candle_tip']));
  add('elder-met', run('stay', ['elder_met']));
  add('elder-hall', run('bus', ['hall_checked']));
  add('strangers-many', run('bus', ['driver_text', 'looter_gave', 'mart_share', 'shelter_water_given', 'rumor_corrected']));
  add('strangers-walk', run('walk', ['driver_text', 'knock_helped', 'looter_escaped', 'looter_fought']));
  add('strangers-late', run('stay', ['shelter_water_received', 'shelter_water_given', 'mart_share']));
  { const G = run('stay', ['rumor_spread']); remember(G, 'rumor_corrected', { after: 'spread' }); add('rumor-spread-corrected', G); }
  add('rumor-spread-only', run('stay', ['rumor_spread']));
  add('yoon-relief', run('volunteer', ['lead_honest', 'driver_text', 'yoon_reply', 'sort_supplies']));
  add('yoon-not-relief', run('stay', ['lead_honest', 'yoon_reply']));
  add('town-busy', run('stay', ['mart_share', 'elder_alert_knock', 'shelter_water_given', 'register_helped', 'looter_gave', 'board_offer', 'queue_carry_elder']));
  add('town-some', run('bus', ['mart_share', 'board_offer']));
  add('town-seoyeon-path', run('seoyeon', ['mart_share', 'board_offer', 'rumor_checked']));
  {
    const G = createGame({ seed: 8 }); G.karma = -5;
    for (const [type, d] of [['help', { who: 'seoyeon', what: '계단에서 보행기를 옆에서 들어 주었다' }], ['help', { who: 'manager', what: '마지막까지 건물을 지키며 길을 안내했다' }],
      ['looter', { result: 'gave' }], ['candle_left', {}], ['path', { path: 'stay' }]]) recordEvent(G, type, d);
    G.know.path = 'stay'; add('old-save', G);
  }
  {
    const G = run('stay');
    for (let i = 0; i < 4; i++) recordEvent(G, 'help', { who: 'manager', what: '같은 말' });
    recordEvent(G, 'help', { who: 'clerk', what: '쪽지와 현금 통', by: 'other' });
    recordEvent(G, 'help', { who: 'seoyeon', kind: 'powerbank', what: '보조배터리', by: 'other' });
    add('help-pressed-again', G, 'record');
  }
  for (const path of ['bus', 'walk', 'seoyeon', 'missed', 'stay', 'volunteer']) { const G = createGame({ seed: 3 }); G.know.path = path; G.stats.health = 20; add(`health20:${path}`, G); }
  // stock and the day ledger
  add('stock-few', flat('stay', { water_500: 4, bars: 4 }));
  add('stock-lots', flat('stay', { water_2l: 5, bars: 4 }));
  add('stock-bag', (() => { const G = flat('missed', { water_2l: 3, water_500: 8, ramen: 2, canned: 2 }); st.pack(G, 'water_2l', 2); st.pack(G, 'canned', 1); return G; })());
  { const G = run('bus'); recordEvent(G, 'help', { who: 'elder', what: '부축했다' }); recordEvent(G, 'help', { who: 'stranger', what: '물 한 병' }); G.day = 3;
    add('ledger-bus', ledger(G, { water: 2, meals: 2, power: 'phone' }, 2), 'record'); }
  { const G = flat('walk'); G.stats.health = 30; G.day = 3; add('ledger-walk-weak-skipped', ledger(G, { water: 1, meals: 1, power: 'phone' }, 2), 'record'); }
  { const G = flat('volunteer'); G.day = 3; add('ledger-volunteer-skipped', ledger(G, { water: 1, meals: 1, power: 'phone' }, 2), 'record'); }
  { const G = flat('stay', { bars: 12 }); G.day = 2; add('ledger-trips', ledger(G, { water: 3, meals: 2, power: 'phone' }, 4), 'record'); }
  { const G = flat('stay', { bars: 12, water_2l: 3 }); G.day = 2; add('ledger-town-thin', ledger(G, { water: 2, meals: 2, power: 'phone' }, 2), 'record'); }
  { const G = flat('stay', { bars: 12, water_2l: 12 }); G.day = 2; add('ledger-town-stocked', ledger(G, { water: 2, meals: 2, power: 'phone' }, 2), 'record'); }
  { const G = flat('bus', { bars: 12, water_2l: 3 }); G.day = 2; add('ledger-leave-thin', ledger(G, { water: 2, meals: 2, power: 'phone' }, 2), 'record'); }
  { const G = flat('missed', { bars: 3, water_2l: 3, old_bars: 2 }); G.day = 2; ledger(G, { water: 2, meals: 3, power: 'phone' }, 2, { place: 'shelter' }); G.day = 5;
    add('ledger-twice', ledger(G, { water: 1, meals: 3, power: 'radio' }, 3), 'record'); }

  // ---------- challenges ----------
  add('ch-network', run('stay', ['blackout_radio', 'rumor_checked', 'rumor_corrected', 'seoyeon_contact', 'manager_contact', 'clerk_contact']));
  add('ch-network-timed', timed(run('stay', ['blackout_radio', 'rumor_checked', 'rumor_corrected', 'seoyeon_contact', 'manager_contact', 'clerk_contact'])));
  add('ch-rumor-spread', run('stay', ['rumor_checked', 'rumor_corrected', 'rumor_spread']));
  add('ch-bus-timed-retry', timed(run('bus'), 90000, 1));
  { const G = timed(run('bus'), 90000, 1); recordEvent(G, 'chapter_time', { id: 'dday', ms: 1000, retries: 0, partial: true }); add('ch-partial', G); }
  add('ch-slow', timed(run('stay', ['blackout_radio', 'rumor_checked']), 120000));
  add('ch-fast', timed(run('stay', ['blackout_radio', 'rumor_checked']), 60000));
  add('ch-fewer', timed(run('bus', []), 1000));
  // the same challenges, both retried: fewer retries rank above a faster time
  add('ch-retry1-slow', timed(run('stay', ['blackout_radio', 'rumor_checked']), 200000, 1));
  add('ch-retry2-fast', timed(run('stay', ['blackout_radio', 'rumor_checked']), 100000, 2));
  for (let i = 0; i < 4; i++) add(`ch-board-${i}`, timed(run('stay', ['blackout_radio', 'rumor_checked']), 50000 + i));
  add('ch-ask-share-elder', timed(run('bus', ['stairs_asked', 'register_helped', 'queue_carry_elder', 'bus_helped', 'mart_share', 'shelter_water_given', 'elder_met', 'elder_visit',
    'handover_precise', 'fire_warned', 'fire_handled', 'promise_note', 'door_note', 'promise_store', 'store_shuttered']), 3725000, 2));
  { const G = run('stay', ['promise_note', 'promise_store', 'store_shuttered']);
    for (const d of [{ id: 'prologue', ms: 0 }, { id: 'prep', ms: 1.5 }, { id: 'nope', ms: 5 }, { id: 'dday', ms: 1e9 }, { id: 'endure', ms: 999999999, retries: 3 }, { ms: 7 }, { id: 'evac', ms: 4000 }, { id: 'evac', ms: 2500, retries: 1, partial: true }])
      recordEvent(G, 'chapter_time', d);
    add('ch-odd-times', G); }

  // ---------- debrief ----------
  const alertReady = () => {
    const G = rich({ water_500: 1 });
    Object.assign(G.know, { shelterLocated: true, entrancesChecked: true, routeChecked: true, extinguisherLocated: true, gateClosedSeen: true });
    st.pack(G, 'water_500');
    return G;
  };
  add('db-fresh9', fresh(9), 'fail record');
  add('db-alert-ready', alertReady(), 'fail record');
  { const G = fresh(1); G.know.shelterLocated = true; add('db-late-located', structuredClone(G), 'somefails');
    Object.assign(G.know, { entrancesChecked: true, routeChecked: true, gateClosedSeen: true }); add('db-late-checked', G, 'somefails'); }
  { const G = fresh(4); Object.assign(G.know, { shelterLocated: true, entrancesChecked: true, routeChecked: true, bagPacked: true }); add('db-late-packed', structuredClone(G), 'somefails');
    recordEvent(G, 'departure', { bag: 'packed' }); add('db-late-departed', structuredClone(G), 'somefails');
    recordEvent(G, 'departure', { bag: 'late', delay: 4 }); add('db-late-late', G, 'somefails'); }
  { const G = fresh(4); Object.assign(G.know, { shelterLocated: true, entrancesChecked: true }); add('db-late-no-route', G, 'somefails'); }
  { const G = rich({ water_2l: 2 }); st.pack(G, 'water_2l', 4); add('db-heavy', G, 'somefails'); }
  { const G = fresh(2); st.adjust(G, 'thirst', 80); st.adjust(G, 'fatigue', 30); st.adjust(G, 'health', -75); G.injury = ['leg']; add('db-dry-hurt', G, 'somefails record'); }
  { const G = fresh(3); dr.recordEncounter(G, 'extinguisher', 'escalated'); add('db-fire-escalated', structuredClone(G), 'somefails');
    dr.recordEncounter(G, 'extinguisher', dr.evalExtinguisher(['shout', 'aim', 'pin', 'squeeze', 'sweep'], { fire: 'small' })); G.know.extinguisherLocated = true;
    add('db-fire-pin', G, 'somefails'); }
  { const G = fresh(3); dr.recordEncounter(G, 'extinguisher', dr.evalExtinguisher(['pin', 'aim', 'squeeze', 'sweep'], { fire: 'small' })); add('db-fire-no-shout', G, 'somefails'); }
  { const G = fresh(3); dr.recordEncounter(G, 'extinguisher', dr.evalExtinguisher(['shout', 'pin', 'aim', 'sweep'], { fire: 'small' })); add('db-fire-no-squeeze', G, 'somefails'); }
  { const G = fresh(3); dr.recordEncounter(G, 'extinguisher', dr.evalExtinguisher(['fight', 'shout', 'pin', 'squeeze'], { fire: 'small' })); add('db-fire-no-aim', G, 'somefails'); }
  { const G = fresh(3); dr.recordEncounter(G, 'extinguisher', dr.evalExtinguisher(['fight'], { fire: 'large' })); add('db-fire-large', G, 'somefails'); }
  { const G = fresh(4); recordEvent(G, 'towel_wet', { with: 'tub' }); G.know.routeChecked = true; add('db-towel-route', G, 'somefails'); }
  {
    const G = rich(); G.chapter = 'prep';
    st.spendSlot(G, 'mart', 2); st.spendSlot(G, 'bag', 1);
    st.buy(G, { water_500: 3, batteries_aa: 1 }, prices); st.check(G, 'flashlight');
    dr.recordEncounter(G, 'bag', 'partial'); dr.recordEncounter(G, 'extinguisher', dr.evalExtinguisher(['retreat'], { fire: 'small' }));
    st.note(G, '라디오 89.1MHz 재난방송', 'official'); st.note(G, '내일 물이 끊긴대', 'rumor');
    recordEvent(G, 'help', { who: 'seoyeon', what: '연락 방법을 정했다' });
    recordEvent(G, 'payoff', { prep: 'bag', moment: '현관에 둔 배낭을 메고 바로 나섰다' });
    G.chapter = 'dday'; recordEvent(G, 'payoff', { prep: 'radio', moment: '안내를 바로 들었다' });
    add('db-summary-prep', structuredClone(G), 'record');
    st.adjust(G, 'thirst', 80); st.adjust(G, 'fatigue', 30); st.adjust(G, 'health', -75);
    add('db-summary-worn', G);
  }
  {
    // every result word, tool, prep id, person and contact the debrief can name
    const G = fresh(1); G.chapter = 'c';
    const results = ['out', 'retreat-ok', 'escalated', 'incomplete', 'risky', 'practice', 'tuned', 'opened', 'missed', 'complete', 'partial', 'planned', 'written', 'skipped',
      'something-new', dr.evalExtinguisher(['retreat'], { fire: 'large' }), dr.evalBag(G, 'alert'), { station: null }, { ok: true }, { ok: false }, { result: 'tuned' }, { result: 'odd' }, null, 7];
    for (const r of results) dr.recordEncounter(G, 'radio', r);
    for (const r of ['sorted', 'misled', 'checked', 'doubted']) dr.recordEncounter(G, 'info', r);
    for (const tool of ['bag', 'route', 'extinguisher', 'docs']) dr.recordEncounter(G, tool, 'practice');
    for (const prep of [...Object.keys(PREP), 'mystery']) recordEvent(G, 'payoff', { prep, moment: '순간' });
    recordEvent(G, 'payoff', { prep: 'bag' });
    for (const who of ['manager', 'clerk', 'seoyeon', 'elder', 'stranger', 'trader', 'passerby']) recordEvent(G, 'help', { who, what: '도왔다' });
    recordEvent(G, 'help', { who: 'seoyeon' }); recordEvent(G, 'help', { what: '누군가를 도왔다' });
    st.trade(G, { ramen: 1 }, { batteries_old: 1 }, 'trader');
    st.trade(G, { ramen: 2, tissue: 1 }, { water_500: 2, bars: 1 }, '지나가던 사람');
    G.items.flashlight.checked = true; st.drainCharge(G, 'flashlight', 100);
    ledger(G, { water: 2, meals: 2, power: 'radio' }, 2);
    G.rel.seoyeon.contact = 'sms'; G.rel.manager.contact = 'office'; G.rel.clerk.contact = 'pigeon';
    add('db-every-word', G, 'fail record');
  }
  {
    const G = rich({ radio: 1, batteries_aa: 1, powerbank: 1, tape: 1, water_500: 2 });
    st.check(G, 'radio'); st.consume(G, 'water_500', 1);
    recordEvent(G, 'payoff', { prep: 'flashlight', moment: '정전 직후 손전등을 바로 켰다' });
    recordEvent(G, 'help', { who: 'seoyeon', what: '계단에서 보행기를 옆에서 들어 주었다' });
    G.rel.manager.contact = 'both'; G.rel.seoyeon.contact = 'sms'; G.know.shelterLocated = true;
    add('db-record-people', structuredClone(G), 'record');
    Object.assign(G.know, { entrancesChecked: true, routeChecked: true, extinguisherLocated: true, radioChecked: true, contactsCard: true, docsCopied: true, meetPoint: '놀이터', waterPoint: true });
    add('db-record-all-known', G, 'record');
  }
  {
    const G = rich({ bars: 3, radio: 1, batteries_aa: 1 }); st.check(G, 'radio'); G.chapter = 'endure';
    st.trade(G, { bars: 3 }, { batteries_old: 1 }, 'trader');
    ledger(G, { water: 2, meals: 2, power: 'powerbank' }, 2); st.drainCharge(G, 'radio', 100);
    add('db-trade-ledger-flat', G, 'record');
  }
  {
    const G = rich({ blackout_curtain: 1 });
    for (let i = 0; i < 3; i++) recordEvent(G, 'help', { who: 'manager', what: '마지막까지 건물을 지키며 길을 안내했다' });
    recordEvent(G, 'help', { who: 'seoyeon', what: '계단에서 보행기를 옆에서 들어 주었다' });
    recordEvent(G, 'help', { who: 'seoyeon', what: '급수소에서 물 한 통을 받아 3층까지 날라 주었다' });
    recordEvent(G, 'help', { who: 'seoyeon', what: '라디오 메모와 급수소 소식을 서로 나누기로 했다' });
    recordEvent(G, 'help', { who: 'elder', what: '304호 어르신을 부축해 함께 내려갔다' });
    recordEvent(G, 'help', { who: 'stranger', what: '정보' });
    add('db-people-rows', structuredClone(G), 'record');
    G.know.curtain = true; G.chapter = 'prep'; recordEvent(G, 'tub_filled', {}); G.day = 1; recordEvent(G, 'payoff', { prep: 'tub', moment: '세수했다' });
    add('db-curtain-ripple', G, 'record');
  }
  {
    const G = fresh(1); G.chapter = 'dday';
    recordEvent(G, 'help', { who: 'seoyeon', kind: 'ask', what: '어떻게 도울지 먼저 묻고, 계단에서 보행기를 옆에서 들어 주었다' });
    remember(G, 'hall_checked'); recordEvent(G, 'elder_choice', { pick: 'help' });
    for (const id of ['manager_detour', 'register_helped', 'shelter_water_given']) remember(G, id);
    add('db-remembered', G, 'record');
  }
  { const G = fresh(1); G.know.path = 'stay'; recordEvent(G, 'path', { path: 'stay' }); add('db-fate-stay', G, 'record'); }
  { // tests/r6-fix4-ledger.test.mjs S3: a ledger that cooked and fetched marks the stove and jug used
    const G = createGame({ seed: 8 }); G.money = 1e7; G.day = 3; G.minute = 10 * 60;
    st.buy(G, { gas_stove: 1, butane: 1, water_jug: 1, ramen: 10 }, prices);
    add('db-ledger-tools-used', ledger(G, { water: 2, meals: 3, power: 'phone' }, 5), 'record');
  }
  {
    const G = fresh(4); G.chapter = 'evac'; recordEvent(G, 'smoke_choice', { pick: 'stay' }); dr.recordEncounter(G, 'extinguisher', 'retreat-ok');
    dr.recordEncounter(G, 'extinguisher', { result: 'incomplete', correct: false, firstError: 'aim' }); dr.recordEncounter(G, 'radio', 'tuned');
    add('db-smoke-stay', G, 'somefails');
    const R = fresh(4); R.chapter = 'evac'; recordEvent(R, 'smoke_choice', { pick: 'run' }); dr.recordEncounter(R, 'extinguisher', 'risky'); add('db-smoke-run', R);
  }
  {
    // a finished, recorded run: the share text and the run block of the record
    const G = timed(run('stay', ['blackout_radio', 'rumor_checked', 'seoyeon_contact']), 61000, 1);
    recordEvent(G, 'run_recorded', { done: ['info_first', 'no_hoard', 'hands_off'], fresh: ['info_first'], bests: ['dday'], rank: 2 });
    add('db-run-recorded', G, 'record');
    const U = run('bus', ['mart_share']); recordEvent(U, 'run_recorded', { done: [], fresh: [], bests: [], rank: null }); add('db-run-untimed', U, 'record');
  }

  // ---------- random runs: everything mixed ----------
  const CH_DAYS = { prologue: [-3, -3], prep: [-3, -1], dday: [0, 0], endure: [1, 6], crossroads: [7, 11], evac: [12, 12] };
  const CH_ORDER = Object.keys(CH_DAYS);
  const WHATS = ['계단에서 보행기를 옆에서 들어 주었다', '어떻게 도울지 먼저 묻고, 계단에서 보행기를 옆에서 들어 주었다', '라디오 메모와 급수소 소식을 서로 나누기로 했다', '떠나기 전 서로의 연락처를 다시 확인했다',
    '정민의 말을 듣고 304호 어르신을 모시고 내려왔다', '보조배터리를 나눠 썼다', '물 한 병을 건넸다', '도왔다'];
  const RESULTS = ['out', 'retreat-ok', 'escalated', 'incomplete', 'risky', 'practice', 'tuned', 'opened', 'missed', 'complete', 'partial', 'planned', 'written', 'skipped', 'sorted', 'misled',
    'doubted', 'checked', 'whatever', { result: 'out', correct: true, firstError: null }, { result: 'incomplete', correct: false, firstError: 'squeeze' }, { result: 'retreat-ok', correct: false, firstError: 'retreat' },
    { result: 'escalated', correct: false, firstError: 'retreat' }, { ok: true }, { ok: false }, null];
  const KNOWS = ['shelterLocated', 'entrancesChecked', 'routeChecked', 'extinguisherLocated', 'radioChecked', 'contactsCard', 'docsCopied', 'meetPoint', 'waterPoint'];
  const PATHS = ['bus', 'walk', 'seoyeon', 'missed', 'stay', 'volunteer'];
  for (let i = 0; i < N_RANDOM; i++) {
    const r = rng(7000 + i);
    const int = (n) => Math.floor(r() * n), pick = (list) => list[int(list.length)], chance = (p) => r() < p;
    const G = createGame({ seed: 500 + i }); G.money = 1e7;
    if (chance(0.3)) for (const id of Object.keys(G.items)) G.items[id].home = 0;
    if (chance(0.4)) st.buy(G, { water_2l: 2 + int(6), bars: int(5) + 1, canned: int(4) + 1, water_500: int(6) + 1 }, prices);
    const steps = [];
    const at = (ch, fn) => steps.push([ch, fn, r()]);
    const density = 0.08 + 0.55 * (i % 6) / 5;
    for (const id of Object.keys(DEEDS)) {
      if (!chance(density)) continue;
      at(DEEDS[id].ch, (G) => {
        if (DEEDS[id].src === 'new') remember(G, id, id === 'rumor_corrected' ? { after: chance(0.5) ? 'check' : 'spread' } : {});
        else if (id === 'signals_clear') for (let k = 0; k < 4; k++) recordEvent(G, 'signal_sorted', { id: `s${k}`, ok: true });
        else for (const [type, d, ch] of RAW[id]) { if (ch) G.chapter = ch; recordEvent(G, type, structuredClone(d)); }
      });
    }
    for (let k = int(4); k > 0; k--) at(pick(['dday', 'endure', 'crossroads']), (G) => recordEvent(G, 'violence', { how: pick(['punch', 'kick']), ko: chance(0.4) }));
    for (let k = int(4); k > 0; k--) at(pick(CH_ORDER), (G) => {
      const d = { who: pick(['seoyeon', 'manager', 'clerk', 'elder', 'stranger', 'trader']), what: pick(WHATS) };
      if (chance(0.3)) d.kind = pick(['ask', 'carry', 'water', 'powerbank', 'memo', 'card', 'wait']);
      if (chance(0.2)) d.by = 'other';
      recordEvent(G, 'help', d);
    });
    for (let k = int(4); k > 0; k--) at('prologue', (G) => recordEvent(G, 'signal_sorted', { id: `x${k}`, ok: chance(0.6) }));
    if (chance(0.5)) at('crossroads', (G) => recordEvent(G, 'decision', { stayMove: pick(['stay', 'move']) }));
    if (chance(0.3)) G.know.stayMove = pick(['stay', 'move']);
    for (let k = int(5); k > 0; k--) at(pick(CH_ORDER), (G) => dr.recordEncounter(G, pick(['radio', 'bag', 'route', 'extinguisher', 'extinguisher', 'docs', 'info']), structuredClone(pick(RESULTS))));
    if (chance(0.3)) at(pick(['crossroads', 'evac']), (G) => recordEvent(G, 'smoke_choice', { pick: pick(['stay', 'run']) }));
    // preparations and their payoffs
    if (chance(0.6)) at('prep', (G) => { st.buy(G, { batteries_aa: 2, radio: 1, powerbank: 1, water_jug: chance(0.5) ? 1 : 0, cash: chance(0.5) ? 1 : 0 }, prices); });
    for (const id of ['flashlight', 'radio', 'powerbank']) if (chance(0.5)) at('prep', (G) => st.check(G, id));
    for (const type of ['bag_packed', 'shelter_checked', 'tub_filled', 'cash', 'home_ready', 'gas_checked']) if (chance(0.35)) at('prep', (G) => recordEvent(G, type, {}));
    for (let k = int(3); k > 0; k--) at('prep', (G) => st.spendSlot(G, pick(['mart', 'bag', 'home', 'route']), 1 + int(2)));
    for (let k = int(3); k > 0; k--) at(pick(['prep', 'endure']), (G) => st.buy(G, { [pick(['water_500', 'bars', 'ramen', 'tape', 'candles', 'gas_stove', 'butane', 'blackout_curtain', 'whistle'])]: 1 + int(3) }, prices));
    for (let k = int(4); k > 0; k--) at(pick(CH_ORDER), (G) => st.note(G, pick(['급수차 9시', '내일 물이 끊긴대', '동쪽 출입구 닫힘', '라디오 89.1MHz 재난방송', '버스 집결지 변경']), pick(['official', 'site', 'neighbor', 'rumor', 'self'])));
    for (let k = int(5); k > 0; k--) at(pick(['dday', 'endure', 'crossroads', 'evac']), (G) => {
      const d = { prep: pick([...Object.keys(PREP), 'mystery']) };
      if (chance(0.85)) d.moment = pick(['현관에 둔 배낭을 메고 바로 나섰다', '안내를 바로 들었다', '세수했다', '순간']);
      recordEvent(G, 'payoff', d);
    });
    if (chance(0.3)) at('endure', (G) => st.trade(G, { ramen: 1 }, { batteries_old: 1 }, pick(['trader', 'clerk', '지나가던 사람'])));
    if (chance(0.25)) at('dday', (G) => recordEvent(G, 'towel_wet', { with: 'tub' }));
    if (chance(0.4)) at('dday', (G) => recordEvent(G, 'departure', { bag: pick(['packed', 'none', 'late']) }));
    const plan = () => ({ water: pick([1, 2, 3]), meals: pick([1, 2, 3]), power: pick(['phone', 'powerbank', 'radio']) });
    if (chance(0.5)) at('endure', (G) => ledger(G, plan(), 1 + int(5), { place: pick(['home', 'home', 'shelter']) }));
    if (chance(0.25)) at('crossroads', (G) => ledger(G, plan(), 1 + int(3)));
    if (chance(0.2)) at('crossroads', (G) => st.drainCharge(G, pick(['radio', 'flashlight']), 100));
    if (chance(0.5)) at('prep', (G) => { for (const id of ['water_2l', 'bars', 'meds', 'flashlight', 'water_500', 'clothes']) if (chance(0.6) && G.items[id]?.home > 0) st.pack(G, id, 1 + int(G.items[id].home)); });
    if (chance(0.4)) at(pick(['endure', 'crossroads']), (G) => { for (const id of ['water_500', 'bars', 'tape', 'candles']) if (G.items[id]?.home > 0 && chance(0.5)) st.consume(G, id, 1); });
    const timing = pick(['none', 'all', 'all', 'some', 'partial']);
    if (timing !== 'none') for (const id of TIMED) {
      if (timing === 'some' && chance(0.4)) continue;
      at(id, (G) => recordEvent(G, 'chapter_time', { id, ms: 20000 + int(900000), retries: chance(0.3) ? int(3) : 0, ...(timing === 'partial' && chance(0.3) ? { partial: true } : {}) }));
    }
    // play the steps chapter by chapter on a clock that only moves forward
    for (const ch of CH_ORDER) {
      const mine = steps.filter((s) => s[0] === ch).sort((a, b) => a[2] - b[2]);
      const [lo, hi] = CH_DAYS[ch];
      let t = lo * 1440 + 6 * 60;
      const span = Math.max(1, Math.floor(((hi + 1) * 1440 - 60 - t) / (mine.length + 1)));
      for (const [, fn] of mine) {
        t += 1 + int(span);
        G.chapter = ch; G.day = Math.floor(t / 1440); G.minute = t - G.day * 1440;
        try { fn(G); } catch { /* not possible in this state: skipped */ }
      }
    }
    for (const k of KNOWS) if (chance(0.5)) G.know[k] = k === 'meetPoint' ? '놀이터' : true;
    for (const k of ['curtain', 'bagPacked', 'gateClosedSeen']) if (chance(0.4)) G.know[k] = true;
    for (const p of ['manager', 'clerk', 'seoyeon']) if (chance(0.4)) G.rel[p].contact = pick(['sms', 'door', 'both', 'office', 'store', 'pigeon']);
    if (chance(0.2)) G.injury = chance(0.8) ? ['leg'] : ['arm'];
    if (chance(0.35)) G.stats.health = 10 + int(45);
    if (chance(0.5)) { G.stats.thirst = int(101); G.stats.fatigue = int(101); G.stats.tension = int(101); }
    const path = pick([...PATHS, ...PATHS, null, 'teleport']);
    if (path) {
      G.chapter = 'evac'; G.day = 12; G.minute = 7 * 60 + int(600);
      if (chance(0.85)) recordEvent(G, 'path', { path: PATHS.includes(path) ? path : 'bus' });
      if (chance(0.9)) G.know.path = path;
      if (chance(0.3)) { G.minute += 5; try { remember(G, pick(['stock_to_manager', 'bus_helped', 'driver_text'])); } catch { /* done before */ } }
    }
    if (chance(0.4)) {
      const done = CHALLENGES.map((c) => c.id).filter(() => chance(0.4));
      recordEvent(G, 'run_recorded', { done, fresh: done.filter(() => chance(0.5)), bests: TIMED.filter(() => chance(0.3)), rank: pick([1, 2, 5, null]) });
    }
    add(`rnd:${i}`, G, 'somefails record');
  }

  const names = new Set(runs.map((x) => x.name));
  if (names.size !== runs.length) throw new Error('run names must be unique');
  return runs;
}

if (isMain(import.meta)) {
  const runs = await buildRuns();
  const { save } = await lib('dist/game/state.js');
  console.log(write('golden-runs.json', { runs: runs.map(({ name, G, tags }) => ({ name, tags, save: JSON.parse(save(G)) })) }), `(${runs.length} runs)`);
}
