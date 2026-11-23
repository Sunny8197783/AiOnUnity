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
| `fix_model.py` | `<Avatar>/Model/<Avatar>.fbx`: the model faces +Z, its transforms are applied, the thighs and clavicles are re-parented for Humanoid, and hair cards are double-sided. Blendshapes: `--hero-shapes` keeps the visemes and ARKit shapes, `--blink-shapes` keeps blinks only (the patient), `--no-shapes` keeps none (crowd). `--drop-baton` applies to Security_Male_01. `--bun-normals` (Medical_Female_01, the elder) bends the bun's card normals out from the bun's centre instead of the head, so the cards shade like the bun's core under them (no dark stripes on the grey bun). | `blender ... fix_model.py -- <in_facial.fbx> <out.fbx> [flags]` |
| `prep_textures.py` | `<Avatar>/Textures/`: TGA to colour/normal JPG and URP metallic/smoothness PNG, hair/lash cards PNG; dark-brown irises. Colour maps 1024, hero heads 2048 (not the patient), bodies 2048 only for jeongmin and seoyeon; normals and cards 1024, metallic/smoothness 512 (`RocketboxCastTests.TextureBudgetPerTier`). Per-avatar fixes: Male_Adult_10's skin a little less orange, Business_Female_03's nape card root cut into strand tips, Female_Adult_03's arm tattoo painted out and her purple top muted to a dusty navy. Hair/lash cards are resized with colour and alpha apart (a plain RGBA resize leaves black under the transparent texels, which bleeds into the card edges). Maps committed before 11-23 were made by earlier versions of this script: a re-run matches them visually, but the metallic/smoothness maps, the iris area and the RGB under transparent card texels differ slightly (Female_Adult_08 checked). | `python -I prep_textures.py <raw Avatars root> <out root> [Name ...]` |
| `age_elder.py` (+ `hairmask.py`) | After prep: Medical_Female_01 (prefix f152) aged for the 304호 elder: grey-white hair with dark strands on the scalp, the bun's core and cards an even mid grey (no dark blotches, card rims toned to the card's own grey), lashes kept dark, the gold hair tie darkened; forehead/frown lines, fanned crow's feet, under-eye bags, deep nasolabial and marionette folds, a soft jowl and under-chin shadow, neck rings and cords, faint upper-lip lines, warm grey-brown brows, age spots on the cheekbones, cheeks and temples, thinner muted lips, 10 % less skin gloss. Profile `f003` (Female_Adult_03) is the alternative that was compared. | `python -I age_elder.py <raw Textures> <out Textures> f152` |
| `dress_elder.py` | After prep: Medical_Female_01's white scrubs and clogs repainted as a dusty-rose blouse with a small, low-contrast flower print (five-petal blossoms in rose-cream or deeper rose with green leaves, evenly spaced), dark grey-brown trousers and plain brown shoes (folds kept). | `python -I dress_elder.py <raw Textures> <out Textures>` |
| `plain_tee.py` | Removes Male_Adult_09's chest graphic, its caption and the forearm tattoo (the manager). | `python -I plain_tee.py <m017_body_color.jpg> <out.jpg>` |
| `make_specs.py` | Clip lists for `rb_clips.py` (which Rocketbox take becomes which game state). | `python -I make_specs.py <animations root> <max pose frames>` |
| `rb_clips.py` | `Anim/rb_{male,female}_{loco,pose}.fbx`: body bones only, Humanoid hierarchy, loop cuts. Carry is the walk with the hold-bag right arm. | `blender ... rb_clips.py -- <spec.json> <out.fbx> <report.json>` |
| `glb_rig.py` | Reads a baked game person (`Art/People/person-*.glb`) and reports its skeleton. | `blender ... glb_rig.py -- <in.glb> <out.fbx> <out.json>` |
| `game_clips.py` | `Anim/game_{male,female,elder}.fbx`: the game's own takes (floor sit, stairs, cover, one-shots) for retargeting. | `blender ... game_clips.py -- <person.glb> <out.fbx> <report.json> <take,take,...>` |

After the files are replaced, use **AiOn → People → Import Person Clips**, then **AiOn → Build People**.
Build People also solves the arm poses (cart, sitKnees, sitBack), builds the controllers (hero and crowd versions),
measures each prefab's gait scale, and writes the prefabs.
