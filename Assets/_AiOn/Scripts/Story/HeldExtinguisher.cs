// 3D 소화기 (competition upgrade): the extinguisher in 정민's hands, first person. The CC0 한국형 소화기 model
// (Poly Haven korean_fire_extinguisher_01, made into Resources/Props/KoreanExtinguisher by AiOn → Props → Build
// Extinguisher Prefab) sits low in the bottom right with a hose to the nozzle; the nozzle follows the view.
// Input (hold inputs become toggles when the setup says so):
//   pin      drag the mouse down with the left button held (the view stays still meanwhile), or hold R / gamepad LB
//   handle   the left mouse button (after the pin is out and the button was let go once), or hold Space / gamepad RT
//            (jumping is off meanwhile; F stays the flashlight)
//   sweep    move the view left and right across the base of the fire while squeezing
//   T        "불이야!"     Q twice / gamepad Select twice   물러나 알리기 (one press asks)     Y   hint (H stays 조작법)
// While it is held: the general key strip, the prompts of the fire and the extinguisher points, and PlayerHands' keys
// stand aside (what was in hand is set down and falls as usual); the view, chalk and first-aid keys wait (ctx.HandsFull).
// Each frame it measures the aim (HeldExtinguisherLogic.Aim against the fire's base and flames, a wall in the way
// blocks it), the distance, whether the exit is behind, and hands that to the logic as its Input; the story ticks the
// logic (ExtinguisherScene.Run3D). It draws what the logic says: the powder cone and the puff where it lands, the fire's
// level (FireFx.SetLevel), extra smoke when the exit is not behind, the HUD (powder left, the fire, the exit, keys).
using System;
using AiOn.Game;
using AiOn.Interaction;
using AiOn.Levels;
using AiOn.Player;
using AiOn.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.Story
{
    [DefaultExecutionOrder(1001)] // after the camera rig placed the camera (the model rides on it)
    public class HeldExtinguisher : MonoBehaviour, IDisposable
    {
        public const string PrefabPath = "Props/KoreanExtinguisher";
        /// <summary>Where the extinguisher hangs in view (camera space, metres) and where the nozzle is.</summary>
        static readonly Vector3 HoldAt = new(0.3f, -0.68f, 0.74f), NozzleAt = new(0.05f, -0.22f, 0.72f);
        /// <summary>The model's scale in hand (it is held low, so only the handle, the gauge and the hose show).</summary>
        const float HoldScale = 0.9f;
        /// <summary>Mouse pixels of downward drag for a full pull of the pin.</summary>
        const float PinPixels = 170f;
        /// <summary>Seconds of a held key for a full pull.</summary>
        const float PinHold = 0.6f;

        UnityStoryHost _host;
        HeldExtinguisherLogic _logic;
        ExtinguisherScene.Place _place;
        Camera _cam;
        Transform _model, _pin, _nozzle;
        LineRenderer _hose;
        ParticleSystem _powder, _puff;
        FireFx _fire, _ownFire;
        SmokeFx _smoke;
        float _smokeBase, _smokeSet = -1, _fxShown = -1;
        Vector3 _fireAt, _exitAt;
        PlayerHands _hands;
        bool _handsKeysWere;
        float _sens = -1;
        bool _dragging, _needRelease, _toggleOn, _hintOpen, _disposed;
        AudioSource _hiss;
        float _lightWas = 1f;
        bool _jumpWas;
        Light _fill;
        float _recoil;

        // HUD
        Canvas _canvas;
        RectTransform _powderFill, _fireFill, _pinFill, _pinBar;
        Image _powderImg, _fireImg, _reticle;
        Text _powderText, _fireText, _exitText, _hint, _keys;
        int _keysShown = -1, _fireShown = -1;

        public HeldExtinguisherLogic Logic => _logic;

        public static HeldExtinguisher Begin(UnityStoryHost host, HeldExtinguisherLogic logic, ExtinguisherScene.Place place)
        {
            var go = new GameObject("HeldExtinguisher");
            var h = go.AddComponent<HeldExtinguisher>();
            h.Init(host, logic, place);
            return h;
        }

        void Init(UnityStoryHost host, HeldExtinguisherLogic logic, ExtinguisherScene.Place place)
        {
            _host = host; _logic = logic; _place = place;
            _cam = host && host.cameraRig ? host.cameraRig.cam : Camera.main;
            _fireAt = LevelLayout.ToUnity((float)place.FireX, (float)place.FireY, (float)place.FireZ);
            _exitAt = LevelLayout.ToUnity((float)place.ExitX, (float)place.ExitY, (float)place.ExitZ);
            if (logic.Practice || host == null || host.StoryFire == null)
            {
                // practice: a translucent teal stand-in fire (no real flames in 1장)
                _ownFire = StoryFx.CreateFire(transform, "PracticeFire", _fireAt, (float)place.Scale);
                _ownFire.Tint(new Color(0.35f, 0.95f, 0.85f, 0.55f), new Color(0.6f, 1f, 0.95f, 0.45f));
                _ownFire.LightScale = 0.2f;
                _fire = _ownFire;
                // an empty box under it: the base to aim at
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(box.GetComponent<Collider>());
                box.name = "PracticeBox";
                box.transform.SetParent(transform, false);
                box.transform.position = _fireAt + Vector3.up * 0.2f * (float)place.Scale;
                box.transform.localScale = new Vector3(0.55f, 0.4f, 0.45f) * (float)place.Scale;
                box.GetComponent<Renderer>().material.color = new Color(0.62f, 0.47f, 0.3f);
            }
            else { _fire = host.StoryFire; _lightWas = _fire.LightScale; _fire.LightScale = 0.3f; } // up close: less glare on the boxes
            _smoke = host ? host.StorySmoke : null;
            _smokeBase = _smoke ? _smoke.Density : 0;
            _hands = host ? host.GetComponent<PlayerHands>() : null;
            if (!_hands) _hands = FindFirstObjectByType<PlayerHands>();
            // G/Q stay off while both hands hold it; what was in hand is set down and falls (PlayerHands keeps running)
            if (_hands) { _hands.Drop(); _handsKeysWere = _hands.SuppressKeys; _hands.SuppressKeys = true; }
            foreach (var id in ExtinguisherScene.MutedPoints) PlayerInteractor.Muted.Add(id);
            UiKeys.StripSuppressed = true;
            if (host && host.player) { _jumpWas = host.player.canJump; host.player.canJump = false; } // Space is the handle
            BuildModel();
            BuildHud();
            if (_fire) { _fxShown = FxLevel(logic.FireLv); _fire.SetLevel(_fxShown); }
        }

        /// <summary>
        /// The drawn fire for the logic's level: never below a clearly visible flame while it burns, so the shrinking shows
        /// on the fire itself (0.36 at the start reads about 0.57, nearly out about 0.32), and none once out.
        /// </summary>
        static float FxLevel(double lv) => lv <= 0.02 ? 0f : Mathf.Clamp01(0.3f + 0.75f * (float)lv);

        // ---------------------------------------------------------------- model and particles

        void BuildModel()
        {
            var root = new GameObject("Extinguisher").transform;
            root.SetParent(transform, false);
            _model = root;
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab)
            {
                var m = Instantiate(prefab, root);
                m.transform.localPosition = Vector3.zero;
                m.transform.localRotation = Quaternion.Euler(0, 200f, 0); // the handle and hose towards the view's left
                foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
                foreach (var r in m.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else
            {
                // no prefab built yet: a red cylinder stands in
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(root, false);
                c.transform.localScale = new Vector3(0.24f, 0.28f, 0.24f);
                c.transform.localPosition = new Vector3(0, 0.28f, 0);
                c.GetComponent<Renderer>().material.color = new Color(0.75f, 0.08f, 0.06f);
            }
            // the safety pin: a small yellow ring on the handle (gone once pulled)
            var pin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(pin.GetComponent<Collider>());
            pin.name = "Pin";
            pin.transform.SetParent(root, false);
            pin.transform.localPosition = new Vector3(0.05f, 0.6f, 0.02f);
            pin.transform.localRotation = Quaternion.Euler(0, 0, 90);
            pin.transform.localScale = new Vector3(0.06f, 0.006f, 0.06f);
            var pr = pin.GetComponent<Renderer>();
            pr.material.color = new Color(1f, 0.82f, 0.1f);
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _pin = pin.transform;

            _nozzle = new GameObject("Nozzle").transform;
            _nozzle.SetParent(transform, false);
            var nz = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(nz.GetComponent<Collider>());
            nz.transform.SetParent(_nozzle, false);
            nz.transform.localRotation = Quaternion.Euler(90, 0, 0);
            nz.transform.localScale = new Vector3(0.028f, 0.05f, 0.028f);
            nz.transform.localPosition = new Vector3(0, 0, -0.05f);
            var nr = nz.GetComponent<Renderer>();
            nr.material.color = new Color(0.08f, 0.08f, 0.09f);
            nr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _fill = new GameObject("HandLight").AddComponent<Light>();
            _fill.transform.SetParent(transform, false);
            _fill.type = LightType.Point; _fill.range = 1.1f; _fill.intensity = 0.5f; _fill.color = new Color(1f, 0.93f, 0.85f); _fill.shadows = LightShadows.None;

            _hose = new GameObject("Hose").AddComponent<LineRenderer>();
            _hose.transform.SetParent(transform, false);
            _hose.positionCount = 6; _hose.widthMultiplier = 0.022f; _hose.useWorldSpace = true;
            _hose.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.07f, 0.07f, 0.08f) };
            _hose.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _powder = Particles("Powder", 1400);
            var main = _powder.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 9.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startColor = new Color(0.96f, 0.96f, 0.94f, 0.5f);
            main.gravityModifier = 0.2f;
            var sh = _powder.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.015f;
            var sol = _powder.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 7f));
            var col = _powder.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.92f, 0.92f, 0.9f), 1) },
                new[] { new GradientAlphaKey(0.6f, 0), new GradientAlphaKey(0.3f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var cl = _powder.collision; cl.enabled = true; cl.type = ParticleSystemCollisionType.World; cl.mode = ParticleSystemCollisionMode.Collision3D;
            cl.dampen = 0.7f; cl.bounce = 0.05f; cl.lifetimeLoss = 0.3f; cl.quality = ParticleSystemCollisionQuality.Low; cl.radiusScale = 0.3f;

            _puff = Particles("Puff", 300);
            var pm = _puff.main;
            pm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);
            pm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            pm.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            pm.startColor = new Color(0.95f, 0.95f, 0.93f, 0.4f);
            pm.gravityModifier = -0.02f;
            var ps = _puff.shape; ps.shapeType = ParticleSystemShapeType.Sphere; ps.radius = 0.3f;
            var pc = _puff.colorOverLifetime; pc.enabled = true;
            var pg = new Gradient();
            pg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.5f, 0.2f), new GradientAlphaKey(0, 1) });
            pc.color = pg;
        }

        ParticleSystem Particles(string name, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = max; main.playOnAwake = false;
            var em = ps.emission; em.rateOverTime = 0;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = StoryFx.SmokeMat;
            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- HUD

        void BuildHud()
        {
            _canvas = UiKit.ScreenCanvas("ExtinguisherHud", 120);
            _canvas.transform.SetParent(transform, false);
            foreach (var gr in _canvas.GetComponents<GraphicRaycaster>()) gr.enabled = false; // nothing here is clicked
            var root = (RectTransform)_canvas.transform;

            // the nozzle's aim: a ring at the centre (teal on the base, amber on the flames, dim elsewhere)
            _reticle = UiKit.Box("Aim", root, new Color(1, 1, 1, 0.5f));
            _reticle.sprite = HudDraw.RingSprite;
            var rr = _reticle.rectTransform;
            rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(46, 46);

            // the pin's pull (while dragging)
            _pinBar = UiKit.Box("PinBar", root, HudTheme.Track).rectTransform;
            _pinBar.anchorMin = _pinBar.anchorMax = _pinBar.pivot = new Vector2(0.5f, 0.5f);
            _pinBar.anchoredPosition = new Vector2(0, -70); _pinBar.sizeDelta = new Vector2(180, 8);
            var pf = UiKit.Box("Fill", _pinBar, HudTheme.Warn).rectTransform;
            pf.anchorMin = Vector2.zero; pf.anchorMax = new Vector2(0, 1); pf.offsetMin = pf.offsetMax = Vector2.zero;
            _pinFill = pf;
            _pinBar.gameObject.SetActive(false);

            // the panel: bottom left, above the vitals (the extinguisher is in the bottom right of the view)
            var plate = HudTheme.Plate("Panel", root).rectTransform;
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(0, 0);
            plate.anchoredPosition = new Vector2(HudTheme.Edge, HudTheme.Edge + 128);
            plate.sizeDelta = new Vector2(440, 226);
            var title = UiKit.Label("Title", plate, _logic.Practice ? "소화기 · 가상 연습" : "소화기", HudTheme.Eyebrow + 1, HudTheme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.At(title.rectTransform, 20, 14, 390, 22);
            var chip = ExtinguisherLogic.ModeText.TryGetValue(_logic.Mode ?? "", out var mt) ? mt : null;
            if (chip != null) UiKit.At(UiKit.Label("Mode", plate, chip, HudTheme.Eyebrow, HudTheme.Accent, TextAnchor.UpperRight).rectTransform, 220, 14, 190, 22);

            (_powderFill, _powderImg, _powderText) = Meter(plate, 44, "남은 분말");
            (_fireFill, _fireImg, _fireText) = Meter(plate, 96, "불");
            _exitText = UiKit.Label("Exit", plate, "", HudTheme.Value, HudTheme.Text);
            UiKit.At(_exitText.rectTransform, 20, 146, 390, 26);
            _keys = UiKit.Label("Keys", plate, "", HudTheme.Eyebrow, HudTheme.Muted);
            UiKit.At(_keys.rectTransform, 20, 176, 410, 44);

            // the next step / hint line: bottom centre
            _hint = HudTheme.Shadowed(UiKit.Label("Hint", root, "", HudTheme.Body + 2, HudTheme.Text, TextAnchor.MiddleCenter));
            var hr = _hint.rectTransform;
            hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(0.5f, 0);
            hr.anchoredPosition = new Vector2(0, 150); hr.sizeDelta = new Vector2(1100, 40);
        }

        (RectTransform fill, Image img, Text text) Meter(RectTransform plate, float y, string label)
        {
            UiKit.At(UiKit.Label("Label", plate, label, HudTheme.Label, HudTheme.Muted).rectTransform, 20, y, 200, 22);
            var t = UiKit.Label("Value", plate, "", HudTheme.Label, HudTheme.Text, TextAnchor.UpperRight);
            UiKit.At(t.rectTransform, 160, y, 250, 22);
            var track = UiKit.Box("Track", plate, HudTheme.Track).rectTransform;
            UiKit.At(track, 20, y + 26, 390, 10);
            var img = UiKit.Box("Fill", track, HudTheme.Accent);
            img.sprite = HudTheme.GradientSprite;
            var f = img.rectTransform;
            f.anchorMin = Vector2.zero; f.anchorMax = new Vector2(1, 1); f.offsetMin = f.offsetMax = Vector2.zero;
            return (f, img, t);
        }

        // ---------------------------------------------------------------- input and aim

        void Update()
        {
            if (_disposed || _logic == null || !_cam) return;
            var l = _logic;
            float dt = Time.deltaTime;
            bool free = !InputGate.Blocked && Time.timeScale > 0;
            var mouse = Mouse.current; var kb = Keyboard.current; var pad = Gamepad.current;
            var a = l.Input;
            a.Squeeze = false;
            if (!free) l.Hold(); // a menu or the pause: the hands let go (the roam does not tick meanwhile)
            if (free && !l.Ended)
            {
                bool lmbDown = mouse != null && mouse.leftButton.isPressed && Cursor.lockState == CursorLockMode.Locked;
                bool lmbPress = mouse != null && mouse.leftButton.wasPressedThisFrame;
                bool rHeld = (kb != null && kb.rKey.isPressed) || (pad != null && pad.leftShoulder.isPressed);
                bool rPress = (kb != null && kb.rKey.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame);
                if (!l.PinOut)
                {
                    if (l.Setup.Toggle)
                    {
                        if (rPress || lmbPress) l.PullPin(1);
                    }
                    else
                    {
                        if (lmbDown)
                        {
                            if (!_dragging) { _dragging = true; Freeze(true); }
                            float dy = -mouse.delta.ReadValue().y;
                            if (dy > 0) l.PullPin(dy / PinPixels);
                        }
                        else if (rHeld) l.PullPin(dt / PinHold);
                        else if (_dragging || l.PinProgress > 0) { if (_dragging) { _dragging = false; Freeze(false); } l.ReleasePin(); }
                    }
                    if (l.PinOut) { _needRelease = true; Sound("pin"); }
                }
                if (l.PinOut && _dragging) { _dragging = false; Freeze(false); }
                if (_needRelease && (mouse == null || !mouse.leftButton.isPressed) && !rHeld) _needRelease = false;

                bool fHeld = (kb != null && kb.spaceKey.isPressed) || (pad != null && pad.rightTrigger.ReadValue() > 0.5f);
                bool fPress = (kb != null && kb.spaceKey.wasPressedThisFrame) || (pad != null && pad.rightTrigger.wasPressedThisFrame);
                if (l.Setup.Toggle)
                {
                    if ((fPress || (lmbPress && !_needRelease)) && l.PinOut) _toggleOn = !_toggleOn;
                    else if (fPress && !l.PinOut) a.Squeeze = true; // says the pin is still in
                    a.Squeeze |= _toggleOn;
                }
                else a.Squeeze = fHeld || (lmbDown && l.PinOut && !_needRelease);
                if (l.Powder <= 0) _toggleOn = false;

                if ((kb != null && kb.tKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame))
                    if (l.Shout()) Sound("shout");
                if ((kb != null && kb.qKey.wasPressedThisFrame) || (pad != null && pad.selectButton.wasPressedThisFrame)) l.RetreatPress();
                if ((kb != null && kb.yKey.wasPressedThisFrame) || (pad != null && pad.dpad.down.wasPressedThisFrame)) _hintOpen = !_hintOpen;
            }
            else if (_dragging) { _dragging = false; Freeze(false); }

            // where the nozzle points (the view's centre), against the fire
            var o = _cam.transform.position; var d = _cam.transform.forward;
            float lv = Mathf.Max(0.15f, FxLevel(l.FireLv));
            float sc = (float)_place.Scale;
            float half = (0.3f + 0.35f * lv) * sc, baseTop = 0.45f * sc, flameTop = baseTop + (0.5f + 1.3f * lv) * sc;
            var hit = HeldExtinguisherLogic.Aim(o.x, o.y, o.z, d.x, d.y, d.z, _fireAt.x, _fireAt.y, _fireAt.z, half, baseTop, flameTop);
            bool onFire = hit.OnBase || hit.OnFlame;
            if (onFire)
            {
                // a wall or a door between the nozzle and the fire stops the powder
                float far = Vector3.Distance(o, _fireAt);
                if (Physics.Raycast(o, d, out var wall, far, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && wall.distance < far - 1.2f
                    && !(_host && _host.player && wall.collider.transform.IsChildOf(_host.player.transform)))
                    hit = new AimHit(false, false, hit.Lateral, hit.Height);
            }
            var p = _host && _host.player ? _host.player.transform.position : o;
            var toFire = _fireAt - p; toFire.y = 0;
            var toExit = _exitAt - p; toExit.y = 0;
            var face = d; face.y = 0;
            a.OnBase = hit.OnBase; a.OnFlame = hit.OnFlame; a.Lateral = hit.Lateral;
            a.Distance = toFire.magnitude;
            a.ExitBehind = HeldExtinguisherLogic.Behind(face.x, face.z, toExit.x, toExit.z);
            a.Away = _place.Level == "apartment" && p.y < 4f && _fireAt.y > 4f;
            a.Seen = true;
            l.Input = a;
        }

        /// <summary>While the pin is dragged the view holds still (the mouse pulls the pin, not the head).</summary>
        void Freeze(bool on)
        {
            var pl = _host ? _host.player : null;
            if (!pl) return;
            if (on) { if (_sens < 0) _sens = pl.sensitivity; pl.sensitivity = 0; }
            else if (_sens >= 0) { pl.sensitivity = _sens; _sens = -1; }
        }

        void Sound(string what)
        {
            if (!_host) return;
            if (what == "pin") _host.Cue("click");
            if (what == "shout") _host.Cue("door");
        }

        // ---------------------------------------------------------------- drawing

        void LateUpdate()
        {
            if (_disposed || _logic == null || !_cam) return;
            var l = _logic;
            var c = _cam.transform;
            float dt = Time.deltaTime;
            bool reduced = _host && _host.ReduceMotion;
            // only while the controls are on: behind a menu the powder, the puff and the hiss stop
            bool live = !InputGate.Blocked && !l.Ended;
            bool spraying = live && l.Spraying, hitting = live && l.Hitting;
            _recoil = Mathf.Lerp(_recoil, spraying && !reduced ? 1f : 0f, 1f - Mathf.Exp(-10f * dt));
            float jitter = spraying && !reduced ? Mathf.PerlinNoise(Time.time * 18f, 0.4f) - 0.5f : 0f;
            _model.SetPositionAndRotation(c.TransformPoint(HoldAt + new Vector3(0, 0.01f * jitter, -0.02f * _recoil)), c.rotation * Quaternion.Euler(4f, 0, -6f));
            _model.localScale = Vector3.one * HoldScale;
            if (_pin) _pin.gameObject.SetActive(!l.PinOut);
            if (_pin && !l.PinOut) _pin.localPosition = new Vector3(0.05f + 0.06f * (float)l.PinProgress, 0.6f, 0.02f);
            _nozzle.SetPositionAndRotation(c.TransformPoint(NozzleAt + new Vector3(0.004f * jitter, 0, 0)), c.rotation);
            _fill.transform.position = c.TransformPoint(new Vector3(0.1f, -0.2f, 0.25f));

            // hose: from the handle's side down and up to the nozzle, a soft sag
            var a0 = _model.TransformPoint(new Vector3(-0.08f, 0.55f, 0.02f));
            var a1 = _nozzle.position - _nozzle.forward * 0.08f;
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                var pt = Vector3.Lerp(a0, a1, t) + c.up * (-0.12f * Mathf.Sin(t * Mathf.PI));
                _hose.SetPosition(i, pt);
            }

            // the powder
            _powder.transform.SetPositionAndRotation(_nozzle.position, _nozzle.rotation);
            var em = _powder.emission; em.rateOverTime = spraying ? (l.Low ? 160f : 300f) : 0f;
            var main = _powder.main; main.startSpeedMultiplier = l.Low ? 0.75f : 1f;
            var pe = _puff.emission; pe.rateOverTime = hitting ? 45f : 0f;
            if (hitting)
            {
                var o = c.position; var d = c.forward;
                // the puff at the base, where the nozzle meets the fire's plane (or the floor in front of it)
                float t = Mathf.Max(0.5f, Vector3.Dot(_fireAt - o, Flat(d)) / Mathf.Max(0.2f, Vector3.Dot(d, Flat(d))));
                var at = o + d * t; at.y = Mathf.Max(_fireAt.y + 0.2f, Mathf.Min(at.y, _fireAt.y + 0.6f));
                _puff.transform.position = at;
            }

            // the fire and the smoke follow the rules
            float fx = FxLevel(l.FireLv);
            if (_fire && Mathf.Abs(fx - _fxShown) > 0.002f) { _fxShown = fx; _fire.SetLevel(fx); }
            if (_smoke)
            {
                // the story may set the smoke meanwhile (the fire spreading): that becomes the base the extra adds to
                if (_smokeSet >= 0 && Mathf.Abs(_smoke.Density - _smokeSet) > 0.001f) _smokeBase = _smoke.Density;
                float want = Mathf.Clamp01(_smokeBase + (float)l.SmokeExtra);
                if (Mathf.Abs(want - _smoke.Density) > 0.001f) _smoke.SetDensity(want);
                _smokeSet = _smoke.Density;
            }
            Hiss(spraying);
            Hud();
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        const string ExitOk = "<color=#3DC7B5>✓</color> 출구가 등 뒤에 있다", ExitNo = "<color=#F2B33D>△</color> 출구를 등지고 서세요";

        /// <summary>The panel's key line: what each key does now (practice: no alarm, Q stops practising).</summary>
        static string KeysText(HeldExtinguisherLogic l)
        {
            string tail = l.Practice ? " · Q 두 번 연습 그만" : " · T 불이야! · Q 두 번 물러나기";
            if (l.Mode == "hint") tail += " · Y 힌트";
            if (!l.PinOut) return (l.Setup.Toggle ? "R·클릭 핀 뽑기" : "마우스 누른 채 아래로 끌기(R 길게) 핀 뽑기") + tail;
            return "마우스 왼쪽 " + (l.Setup.Toggle ? "누르기" : "길게") + "(Space) 손잡이 · 좌우로 쓸기" + tail;
        }

        void Hud()
        {
            var l = _logic;
            var a = l.Input;
            float share = (float)l.PowderShare;
            _powderFill.anchorMax = new Vector2(share, 1);
            _powderImg.color = share > 0.45f ? HudTheme.Accent : share > 0.15f ? HudTheme.Warn : HudTheme.Danger;
            _powderText.text = l.Practice ? "가상 분말" : share <= 0 ? "다 떨어짐" : l.Low ? "<color=#F2B33D>압력이 낮다</color>" : "약 10초 남짓 · 제품 표시 확인";
            float fire = Mathf.Clamp01((float)l.FireLv);
            _fireFill.anchorMax = new Vector2(fire, 1);
            _fireImg.color = fire > 0.66f ? HudTheme.Danger : fire > 0.25f ? HudTheme.Warn : HudTheme.Accent;
            // the texts change a few times a try: rebuilt only when what they say changes (no per-frame strings)
            int fk = (l.FireLv > 0.66 ? 3 : l.FireLv > 0.25 ? 2 : l.FireLv > 0.02 ? 1 : 0) | (l.Grown ? 4 : 0);
            if (fk != _fireShown) { _fireShown = fk; _fireText.text = HeldExtinguisherLogic.FireWord(l.FireLv) + (l.Grown ? " · 번졌다" : ""); }
            _exitText.text = a.ExitBehind ? ExitOk : ExitNo;
            int kk = l.PinOut ? 1 : 0;
            if (kk != _keysShown) { _keysShown = kk; _keys.text = KeysText(l); }
            _reticle.color = a.OnBase ? new Color(0.24f, 0.78f, 0.71f, 0.95f) : a.OnFlame ? new Color(0.95f, 0.7f, 0.24f, 0.9f) : new Color(1, 1, 1, 0.4f);
            _reticle.rectTransform.sizeDelta = Vector2.one * (l.Spraying ? 54f : 46f);
            _pinBar.gameObject.SetActive(!l.PinOut && l.PinProgress > 0);
            _pinFill.anchorMax = new Vector2((float)l.PinProgress, 1);
            bool showHint = l.Mode == "guided" || (l.Mode == "hint" && _hintOpen);
            _hint.text = showHint && !l.Ended ? l.Hint() : "";
        }

        // ---------------------------------------------------------------- sound

        static AudioClip _hissClip;

        void Hiss(bool on)
        {
            if (!_hiss)
            {
                if (!on) return;
                if (!_hissClip)
                {
                    int rate = 22050, n = rate; // one second of soft filtered noise, looped
                    var data = new float[n];
                    var rng = new System.Random(7);
                    float lp = 0;
                    for (int i = 0; i < n; i++) { float w = (float)(rng.NextDouble() * 2 - 1); lp += (w - lp) * 0.35f; data[i] = (w - lp) * 0.5f; }
                    for (int i = 0; i < 400; i++) { float k = i / 400f; data[i] *= k; data[n - 1 - i] *= k; }
                    _hissClip = AudioClip.Create("ExtinguisherHiss", n, 1, rate, false);
                    _hissClip.SetData(data, 0);
                }
                _hiss = gameObject.AddComponent<AudioSource>();
                _hiss.clip = _hissClip; _hiss.loop = true; _hiss.spatialBlend = 0; _hiss.playOnAwake = false;
            }
            float want = on ? 0.55f * AiOn.Settings.AudioBus.Effects * (_logic.Low ? 0.6f : 1f) : 0f;
            _hiss.volume = Mathf.MoveTowards(_hiss.volume, want, Time.deltaTime * 4f);
            if (want > 0 && !_hiss.isPlaying) _hiss.Play();
            else if (want <= 0 && _hiss.volume <= 0.001f && _hiss.isPlaying) _hiss.Stop();
        }

        // ---------------------------------------------------------------- end

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Restore();
            if (_smoke) _smoke.SetDensity(_smokeBase); // the story's own level (it tracked any change the story made meanwhile)
            if (this) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (!_disposed) { _disposed = true; Restore(); }
        }

        void Restore()
        {
            Freeze(false);
            if (_hands) _hands.SuppressKeys = _handsKeysWere;
            foreach (var id in ExtinguisherScene.MutedPoints) PlayerInteractor.Muted.Remove(id);
            UiKeys.StripSuppressed = false;
            if (_fire && _fire != _ownFire) _fire.LightScale = _lightWas;
            if (_host && _host.player) _host.player.canJump = _jumpWas;
        }
    }
}
