// Standalone level exporter: builds each level with the game's own kit and builders (no engine loop, so it also
// runs in a hidden tab), then saves <id>-level.json and <id>.glb through the local export server.
// ?levels=apartment,mart,...  (default: all six)
import * as THREE from 'three';
import { GLTFExporter } from './vendor/addons/exporters/GLTFExporter.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { createKit } from './engine/kit.js';
import { PRESETS } from './engine/presets.js';
import { LEVEL_DEFAULTS } from './story/runtime.js';

const REGISTRY = { apartment: 'buildApartment', mart: 'buildMart', store: 'buildStore', office: 'buildOffice', shelter: 'buildShelter', town: 'buildTown' };
const QS = new URLSearchParams(location.search);
const ids = (QS.get('levels') || Object.keys(REGISTRY).join(',')).split(',').filter((id) => REGISTRY[id]);
const log = document.getElementById('log');
const say = (t) => { log.textContent += t + '\n'; };
const r3 = (v) => Math.round(v * 1000) / 1000;
const plain = (o) => JSON.parse(JSON.stringify(o, (k, v) => typeof v === 'function' || (v && v.isObject3D) ? undefined : typeof v === 'number' ? r3(v) : v));
const save = async (name, body) => { const r = await fetch('/__save/' + name, { method: 'PUT', body }); if (!r.ok) throw new Error(`save ${name}: ${r.status}`); };
const excluded = (o) => !o.visible || o.isLight || o.isSprite || o.isSkinnedMesh || o.isBone || o.isPoints || o.isLine || o.isCamera
  || /^person:|^fx-|^crowd/.test(o.name) || (o.material && !Array.isArray(o.material) && (o.material.isShaderMaterial || o.material.type === 'ShaderMaterial'));

const renderer = new THREE.WebGLRenderer({ canvas: document.createElement('canvas'), antialias: false });
renderer.setSize(64, 64);
const kit = createKit(renderer, { quality: 'high' });
const people = await import('./engine/people.js');
const crowd = await import('./engine/crowd.js');
const fxMod = await import('./engine/fx.js');
const fx = fxMod.createFx(THREE, kit, { reduceMotion: true, renderer });
const peopleApi = { ...people, createCrowd: crowd.createCrowd };
await save('presets.json', JSON.stringify(plain(PRESETS)));

for (const id of ids) {
  try {
    const t0 = performance.now();
    const mod = await import(`./levels/${id}.js`);
    const L = await mod[REGISTRY[id]](kit, peopleApi, fx);
    L.id ??= id;
    L.group.updateWorldMatrix(true, true);

    // ---- lit groups: which emissive materials and lights each kit.setLit group drives (street, windows, shops,
    // signs, indoor, emergency, yard). Found by switching each group off and seeing what goes dark; the material is
    // renamed 'lit:<group>' (plus ':dim=<unlitColor>' for backlit panels whose colour also darkens), which the glTF
    // keeps, so Unity can dim them with the preset's lit levels and the grid power like the original's core.js.
    const GROUPS = ['street', 'windows', 'shops', 'signs', 'indoor', 'emergency', 'yard'];
    const litMats = new Set(), litLights = [];
    L.group.traverse((o) => {
      if (o.isLight) litLights.push(o);
      if (o.material) for (const m of [o.material].flat()) if (m && typeof m.emissiveIntensity === 'number') litMats.add(m);
    });
    const matGroup = new Map(), lightGroup = new Map();
    for (const g of GROUPS) {
      const mb = new Map([...litMats].map((m) => [m, m.emissiveIntensity])), lb = new Map(litLights.map((l) => [l, l.intensity]));
      kit.setLit(g, false);
      for (const m of litMats) if (mb.get(m) > 0 && m.emissiveIntensity === 0 && !matGroup.has(m)) matGroup.set(m, g);
      for (const l of litLights) if (lb.get(l) > 0 && l.intensity === 0 && !lightGroup.has(l)) lightGroup.set(l, g);
      kit.setLit(g, true, 1);
    }
    const litCount = {};
    for (const [m, g] of matGroup) {
      const dim = m.userData?.unlitColor;
      m.name = `lit:${g}` + (typeof dim === 'number' ? `:dim=${r3(dim)}` : '');
      litCount[g] = (litCount[g] || 0) + 1;
    }
    say(`  ${id}: lit materials ${JSON.stringify(litCount)}, lit lights ${lightGroup.size}/${litLights.length}`);

    const lights = [];
    L.group.traverse((o) => {
      if (!o.isLight) return;
      const p = o.getWorldPosition(new THREE.Vector3());
      lights.push({ type: o.type, x: r3(p.x), y: r3(p.y), z: r3(p.z), color: '#' + o.color.getHexString(), intensity: r3(o.intensity), distance: r3(o.distance ?? 0), visible: o.visible, group: lightGroup.get(o) ?? null });
    });
    const npcs = {};
    for (const [k, n] of Object.entries(L.npcs || {})) {
      const g = n?.group; if (!g) continue;
      npcs[k] = { x: r3(g.position.x), y: r3(g.position.y), z: r3(g.position.z), yaw: r3(g.rotation.y), visible: g.visible, preset: n.preset ?? null };
    }
    const data = {
      id, env: L.env ?? null,
      frame: 'three.js: x east, y up, z south, metres; yaw 0 faces north (-z), positive yaw turns left',
      walk: plain(L.walk || []), solids: plain(L.solids || []), ceilings: plain(L.ceilings || []), spawns: plain(L.spawns || {}),
      points: plain(L.points || []), anchors: plain(L.anchors || {}), states: plain(L.states || {}), npcs, lights,
    };
    await save(`${id}-level.json`, JSON.stringify(data));

    // ---- state variants: which objects show or hide for each level state value ----
    // Stable unique names first, on the original objects, so the clone and the GLB carry them.
    let idx = 0;
    L.group.traverse((o) => { o.name = `${o.name || o.type}~${idx++}`; });
    const snap = () => { const m = new Map(); L.group.traverse((o) => m.set(o.name, o.visible)); return m; };
    const defaults = { ...(LEVEL_DEFAULTS[id] || {}), ...(L.defaults || {}) };
    const ARRAY_KEYS = new Set(['closures', 'ways', 'soldOut']);
    const probes = [];
    for (const [key, opts] of Object.entries(L.states || {})) {
      if (key === 'soldOut' || key === 'crowd' || key === 'stock' && id !== 'apartment') continue; // goods: ShopGoods; crowds: people
      if (Array.isArray(opts)) {
        const vals = [];
        for (const o of opts) {
          if (ARRAY_KEYS.has(key)) { if (Array.isArray(o)) o.forEach((e) => vals.push([e])); else vals.push([o]); }
          else vals.push(o);
        }
        for (const v of vals) probes.push([key, v]);
      } else if (opts && typeof opts === 'object') {
        for (const [sub, list] of Object.entries(opts)) for (const v of list) probes.push([key, { [sub]: v }]);
      }
    }
    const reset = (key) => { if (key in defaults) { try { L.setState(key, defaults[key]); } catch { /* ignore */ } } };
    for (const key of Object.keys(defaults)) reset(key);
    L.update?.(0.016, 0, {});
    const base = snap();
    // Interaction points a state enables, disables or relabels (the water point with waterPoint, a closure's guide,
    // the 304 door, the store counter, ...): recorded like the variants, applied by Unity's LevelPointStates.
    const psnap = () => new Map((L.points || []).map((p) => [p.id, { enabled: p.enabled !== false, label: p.label ?? '' }]));
    const pbase = psnap();
    const pointRules = {}; // point id -> { def: {enabled, label}, rules: [{ key, value, enabled?, label? }] }

    // Materials that change with a state (the apartment's fixtures with 'power', the window view's colours and its
    // redrawn canvas pictures with 'windowView', ...): every material gets a stable unique name '<tag>~m<n>' (tag =
    // 'lit:<group>' from above, else the old name) and each probe records how its colour, emission and opacity differ
    // from the default state; a redrawn canvas map is saved as a PNG for that state.
    const matList = [];
    L.group.traverse((o) => { if (o.material && !excluded({ ...o, visible: true, isLight: o.isLight, isSprite: o.isSprite, material: o.material, name: o.name })) for (const m of [o.material].flat()) if (m && !matList.includes(m)) matList.push(m); }); // only what the model will hold
    matList.forEach((m, i) => { m.name = `${m.name || 'm'}~m${i}`; });
    const rgb = (c) => (c ? [c.r, c.g, c.b].map((v) => Math.round(v * 10000) / 10000) : null); // linear (three's working space)
    const msnap = (m) => ({
      c: rgb(m.color), e: m.emissive ? rgb(m.emissive.clone().multiplyScalar(m.emissiveIntensity ?? 1)) : null, o: r3(m.opacity ?? 1),
      mv: m.map?.version ?? null, ev: m.emissiveMap?.version ?? null,
    });
    const mbase = new Map(matList.map((m) => [m, msnap(m)]));
    const canvasHash = async (tex) => {
      const img = tex?.image; if (!img || !img.getContext) return null;
      const d = img.getContext('2d').getImageData(0, 0, img.width, img.height).data;
      const h = await crypto.subtle.digest('SHA-1', d); return [...new Uint8Array(h)].map((b) => b.toString(16).padStart(2, '0')).join('');
    };
    const matVariants = {}; // material name -> { def: {c, e, o}, rules: [{ key, value, c?, e?, o?, map?, emap? }] }
    const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
    let texFiles = 0;
    const versions = () => new Map(matList.map((m) => [m, { mv: m.map?.version ?? null, ev: m.emissiveMap?.version ?? null }]));
    async function probeMaterials(key, value, probeNo, pre) {
      const pend = [];
      for (const m of matList) {
        const b = { ...mbase.get(m), ...pre.get(m) }, n = msnap(m), rule = {}; // a picture counts only if this probe redrew it
        if (!same(n.c, b.c)) rule.c = n.c;
        if (!same(n.e, b.e)) rule.e = n.e;
        if (n.o !== b.o) rule.o = n.o;
        for (const [slot, ver, tex] of [['map', 'mv', m.map], ['emap', 'ev', m.emissiveMap]]) {
          if (n[ver] === b[ver] || !tex?.image?.getContext) continue;
          const h = await canvasHash(tex);
          pend.push({ m, tex, h, slot, rule });
          const file = `${id}-tex-m${matList.indexOf(m)}-${slot}-p${probeNo}.png`;
          const blob = await new Promise((res) => tex.image.toBlob(res, 'image/png'));
          rule[slot] = { file, hash: h, blob };
        }
        if (Object.keys(rule).length) pend.push({ m, rule, final: true });
      }
      return pend;
    }
    const variants = {}; // node name -> { def, rules: [{ key, value, visible }] }
    const everVisible = new Set([...base].filter(([, v]) => v).map(([n]) => n));
    let probeNo = 0;
    for (const [key, value] of probes) {
      const pre = versions();
      try { L.setState(key, value); } catch (e) { say(`  ${key}=${JSON.stringify(value)}: ${e.message}`); continue; }
      L.update?.(0.016, 0, {});
      const now = snap();
      for (const [n, v] of now) {
        if (v === base.get(n)) continue;
        (variants[n] ||= { def: !!base.get(n), rules: [] }).rules.push({ key, value, visible: v });
        if (v) everVisible.add(n);
      }
      for (const [pid, now] of psnap()) {
        const b = pbase.get(pid); if (!b) continue;
        const r = {};
        if (now.enabled !== b.enabled) r.enabled = now.enabled;
        if (now.label !== b.label) r.label = now.label;
        if (Object.keys(r).length) (pointRules[pid] ||= { def: b, rules: [] }).rules.push({ key, value, ...r });
      }
      const pend = await probeMaterials(key, value, probeNo++, pre);
      reset(key);
      L.update?.(0.016, 0, {});
      for (const p of pend) {
        if (!p.final) continue;
        const rule = p.rule;
        for (const slot of ['map', 'emap']) {
          if (!rule[slot]) continue;
          await save(rule[slot].file, rule[slot].blob); texFiles++;
          rule[slot] = rule[slot].file;
        }
        if (!Object.keys(rule).length) continue;
        const b = mbase.get(p.m);
        (matVariants[p.m.name] ||= { def: { c: b.c, e: b.e, o: b.o }, rules: [] }).rules.push({ key, value, ...rule });
      }
    }
    for (const key of Object.keys(defaults)) reset(key);
    // Export every object that shows in any state; the default visibility is in the variants table.
    const forceShow = [];
    L.group.traverse((o) => { if (!o.visible && everVisible.has(o.name) && !excluded({ ...o, visible: true })) { forceShow.push(o); o.visible = true; } });
    await save(`${id}-states.json`, JSON.stringify({ id, probes: probes.length, variants, materials: matVariants, points: pointRules }));
    say(`  ${id}: ${probes.length} state probes, ${Object.keys(variants).length} objects change with states, ${forceShow.length} hidden-by-default exported, ${Object.keys(matVariants).length} materials change with states (${texFiles} pictures), ${Object.keys(pointRules).length} points change with states`);

    const root = L.group.clone(true);
    root.name = 'level-' + id;
    const drop = [];
    root.traverse((o) => { if (o !== root && excluded(o)) drop.push(o); });
    for (const o of drop) o.parent?.remove(o);
    // Instanced meshes become plain merged meshes (instance transforms and colours baked in): simple for any importer,
    // and one static mesh per kind of thing instead of one object per instance.
    const inst = [];
    root.traverse((o) => { if (o.isInstancedMesh) inst.push(o); });
    let baked = 0;
    let keptInst = 0, keptCount = 0;
    for (const im of inst) {
      // Very large sets (the mart's shelves of goods) stay instanced: baked they would be hundreds of MB.
      // Shop goods pick their label from an atlas with a per-instance attribute (aCell) in a custom shader, in sets of
      // tens of thousands: they are left out here and exported separately as instance data.
      if (im.geometry.attributes.aCell || im.count * im.geometry.attributes.position.count > 150000) { keptInst++; keptCount += im.count; im.parent?.remove(im); continue; }
      const geos = [], m = new THREE.Matrix4(), c = new THREE.Color(), hasColor = !!im.instanceColor;
      for (let i = 0; i < im.count; i++) {
        im.getMatrixAt(i, m);
        const g = im.geometry.clone().applyMatrix4(m);
        if (hasColor) {
          im.getColorAt(i, c);
          const cnt = g.attributes.position.count, base = g.attributes.color, arr = new Float32Array(cnt * 3);
          for (let k = 0; k < cnt; k++) { arr[k * 3] = c.r * (base ? base.getX(k) : 1); arr[k * 3 + 1] = c.g * (base ? base.getY(k) : 1); arr[k * 3 + 2] = c.b * (base ? base.getZ(k) : 1); }
          g.setAttribute('color', new THREE.BufferAttribute(arr, 3));
        }
        geos.push(g);
      }
      const parent = im.parent;
      if (!geos.length) { parent?.remove(im); continue; }
      const merged = mergeGeometries(geos, false);
      if (!merged) { say(`  could not merge instanced ${im.name || '(unnamed)'}: kept out`); parent?.remove(im); continue; }
      let mat = Array.isArray(im.material) ? im.material[0] : im.material;
      if (hasColor) { mat = mat.clone(); mat.vertexColors = true; }
      const mesh = new THREE.Mesh(merged, mat);
      mesh.name = im.name; mesh.position.copy(im.position); mesh.quaternion.copy(im.quaternion); mesh.scale.copy(im.scale);
      parent?.add(mesh); parent?.remove(im); baked++;
    }
    let meshes = 0, n = 0;
    root.traverse((o) => { if (o.isMesh) meshes++; n++; });
    // Materials with state rules that will not be in the model: say where they live, so the gap is visible.
    const inModel = new Set();
    root.traverse((o) => { if (o.material && o.visible) for (const m of [o.material].flat()) if (m) inModel.add(m.name); });
    const missing = Object.keys(matVariants).filter((nm) => !inModel.has(nm));
    if (missing.length) {
      const owners = {};
      L.group.traverse((o) => {
        if (!o.material) return;
        for (const m of [o.material].flat()) if (m && missing.includes(m.name)) {
          let why = !o.visible ? 'hidden' : o.isInstancedMesh ? 'instanced' : excluded(o) ? 'excluded' : '?';
          let p = o.parent; while (p && p !== L.group) { if (!p.visible) { why = 'hidden parent ' + p.name; break; } p = p.parent; }
          (owners[m.name] ||= []).push(`${o.name}(${o.type}, ${why})`);
        }
      });
      say(`  ${id}: ${missing.length} state materials not in the model: ${JSON.stringify(owners).slice(0, 1500)}`);
    }
    const glb = await new GLTFExporter().parseAsync(root, { binary: true, onlyVisible: true, maxTextureSize: 1024 });
    await save(`${id}.glb`, glb);
    say(`${id}: ${data.walk.length} walk, ${data.solids.length} solids, ${data.points.length} points, ${meshes} meshes (${baked} baked from instances, ${keptInst} left out as goods = ${keptCount} instances), ${(glb.byteLength / 1048576).toFixed(1)} MB, ${((performance.now() - t0) / 1000).toFixed(1)} s`);
  } catch (e) {
    say(`${id}: FAILED ${e?.stack || e}`);
  }
}
say('EXPORT DONE');
