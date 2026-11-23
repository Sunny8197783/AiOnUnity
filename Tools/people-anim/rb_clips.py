"""Merge chosen Rocketbox mocap clips into one lean FBX (armature only, body bones, canonical Humanoid hierarchy).
blender -b --factory-startup -P rb_clips.py -- <spec.json> <out.fbx> <report.json>
spec: {"src": <folder with static/ and xy/ subfolders>, "clips": [[take, file, kind("static"|"xy"), arm_from_file_or_null], ...]}
- thighs re-parented to Pelvis, clavicles to Spine2 (world motion kept exactly: per-frame bake);
- face, nub and helper bones dropped; character turned to face Blender +Y (Unity +Z with this export);
- static clips centred on their mean pelvis xy, xy clips start with the pelvis over the origin;
- arm_from: the right arm (clavicle..fingers) taken from that clip (parent-relative), e.g. walk + hold-bag arm = carry.
"""
import bpy, sys, os, json, math
from mathutils import Matrix, Vector, Quaternion

argv = sys.argv[sys.argv.index('--') + 1:]
spec = json.load(open(argv[0], encoding='utf-8'))
dst, rep = os.path.abspath(argv[1]), os.path.abspath(argv[2])
SRC = spec['src']
info = {'clips': []}

BODY = ['Pelvis', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head']
for s in 'LR':
    BODY += [f'{s} Clavicle', f'{s} UpperArm', f'{s} Forearm', f'{s} Hand', f'{s} Thigh', f'{s} Calf', f'{s} Foot', f'{s} Toe0']
    for f in range(5):
        BODY += [f'{s} Finger{f}', f'{s} Finger{f}1', f'{s} Finger{f}2']
KEEP = ['Bip01 ' + b for b in BODY]
PARENT_FIX = {'Bip01 L Thigh': 'Bip01 Pelvis', 'Bip01 R Thigh': 'Bip01 Pelvis',
              'Bip01 L Clavicle': 'Bip01 Spine2', 'Bip01 R Clavicle': 'Bip01 Spine2'}
RARM = [b for b in KEEP if b.startswith('Bip01 R ') and ('Clavicle' in b or 'Arm' in b or 'Forearm' in b or 'Hand' in b or 'Finger' in b)]


def path(file, kind):
    sub = 'all_animations_max_motextr_static' if kind == 'static' else 'all_animations_max_motextr_xy'
    return os.path.join(SRC, sub, file)


def sample(file, kind):
    """World matrices (metres) of the kept bones on every frame, plus rest data and parents."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path(file, kind))
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    sc = bpy.context.scene
    act = arm.animation_data.action
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    names = [b for b in KEEP if b in arm.data.bones]
    frames = []
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        mw = arm.matrix_world
        frames.append({n: (mw @ arm.pose.bones[n].matrix).copy() for n in names})
    rest = {n: (arm.matrix_world @ arm.data.bones[n].matrix_local).copy() for n in names}
    length = {n: arm.data.bones[n].length * arm.matrix_world.to_scale()[0] for n in names}
    parents = {n: (arm.data.bones[n].parent.name if arm.data.bones[n].parent else None) for n in names}
    return {'frames': frames, 'rest': rest, 'len': length, 'parents': parents, 'names': names, 'fps': sc.render.fps}


def strip_scale(m):
    loc, rot, _ = m.decompose()
    return Matrix.Translation(loc) @ rot.to_matrix().to_4x4()


def frame_of(d, kind):
    """Turn (about Z) and shift that put the character facing -Y with the pelvis over the origin."""
    fr = d['frames']
    first = fr[0]
    if kind == 'xy':  # travel direction
        F = fr[-1]['Bip01 Pelvis'].translation - first['Bip01 Pelvis'].translation
        F.z = 0
    else:  # mean facing of the hips over the clip
        L = Vector((0, 0, 0))
        for f in fr: L += f['Bip01 L Thigh'].translation - f['Bip01 R Thigh'].translation
        L.z = 0
        F = L.cross(Vector((0, 0, 1)))  # forward = left x up
    ang = math.atan2(F.y, F.x)
    turn = Matrix.Rotation(math.pi / 2 - ang, 4, 'Z')  # F -> +Y (comes into Unity as +Z with this export)
    if kind == 'static':
        c = Vector((0, 0, 0))
        for f in fr: c += turn @ f['Bip01 Pelvis'].translation
        c /= len(fr)
    else:
        c = turn @ first['Bip01 Pelvis'].translation
    c.z = 0
    return Matrix.Translation(-c) @ turn, math.degrees(math.pi / 2 - ang)


def loop_cut(d, lo, hi):
    """Shortens a long static clip to the frame in [lo, hi) whose pose is closest to the first (a clean loop)."""
    fr = d['frames']
    n = len(fr)
    if n <= hi: return n
    keys = ['Bip01 Head', 'Bip01 L Hand', 'Bip01 R Hand', 'Bip01 L Foot', 'Bip01 R Foot', 'Bip01 Pelvis', 'Bip01 L Forearm', 'Bip01 R Forearm']
    base = fr[0]
    best, bj = 1e9, hi
    for j in range(lo, hi):
        e = sum((fr[j][k].translation - base[k].translation).length for k in keys if k in base)
        if e < best: best, bj = e, j
    d['frames'] = fr[:bj]
    return bj


clips = []
master = None
MAXF = int(spec.get('max_frames', 0))
FPS = int(spec.get('fps', 30))
for take, file, kind, arm_from in spec['clips']:
    d = sample(file, kind)
    n0 = len(d['frames'])
    if MAXF and kind == 'static' and take not in ('wave', 'knock'): loop_cut(d, int(MAXF * 0.6), MAXF)
    if FPS != 30: d['frames'] = d['frames'][::int(round(30 / FPS))]
    M, turn_deg = frame_of(d, kind)
    d['frames'] = [{n: strip_scale(M @ m) for n, m in f.items()} for f in d['frames']]
    if master is None:
        rest = {n: strip_scale(M @ m) for n, m in d['rest'].items()}
        pc = rest['Bip01 Pelvis'].translation.copy(); pc.z = 0  # rest pose centred over the origin
        rest = {n: Matrix.Translation(-pc) @ m for n, m in rest.items()}
        master = {'rest': rest, 'len': d['len'], 'names': d['names']}
    arm_d = None
    if arm_from:
        a = sample(arm_from, 'static')
        Ma, _ = frame_of(a, 'static')
        a['frames'] = [{n: strip_scale(Ma @ m) for n, m in f.items()} for f in a['frames']][::int(round(30 / FPS))]
        arm_d = a
    clips.append((take, kind, d, arm_d))
    info['clips'].append({'take': take, 'file': file, 'frames': len(d['frames']), 'src_frames': n0, 'turn': round(turn_deg, 1), 'arm_from': arm_from})

# ---- the master armature: rest from the first clip, canonical hierarchy, metres, object at identity
bpy.ops.wm.read_factory_settings(use_empty=True)
ad = bpy.data.armatures.new('Bip01')
ob = bpy.data.objects.new('Bip01', ad)
bpy.context.scene.collection.objects.link(ob)
bpy.context.view_layer.objects.active = ob
bpy.ops.object.mode_set(mode='EDIT')
names = master['names']
d0parents = clips[0][2]['parents']
for n in names:
    eb = ad.edit_bones.new(n)
    eb.head = (0, 0, 0); eb.tail = (0, max(master['len'][n], 0.02), 0)
    eb.matrix = master['rest'][n]
    eb.length = max(master['len'][n], 0.02)
for n in names:
    p = PARENT_FIX.get(n, d0parents[n])
    while p and p not in names: p = None  # parents outside the kept set: root
    if p: ad.edit_bones[n].parent = ad.edit_bones[p]; ad.edit_bones[n].use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')
R = {n: ad.bones[n].matrix_local.copy() for n in names}
P = {n: (ad.bones[n].parent.name if ad.bones[n].parent else None) for n in names}
order = [b.name for b in ad.bones]  # parents before children


def basis_frames(d):
    out = []
    for f in d['frames']:
        fb = {}
        for n in order:
            if n not in f: continue
            Mb = f[n]
            p = P[n]
            if p and p in f:
                local = (R[p].inverted() @ R[n]).inverted() @ f[p].inverted() @ Mb
            else:
                local = R[n].inverted() @ Mb
            fb[n] = local
        out.append(fb)
    return out


for pb in ob.pose.bones: pb.rotation_mode = 'QUATERNION'
ob.animation_data_create()
for take, kind, d, arm_d in clips:
    bf = basis_frames(d)
    if arm_d is not None:
        ab = basis_frames(arm_d)
        for i, fb in enumerate(bf):
            src = ab[i % len(ab)]
            for n in RARM:
                if n in src: fb[n] = src[n]
    act = bpy.data.actions.new(take)
    act.use_fake_user = True
    ob.animation_data.action = act
    prev = {}
    # keys: body every STEP frames (2 for standing clips, 1 for gaits), fingers every 2*STEP; only the pelvis keeps
    # translation (Humanoid ignores the others); linear keys so the exporter's simplify drops what lies on the line
    step = 1
    last = len(bf) - 1
    for i, fb in enumerate(bf):
        for n, m in fb.items():
            pb = ob.pose.bones[n]
            loc, rot, _ = m.decompose()
            if n in prev: rot.make_compatible(prev[n])
            prev[n] = rot
            st = step * 2 if 'Finger' in n else step
            if i % st and i != last: continue
            pb.location = loc; pb.rotation_quaternion = rot; pb.scale = (1, 1, 1)
            if n == 'Bip01 Pelvis' or i == 0: pb.keyframe_insert('location', frame=i + 1, group=n)
            pb.keyframe_insert('rotation_quaternion', frame=i + 1, group=n)
    for layer in act.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in cb.fcurves:
                    for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
    act.frame_range = (1, len(bf))
    act.use_frame_range = True

sc = bpy.context.scene
sc.render.fps = FPS
sc.frame_start = 1; sc.frame_end = max(len(c[2]['frames']) for c in clips)
# a first take "_rest" holding the rest pose: FBX readers (Unity) pose the model at time 0 of the first take
rest_act = bpy.data.actions.new('_rest'); rest_act.use_fake_user = True
ob.animation_data.action = rest_act
for pb in ob.pose.bones:
    pb.rotation_mode = 'QUATERNION'
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    for fr in (1, 2):
        pb.keyframe_insert('location', frame=fr); pb.keyframe_insert('rotation_quaternion', frame=fr)
ob.animation_data.action = None  # default node transforms = the rest pose (Unity builds the avatar from them)
for pb in ob.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
bpy.context.view_layer.update()
# a second top-level node, so the FBX reader makes its own root at identity with the armature under it (as the avatars)
ground = bpy.data.objects.new('Ground', None)
bpy.context.scene.collection.objects.link(ground)
for o2 in bpy.data.objects: o2.select_set(o2 in (ob, ground))
r = bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={'ARMATURE', 'EMPTY'}, add_leaf_bones=False,
    use_armature_deform_only=False, armature_nodetype='NULL', apply_scale_options='FBX_SCALE_ALL', apply_unit_scale=True,
    bake_space_transform=False, axis_forward='-Z', axis_up='Y', primary_bone_axis='Y', secondary_bone_axis='X',
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_use_all_bones=True,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=float(spec.get('simplify', 1.0)), path_mode='STRIP')
info['export'] = list(r)
info['bytes'] = os.path.getsize(dst)
info['bones'] = len(names)
json.dump(info, open(rep, 'w'), indent=1)
print('@@OK', dst, info['bytes'])
