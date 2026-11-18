using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AiOn.Levels.Crowd
{
    /// <summary>
    /// The town's crowds (port of levels/town-crowd.js createTownCrowd plus the crowd side of town.js setState):
    /// groups main / extra / few / line created lazily in first-use order (seed 900 + 17 per group made before),
    /// sidewalk strolls cut by closures, evacuation to the open shelter gates, the water-point and assembly queues, and
    /// strollers waiting at the signalled zebras. Also switches the crowd's solids (closures, water point, buses, gates)
    /// and says which of the town's own NPCs show (town.js refreshNpcs).
    /// </summary>
    public sealed class TownCrowd : ICrowdLogic
    {
        readonly List<(string name, CrowdGroup group)> _groups = new();
        readonly List<CrowdAgent> _peers = new();
        readonly List<CrowdGroup.Hold> _holds = TownCrowdPlan.SignalHolds();
        readonly List<CrowdBody> _bodies = new();
        readonly Func<double, double, double> _height;
        readonly SolidGrid _solids;
        string _mode = "none", _traffic = "normal";
        List<string> _closures = new();
        readonly Dictionary<string, string> _gates = new(TownCrowdPlan.DefaultGates);
        bool _waterPoint, _buses, _trickle = true;

        public TownCrowd(WalkHeight walk, SolidGrid solids)
        {
            _height = walk != null ? walk.At : (Func<double, double, double>)((x, z) => 0);
            _solids = solids;
            // the original's defaults (town.js setState calls at build): closures none, gates default, water point and buses off
            SyncSolids();
            SetMode("normal");
        }

        public IReadOnlyList<CrowdBody> Bodies => _bodies;
        public string Mode => _mode;
        public IReadOnlyList<CrowdGroup.Hold> Holds => _holds;
        public IReadOnlyDictionary<string, string> Gates => _gates;
        public IReadOnlyList<string> Closures => _closures;
        public string Traffic => _traffic;
        /// <summary>The crowd's solids, with the closures, water point, buses and gates switched as the story set them.</summary>
        public SolidGrid Solids => _solids;

        /// <summary>The group made for a name, or null while it has not been used.</summary>
        public CrowdGroup Group(string name)
        {
            foreach (var (n, g) in _groups) if (n == name) return g;
            return null;
        }

        public IEnumerable<(string name, CrowdGroup group)> Groups => _groups;

        // town.js refreshNpcs
        public bool GuideEastVisible => _gates.TryGetValue("east", out var e) && e == "closed";
        public bool GuideCVisible => _gates.TryGetValue("access", out var a) && a == "open" && _mode != "none";
        public bool ClerkOutsideVisible => _mode == "normal" || _mode == "busy";

        CrowdGroup Get(string name)
        {
            var g = Group(name);
            if (g != null) return g;
            int seed = TownCrowdPlan.PortSeeds.TryGetValue(name, out var ps) ? ps : 900 + OriginalCount() * 17;
            g = new CrowdGroup(TownCrowdPlan.Sizes[name], seed, _height, _solids, _holds, _peers);
            _groups.Add((name, g));
            _bodies.AddRange(g.Bodies);
            return g;
        }

        int OriginalCount() { int n = 0; foreach (var (name, _) in _groups) if (!TownCrowdPlan.PortSeeds.ContainsKey(name)) n++; return n; }

        void Run(string name, Action<CrowdGroup> fn) { var c = Get(name); c.Visible = true; fn(c); }

        void ApplyMode()
        {
            foreach (var (_, g) in _groups) g.Visible = false;
            var walkers = TownCrowdPlan.StrollFor(_closures);
            switch (_mode)
            {
                case "none": return;
                case "normal": Run("main", c => { c.SetPaths(walkers); c.SetMode("stroll"); }); return; // speed 1.25 is passed but crowd.js ignores it
                case "busy":
                    foreach (var n in new[] { "main", "extra" }) Run(n, c => { c.SetPaths(walkers); c.SetMode("stroll"); });
                    return;
                case "sparse": Run("few", c => { c.SetPaths(walkers); c.SetMode("stroll"); }); return;
                case "evacuating":
                    {
                        var (paths, targets) = TownCrowdPlan.EvacFor(_gates, _closures);
                        foreach (var n in new[] { "main", "extra" }) Run(n, c => { c.SetPaths(paths); c.SetMode("brisk", new CrowdGroup.ModeOpts { Targets = targets, Then = "despawn" }); });
                        // Port: the first wave leaves for good, but for a quarter of an hour a thin stream keeps coming out of
                        // the complex (state trickle on), some with phone torches and bags; then it stops too.
                        RunTrickle();
                        foreach (var kv in TownCrowdPlan.TorchAreas)
                        {
                            var area = kv.Value;
                            Run(kv.Key, c => c.SetMode("idle", new CrowdGroup.ModeOpts { Area = area, Paths = new List<Pt[]>(), Instant = true, Props = new List<(PropMask, double)> { (PropMask.Flashlight, 1.0) } }));
                        }
                        return;
                    }
                case "boarded":
                    {
                        // The buses are leaving: most of the assembly has boarded; a handful stay at the bay to see them off.
                        Run("main", c => c.SetMode("idle", new CrowdGroup.ModeOpts { Count = 5, Area = TownCrowdPlan.BoardedArea, Paths = new List<Pt[]>(), Instant = true }));
                        return;
                    }
                case "queue":
                    // Served people leave west through the yard, not across 연무로. The original passed no carry here, so its
                    // leavers walked off empty-handed; they carry a 10 L jug now (a mood-keeping upgrade, queueTick's own path).
                    Run("line", c => c.SetMode("queue", new CrowdGroup.ModeOpts
                    {
                        At = TownCrowdPlan.WaterQueue, Dir = new Pt(1, 0), Spacing = 0.9, Leave = new Pt(-1, 0), Carry = "waterJug",
                    }));
                    Run("few", c => { c.SetPaths(walkers); c.SetMode("stroll"); });
                    return;
                case "assembly":
                    Run("main", c => c.SetMode("idle", new CrowdGroup.ModeOpts { Area = TownCrowdPlan.AssemblyWaitArea, Paths = new List<Pt[]>(), Instant = true }));
                    // served leave east toward 문화로, clear of the buses
                    // Port: two single-file lines at 0.7 m, side by side (the original had one at 0.85 m).
                    Run("line", c => c.SetMode("queue", new CrowdGroup.ModeOpts { At = TownCrowdPlan.AssemblyQueue, Dir = new Pt(0, 1), Spacing = TownCrowdPlan.AssemblySpacing, Leave = new Pt(1, 0) }));
                    Run("line2", c => c.SetMode("queue", new CrowdGroup.ModeOpts { At = TownCrowdPlan.AssemblyQueue2, Dir = new Pt(0, 1), Spacing = TownCrowdPlan.AssemblySpacing, Leave = new Pt(1, 0), Serve = 13 }));
                    return;
            }
        }

        void RunTrickle()
        {
            var (tp, tt) = TownCrowdPlan.TricklePaths(_gates, _closures);
            if (tp.Count == 0) return;
            Run("trickle", c =>
            {
                c.SetPaths(tp);
                c.SetMode("brisk", new CrowdGroup.ModeOpts
                                {
                                    Targets = tt, Then = _trickle ? "respawn" : "despawn", RespawnDelay = TownCrowdPlan.TrickleDelay,
                                    Props = new List<(PropMask, double)> { (PropMask.Flashlight, 0.5), (PropMask.Backpack, 0.45) },
                                });
            });
        }

        /// <summary>town-crowd setMode: false (mode unchanged, an error) for an unknown mode.</summary>
        public bool SetMode(string m)
        {
            if (Array.IndexOf(TownCrowdPlan.Modes, m) < 0)
            {
                UnityEngine.Debug.LogError($"[town] crowd \"{m}\" unknown; use {string.Join(" | ", TownCrowdPlan.Modes)}");
                return false;
            }
            _mode = m; ApplyMode();
            return true;
        }

        void SetContext()
        {
            if (_mode == "normal" || _mode == "busy" || _mode == "sparse" || _mode == "evacuating" || _mode == "queue") ApplyMode();
        }

        void SyncSolids()
        {
            if (_solids == null) return;
            foreach (var id in TownCrowdPlan.Barricades.Keys) _solids.SetOff("closure:" + id, !_closures.Contains(id));
            foreach (var id in new[] { "waterTank", "waterTable", "queueBelt" }) _solids.SetOff(id, !_waterPoint);
            foreach (var id in new[] { "bus", "assemblySign", "assemblyTable" }) _solids.SetOff(id, !_buses);
            _solids.SetOff("gateEast", !(_gates.TryGetValue("east", out var e) && e == "closed"));
            _solids.SetOff("boomB", !(_gates.TryGetValue("ramp", out var r) && r == "closed"));
            _solids.SetOff("movingCar", true); // the crowd keeps to walks and zebras; the cars wait for it (TownStreet)
        }

        public bool Apply(string key, JToken v)
        {
            switch (key)
            {
                case "crowd":
                    SetMode(v?.Type == JTokenType.String ? (string)v : v?.ToString());
                    return true;
                case "closures":
                    {
                        var list = new List<string>();
                        if (v is JArray arr) { foreach (var t in arr) if (t.Type == JTokenType.String) list.Add((string)t); }
                        else if (v != null && v.Type == JTokenType.String && !string.IsNullOrEmpty((string)v)) list.Add((string)v);
                        _closures = list;
                        SyncSolids(); SetContext();
                        return true;
                    }
                case "gates":
                    if (v is JObject o)
                        foreach (var p in o.Properties())
                        {
                            string val = p.Value.Type == JTokenType.String ? (string)p.Value : null;
                            if (!_gates.ContainsKey(p.Name) || (val != "open" && val != "closed"))
                            { UnityEngine.Debug.LogError($"[town] gates.{p.Name} = \"{p.Value}\" invalid; use east/stairs/ramp/access : open | closed"); continue; }
                            _gates[p.Name] = val;
                        }
                    SyncSolids(); SetContext();
                    return true;
                case "traffic":
                    {
                        // town.js: traffic.setMode(value); state.traffic = traffic.mode (an unknown mode keeps the old one)
                        string m = v != null && v.Type == JTokenType.String ? (string)v : v?.ToString();
                        if (Array.IndexOf(AiOn.Levels.Town.TownTraffic.Modes, m) < 0)
                            UnityEngine.Debug.LogError($"[town] traffic \"{m}\" unknown; use normal | light | none");
                        else _traffic = m;
                        return true;
                    }
                case "waterPoint":
                    _waterPoint = v?.ToString() == "on"; SyncSolids();
                    return true;
                case "buses":
                    _buses = v?.ToString() == "on"; SyncSolids(); // departing: the bay is clear (the host moves the buses out)
                    return true;
                case "trickle":
                    {
                        bool on = v?.ToString() != "off";
                        if (on == _trickle) return true;
                        _trickle = on;
                        var g = Group("trickle");
                        if (g != null && g.Visible && _mode == "evacuating") RunTrickle();
                        return true;
                    }
            }
            return false;
        }

        public void Update(double dt, in CrowdFrame f)
        {
            // No grid power after the blackout: dead signals (town.js also stops the traffic then).
            bool on = f.Power && f.Env != "blackout" && _traffic != "none";
            TownCrowdPlan.SetSignals(_holds, f.T, on);
            foreach (var (_, g) in _groups) if (g.Visible) g.Update(dt, f);
        }
    }
}
