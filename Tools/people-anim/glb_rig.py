"""Import a game person glb (with animations), report the skeleton, export FBX with every action as a take.
blender -b --factory-startup -P glb_rig.py -- <in.glb> <out.fbx> <out.json>"""
import bpy, sys, os, json, math
argv = sys.argv[sys.argv.index('--')+1:]
src, dst, rep = [os.path.abspath(a) for a in argv[:3]]
bpy.ops.wm.read_factory_settings(use_empty=True)
r = bpy.ops.import_scene.gltf(filepath=src)
info = {'src': os.path.basename(src), 'import': list(r)}
arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
info['objects'] = [(o.name, o.type, o.parent.name if o.parent else None, [round(x, 4) for x in o.scale], [round(x, 4) for x in o.rotation_euler], [round(x, 3) for x in o.dimensions]) for o in bpy.data.objects]
arm = arms[0]
info['bones'] = [(b.name, b.parent.name if b.parent else None, [round(x, 4) for x in (arm.matrix_world @ b.head_local)], round(b.length, 4)) for b in arm.data.bones]
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
info['meshes'] = []
for m in meshes:
    m.data.calc_loop_triangles()
    vg = set(g.name for g in m.vertex_groups)
    info['meshes'].append({'name': m.name, 'tris': len(m.data.loop_triangles), 'vgroups': len(vg), 'mats': [x.name for x in m.data.materials if x],
                           'shape_keys': len(m.data.shape_keys.key_blocks) if m.data.shape_keys else 0,
                           'unweighted_bones': [b.name for b in arm.data.bones if b.name not in vg]})
info['actions'] = []
for a in bpy.data.actions:
    info['actions'].append((a.name, [round(x, 2) for x in a.frame_range]))
sc = bpy.context.scene
info['fps'] = sc.render.fps
# channel analysis (5.x: layered actions -> channelbags)
chan = {}
for a in bpy.data.actions:
    for layer in a.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in cb.fcurves:
                    bone = fc.data_path.split('"')[1] if '"' in fc.data_path else fc.data_path
                    prop = fc.data_path.rsplit('.', 1)[-1]
                    vals = [k.co[1] for k in fc.keyframe_points]
                    rng = (max(vals) - min(vals)) if vals else 0
                    key = (bone, prop)
                    if rng > 1e-4: chan.setdefault(key, set()).add(a.name)
info['animated_channels'] = {f'{b}.{p}': sorted(v) for (b, p), v in sorted(chan.items()) if p != 'rotation_quaternion'}
info['rotated_bones'] = sorted({b for (b, p) in chan if p == 'rotation_quaternion'})
# the glTF importer adds an 'Icosphere' bone-shape helper: not part of the character
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.parent is None and not o.vertex_groups: bpy.data.objects.remove(o)
# rest pose arm angle
def wpos(n): return arm.matrix_world @ arm.data.bones[n].head_local
info['scene_unit'] = sc.unit_settings.scale_length
# export FBX with all actions as takes
for o in bpy.data.objects: o.select_set(o.type in ('ARMATURE', 'MESH'))
info['exported_objects'] = [o.name for o in bpy.data.objects if o.select_get()]
bpy.context.view_layer.objects.active = arm
r = bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=False,
    mesh_smooth_type='FACE', add_leaf_bones=False, use_armature_deform_only=False, armature_nodetype='NULL',
    apply_scale_options='FBX_SCALE_ALL', apply_unit_scale=True, bake_space_transform=False, axis_forward='-Z', axis_up='Y',
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_use_all_bones=True,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, path_mode='STRIP')
info['export'] = list(r); info['bytes'] = os.path.getsize(dst)
# re-import check
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=dst)
a2 = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
info['reimport'] = {'arm_scale': list(a2.scale), 'bones': len(a2.data.bones), 'actions': len(bpy.data.actions),
                    'action_names': [a.name for a in bpy.data.actions][:40],
                    'dims': [round(x, 3) for x in max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: o.dimensions.z).dimensions]}
json.dump(info, open(rep, 'w'), indent=1)
print('@@OK', rep)
