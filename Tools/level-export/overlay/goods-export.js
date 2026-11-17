// Exports the shop shelf goods of mart and store (instanced in the original, labels from one atlas) as data:
// <id>-goods.json (shapes, batches of instances per section and full/out state) and <id>-goods-atlas.png / <id>-goods-tag.png.
// Sections are found by asking the level to sell out one section at a time and watching which groups switch.
import * as THREE from 'three';
import { createKit } from './engine/kit.js';

const log = document.getElementById('log');
const say = (t) => { log.textContent += t + '\n'; };
const save = async (name, body) => { const r = await fetch('/__save/' + name, { method: 'PUT', body }); if (!r.ok) throw new Error(`save ${name}: ${r.status}`); };
const LEVELS = { mart: ['buildMart', ['water', 'food', 'daily', 'elec', 'health']], store: ['buildStore', ['water', 'batteries', 'food']] };
const r4 = (v) => Math.round(v * 10000) / 10000;

const renderer = new THREE.WebGLRenderer({ canvas: document.createElement('canvas') });
const kit = createKit(renderer, { quality: 'high' });
const people = await import('./engine/people.js');
const crowd = await import('./engine/crowd.js');
const fx = (await import('./engine/fx.js')).createFx(THREE, kit, { reduceMotion: true, renderer });

async function png(tex) {
  const img = tex?.image; if (!img) return null;
  let c = img;
  if (!(img instanceof HTMLCanvasElement) && !(img instanceof OffscreenCanvas)) { c = document.createElement('canvas'); c.width = img.width; c.height = img.height; c.getContext('2d').drawImage(img, 0, 0); }
  if (c instanceof OffscreenCanvas) return await c.convertToBlob({ type: 'image/png' });
  return await new Promise((res) => c.toBlob(res, 'image/png'));
}
function geomData(g) {
  const p = g.attributes.position, n = g.attributes.normal, uv = g.attributes.uv;
  return {
    positions: Array.from(p.array, r4), normals: n ? Array.from(n.array, r4) : null, uvs: uv ? Array.from(uv.array, r4) : null,
    indices: g.index ? Array.from(g.index.array) : null,
  };
}

for (const [id, [fn, secs]] of Object.entries(LEVELS)) {
  try {
    const mod = await import(`./levels/${id}.js`);
    const L = await mod[fn](kit, { ...people, createCrowd: crowd.createCrowd }, fx);
    L.group.updateWorldMatrix(true, true);
    const goods = L.group.getObjectByName('goods');
    if (!goods) throw new Error('no goods group');
    const kids = goods.children;
    const secOf = new Map();
    for (const sec of secs) {
      L.setState('soldOut', []);
      const before = kids.map((k) => k.visible);
      L.setState('soldOut', [sec]);
      kids.forEach((k, i) => { if (k.visible !== before[i]) secOf.set(k, sec); });
    }
    L.setState('soldOut', []);
    const shapes = {}, batches = [];
    let atlasTex = null, tagTex = null, nInst = 0, geomId = new Map();
    for (const k of kids) {
      if (!k.isGroup) continue;
      const sec = secOf.get(k) ?? 'misc', state = k.visible ? 'full' : 'out';
      k.traverse((o) => {
        if (!o.isInstancedMesh) return;
        const mat = Array.isArray(o.material) ? o.material[0] : o.material;
        const isTag = !o.geometry.attributes.aCell;
        if (isTag) tagTex ||= mat.map; else atlasTex ||= mat.map;
        let gid = geomId.get(o.geometry.uuid);
        if (!gid) { gid = 'g' + geomId.size; geomId.set(o.geometry.uuid, gid); shapes[gid] = geomData(o.geometry); }
        const cells = o.geometry.attributes.aCell, m = new THREE.Matrix4(), w = new THREE.Matrix4(), c = new THREE.Color(), inst = [];
        for (let i = 0; i < o.count; i++) {
          o.getMatrixAt(i, m); w.multiplyMatrices(o.matrixWorld, m);
          const tint = o.instanceColor ? (o.getColorAt(i, c), c.r) : 1;
          inst.push([...Array.from(w.elements, r4), r4(tint), cells ? r4(cells.getX(i)) : 0, cells ? r4(cells.getY(i)) : 0]);
        }
        nInst += inst.length;
        batches.push({ section: sec, state, shape: gid, tag: isTag, roughness: r4(mat.roughness), metalness: r4(mat.metalness), instances: inst });
      });
    }
    const atlas = await png(atlasTex), tag = await png(tagTex);
    if (atlas) await save(`${id}-goods-atlas.png`, atlas);
    if (tag) await save(`${id}-goods-tag.png`, tag);
    // atlas cell scale (vMapUv * scale + aCell): GRID 8, pad 3 px of the atlas size
    const S = atlasTex?.image?.width ?? 2048;
    await save(`${id}-goods.json`, JSON.stringify({ id, cellScale: r4(1 / 8 - 2 * 3 / S), atlasSize: S, sections: [...new Set(batches.map((b) => b.section))], shapes, batches }));
    say(`${id}: ${batches.length} batches, ${nInst} instances, ${Object.keys(shapes).length} shapes, sections ${[...new Set(batches.map((b) => b.section + ':' + b.state))].join(' ')}`);
  } catch (e) { say(`${id}: FAILED ${e?.stack || e}`); }
}
say('EXPORT DONE');
