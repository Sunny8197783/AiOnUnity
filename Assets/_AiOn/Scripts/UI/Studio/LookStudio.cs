// The 「불나면 살펴서 대피」 screen's pictures (competition upgrade, minigame art), lit 3D sets instead of flat boxes:
//  - 302호 현관 from inside: the sunken tiled entry, a step up to the wood floor, the shoe cabinet, the steel front door
//    (lever handle, door lock, peephole, closer) in its frame, the corridor's fire light leaking through the slit under
//    the door (a thin strip, brighter toward the right where 304 is, and a low wash across the tiles from a light out in
//    the corridor) with smoke curling in; the towel along the gap once it is sealed, which all but shuts the light out
//    when wet and lets a little through when dry. The handle never glows: how hot it felt is the screen's words;
//  - the balcony: the light partition (경량칸막이, its red notice) at the end with the moving boxes stacked in front; as it
//    is kicked the gypsum board dents, a few cracks run out from the dent, and at the end it breaks through to a dark
//    hole with torn board around it. The window and the night outside are on the left.
// The smoke drifts up and fades (it holds still with reduce motion); only then does the picture redraw every frame.
// LookFirstScreen drives it.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AiOn.UI.Studio
{
    public sealed class LookStudio : MonoBehaviour
    {
        static readonly Vector3 BalconyAt = new(20f, 0f, 0f);

        MiniStudio _s;
        bool _reduce;
        GameObject _door, _balcony;
        Light _gapLight;
        readonly List<Material> _gapMats = new();
        Material _towelMat, _panelMat;
        GameObject _towel;
        readonly List<Transform> _boxes = new();
        readonly List<GameObject> _cracks = new();
        Transform _panel;
        Material _dentMat;
        GameObject _hole;

        // smoke puffs: position, phase, size
        readonly List<(Transform t, Material m, float phase, float x)> _puffs = new();
        int _puffCount;
        bool _large;

        public MiniStudio Studio => _s;

        public static LookStudio Create(Component owner, Vector2Int pixels, bool reduceMotion)
        {
            var s = MiniStudio.Create("LookFirst", owner, pixels, new Color(0.02f, 0.025f, 0.03f));
            var me = s.gameObject.AddComponent<LookStudio>();
            me._s = s;
            me._reduce = reduceMotion;
            me.BuildDoor();
            me.BuildBalcony();
            me.Door(false, "none", false, "none");
            return me;
        }

        // ------------------------------------------------------------------ the front door

        void BuildDoor()
        {
            _door = _s.Empty("Door", null, Vector3.zero, Vector3.zero);
            var t = _door.transform;
            t.localScale = new Vector3(-1, 1, 1); // built with +x on the viewer's right (the camera looks down -z)
            var tile = _s.Lit(Color.white, 0.45f, 0, StudioTextures.Tile, new Vector2(1.2f, 0.8f), StudioTextures.TileNormal, 0.8f);
            var wood = _s.Lit(new Color(0.95f, 0.9f, 0.85f), 0.35f, 0, StudioTextures.Wood, new Vector2(3, 1.5f), StudioTextures.WoodNormal, 0.6f);
            var plaster = _s.Lit(new Color(0.86f, 0.85f, 0.82f), 0.12f, 0, StudioTextures.Plaster, new Vector2(2, 2), StudioTextures.PlasterNormal, 0.5f);
            var trim = _s.Lit(new Color(0.3f, 0.29f, 0.28f), 0.4f, 0.3f);
            // floor: tiles in the sunken entry, a step up to the wood floor of the hall
            _s.Part(PrimitiveType.Cube, "Tiles", t, new Vector3(0, -0.05f, 0.55f), Vector3.zero, new Vector3(2.5f, 0.1f, 1.1f), tile);
            _s.Part(PrimitiveType.Cube, "Hall", t, new Vector3(0, 0.06f, 2.6f), new Vector3(0, 90, 0), new Vector3(3f, 0.12f, 2.5f), wood);
            _s.Part(PrimitiveType.Cube, "StepEdge", t, new Vector3(0, 0.06f, 1.105f), Vector3.zero, new Vector3(2.5f, 0.12f, 0.02f), trim);
            // walls around the door and down both sides, the ceiling
            _s.Part(PrimitiveType.Cube, "WallL", t, new Vector3(-1.25f, 1.2f, 1.9f), Vector3.zero, new Vector3(0.1f, 2.5f, 4f), plaster);
            _s.Part(PrimitiveType.Cube, "WallR", t, new Vector3(1.25f, 1.2f, 1.9f), Vector3.zero, new Vector3(0.1f, 2.5f, 4f), plaster);
            _s.Part(PrimitiveType.Cube, "WallBackL", t, new Vector3(-0.945f, 1.2f, -0.05f), Vector3.zero, new Vector3(0.51f, 2.5f, 0.1f), plaster);
            _s.Part(PrimitiveType.Cube, "WallBackR", t, new Vector3(0.825f, 1.2f, -0.05f), Vector3.zero, new Vector3(0.75f, 2.5f, 0.1f), plaster);
            _s.Part(PrimitiveType.Cube, "WallOver", t, new Vector3(-0.12f, 2.29f, -0.05f), Vector3.zero, new Vector3(1.14f, 0.32f, 0.1f), plaster);
            _s.Part(PrimitiveType.Cube, "Ceiling", t, new Vector3(0, 2.45f, 1.9f), Vector3.zero, new Vector3(2.6f, 0.05f, 4f), plaster);
            // the steel frame
            var frame = _s.Lit(new Color(0.24f, 0.21f, 0.19f), 0.45f, 0.5f);
            _s.Part(PrimitiveType.Cube, "FrameL", t, new Vector3(-0.655f, 1.05f, 0.0f), Vector3.zero, new Vector3(0.07f, 2.12f, 0.14f), frame);
            _s.Part(PrimitiveType.Cube, "FrameR", t, new Vector3(0.415f, 1.05f, 0.0f), Vector3.zero, new Vector3(0.07f, 2.12f, 0.14f), frame);
            _s.Part(PrimitiveType.Cube, "FrameTop", t, new Vector3(-0.12f, 2.115f, 0.0f), Vector3.zero, new Vector3(1.14f, 0.07f, 0.14f), frame);
            // the door leaf: steel with a dark wood-grain film
            var leafMat = _s.Lit(new Color(0.55f, 0.42f, 0.33f), 0.38f, 0.1f, StudioTextures.Wood, new Vector2(1, 1), StudioTextures.WoodNormal, 0.35f);
            _s.Part(PrimitiveType.Cube, "Leaf", t, new Vector3(-0.12f, 1.04f, 0.02f), Vector3.zero, new Vector3(1.0f, 2.06f, 0.05f), leafMat);
            var steel = _s.Lit(new Color(0.78f, 0.78f, 0.76f), 0.75f, 0.9f);
            var black = _s.Lit(new Color(0.05f, 0.05f, 0.055f), 0.7f, 0.2f);
            // the lever (on the right, the hinges on the left), its rose, the door lock above, the peephole, the closer
            _s.Part(PrimitiveType.Cylinder, "Rose", t, new Vector3(0.28f, 1.0f, 0.06f), new Vector3(90, 0, 0), new Vector3(0.065f, 0.012f, 0.065f), steel);
            _s.Part(PrimitiveType.Cylinder, "Lever", t, new Vector3(0.21f, 1.0f, 0.1f), new Vector3(0, 0, 90), new Vector3(0.026f, 0.075f, 0.026f), steel);
            _s.Part(PrimitiveType.Cylinder, "Neck", t, new Vector3(0.28f, 1.0f, 0.075f), new Vector3(90, 0, 0), new Vector3(0.024f, 0.03f, 0.024f), steel);
            _s.Part(PrimitiveType.Cube, "Lock", t, new Vector3(0.28f, 1.3f, 0.065f), Vector3.zero, new Vector3(0.075f, 0.2f, 0.04f), black);
            _s.Part(PrimitiveType.Cube, "LockPad", t, new Vector3(0.28f, 1.31f, 0.086f), Vector3.zero, new Vector3(0.05f, 0.1f, 0.002f), _s.Glow(new Color(0.25f, 0.5f, 0.6f), 0.35f));
            _s.Part(PrimitiveType.Cylinder, "Peephole", t, new Vector3(-0.12f, 1.55f, 0.05f), new Vector3(90, 0, 0), new Vector3(0.03f, 0.008f, 0.03f), steel);
            _s.Part(PrimitiveType.Cube, "Closer", t, new Vector3(-0.38f, 1.98f, 0.09f), Vector3.zero, new Vector3(0.3f, 0.06f, 0.06f), _s.Lit(new Color(0.32f, 0.32f, 0.33f), 0.5f, 0.6f));
            _s.Part(PrimitiveType.Cube, "CloserArm", t, new Vector3(-0.22f, 2.07f, 0.09f), new Vector3(0, 0, 8), new Vector3(0.34f, 0.02f, 0.02f), black);
            // the shoe cabinet on the right, shoes on the tiles
            var cab = _s.Lit(new Color(0.92f, 0.9f, 0.86f), 0.3f, 0, StudioTextures.Wood, new Vector2(1, 2), StudioTextures.WoodNormal, 0.25f);
            _s.Part(PrimitiveType.Cube, "ShoeCabinet", t, new Vector3(1.0f, 1.12f, 0.7f), Vector3.zero, new Vector3(0.4f, 2.1f, 1.1f), cab);
            _s.Part(PrimitiveType.Cube, "CabSeam", t, new Vector3(0.795f, 1.12f, 0.7f), Vector3.zero, new Vector3(0.004f, 2.06f, 0.006f), trim);
            _s.Part(PrimitiveType.Cube, "CabPull", t, new Vector3(0.79f, 1.0f, 0.6f), Vector3.zero, new Vector3(0.015f, 0.18f, 0.02f), steel);
            var shoe = _s.Lit(new Color(0.12f, 0.12f, 0.14f), 0.35f);
            var sole = _s.Lit(new Color(0.85f, 0.85f, 0.82f), 0.2f);
            for (int i = 0; i < 2; i++)
            {
                float x = -0.55f + i * 0.13f;
                _s.Part(PrimitiveType.Cube, "Shoe", t, new Vector3(x, 0.045f, 0.75f), new Vector3(0, -4 + i * 9, 0), new Vector3(0.1f, 0.07f, 0.27f), shoe);
                _s.Part(PrimitiveType.Cube, "Sole", t, new Vector3(x, 0.01f, 0.75f), new Vector3(0, -4 + i * 9, 0), new Vector3(0.105f, 0.02f, 0.28f), sole);
            }
            // the slit under the door: the corridor's fire light, a thin strip along the whole leaf, brighter toward 304
            // (the right), kept under the bloom threshold so it reads as light through a slit, not a flame on the floor
            for (int i = 0; i < GapPieces; i++)
            {
                var m = _s.Glow(new Color(1f, 0.5f, 0.16f), 0.5f);
                float w = 1.0f / GapPieces;
                _s.Part(PrimitiveType.Cube, "Gap", t, new Vector3(-0.62f + w * (i + 0.5f), 0.004f, 0.048f), Vector3.zero, new Vector3(w + 0.001f, 0.006f, 0.006f), m, false);
                _gapMats.Add(m);
            }
            // and its light on the tiles: a low wide wash from out in the corridor, through the slit, along the floor
            // (behind the door plane, so the towel sits between it and the room)
            _gapLight = _s.Spot("GapLight", new Vector3(-0.15f, 0.012f, -0.12f), new Vector3(-0.05f, 0f, 1.1f), new Color(1f, 0.55f, 0.22f), 0.5f, 1.6f, 150f, false);
            // the towel along the gap (Seal)
            _towelMat = _s.Lit(Color.white, 0.1f, 0, StudioTextures.Fabric, new Vector2(3, 1), StudioTextures.FabricNormal, 1.4f);
            _towel = _s.Part(PrimitiveType.Capsule, "Towel", t, new Vector3(-0.12f, 0.04f, 0.12f), new Vector3(0, 0, 90), new Vector3(0.09f, 0.55f, 0.11f), _towelMat);
            _towel.SetActive(false);
            // smoke curling in under the door
            var smokeTex = StudioTextures.Smoke;
            for (int i = 0; i < 10; i++)
            {
                var m = _s.Clear(new Color(0.7f, 0.7f, 0.72f, 0.3f), 0f, smokeTex);
                var q = _s.Part(PrimitiveType.Quad, "Smoke", t, Vector3.zero, Vector3.zero, Vector3.one * 0.4f, m, false);
                _puffs.Add((q.transform, m, i / 10f, -0.55f + (i * 0.37f) % 0.85f));
            }
            // light: the hall's night light behind us (cool), a little from the living room on the left (warm, dim)
            _s.Spot("Hall", new Vector3(-0.1f, 2.3f, 3.2f), new Vector3(0.1f, 0.9f, 0f), new Color(0.78f, 0.84f, 0.95f), 2.6f, 6f, 60f, true);
            _s.Spot("Room", new Vector3(0.9f, 1.4f, 3.4f), new Vector3(-0.2f, 0.8f, 0f), new Color(1f, 0.82f, 0.62f), 1.2f, 6f, 70f, false);
        }

        const int GapPieces = 10;

        /// <summary>How much of the fire's light still comes under the door: all of it, a little past a dry towel, almost none past a wet one.</summary>
        public static float Leak(bool towel, string wet) => !towel ? 1f : wet == "dry" || wet == "none" || string.IsNullOrEmpty(wet) ? 0.3f : 0.06f;

        /// <summary>
        /// The door as it is now: a <paramref name="large"/> fire (more light and smoke), the handle as felt
        /// ("none" not touched, "warm", "hot": words on the screen, the steel looks the same), the towel along the gap and
        /// what it was wet with (dry, tub, bottle, cloth).
        /// </summary>
        public void Door(bool large, string handle, bool towel, string wet)
        {
            _door.SetActive(true); _balcony.SetActive(false);
            _s.Resume();
            _s.Aim(new Vector3(0.0f, 1.42f, 3.45f), new Vector3(0.08f, 0.98f, 0f), 50f);
            _large = large;
            float leak = Leak(towel, wet);
            for (int i = 0; i < _gapMats.Count; i++)
            {
                // brighter toward the right (304); at most just under the bloom threshold
                float k = Mathf.Lerp(0.35f, 1f, i / (float)(_gapMats.Count - 1));
                float e = (large ? 1.0f : 0.55f) * k * leak;
                _gapMats[i].SetColor("_EmissionColor", new Color(1f, 0.5f, 0.16f) * e);
            }
            _gapLight.intensity = (large ? 0.9f : 0.4f) * leak;
            _gapLight.range = large ? 1.8f : 1.2f;
            _puffCount = large ? 10 : 5;
            if (_towel.activeSelf != towel) _towel.SetActive(towel);
            if (towel)
            {
                bool dry = wet == "dry" || wet == "none";
                _towelMat.SetColor("_BaseColor", dry ? new Color(0.92f, 0.92f, 0.88f) : wet == "cloth" ? new Color(0.36f, 0.42f, 0.5f) : new Color(0.58f, 0.66f, 0.74f));
                _towelMat.SetFloat("_Smoothness", dry ? 0.08f : 0.55f);
            }
            for (int i = 0; i < _puffs.Count; i++) _puffs[i].t.gameObject.SetActive(i < _puffCount && !towel);
            PlacePuffs(_reduce ? 0f : Time.unscaledTime);
            _s.Live = !_reduce && !towel; // the smoke drifts: redraw each frame; else a still picture
            _s.Invalidate();
        }

        // ------------------------------------------------------------------ the balcony and the partition

        void BuildBalcony()
        {
            _balcony = _s.Empty("Balcony", null, BalconyAt, Vector3.zero);
            var t = _balcony.transform;
            t.localScale = new Vector3(-1, 1, 1); // built with +x on the viewer's right
            var tile = _s.Lit(new Color(0.8f, 0.78f, 0.75f), 0.35f, 0, StudioTextures.Tile, new Vector2(1.5f, 2f), StudioTextures.TileNormal, 0.8f);
            var plaster = _s.Lit(new Color(0.8f, 0.8f, 0.78f), 0.12f, 0, StudioTextures.Plaster, new Vector2(2, 2), StudioTextures.PlasterNormal, 0.18f);
            var alu = _s.Lit(new Color(0.7f, 0.71f, 0.72f), 0.55f, 0.7f);
            _s.Part(PrimitiveType.Cube, "Floor", t, new Vector3(0, -0.05f, 1.6f), Vector3.zero, new Vector3(1.5f, 0.1f, 3.4f), tile);
            _s.Part(PrimitiveType.Cube, "Ceiling", t, new Vector3(0, 2.4f, 1.6f), Vector3.zero, new Vector3(1.5f, 0.05f, 3.4f), plaster);
            // right: the living room's wall and glass door (warm light through it)
            _s.Part(PrimitiveType.Cube, "InnerWall", t, new Vector3(0.78f, 1.2f, 1.6f), Vector3.zero, new Vector3(0.08f, 2.5f, 3.4f), plaster);
            _s.Part(PrimitiveType.Cube, "RoomGlass", t, new Vector3(0.735f, 1.1f, 1.25f), Vector3.zero, new Vector3(0.01f, 1.9f, 1.3f), _s.Glow(new Color(1f, 0.78f, 0.55f), 0.55f));
            _s.Part(PrimitiveType.Cube, "RoomFrameA", t, new Vector3(0.725f, 1.1f, 0.6f), Vector3.zero, new Vector3(0.03f, 1.94f, 0.05f), alu);
            _s.Part(PrimitiveType.Cube, "RoomFrameB", t, new Vector3(0.725f, 1.1f, 1.25f), Vector3.zero, new Vector3(0.03f, 1.94f, 0.05f), alu);
            _s.Part(PrimitiveType.Cube, "RoomFrameC", t, new Vector3(0.725f, 1.1f, 1.9f), Vector3.zero, new Vector3(0.03f, 1.94f, 0.05f), alu);
            // left: the low wall with the outer windows, the night outside
            _s.Part(PrimitiveType.Cube, "Sill", t, new Vector3(-0.78f, 0.45f, 1.6f), Vector3.zero, new Vector3(0.12f, 0.9f, 3.4f), plaster);
            _s.Part(PrimitiveType.Cube, "Night", t, new Vector3(-1.4f, 1.6f, 1.6f), Vector3.zero, new Vector3(0.02f, 1.5f, 3.4f), _s.Glow(new Color(0.06f, 0.09f, 0.16f), 1f));
            for (int i = 0; i < 4; i++)
                _s.Part(PrimitiveType.Cube, "Mullion", t, new Vector3(-0.78f, 1.62f, 0.05f + i * 1.0f), Vector3.zero, new Vector3(0.05f, 1.45f, 0.05f), alu);
            _s.Part(PrimitiveType.Cube, "Rail", t, new Vector3(-0.78f, 0.92f, 1.6f), Vector3.zero, new Vector3(0.06f, 0.04f, 3.4f), alu);
            _s.Part(PrimitiveType.Cube, "TopRail", t, new Vector3(-0.78f, 2.35f, 1.6f), Vector3.zero, new Vector3(0.06f, 0.05f, 3.4f), alu);
            // far windows of the block across (a few lit)
            for (int i = 0; i < 7; i++)
            {
                float z = 0.3f + i * 0.47f, y = 1.2f + (i * 37 % 5) * 0.18f;
                var c = (i % 3 == 0) ? new Color(1f, 0.8f, 0.5f) : new Color(0.25f, 0.3f, 0.4f);
                _s.Part(PrimitiveType.Cube, "Far", t, new Vector3(-1.38f, y, z), Vector3.zero, new Vector3(0.01f, 0.08f, 0.12f), _s.Glow(c, i % 3 == 0 ? 1.2f : 0.4f), false);
            }
            // the partition at the end: a thin board in a frame, the notice on it
            _panelMat = _s.Lit(new Color(0.9f, 0.9f, 0.88f), 0.15f, 0, StudioTextures.Plaster, new Vector2(1, 2), StudioTextures.PlasterNormal, 0.35f);
            var frameMat = _s.Lit(new Color(0.55f, 0.55f, 0.55f), 0.4f, 0.6f);
            _s.Part(PrimitiveType.Cube, "EndWall", t, new Vector3(0, 1.2f, -0.12f), Vector3.zero, new Vector3(1.5f, 2.5f, 0.1f), plaster);
            _panel = _s.Part(PrimitiveType.Cube, "Partition", t, new Vector3(0.05f, 1.02f, -0.05f), Vector3.zero, new Vector3(0.92f, 1.96f, 0.03f), _panelMat).transform;
            _s.Part(PrimitiveType.Cube, "PFrameL", t, new Vector3(-0.42f, 1.02f, -0.04f), Vector3.zero, new Vector3(0.03f, 2.0f, 0.05f), frameMat);
            _s.Part(PrimitiveType.Cube, "PFrameR", t, new Vector3(0.52f, 1.02f, -0.04f), Vector3.zero, new Vector3(0.03f, 2.0f, 0.05f), frameMat);
            _s.Part(PrimitiveType.Cube, "PFrameT", t, new Vector3(0.05f, 2.02f, -0.04f), Vector3.zero, new Vector3(0.97f, 0.03f, 0.05f), frameMat);
            Notice(t, new Vector3(0.05f, 1.55f, -0.032f));
            // the damage as it is kicked, low in the middle where the kicks land: a dent (a soft shadow), five cracks in the
            // gypsum running out from it (grey, a few long fairly straight runs, not a glass star), then a hole broken
            // through with torn board around it and the dark of 301's balcony behind
            var crackMat = _s.Lit(new Color(0.3f, 0.29f, 0.27f), 0.05f);
            var hit = new Vector3(0.05f, 0.5f, -0.034f);
            var segs = new List<(GameObject g, float r)>();
            float[] heading = { 18f, 152f, 205f, 290f, 80f };
            for (int arm = 0; arm < heading.Length; arm++)
            {
                float ang = heading[arm];
                var p = hit;
                for (int k = 0; k < 3; k++)
                {
                    ang += Mathf.Sin(arm * 5.3f + k * 2.1f) * 12f;
                    float len = 0.07f + 0.03f * ((arm + k) % 3);
                    var d = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad), 0) * len;
                    var q = p + d;
                    if (q.x < -0.38f || q.x > 0.48f || q.y < 0.08f || q.y > 1.95f) break;
                    var c = _s.Part(PrimitiveType.Cube, "Crack", t, (p + q) * 0.5f, new Vector3(0, 0, ang), new Vector3(len + 0.004f, 0.0045f - k * 0.0011f, 0.004f), crackMat, false);
                    c.SetActive(false);
                    segs.Add((c, k + arm * 0.01f));
                    p = q;
                }
            }
            segs.Sort((a, b) => a.r.CompareTo(b.r));
            foreach (var sg in segs) _cracks.Add(sg.g);
            _dentMat = _s.Clear(new Color(0.12f, 0.1f, 0.09f, 0f), 0f, StudioTextures.Smoke);
            _s.Part(PrimitiveType.Quad, "Dent", t, hit + new Vector3(0, 0, 0.001f), new Vector3(0, 180, 0), new Vector3(0.36f, 0.3f, 1f), _dentMat, false);
            // the hole: the dark behind, ragged (three overlapping pieces), torn bits of board and paper bent in around it
            // (the panel's face toward us is at z -0.035; the hole's layers stack just in front of it, back to front)
            _hole = _s.Empty("Hole", t, new Vector3(hit.x, hit.y, -0.0335f), Vector3.zero);
            var dark = _s.Lit(new Color(0.015f, 0.016f, 0.02f), 0f);
            var core = _s.Lit(new Color(0.8f, 0.78f, 0.72f), 0.02f, 0, StudioTextures.Plaster, new Vector2(3, 3), StudioTextures.PlasterNormal, 1.5f);
            var torn = _s.Lit(new Color(0.93f, 0.92f, 0.89f), 0.1f);
            // the crumbled gypsum core showing around the break, then the dark through it
            _s.Part(PrimitiveType.Cylinder, "Core", _hole.transform, Vector3.zero, new Vector3(90, 0, 8), new Vector3(0.21f, 0.0005f, 0.17f), core, false);
            _s.Part(PrimitiveType.Cylinder, "Dark", _hole.transform, new Vector3(0, 0, 0.0008f), new Vector3(90, 0, 0), new Vector3(0.16f, 0.0005f, 0.12f), dark, false);
            _s.Part(PrimitiveType.Cylinder, "Dark2", _hole.transform, new Vector3(0.045f, 0.028f, 0.0009f), new Vector3(90, 0, 25), new Vector3(0.09f, 0.0005f, 0.07f), dark, false);
            _s.Part(PrimitiveType.Cylinder, "Dark3", _hole.transform, new Vector3(-0.05f, -0.022f, 0.0009f), new Vector3(90, 0, -30), new Vector3(0.08f, 0.0005f, 0.065f), dark, false);
            // flaps of the paper face torn and bent in toward the hole, catching the light
            for (int i = 0; i < 7; i++)
            {
                float a = i * 51f + 10f;
                var at = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * 0.078f, Mathf.Sin(a * Mathf.Deg2Rad) * 0.062f, 0.006f);
                _s.Part(PrimitiveType.Cube, "Torn", _hole.transform, at, new Vector3(30f + i % 3 * 10f, 0, a + 90f), new Vector3(0.05f + (i % 2) * 0.025f, 0.022f, 0.002f), torn, false);
            }
            _hole.SetActive(false);
            // the moving boxes stacked in front: two on the floor, one on top between them
            var card = _s.Lit(Color.white, 0.2f, 0, StudioTextures.Cardboard, Vector2.one, StudioTextures.CardboardNormal, 0.6f);
            var tape = _s.Lit(new Color(0.85f, 0.75f, 0.55f), 0.6f);
            for (int i = 0; i < LookFirstBoxes; i++)
            {
                var pos = i < 2 ? new Vector3(-0.2f + i * 0.5f, 0.21f, 0.35f) : new Vector3(0.05f, 0.62f, 0.38f);
                var b = _s.Part(PrimitiveType.Cube, "Box", t, pos, new Vector3(0, (i * 11) % 9 - 4, 0), new Vector3(0.46f, 0.42f, 0.42f), card).transform;
                _s.Part(PrimitiveType.Cube, "Tape", b, new Vector3(0, 0.502f, 0), Vector3.zero, new Vector3(0.12f, 0.01f, 1.01f), tape);
                _boxes.Add(b);
            }
            // light: the living room through its glass door (warm), the sky through the windows (cool), a little from us
            _s.Spot("Room", new Vector3(BalconyAt.x - 0.62f, 1.9f, 1.3f), BalconyAt + new Vector3(0.15f, 0.7f, 0f), new Color(1f, 0.82f, 0.62f), 3.4f, 6f, 85f, true);
            _s.Spot("Sky", new Vector3(BalconyAt.x + 1.3f, 2f, 1.4f), BalconyAt + new Vector3(-0.3f, 0.6f, 0.4f), new Color(0.55f, 0.65f, 0.9f), 0.8f, 5f, 80f, false);
            _s.Spot("Torch", new Vector3(BalconyAt.x - 0.1f, 1.5f, 3.1f), BalconyAt + new Vector3(-0.05f, 1.1f, 0f), new Color(0.95f, 0.95f, 1f), 1.4f, 6f, 30f, false);
        }

        const int LookFirstBoxes = AiOn.Game.LookFirstLogic.Boxes;

        /// <summary>The partition's printed notice: a white plate with the red words, on a small world-space canvas.</summary>
        void Notice(Transform parent, Vector3 pos)
        {
            _s.Part(PrimitiveType.Cube, "NoticePlate", parent, pos, Vector3.zero, new Vector3(0.6f, 0.26f, 0.004f), _s.Lit(new Color(0.96f, 0.95f, 0.92f), 0.4f), false);
            var go = new GameObject("NoticeCanvas", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + new Vector3(0, 0, 0.003f);
            go.transform.localRotation = Quaternion.identity; // in the mirrored set this reads the right way round from +z
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _s.Cam;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(580, 240);
            rt.localScale = Vector3.one * 0.001f;
            var title = UiKit.Label("Title", rt, "경량칸막이", 92, new Color(0.72f, 0.1f, 0.1f), TextAnchor.UpperCenter, FontStyle.Bold);
            UiKit.At(title.rectTransform, 0, 10, 580, 110);
            var line = UiKit.Label("Line", rt, "비상시 파괴 후 옆 세대로 대피", 46, new Color(0.72f, 0.1f, 0.1f), TextAnchor.UpperCenter, FontStyle.Bold);
            UiKit.At(line.rectTransform, 0, 140, 580, 70);
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = MiniStudio.Layer;
        }

        /// <summary>The balcony with <paramref name="boxesLeft"/> boxes still in front and the partition pushed <paramref name="push"/> (0..1).</summary>
        public void Partition(int boxesLeft, double push)
        {
            _door.SetActive(false); _balcony.SetActive(true);
            _s.Resume();
            _s.Live = false;
            _s.Aim(BalconyAt + new Vector3(-0.1f, 1.4f, 2.75f), BalconyAt + new Vector3(-0.05f, 0.95f, 0f), 50f);
            for (int i = 0; i < _boxes.Count; i++)
            {
                // the top box goes first (the last one placed), then the two on the floor
                bool on = i < boxesLeft;
                if (_boxes[i].gameObject.activeSelf != on) _boxes[i].gameObject.SetActive(on);
            }
            Push(push);
        }

        public void Push(double push)
        {
            float p = Mathf.Clamp01((float)push);
            int n = Mathf.RoundToInt(p * _cracks.Count);
            for (int i = 0; i < _cracks.Count; i++) if (_cracks[i].activeSelf != (i < n)) _cracks[i].SetActive(i < n);
            _panelMat.SetColor("_BaseColor", Color.Lerp(new Color(0.9f, 0.9f, 0.88f), new Color(0.8f, 0.78f, 0.75f), p * 0.5f));
            if (_dentMat) _dentMat.SetColor("_BaseColor", new Color(0.12f, 0.1f, 0.09f, 0.85f * Mathf.Clamp01(p * 1.6f)));
            bool through = p >= HoleAt;
            if (_hole && _hole.activeSelf != through) _hole.SetActive(through);
            if (through && _hole) _hole.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.15f, (p - HoleAt) / (1f - HoleAt));
            _s.Invalidate();
        }

        /// <summary>How far through the kicking the board breaks through to a hole.</summary>
        public const float HoleAt = 0.8f;

        /// <summary>No picture (the 119 call shows the phone instead).</summary>
        public void Off() => _s.Pause();

        // ------------------------------------------------------------------ smoke

        void LateUpdate()
        {
            if (!_door || !_door.activeSelf || _reduce || !_s.Live) return;
            PlacePuffs(Time.unscaledTime);
        }

        /// <summary>Puffs rise from the gap, drift in toward us and fade; each loops on its own phase.</summary>
        void PlacePuffs(float time)
        {
            var cam = _s.Cam.transform;
            for (int i = 0; i < _puffCount && i < _puffs.Count; i++)
            {
                var (t, m, phase, x) = _puffs[i];
                if (!t.gameObject.activeSelf) continue;
                float k = Mathf.Repeat(phase + time * (_large ? 0.09f : 0.06f), 1f);
                float lift = k * (_large ? 1.2f : 0.7f);
                t.localPosition = new Vector3(x + Mathf.Sin(k * 5f + i) * 0.06f, 0.05f + lift, 0.12f + k * (_large ? 0.5f : 0.3f));
                float size = (_large ? 0.36f : 0.26f) * (0.6f + k * 1.4f);
                t.localScale = new Vector3(size * 1.3f, size, 1);
                t.rotation = Quaternion.LookRotation(t.position - cam.position, Vector3.up) * Quaternion.Euler(0, 0, i * 37f + k * 30f);
                float a = Mathf.Sin(k * Mathf.PI) * (_large ? 0.62f : 0.45f);
                m.SetColor("_BaseColor", new Color(0.72f, 0.72f, 0.74f, a));
            }
        }
    }
}
