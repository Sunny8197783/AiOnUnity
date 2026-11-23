"""(peopleimport v2: + hair card normals bent toward a head capsule (smooth volume lighting), --drop-baton for Security_Male_01)
(peopleimport variant: --no-shapes for crowd, --hero-shapes keeps visemes + ARKit only)
Prepare a Unity-ready Rocketbox FBX from <Name>_facial.fbx.
- Bip02 -> Bip01 names (Male_Child_01 non-facial only; facial is already Bip01)
- transforms applied: armature + mesh at scale 1, metres, character faces -Y (Blender front) -> +Z in Unity
- hair/eyelash cards (the *_opacity material) made double-sided by duplicating them with reversed winding, so URP Lit
  with Render Face = Front lights both sides correctly (several cards in the source face inward)
- thighs re-parented to Bip01 Pelvis and clavicles to Bip01 Spine2 (canonical Humanoid chain; Rocketbox's own
  Unity importer does the same when not retargeting)
- all 175 blendshapes kept; no animation
blender -b --factory-startup -P fix_model.py -- <in_fbx> <out_fbx> [--no-reparent] [--no-shapes | --hero-shapes | --blink-shapes]"""
import bpy, bmesh, sys, os, json
argv = sys.argv[sys.argv.index('--')+1:]
src, dst = os.path.abspath(argv[0]), os.path.abspath(argv[1])
reparent = '--no-reparent' not in argv
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
info = {'src': os.path.basename(src)}
for o in list(bpy.data.objects):
    if o.type == 'EMPTY': bpy.data.objects.remove(o)
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
arm.animation_data_clear(); mesh.animation_data_clear()
for b in arm.data.bones:
    if b.name.startswith('Bip02'): b.name = b.name.replace('Bip02', 'Bip01')
arm.name = 'Bip01'; arm.data.name = 'Bip01'
for pb in arm.pose.bones: pb.matrix_basis.identity()
me = mesh.data
info['custom_normals_before'] = me.has_custom_normals
info['shape_keys'] = len(me.shape_keys.key_blocks) if me.shape_keys else 0
# turn half round so the character faces +Z in Unity (measured: the plain export faced -Z, nose at z<0)
import math
for o in (arm, mesh):
    if o.parent is None: o.rotation_euler.z += math.pi
bpy.context.view_layer.update()
# apply transforms
with bpy.context.temp_override(object=arm, active_object=arm, selected_objects=[arm, mesh], selected_editable_objects=[arm, mesh]):
    r = bpy.ops.object.transform_apply(location=False, rotation=True, scale=True, properties=False)
info['apply'] = list(r)
# double-sided hair cards
op_idx = [i for i, m in enumerate(me.materials) if m and m.name.endswith('_opacity')]
if op_idx:
    nf0 = len(me.polygons)
    bm = bmesh.new(); bm.from_mesh(me)
    faces = [f for f in bm.faces if f.material_index in op_idx]
    ret = bmesh.ops.duplicate(bm, geom=faces + list({e for f in faces for e in f.edges}) + list({v for f in faces for v in f.verts}))
    newf = [g for g in ret['geom'] if isinstance(g, bmesh.types.BMFace)]
    bmesh.ops.reverse_faces(bm, faces=newf)
    bm.to_mesh(me); bm.free(); me.update()
    info['hair_faces_duplicated'] = len(newf)
    def against():
        return sum(1 for p in me.polygons[nf0:] for li in p.loop_indices if me.corner_normals[li].vector.dot(p.normal) < 0)
    info['dup_normals_against_face_before'] = against()
    if me.has_custom_normals and info['dup_normals_against_face_before'] > 0:
        # duplicates still carry the originals' custom normals: point them the other way
        cn = [n.vector.copy() for n in me.corner_normals]
        for p in me.polygons[nf0:]:
            for li in p.loop_indices:
                if cn[li].dot(p.normal) < 0: cn[li] = -cn[li]
        me.normals_split_custom_set(cn); me.update()
    info['dup_normals_against_face'] = against()
    info['orig_hair_normals_against_face'] = sum(1 for p in me.polygons[:nf0] if p.material_index in op_idx for li in p.loop_indices if me.corner_normals[li].vector.dot(p.normal) < 0)
if '--drop-baton' in argv:
    # the guard's baton and its belt holder (equipment parts at the character's right hip, x < -0.15 m) go: no weapons on screen
    eq = [i for i, m in enumerate(me.materials) if m and m.name.endswith('_equipment')]
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    seen = set(); drop = []
    for f in bm.faces:
        if f.index in seen or f.material_index not in eq: continue
        stack = [f]; comp = []; seen.add(f.index)
        while stack:
            g = stack.pop(); comp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen and h.material_index in eq: seen.add(h.index); stack.append(h)
        if max(v.co.x for g in comp for v in g.verts) < -0.15: drop.extend(comp)
    vs = list({v for g in drop for v in g.verts})
    bmesh.ops.delete(bm, geom=drop, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(me); bm.free(); me.update()
    info['baton_faces_dropped'] = len(drop)
# hair/lash cards: bend every card normal toward the outward direction of a capsule around the head (vertical
# segment from below the head bone to above it), 70 %, so neighbouring cards and both sides of a card shade as one
# smooth volume instead of a patchwork of flat planes; the reversed duplicates get the same outward normal.
if op_idx:
    from mathutils import Vector
    bpy.context.view_layer.update()
    hb = arm.data.bones['Bip01 Head']
    hc = arm.matrix_world @ hb.head_local
    top_z, low_z = hc.z + 0.07, hc.z - 0.22
    cn = [n.vector.copy() for n in me.corner_normals]
    bent = 0
    for p in me.polygons:
        if p.material_index not in op_idx: continue
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            c = Vector((hc.x, hc.y, min(max(v.z, low_z), top_z)))
            out = v - c
            if out.length < 1e-5: continue
            out.normalize()
            n0 = cn[li] if cn[li].dot(out) >= 0 else -cn[li]
            cn[li] = (n0 * 0.3 + out * 0.7).normalized(); bent += 1
    me.normals_split_custom_set(cn); me.update()
    info['hair_loops_bent'] = bent
if "--no-shapes" in argv and me.shape_keys:
    mesh.shape_key_clear()
elif "--blink-shapes" in argv and me.shape_keys:
    # only the two ARKit eye blinks (a person lying with the eyes shut): the cheapest face that can close its eyes
    ref = me.shape_keys.reference_key
    for kb in list(me.shape_keys.key_blocks):
        if kb == ref or kb.name in ("AK_09_EyeBlinkLeft", "AK_10_EyeBlinkRight"): continue
        mesh.shape_key_remove(kb)
elif "--hero-shapes" in argv and me.shape_keys:
    # keep the 15 visemes and the 52 ARKit shapes (blink, jaw, brows, smile...); drop the AU/HB/SR duplicates
    ref = me.shape_keys.reference_key
    for kb in list(me.shape_keys.key_blocks):
        if kb == ref or kb.name.startswith(("AA_VI_", "AK_")): continue
        mesh.shape_key_remove(kb)
info["custom_normals_after"] = me.has_custom_normals
info['shape_keys_after'] = len(me.shape_keys.key_blocks) if me.shape_keys else 0
# reparent
if reparent:
    with bpy.context.temp_override(object=arm, active_object=arm, selected_objects=[arm], selected_editable_objects=[arm]):
        bpy.ops.object.mode_set(mode='EDIT')
        eb = arm.data.edit_bones
        for s in ('L', 'R'):
            eb[f'Bip01 {s} Thigh'].parent = eb['Bip01 Pelvis']
            eb[f'Bip01 {s} Clavicle'].parent = eb['Bip01 Spine2']
        bpy.ops.object.mode_set(mode='OBJECT')
# material names stay <prefix>_body/_head/_opacity/... (Unity remaps by name)
me.calc_loop_triangles()
info['tris'] = len(me.loop_triangles); info['verts'] = len(me.vertices)
info['dims'] = [round(x, 3) for x in mesh.dimensions]; info['arm_scale'] = list(arm.scale)
info['max_influences'] = max(sum(1 for g in v.groups if g.weight > 0) for v in me.vertices)
os.makedirs(os.path.dirname(dst), exist_ok=True)
r = bpy.ops.export_scene.fbx(filepath=dst, use_selection=False, object_types={'ARMATURE', 'MESH'},
    use_mesh_modifiers=False, mesh_smooth_type='FACE', add_leaf_bones=False, use_armature_deform_only=False,
    armature_nodetype='NULL', use_triangles=False, use_tspace=False, use_custom_props=False, bake_anim=False,
    apply_scale_options='FBX_SCALE_ALL', apply_unit_scale=True, bake_space_transform=False,
    axis_forward='-Z', axis_up='Y', primary_bone_axis='Y', secondary_bone_axis='X', path_mode='STRIP', embed_textures=False)
info['export'] = list(r); info['bytes'] = os.path.getsize(dst)
print('@@INFO', json.dumps(info))
