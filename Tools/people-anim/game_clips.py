"""The game's own clips of one baked person, as an armature-only FBX for Humanoid retargeting.
blender -b --factory-startup -P game_clips.py -- <person.glb> <out.fbx> <report.json> <take,take,...> [simplify]
Keeps only the listed takes, drops the mesh and the glTF helper, turns the character to face Blender -Y
(Unity +Z with this export) and applies the transform so the armature sits at identity in metres.
"""
import bpy, sys, os, json, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
src, dst, rep = [os.path.abspath(a) for a in argv[:3]]
keep = set(argv[3].split(','))
simplify = float(argv[4]) if len(argv) > 4 else 0.5
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
info = {'src': os.path.basename(src)}
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE' and 'lThigh' in o.data.bones][0]
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE' and o != arm: bpy.data.objects.remove(o)
for o in list(bpy.data.objects):
    if o.type == 'MESH': bpy.data.objects.remove(o)
# un-parent from the glTF root empty keeping the world transform
mw = arm.matrix_world.copy()
arm.parent = None
arm.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type == 'EMPTY': bpy.data.objects.remove(o)
for a in list(bpy.data.actions):
    if a.name not in keep: bpy.data.actions.remove(a)
    else: a.use_fake_user = True
info['takes'] = sorted(a.name for a in bpy.data.actions)
info['missing'] = sorted(keep - set(info['takes']))
# facing: l* bones are the character's left
L = (arm.matrix_world @ arm.data.bones['lThigh'].head_local) - (arm.matrix_world @ arm.data.bones['rThigh'].head_local)
L.z = 0
F = L.cross(Vector((0, 0, 1)))
ang = math.atan2(F.y, F.x)
turn = math.pi / 2 - ang  # F -> +Y (Unity +Z with this export)
info['turn_deg'] = round(math.degrees(turn), 1)
arm.matrix_world = Matrix.Rotation(turn, 4, 'Z') @ arm.matrix_world
bpy.context.view_layer.objects.active = arm
for o in bpy.data.objects: o.select_set(o == arm)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
arm.name = 'GameRig'
sc = bpy.context.scene
info['fps'] = sc.render.fps
if arm.animation_data is None: arm.animation_data_create()
# a first take "_rest" holding the rest pose: FBX readers (Unity) pose the model at time 0 of the first take
rest_act = bpy.data.actions.new('_rest'); rest_act.use_fake_user = True
arm.animation_data.action = rest_act
for pb in arm.pose.bones:
    pb.rotation_mode = 'QUATERNION'
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    for fr in (1, 2):
        pb.keyframe_insert('location', frame=fr); pb.keyframe_insert('rotation_quaternion', frame=fr)
arm.animation_data.action = None  # default node transforms = the rest pose (Unity builds the avatar from them)
for pb in arm.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
bpy.context.view_layer.update()
# a second top-level node, so the FBX reader makes its own root at identity with the armature under it (as the avatars)
ground = bpy.data.objects.new('Ground', None)
bpy.context.scene.collection.objects.link(ground)
for o2 in bpy.data.objects: o2.select_set(o2 in (arm, ground))
r = bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={'ARMATURE', 'EMPTY'}, add_leaf_bones=False,
    use_armature_deform_only=False, armature_nodetype='NULL', apply_scale_options='FBX_SCALE_ALL', apply_unit_scale=True,
    bake_space_transform=False, axis_forward='-Z', axis_up='Y', primary_bone_axis='Y', secondary_bone_axis='X',
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_use_all_bones=True,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=simplify, path_mode='STRIP')
info['export'] = list(r)
info['bytes'] = os.path.getsize(dst)
json.dump(info, open(rep, 'w'), indent=1)
print('@@OK', dst, info['bytes'])
