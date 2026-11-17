# Level export tool

Exports each level of the original web game (github.com/Sunny8197783/AiOn, `safe-route/dist`) as `<id>.glb` (the model
the original's own builders make) and `<id>-level.json` (walk zones, solids, ceilings, spawns, interaction points, states,
NPC spots), plus `presets.json` (environment presets).

    node Tools/level-export/server.mjs <path to safe-route/dist> Tools/level-export/overlay <output folder>

Then open http://127.0.0.1:4173/export.html (optionally `?levels=mart,store`) in a browser and wait for "EXPORT DONE".
Copy the `.glb` files to `Assets/_AiOn/Art/Levels/`, the `-level.json` files to `Assets/_AiOn/Data/Source/` (keep the
`doors` table of apartment-level.json), `presets.json` to `Assets/_AiOn/Data/Source/env-presets.json`, and run
`AiOn → Rebuild All Level Scenes` in Unity.

Small instanced sets are baked into merged meshes. Shop goods (instanced with a per-instance label cell) are left out of
the model. `overlay/vendor/addons/exporters/GLTFExporter.js` is from three.js r180 (MIT, see LICENSE-three.txt).
