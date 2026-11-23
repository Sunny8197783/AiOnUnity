# people-anim: offline tools behind the Rocketbox people

These scripts made the committed files under `Assets/_AiOn/Art/People/Rocketbox/`. Unity does not run them.
Run them again only to rebuild those assets. Raw downloads never go into the repository.

Source: Microsoft Rocketbox (MIT), https://github.com/microsoft/Microsoft-Rocketbox at commit
`0943055db6ec570bcef9f2c8b41c9e5467c808f9`. The scripts read these folders:

- `Assets/Avatars/<Group>/<Avatar>/Export/<Avatar>_facial.fbx` and its `Textures/*.tga` (models, textures)
- `Assets/Animations/all_animations_max_motextr_static/` and `.../all_animations_max_motextr_xy/` (mocap clips)

Blender 5.2 or newer, run headless as `blender -b --factory-startup -P <script> -- <args>`.
The Python tools need Pillow and numpy; run them with `python -I`.

| Script | Makes | Usage |
|---|---|---|
| `fix_model.py` | `<Avatar>/Model/<Avatar>.fbx`: the model faces +Z, its transforms are applied, the thighs and clavicles are re-parented for Humanoid, and hair cards are double-sided. Blendshapes: `--hero-shapes` keeps the visemes and ARKit shapes, `--blink-shapes` keeps blinks only (the patient), `--no-shapes` keeps none (crowd). `--drop-baton` applies to Security_Male_01. | `blender ... fix_model.py -- <in_facial.fbx> <out.fbx> [flags]` |
| `prep_textures.py` (+ `hairmask.py`) | TGA to JPG/PNG maps, the URP metallic/smoothness map, dark-brown irises. Crowd maps are 1024 and hero heads 2048. | `python -I prep_textures.py <raw Avatars root> <prepared root> [Name ...]` |
| `age_elder.py` | Business_Female_02 aged for the elder: grey hair, soft wrinkles. | `python -I age_elder.py <raw Textures> <prepared Textures> <this folder> <out>` |
| `plain_tee.py` | Removes Male_Adult_09's chest graphic, its caption and the forearm tattoo (the manager). | `python -I plain_tee.py <m017_body_color.jpg> <out.jpg>` |
| `make_specs.py` | Clip lists for `rb_clips.py` (which Rocketbox take becomes which game state). | `python -I make_specs.py <animations root> <max pose frames>` |
| `rb_clips.py` | `Anim/rb_{male,female}_{loco,pose}.fbx`: body bones only, Humanoid hierarchy, loop cuts. Carry is the walk with the hold-bag right arm. | `blender ... rb_clips.py -- <spec.json> <out.fbx> <report.json>` |
| `glb_rig.py` | Reads a baked game person (`Art/People/person-*.glb`) and reports its skeleton. | `blender ... glb_rig.py -- <in.glb> <out.fbx> <out.json>` |
| `game_clips.py` | `Anim/game_{male,female,elder}.fbx`: the game's own takes (floor sit, stairs, cover, one-shots) for retargeting. | `blender ... game_clips.py -- <person.glb> <out.fbx> <report.json> <take,take,...>` |

After the files are replaced, use **AiOn → People → Import Person Clips**, then **AiOn → Build People**.
Build People also solves the arm poses (cart, sitKnees, sitBack), builds the controllers (hero and crowd versions),
measures each prefab's gait scale, and writes the prefabs.
