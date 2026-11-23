// The CPR screen's picture (competition upgrade, minigame art): the grandfather (the "patient" Rocketbox preset, eyes
// shut, head rolled a little to one side) lying on his back on a shelter mat, and the rescuer kneeling at his right
// side, seen from across him (from his left, a little toward his feet and low, so the rescuer's face and the hands
// fill the frame): 정민 in his red track jacket or, when they take over at the same spot, the neighbour (crowd5, his
// dark hoodie made a blue jumper, 파란 점퍼) or the volunteer (crowd1, his waistcoat made a safety-yellow vest with
// reflective bands, 노란 조끼). The knees rest on the floor against his flank, the trunk leans from the hips until the
// shoulders are straight over the hands, the arms all but locked, the heel of one hand on the lower half of the
// breastbone (measured from his shoulder line) and the other hand stacked on it, its fingers curled over. A press
// lowers the body with the arms kept straight and the chest gives under the hands. The AED arrives as a box by his head;
// its pads go on bare skin (the shirt pulled aside), one below the right collarbone and one on the left side under the
// armpit, their leads run around his head on the mat, clear of the hands. CprScreen drives it.
using UnityEngine;

namespace AiOn.UI.Studio
{
    public sealed class CprStudio : MonoBehaviour
    {
        public const string Me = "jeongmin", Neighbour = "crowd5", Volunteer = "crowd1", Patient = AiOn.People.RocketboxCast.Patient;

        /// <summary>The neighbour's dark brown hoodie (linear value under 0.2, so the lighter skin of the hands stays) as a navy jumper.</summary>
        public static readonly Recolor BlueJumper = new Recolor { FromHue = 0.07f, HueWidth = 0.09f, MinSat = 0.2f, MaxSat = 2f, MinVal = -1f, MaxVal = 0.2f, ToHue = 0.6f, SatScale = 1.15f, ValScale = 1.6f };

        /// <summary>
        /// The volunteer's dark waistcoat (m015's body map: the back, upper middle; the two front panels, lower middle,
        /// where the lighter shirt in the V stays) as a fluorescent safety-yellow vest with two silver reflective bands
        /// across the front and the back.
        /// </summary>
        public static readonly Recolor YellowVest = new Recolor
        {
            FromHue = 0f, HueWidth = 0.5f, MinSat = 0f, MaxSat = 2f, MinVal = -1f, MaxVal = 2f,
            ToHue = 0.165f, SatScale = 0f, SatAdd = 0.88f, ValScale = 5f, ValAdd = 0.5f,
            RectA = new Vector4(0.29f, 0.555f, 0.71f, 1f),      // the back (upside down in the map)
            RectB = new Vector4(0.29f, 0.15f, 0.71f, 0.555f),   // the front panels
            MaxValB = 0.075f,                                   // the shirt in the V is lighter: it stays
            Bands = new Vector4(0.41f, 0.275f, 0.70f, 0.845f), BandHalf = 0.0095f,
        };

        /// <summary>How far a full press goes (m): the guideline's 약 5cm.</summary>
        public const float Depth = 0.05f;
        /// <summary>The wrist bent back so the heel of the hand presses (degrees the fingers rise).</summary>
        public const float WristUp = 22f;

        // the picture's layout (statics so a play-mode check can try values; the shipped values are these)
        /// <summary>The hands' spot: this far from his shoulder line (the top of the breastbone) toward his feet: the middle of
        /// the chest, the lower half of the breastbone, level with the nipples.</summary>
        public static float HandsFromNotch = 0.15f;
        /// <summary>The hands' height on his chest (m above the floor, the shirt's surface over the breastbone).</summary>
        public static float ChestTop = 0.235f;
        /// <summary>The camera, from the hands' spot (set space: +x toward his feet, +z his left, away from the rescuer).</summary>
        public static Vector3 EyeOffset = new(0.55f, 1.0f, 1.25f), LookOffset = new(-0.12f, 0.22f, -0.22f);
        public static float Fov = 42f;
        /// <summary>The framing before anyone presses (the intro, 반응 확인, 도움 요청, 호흡 pages): the grandfather from his
        /// head to his knees, centred. The pressing framing above left him in the lower left corner with no rescuer.</summary>
        public static Vector3 WideEye = new(0.3f, 1.25f, 1.45f), WideLook = new(0.2f, 0.02f, -0.05f);
        public static float WideFov = 40f;
        /// <summary>The top hand's fingers curl down over the lower hand (degrees at each finger joint).</summary>
        public static float TopCurl = 28f;

        MiniStudio _s;
        Animator _patient;
        Rescuer _me, _neighbour, _volunteer, _shown;
        bool _triedNeighbour, _triedVolunteer;
        Transform _pChest, _pNeck, _pHead;
        Transform[] _pChestKids;
        GameObject _aed, _pads;
        Vector3 _chestLocal; // the heel of the lower hand on the breastbone, set space

        // driven each frame by the screen
        string _who = "none";
        float _press, _gasp;
        Vector2 _shake;

        public MiniStudio Studio => _s;
        /// <summary>The patient and 정민 are there (else the screen draws the flat picture).</summary>
        public bool Ready => _patient && _me != null;

        /// <summary>The set for <paramref name="owner"/>, rendering <paramref name="pixels"/>; null when the people prefabs are missing.</summary>
        public static CprStudio Create(Component owner, Vector2Int pixels, bool reduceMotion)
        {
            var s = MiniStudio.Create("Cpr", owner, pixels, new Color(0.035f, 0.04f, 0.045f));
            var me = s.gameObject.AddComponent<CprStudio>();
            me._s = s;
            s.Ambient(new Color(0.055f, 0.066f, 0.07f), new Color(0.035f, 0.045f, 0.05f)); // the shelter's (measured in the level)
            me.Build();
            if (!me.Ready) { Destroy(s.gameObject); return null; }
            s.Live = true; // the presses move every frame
            return me;
        }

        void Build()
        {
            // the shelter floor and the grandfather's mat (돗자리)
            var floor = _s.Lit(new Color(0.55f, 0.56f, 0.56f), 0.18f, 0, StudioTextures.Concrete, new Vector2(9, 9), StudioTextures.ConcreteNormal, 0.6f);
            _s.Part(PrimitiveType.Quad, "Floor", null, Vector3.zero, new Vector3(90, 0, 0), new Vector3(24, 24, 1), floor);
            var mat = _s.Lit(new Color(0.86f, 0.8f, 0.62f), 0.22f, 0, StudioTextures.StrawMat, new Vector2(3, 6), StudioTextures.StrawMatNormal, 0.9f);
            _s.Part(PrimitiveType.Cube, "Mat", null, new Vector3(-0.15f, 0.004f, 0.05f), new Vector3(0, 90, 0), new Vector3(1.05f, 0.008f, 2.1f), mat);
            var blanket = _s.Lit(new Color(0.2f, 0.27f, 0.36f), 0.12f, 0, StudioTextures.Fabric, new Vector2(2, 2), StudioTextures.FabricNormal, 0.7f);
            _s.Part(PrimitiveType.Cube, "Blanket", null, new Vector3(-1.45f, 0.03f, -0.45f), new Vector3(0, 8, 0), new Vector3(0.42f, 0.06f, 0.34f), blanket); // a folded blanket by the mat
            var bag = _s.Lit(new Color(0.16f, 0.17f, 0.15f), 0.2f, 0, StudioTextures.Fabric, Vector2.one, StudioTextures.FabricNormal, 1f);
            _s.Part(PrimitiveType.Cube, "Bag", null, new Vector3(0.95f, 0.11f, 0.78f), new Vector3(0, -14, 0), new Vector3(0.34f, 0.22f, 0.2f), bag);

            // the grandfather on his back: spine along x, head toward -x, his right side toward the rescuer (-z)
            _patient = _s.Puppet(Patient, new Vector3(0.82f, 0, 0), 90f);
            if (!_patient) return;
            var model = _patient.transform;
            model.localRotation = Quaternion.Euler(-90f, 0f, 0f) * model.localRotation; // tipped back about the feet (UnityPerson "lie")
            model.localPosition += new Vector3(0, AiOn.People.UnityPerson.LieLift, 0);
            MiniStudio.Shape(_patient.gameObject, "AK_09_EyeBlinkLeft", 100f);
            MiniStudio.Shape(_patient.gameObject, "AK_10_EyeBlinkRight", 100f);
            _patient.Update(0f);
            _pChest = _patient.GetBoneTransform(HumanBodyBones.UpperChest) ?? _patient.GetBoneTransform(HumanBodyBones.Chest);
            _pNeck = _patient.GetBoneTransform(HumanBodyBones.Neck);
            _pHead = _patient.GetBoneTransform(HumanBodyBones.Head);
            if (_pChest)
            {
                _pChestKids = new Transform[_pChest.childCount];
                for (int i = 0; i < _pChestKids.Length; i++) _pChestKids[i] = _pChest.GetChild(i);
            }
            // the lower half of the breastbone: from the shoulder line (level with the top of the breastbone) toward his feet
            var spineB = _patient.GetBoneTransform(HumanBodyBones.Spine);
            var lSh = _patient.GetBoneTransform(HumanBodyBones.LeftUpperArm); var rSh = _patient.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var notch = _s.Set.InverseTransformPoint(lSh && rSh ? (lSh.position + rSh.position) * 0.5f : _pNeck ? _pNeck.position : _patient.transform.position);
            var toFeet = spineB ? _s.Set.InverseTransformPoint(spineB.position) - notch : Vector3.right;
            toFeet.y = 0; toFeet = toFeet.sqrMagnitude > 1e-6f ? toFeet.normalized : Vector3.right;
            var spot = notch + toFeet * HandsFromNotch;
            _chestLocal = new Vector3(spot.x, ChestTop, spot.z);

            _me = Rescuer.Make(_s, Me, _chestLocal, null);
            if (_me == null) return;
            _me.Root.SetActive(false); // nobody until the screen names who presses
            _shown = null;

            BuildAed(notch, toFeet);
            BuildShelter();

            // light: a cool overhead fluorescent a little toward us, a warm fill low from his head end, a rim behind the rescuer
            _s.Spot("Key", new Vector3(_chestLocal.x - 0.3f, 2.7f, 0.9f), _chestLocal + new Vector3(0, 0.3f, -0.2f), new Color(0.95f, 0.97f, 1f), 10f, 6f, 64f, true);
            _s.Spot("Fill", new Vector3(_chestLocal.x - 1.7f, 0.8f, 0.7f), _chestLocal + new Vector3(0, 0.3f, 0), new Color(1f, 0.86f, 0.7f), 1.8f, 5f, 80f, false);
            _s.Spot("Face", _chestLocal + new Vector3(0.6f, 0.55f, 1.3f), _chestLocal + new Vector3(0, 0.6f, -0.35f), new Color(0.95f, 0.95f, 1f), 1.6f, 5f, 40f, false);
            _s.Spot("Rim", new Vector3(_chestLocal.x + 0.5f, 1.7f, -1.6f), _chestLocal + new Vector3(0, 0.6f, 0), new Color(0.75f, 0.85f, 1f), 4f, 5f, 55f, false);

            Frame(false);
        }

        /// <summary>The pressing framing (true: the rescuer over his chest) or the wider one of him alone (false).</summary>
        public void Frame(bool hands)
        {
            if (hands) _s.Aim(_chestLocal + EyeOffset, _chestLocal + LookOffset, Fov);
            else _s.Aim(_chestLocal + WideEye, _chestLocal + WideLook, WideFov);
        }

        /// <summary>The dim shelter behind the rescuer: a concrete wall and pillar, a strip light, the next family's mat.</summary>
        void BuildShelter()
        {
            var wall = _s.Lit(new Color(0.5f, 0.52f, 0.5f), 0.12f, 0, StudioTextures.Concrete, new Vector2(4, 1.5f), StudioTextures.ConcreteNormal, 0.5f);
            _s.Part(PrimitiveType.Cube, "Wall", null, new Vector3(_chestLocal.x, 1.5f, -2.9f), Vector3.zero, new Vector3(9f, 3f, 0.1f), wall);
            var paint = _s.Lit(new Color(0.24f, 0.4f, 0.34f), 0.25f);
            _s.Part(PrimitiveType.Cube, "WallBand", null, new Vector3(_chestLocal.x, 0.5f, -2.84f), Vector3.zero, new Vector3(9f, 1f, 0.02f), paint); // the painted dado
            _s.Part(PrimitiveType.Cube, "Pillar", null, new Vector3(_chestLocal.x + 1.5f, 1.5f, -2.3f), Vector3.zero, new Vector3(0.6f, 3f, 0.6f), wall);
            _s.Part(PrimitiveType.Cube, "PillarBand", null, new Vector3(_chestLocal.x + 1.5f, 0.5f, -2.3f), Vector3.zero, new Vector3(0.62f, 1f, 0.62f), paint);
            _s.Part(PrimitiveType.Cube, "Exit", null, new Vector3(_chestLocal.x - 1.2f, 2.2f, -2.84f), Vector3.zero, new Vector3(0.4f, 0.16f, 0.02f), _s.Glow(new Color(0.2f, 0.95f, 0.45f), 1.4f), false);
            _s.Part(PrimitiveType.Cube, "Strip", null, new Vector3(_chestLocal.x + 0.2f, 2.75f, -2.8f), Vector3.zero, new Vector3(1.2f, 0.05f, 0.05f), _s.Glow(new Color(0.85f, 0.92f, 1f), 1.2f), false);
            var foil = _s.Lit(new Color(0.42f, 0.44f, 0.47f), 0.35f, 0.5f, StudioTextures.Fabric, new Vector2(4, 4), StudioTextures.FabricNormal, 1.4f);
            _s.Part(PrimitiveType.Cube, "NextMat", null, new Vector3(_chestLocal.x + 0.3f, 0.004f, -1.75f), new Vector3(0, 92, 0), new Vector3(0.95f, 0.008f, 1.9f), foil);
            var roll = _s.Lit(new Color(0.3f, 0.16f, 0.18f), 0.2f, 0, StudioTextures.Fabric, new Vector2(2, 1), StudioTextures.FabricNormal, 1f);
            _s.Part(PrimitiveType.Capsule, "SleepingBag", null, new Vector3(_chestLocal.x - 0.45f, 0.13f, -1.85f), new Vector3(0, 0, 90), new Vector3(0.26f, 0.32f, 0.26f), roll);
            var slipper = _s.Lit(new Color(0.22f, 0.3f, 0.38f), 0.3f);
            _s.Part(PrimitiveType.Cube, "SlipperL", null, new Vector3(_chestLocal.x + 1.2f, 0.015f, -1.05f), new Vector3(0, 12, 0), new Vector3(0.1f, 0.03f, 0.26f), slipper);
            _s.Part(PrimitiveType.Cube, "SlipperR", null, new Vector3(_chestLocal.x + 1.33f, 0.015f, -1.02f), new Vector3(0, 4, 0), new Vector3(0.1f, 0.03f, 0.26f), slipper);
            var bottle = _s.Clear(new Color(0.75f, 0.85f, 0.95f, 0.45f), 0.9f);
            _s.Part(PrimitiveType.Cylinder, "Bottle", null, new Vector3(_chestLocal.x - 0.9f, 0.1f, -1.4f), Vector3.zero, new Vector3(0.07f, 0.1f, 0.07f), bottle, false);
        }

        /// <summary>
        /// The AED: the box on the floor by his head on the rescuer's side; the two pads on bare skin (the shirt pulled
        /// aside under each), one below his right collarbone and one on his left side under the armpit, facing out; the
        /// leads from the box around his head on the mat, never across his chest.
        /// </summary>
        void BuildAed(Vector3 notch, Vector3 toFeet)
        {
            var aedBody = _s.Lit(new Color(0.12f, 0.45f, 0.3f), 0.45f);
            var aedFace = _s.Lit(new Color(0.9f, 0.88f, 0.82f), 0.5f);
            var boxAt = notch + toFeet * -0.6f + new Vector3(0, 0, -0.34f);
            _aed = _s.Empty("Aed", null, new Vector3(boxAt.x, 0, boxAt.z), new Vector3(0, 18, 0));
            _s.Part(PrimitiveType.Cube, "Case", _aed.transform, new Vector3(0, 0.045f, 0), Vector3.zero, new Vector3(0.28f, 0.09f, 0.24f), aedBody);
            _s.Part(PrimitiveType.Cube, "Face", _aed.transform, new Vector3(0, 0.091f, 0.01f), Vector3.zero, new Vector3(0.24f, 0.004f, 0.17f), aedFace);
            _s.Part(PrimitiveType.Cylinder, "Button", _aed.transform, new Vector3(0.05f, 0.095f, 0.03f), Vector3.zero, new Vector3(0.05f, 0.004f, 0.05f), _s.Glow(new Color(1f, 0.45f, 0.1f), 2.2f));
            _s.Part(PrimitiveType.Cylinder, "Power", _aed.transform, new Vector3(-0.07f, 0.095f, 0.03f), Vector3.zero, new Vector3(0.035f, 0.004f, 0.035f), _s.Glow(new Color(0.3f, 1f, 0.5f), 2f));
            _aed.SetActive(false);

            _pads = _s.Empty("Pads", null, Vector3.zero, Vector3.zero);
            var p = _pads.transform;
            var side = Vector3.Cross(Vector3.up, toFeet).normalized; // toward his right (the rescuer's side, -z)
            var skin = _s.Lit(new Color(0.66f, 0.47f, 0.38f), 0.36f);
            var pad = _s.Lit(new Color(0.95f, 0.95f, 0.93f), 0.3f);
            var print = _s.Lit(new Color(0.75f, 0.18f, 0.16f), 0.3f);
            float yaw = Mathf.Atan2(toFeet.x, toFeet.z) * Mathf.Rad2Deg;
            // his right upper chest, below the collarbone: nearly flat, rolled a little toward his right
            var aPos = notch + toFeet * 0.075f + side * 0.07f; aPos.y = ChestTop + 0.012f; // the upper chest (and the jacket bunched at it) stands higher than the lower breastbone
            var aRot = Quaternion.Euler(0, yaw, 0) * Quaternion.AngleAxis(-10f, Vector3.forward);
            Pad("PadA", p, aPos, aRot, new Vector2(0.115f, 0.08f), skin, pad, print);
            // his left side under the armpit (the far side from the rescuer, facing us): steep, facing out and up
            var bPos = notch + toFeet * 0.17f - side * 0.16f; bPos.y = ChestTop - 0.085f;
            var bRot = Quaternion.Euler(0, yaw, 0) * Quaternion.AngleAxis(62f, Vector3.forward);
            Pad("PadB", p, bPos, bRot, new Vector2(0.08f, 0.115f), skin, pad, print);
            // the leads: from the box's socket down to the mat, around his head, and up to each pad
            var lead = _s.Lit(new Color(0.08f, 0.08f, 0.09f), 0.4f);
            var socket = _s.Set.InverseTransformPoint(_aed.transform.TransformPoint(new Vector3(0.12f, 0.06f, 0.06f)));
            var head = notch - toFeet * 0.4f;
            Lead("LeadA", p, lead, new[]
            {
                socket, new Vector3(socket.x + 0.06f, 0.012f, socket.z + 0.04f),
                new Vector3(aPos.x - 0.04f, 0.012f, aPos.z - 0.12f) + side * 0.12f, aPos + side * 0.06f + Vector3.up * 0.002f,
            });
            Lead("LeadB", p, lead, new[]
            {
                socket, new Vector3(socket.x + 0.03f, 0.012f, socket.z + 0.06f),
                new Vector3(head.x - 0.16f, 0.012f, notch.z), new Vector3(head.x + 0.05f, 0.012f, notch.z - side.z * 0.32f),
                bPos - side * 0.06f + new Vector3(0, -0.06f, 0), bPos - side * 0.01f,
            });
            _pads.SetActive(false);
        }

        /// <summary>A pad on bare skin: the skin where the shirt is pulled aside, the thin gel pad on it with its printed outline.</summary>
        void Pad(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 size, Material skin, Material pad, Material print)
        {
            var g = _s.Empty(name, parent, pos, Vector3.zero);
            g.transform.localRotation = rot;
            _s.Part(PrimitiveType.Sphere, "Skin", g.transform, new Vector3(0, -0.009f, 0), Vector3.zero, new Vector3(size.x * 1.35f, 0.028f, size.y * 1.4f), skin, false);
            _s.Part(PrimitiveType.Cube, "Gel", g.transform, new Vector3(0, 0.0045f, 0), Vector3.zero, new Vector3(size.x, 0.0025f, size.y), pad, false);
            _s.Part(PrimitiveType.Cube, "Mark", g.transform, new Vector3(0, 0.0062f, 0), Vector3.zero, new Vector3(size.x * 0.45f, 0.0008f, size.y * 0.12f), print, false);
        }

        /// <summary>A pad's lead through <paramref name="points"/> (set space), smoothed (Catmull-Rom) into short tube segments.</summary>
        void Lead(string name, Transform parent, Material mat, Vector3[] points)
        {
            var g = _s.Empty(name, parent, Vector3.zero, Vector3.zero);
            var prev = points[0];
            const int per = 5;
            for (int i = 0; i < points.Length - 1; i++)
                for (int k = 1; k <= per; k++)
                {
                    float t = k / (float)per;
                    var p0 = points[Mathf.Max(0, i - 1)]; var p1 = points[i]; var p2 = points[i + 1]; var p3 = points[Mathf.Min(points.Length - 1, i + 2)];
                    var q = 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (3f * p1 - p0 - 3f * p2 + p3) * (t * t * t));
                    var d = q - prev;
                    if (d.sqrMagnitude < 1e-8f) continue;
                    var seg = _s.Part(PrimitiveType.Cylinder, "Seg", g.transform, (prev + q) * 0.5f, Vector3.zero, new Vector3(0.008f, d.magnitude * 0.5f + 0.002f, 0.008f), mat, false);
                    seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                    prev = q;
                }
        }

        /// <summary>Where the hands go on the picture, 0..1 from the lower left (the screen's beat ring sits there).</summary>
        public Vector2 ChestOnView => _s.ViewportOf(_chestLocal + new Vector3(0, 0.04f, -0.01f));

        /// <summary>Who presses ("me", "neighbour", "volunteer", "none"), how far down (0..1), the tremble (m) and the gasp (0..1).</summary>
        public void Drive(string who, float press, Vector2 shake, float gasp)
        {
            _who = who ?? "none";
            _press = Mathf.Clamp01(press);
            _shake = shake;
            _gasp = Mathf.Clamp01(gasp);
        }

        public void ShowAed(bool box, bool pads)
        {
            if (_aed && _aed.activeSelf != box) _aed.SetActive(box);
            if (_pads && _pads.activeSelf != pads) _pads.SetActive(pads);
        }

        /// <summary>The rescuer for <paramref name="who"/>; the neighbour and the volunteer are only built the first time they take over.</summary>
        Rescuer For(string who)
        {
            switch (who)
            {
                case "me": return _me;
                case "neighbour":
                    if (_neighbour == null && !_triedNeighbour) { _triedNeighbour = true; _neighbour = Rescuer.Make(_s, Neighbour, _chestLocal, BlueJumper); }
                    return _neighbour ?? _me; // 정민's own body if the neighbour's prefab is missing
                case "volunteer":
                    if (_volunteer == null && !_triedVolunteer) { _triedVolunteer = true; _volunteer = Rescuer.Make(_s, Volunteer, _chestLocal, YellowVest); }
                    return _volunteer ?? _me;
                default: return null;
            }
        }

        void Show(Rescuer r)
        {
            if (r == _shown) return;
            if (_me != null) _me.Root.SetActive(r == _me);
            if (_neighbour != null) _neighbour.Root.SetActive(r == _neighbour);
            if (_volunteer != null) _volunteer.Root.SetActive(r == _volunteer);
            _shown = r;
        }

        void LateUpdate()
        {
            if (!_patient) return;
            var active = For(_who);
            Show(active);
            float drop = _press * Depth;

            // the chest gives under the hands (the chest bone down, its children kept in place), and a gasp lifts it
            if (_pChest)
            {
                float dy = -drop * 0.9f + _gasp * 0.012f;
                if (Mathf.Abs(dy) > 1e-5f)
                {
                    var up = _s.Set.up;
                    _pChest.position += up * dy;
                    foreach (var k in _pChestKids) if (k) k.position -= up * dy;
                }
            }
            // slack: the head rolled a little toward the camera about the spine (LieSlump's 22°, the neck a third)
            if (_pHead && _pNeck && _pChest)
            {
                var axis = (_pHead.position - _pChest.position).normalized;
                _pNeck.rotation = Quaternion.AngleAxis(AiOn.People.UnityPerson.LieHeadRoll / 3f, axis) * _pNeck.rotation;
                _pHead.rotation = Quaternion.AngleAxis(AiOn.People.UnityPerson.LieHeadRoll * 2f / 3f, axis) * _pHead.rotation;
            }

            if (active != null)
            {
                var target = _chestLocal + new Vector3(_shake.x, -drop + _shake.y, 0);
                active.Pose(_s.Set, _s.Set.TransformPoint(target), drop);
            }
        }

        /// <summary>A recolour of a body map's clothes (Hidden/AiOn/HueSwap's window, target and optional map regions, linear values).</summary>
        public struct Recolor
        {
            public float FromHue, HueWidth, MinSat, MaxSat, MinVal, MaxVal, ToHue, SatScale, SatAdd, ValScale, ValAdd;
            /// <summary>Map regions (u min, v min, u max, v max): when RectA has area only pixels in A, or in B under MaxValB, change.</summary>
            public Vector4 RectA, RectB;
            public float MaxValB;
            /// <summary>Up to four v positions of silver bands (reflective tape) inside the changed pixels, BandHalf wide each way.</summary>
            public Vector4 Bands;
            public float BandHalf;
        }

        /// <summary>A kneeling rescuer: the knees on the floor by his flank, the trunk leaning over the chest, the arms reaching by IK.</summary>
        sealed class Rescuer
        {
            public GameObject Root;
            float _lean, _trunk;
            Transform _hips, _spine, _neck;
            Transform _lUp, _lLo, _lHand, _rUp, _rLo, _rHand;
            Transform _lMid, _lIdx, _lPinky, _rMid, _rIdx, _rPinky;
            Transform[] _topFingers; // the top (right) hand's finger joints, proximal to distal
            Leg _lLeg, _rLeg;
            Vector3 _base;

            /// <summary>One leg's bones and where it kneels.</summary>
            sealed class Leg
            {
                public Transform Thigh, Calf, Foot, Toes;
                public Vector3 Knee, Ankle; // set space, on the floor

                public static Leg Of(Animator an, bool left)
                {
                    var l = new Leg
                    {
                        Thigh = an.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg),
                        Calf = an.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg),
                        Foot = an.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot),
                        Toes = an.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes),
                    };
                    return l.Thigh && l.Calf && l.Foot ? l : null;
                }

                /// <summary>The knee down on its spot, the shin back along the floor, the top of the foot flat behind it.</summary>
                public void Kneel(Transform set, Vector3 fwd, Vector3 up)
                {
                    var knee = set.TransformPoint(Knee);
                    Arm(Thigh, Calf, Foot, set.TransformPoint(Ankle), knee + fwd * 0.4f);
                    if (!Toes) return;
                    var want = (-fwd * Mathf.Cos(24f * Mathf.Deg2Rad) - up * Mathf.Sin(24f * Mathf.Deg2Rad)).normalized;
                    var have = Toes.position - Foot.position;
                    if (have.sqrMagnitude > 1e-8f) Foot.rotation = Quaternion.FromToRotation(have, want) * Foot.rotation;
                }
            }

            public static Rescuer Make(MiniStudio s, string preset, Vector3 chest, Recolor? hue)
            {
                // kneeling at his right side (-z), facing across him (+z)
                var an = s.Puppet(preset, Vector3.zero, 0f);
                if (!an) return null;
                var r = new Rescuer { Root = an.transform.parent.parent.gameObject };
                r._hips = an.GetBoneTransform(HumanBodyBones.Hips);
                r._spine = an.GetBoneTransform(HumanBodyBones.Spine);
                r._neck = an.GetBoneTransform(HumanBodyBones.Neck);
                r._lUp = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); r._lLo = an.GetBoneTransform(HumanBodyBones.LeftLowerArm); r._lHand = an.GetBoneTransform(HumanBodyBones.LeftHand);
                r._rUp = an.GetBoneTransform(HumanBodyBones.RightUpperArm); r._rLo = an.GetBoneTransform(HumanBodyBones.RightLowerArm); r._rHand = an.GetBoneTransform(HumanBodyBones.RightHand);
                r._lMid = an.GetBoneTransform(HumanBodyBones.LeftMiddleProximal); r._lIdx = an.GetBoneTransform(HumanBodyBones.LeftIndexProximal); r._lPinky = an.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
                r._rMid = an.GetBoneTransform(HumanBodyBones.RightMiddleProximal); r._rIdx = an.GetBoneTransform(HumanBodyBones.RightIndexProximal); r._rPinky = an.GetBoneTransform(HumanBodyBones.RightLittleProximal);
                r._topFingers = new[]
                {
                    an.GetBoneTransform(HumanBodyBones.RightIndexProximal), an.GetBoneTransform(HumanBodyBones.RightMiddleProximal),
                    an.GetBoneTransform(HumanBodyBones.RightRingProximal), an.GetBoneTransform(HumanBodyBones.RightLittleProximal),
                    an.GetBoneTransform(HumanBodyBones.RightIndexIntermediate), an.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate),
                    an.GetBoneTransform(HumanBodyBones.RightRingIntermediate), an.GetBoneTransform(HumanBodyBones.RightLittleIntermediate),
                };
                r._lLeg = Leg.Of(an, true); r._rLeg = Leg.Of(an, false);
                if (!r._hips || !r._spine || !r._lUp || !r._rUp || !r._lLo || !r._rLo || !r._lHand || !r._rHand || !r._lMid || !r._rMid || r._lLeg == null || r._rLeg == null)
                { Object.Destroy(r.Root); return null; }

                // measure the standing body, then kneel: the hips where the legs on the floor and the straight arms agree
                var shoulders = (r._lUp.position + r._rUp.position) * 0.5f;
                var hipJoints = (r._lLeg.Thigh.position + r._rLeg.Thigh.position) * 0.5f;
                float trunk = Vector3.Distance(hipJoints, shoulders);
                float arm = Vector3.Distance(r._lUp.position, r._lLo.position) + Vector3.Distance(r._lLo.position, r._lHand.position); // to the wrist
                float half = Vector3.Distance(r._lUp.position, r._rUp.position) * 0.5f;
                float thigh = Vector3.Distance(r._lLeg.Thigh.position, r._lLeg.Calf.position);
                float shin = Vector3.Distance(r._lLeg.Calf.position, r._lLeg.Foot.position);
                const float kneeY = 0.055f, ankleY = 0.075f;
                // the shoulders over the lower wrist (WristFor: back from the heel and up by the hand); the knees come up
                // against his right side, about 0.2 m from the middle of his chest
                var wrist = WristFor(chest, Fingers(Vector3.forward, Vector3.up));
                var k = StudioMath.Kneel(trunk, arm, half, thigh, wrist.y, 0.21f - (chest.z - wrist.z), kneeY);
                r._lean = k.Lean; r._trunk = trunk;
                float hipNow = s.Set.InverseTransformPoint(hipJoints).y;
                float hipBack = s.Set.InverseTransformPoint(hipJoints).z - s.Set.InverseTransformPoint(r.Root.transform.position).z;
                r._base = new Vector3(chest.x, k.HipHeight - hipNow, wrist.z - k.Back - hipBack);
                r.Root.transform.localPosition = r._base;
                // the knees on the floor in front of the hip joints, a little apart; the shins back along the floor
                foreach (var leg in new[] { r._lLeg, r._rLeg })
                {
                    var hj = s.Set.InverseTransformPoint(leg.Thigh.position);
                    float sideways = (hj.x - chest.x) * 1.5f;
                    leg.Knee = new Vector3(chest.x + sideways, kneeY, hj.z + k.KneeForward);
                    float flat = Mathf.Sqrt(Mathf.Max(0.01f, shin * shin - (ankleY - kneeY) * (ankleY - kneeY)));
                    leg.Ankle = new Vector3(chest.x + sideways * 1.15f, ankleY, leg.Knee.z - flat);
                }

                if (hue.HasValue) Recolour(s, an.gameObject, hue.Value);
                s.HairFromAbove(an.gameObject);
                return r;
            }

            public void Pose(Transform set, Vector3 heel, float drop)
            {
                var root = Root.transform;
                // a press comes mostly from the hips (the trunk hinges further forward, the knees stay put), a little from
                // the whole body sinking; the arms stay straight either way
                root.localPosition = _base + Vector3.down * (drop * 0.35f);
                float cos = Mathf.Cos(_lean * Mathf.Deg2Rad) - drop * 0.65f / _trunk;
                float lean = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;
                var right = root.right;
                var fwd = root.forward;
                var up = Vector3.up;
                // the trunk leans from the hip joints (the pelvis turns), then the legs reach back to the knees on the floor
                _hips.rotation = Quaternion.AngleAxis(lean, right) * _hips.rotation;
                _lLeg.Kneel(set, fwd, up);
                _rLeg.Kneel(set, fwd, up);
                if (_neck) _neck.rotation = Quaternion.AngleAxis(12f, right) * _neck.rotation; // looks down at the hands

                // the heel of the left hand on the spot, the right hand stacked straight on top of it, fingers raised
                var fingers = Fingers(fwd, up);
                var lowerHeel = heel;
                var upperHeel = heel + up * 0.027f + fwd * 0.004f;
                Arm(_lUp, _lLo, _lHand, WristFor(lowerHeel, fingers), _lUp.position + right * -0.45f - fwd * 0.25f);
                Arm(_rUp, _rLo, _rHand, WristFor(upperHeel, fingers), _rUp.position + right * 0.45f - fwd * 0.25f);
                Orient(_lLo, _lHand, _lMid, _lIdx, _lPinky, fingers, right);   // left thumb to the right
                Orient(_rLo, _rHand, _rMid, _rIdx, _rPinky, fingers, -right);  // right thumb to the left
                // the top hand's fingers curl down over the lower hand's back (interlaced)
                var bend = Quaternion.AngleAxis(TopCurl, right);
                foreach (var f in _topFingers)
                    if (f) f.rotation = bend * f.rotation;
            }

            /// <summary>Where the fingers point: across him, raised by the bent-back wrist.</summary>
            static Vector3 Fingers(Vector3 fwd, Vector3 up) => (fwd * Mathf.Cos(WristUp * Mathf.Deg2Rad) + up * Mathf.Sin(WristUp * Mathf.Deg2Rad)).normalized;

            /// <summary>The wrist joint for a heel of the hand at <paramref name="heel"/>: a little back along the fingers and up by the hand's thickness.</summary>
            static Vector3 WristFor(Vector3 heel, Vector3 fingers) => heel - fingers * 0.03f + Vector3.up * 0.028f;

            /// <summary>Two-bone reach: <paramref name="up"/> and <paramref name="lo"/> turned so <paramref name="end"/> meets <paramref name="target"/>, the middle joint bent toward <paramref name="pole"/>.</summary>
            static void Arm(Transform up, Transform lo, Transform end, Vector3 target, Vector3 pole)
            {
                float la = Vector3.Distance(up.position, lo.position), lb = Vector3.Distance(lo.position, end.position);
                var mid = StudioMath.Elbow(up.position, target, la, lb, pole, out _);
                up.rotation = Quaternion.FromToRotation(lo.position - up.position, mid - up.position) * up.rotation;
                lo.rotation = Quaternion.FromToRotation(end.position - lo.position, target - lo.position) * lo.rotation;
            }

            /// <summary>Turns the hand palm down, fingers along <paramref name="fingers"/>, thumb toward <paramref name="thumb"/>; the turn about the forearm goes into the forearm.</summary>
            static void Orient(Transform lo, Transform hand, Transform mid, Transform idx, Transform pinky, Vector3 fingers, Vector3 thumb)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    var f = (mid.position - hand.position).normalized;
                    var s = idx && pinky ? (idx.position - pinky.position).normalized : Vector3.Cross(f, Vector3.up);
                    var want = Quaternion.LookRotation(fingers, Vector3.Cross(fingers, thumb));
                    var have = Quaternion.LookRotation(f, Vector3.Cross(f, s));
                    var delta = want * Quaternion.Inverse(have);
                    if (pass == 0) lo.rotation = StudioMath.Twist(delta, hand.position - lo.position) * lo.rotation;
                    else hand.rotation = delta * hand.rotation;
                }
            }

            static Material _hueMat;

            /// <summary>The clothes recoloured (the body map through Hidden/AiOn/HueSwap into a texture the studio owns, at the map's own size).</summary>
            static void Recolour(MiniStudio s, GameObject body, Recolor hue)
            {
                if (!_hueMat)
                {
                    var sh = Resources.Load<Shader>("Shaders/AiOnHueSwap");
                    if (!sh) return;
                    _hueMat = StudioShared.Keep(new Material(sh), "HueSwap");
                }
                foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mats = smr.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (!m || !m.name.EndsWith("_body") || !m.HasProperty("_BaseMap")) continue;
                        var src = m.GetTexture("_BaseMap");
                        if (!src) continue;
                        var rt = new RenderTexture(Mathf.Clamp(src.width, 64, 2048), Mathf.Clamp(src.height, 64, 2048), 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                        { useMipMap = true, autoGenerateMips = true, name = "ClothesRecolour", anisoLevel = 4 };
                        rt.Create();
                        _hueMat.SetFloat("_FromHue", hue.FromHue);
                        _hueMat.SetFloat("_HueWidth", hue.HueWidth);
                        _hueMat.SetFloat("_MinSat", hue.MinSat);
                        _hueMat.SetFloat("_MaxSat", hue.MaxSat);
                        _hueMat.SetFloat("_MinVal", hue.MinVal);
                        _hueMat.SetFloat("_MaxVal", hue.MaxVal);
                        _hueMat.SetFloat("_ToHue", hue.ToHue);
                        _hueMat.SetFloat("_SatScale", hue.SatScale);
                        _hueMat.SetFloat("_SatAdd", hue.SatAdd);
                        _hueMat.SetFloat("_ValScale", hue.ValScale);
                        _hueMat.SetFloat("_ValAdd", hue.ValAdd);
                        bool rects = hue.RectA.z > hue.RectA.x;
                        _hueMat.SetFloat("_UseRects", rects ? 1f : 0f);
                        _hueMat.SetVector("_RectA", hue.RectA);
                        _hueMat.SetVector("_RectB", hue.RectB);
                        _hueMat.SetFloat("_MaxValB", hue.MaxValB);
                        _hueMat.SetVector("_BandV", hue.Bands);
                        _hueMat.SetFloat("_BandHalf", hue.BandHalf);
                        Graphics.Blit(src, rt, _hueMat);
                        s.Own(rt);
                        var copy = s.Own(new Material(m) { name = m.name + "_recolour" });
                        copy.SetTexture("_BaseMap", rt);
                        mats[i] = copy;
                        changed = true;
                    }
                    if (changed) smr.sharedMaterials = mats;
                }
            }

        }
    }
}
