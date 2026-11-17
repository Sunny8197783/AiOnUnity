// Bakes the original's procedural people (engine/people.js) to glTF: one skinned mesh per preset with every looping
// animation and one-shot clip sampled at 30 fps into AnimationClips. Saves person-<preset>.glb through the export server.
import * as THREE from 'three';
import { GLTFExporter } from './vendor/addons/exporters/GLTFExporter.js';
import { createKit } from './engine/kit.js';
import { createPerson, ANIMS } from './engine/people.js';
import { CLIP_NAMES, clipInfo } from './engine/people-clips.js';

const log = document.getElementById('log');
const say = (t) => { log.textContent += t + '\n'; };
const save = async (name, body) => { const r = await fetch('/__save/' + name, { method: 'PUT', body }); if (!r.ok) throw new Error(`save ${name}: ${r.status}`); };
const QS = new URLSearchParams(location.search);
const FPS = 30, DT = 1 / FPS;
// Speeds for the gaits (m/s) as the original's blend space names them; other loops ignore speed.
const SPEED = { walk: 1.3, brisk: 2.1, run: 3.9, carry: 1.1, walker: 0.7, stairsUp: 0.9, stairsDown: 0.9 };
const PEOPLE = (QS.get('people') || 'jeongmin,seoyeon,manager,clerk,guide,coworker,elder,crowd:1,crowd:2,crowd:3,crowd:4,crowd:5,crowd:6').split(',');

const renderer = new THREE.WebGLRenderer({ canvas: document.createElement('canvas') });
createKit(renderer, { quality: 'high' }); // people share the kit's setup (materials)

function sample(person, bones, frames, begin, dt = DT) {
  const times = new Float32Array(frames), pos = bones.map(() => new Float32Array(frames * 3)), rot = bones.map(() => new Float32Array(frames * 4));
  begin?.();
  for (let f = 0; f < frames; f++) {
    if (f > 0) person.update(dt);
    times[f] = f * dt;
    bones.forEach((b, i) => { b.position.toArray(pos[i], f * 3); b.quaternion.toArray(rot[i], f * 4); });
  }
  return { times, pos, rot };
}
function clipOf(name, bones, s) {
  const tracks = [];
  bones.forEach((b, i) => {
    tracks.push(new THREE.QuaternionKeyframeTrack(`${b.name}.quaternion`, s.times, s.rot[i]));
    let moves = false; for (let k = 3; k < s.pos[i].length; k++) if (Math.abs(s.pos[i][k] - s.pos[i][k % 3]) > 1e-5) { moves = true; break; }
    if (moves || i === 0) tracks.push(new THREE.VectorKeyframeTrack(`${b.name}.position`, s.times, s.pos[i]));
  });
  return new THREE.AnimationClip(name, s.times[s.times.length - 1], tracks);
}

for (const entry of PEOPLE) {
  try {
    const [preset, seedStr] = entry.split(':'), seed = Number(seedStr) || 1;
    const person = createPerson({ preset, seed });
    const bones = person.mesh.skeleton.bones;
    const clips = [];
    for (const anim of ANIMS) {
      person.setAnim(anim, { instant: true, speed: SPEED[anim] ?? null });
      person.setSpeed(SPEED[anim] ?? null);
      for (let i = 0; i < 45; i++) person.update(DT); // settle fades and gait amplitude
      // The gait phase runs in radians (2*PI per cycle). Measure its rate, then sample exactly one cycle with a time
      // step that divides it evenly, so the loop has no seam. Loops without a gait (idle, sit, talk...) get 3 s.
      let frames = 90, dt = DT;
      const p0 = person.phase; person.update(DT);
      let dph = person.phase - p0; if (dph < 0) dph += Math.PI * 200;
      if (dph > 1e-4) {
        const period = 2 * Math.PI / (dph / DT);
        frames = Math.max(8, Math.round(period / DT));
        dt = period / frames;
        frames += 1; // first and last frame are the same pose
      }
      clips.push(clipOf(anim, bones, sample(person, bones, frames, null, dt)));
    }
    for (const name of CLIP_NAMES) {
      const dur = clipInfo(name).dur;
      person.setAnim('idle', { instant: true }); person.setSpeed(null);
      for (let i = 0; i < 30; i++) person.update(DT);
      const frames = Math.ceil(dur * FPS) + 2;
      clips.push(clipOf('clip_' + name, bones, sample(person, bones, frames, () => person.play(name))));
    }
    person.setAnim('idle', { instant: true }); person.setSpeed(null);
    for (let i = 0; i < 30; i++) person.update(DT);
    const name = seedStr ? `${preset}${seed}` : preset;
    person.group.name = 'person-' + name;
    const glb = await new GLTFExporter().parseAsync(person.group, { binary: true, animations: clips, onlyVisible: true });
    await save(`person-${name}.glb`, glb);
    say(`${name}: ${bones.length} bones, ${clips.length} clips, height ${person.height.toFixed(2)} m, ${(glb.byteLength / 1024).toFixed(0)} KB`);
  } catch (e) {
    say(`${entry}: FAILED ${e?.stack || e}`);
  }
}
say('EXPORT DONE');
