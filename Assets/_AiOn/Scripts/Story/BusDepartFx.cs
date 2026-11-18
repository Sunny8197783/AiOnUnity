using System.Collections.Generic;
using UnityEngine;

namespace AiOn.Story
{
    /// <summary>
    /// The assembly's buses leaving (town state buses = departing / departed, port addition). The 'buses' variant is merged
    /// per material across both buses and the reception (the white mesh holds both bus bodies and the reception table),
    /// so on first use each merged mesh is split by triangle: the parts over each bus become that bus's own object
    /// (pivot at its parked centre), the rest (tent, sign, table) stays where it is. Departing drives each bus along
    /// <see cref="BusRoute"/>; departed leaves the bay empty; on puts them back. Applying the same state again changes nothing.
    /// </summary>
    public class BusDepartFx : MonoBehaviour
    {
        enum State { Parked, Departing, Departed }

        readonly List<Transform> _bus = new();
        readonly List<float> _baseY = new();
        bool _split;
        State _state = State.Parked;
        float _t;

        /// <summary>The bus a world point belongs to (0 west, 1 east), or -1 for the reception (Unity frame: z is the original's -z).</summary>
        static int BusOf(Vector3 w)
        {
            if (w.x < -71.6f || w.x > -62.6f || w.z < 34.4f || w.z > 46.6f) return -1;
            return w.x < (float)((BusRoute.Parked[0].x + BusRoute.Parked[1].x) / 2) ? 0 : 1;
        }

        void Split()
        {
            if (_split) return;
            _split = true;
            var parent = transform.parent; // the buses follow this root's visibility through OnEnable/OnDisable
            for (int b = 0; b < 2; b++)
            {
                var go = new GameObject("Bus_" + b);
                go.transform.SetParent(parent, false);
                var (px, pz) = BusRoute.Parked[b];
                go.transform.SetPositionAndRotation(new Vector3((float)px, 0, (float)-pz), Quaternion.identity);
                _bus.Add(go.transform);
                _baseY.Add(0);
            }
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (!mesh || !mr) continue;
                if (!mesh.isReadable)
                { // a mesh the split cannot read: take it whole if it sits over a bus
                    int whole = BusOf(mr.bounds.center);
                    if (whole >= 0) mf.transform.SetParent(_bus[whole], true);
                    continue;
                }
                verts.Clear(); norms.Clear(); uvs.Clear();
                mesh.GetVertices(verts); mesh.GetNormals(norms); mesh.GetUVs(0, uvs);
                var l2w = mf.transform.localToWorldMatrix;
                var parts = new List<int>[3]; // west bus, east bus, rest: triangle corner indices
                for (int i = 0; i < 3; i++) parts[i] = new List<int>();
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    tris.Clear(); mesh.GetTriangles(tris, sm);
                    for (int i = 0; i + 2 < tris.Count; i += 3)
                    {
                        var c = l2w.MultiplyPoint3x4((verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]) / 3f);
                        int who = BusOf(c);
                        var list = parts[who < 0 ? 2 : who];
                        list.Add(tris[i]); list.Add(tris[i + 1]); list.Add(tris[i + 2]);
                    }
                }
                if (parts[0].Count == 0 && parts[1].Count == 0) continue; // the tent, the sign, the table legs
                for (int b = 0; b < 2; b++)
                {
                    if (parts[b].Count == 0) continue;
                    var m = Sub(mesh.name + "_bus" + b, verts, norms, uvs, parts[b], _bus[b].worldToLocalMatrix * l2w);
                    UnflushWheels(m);
                    var go = new GameObject(mf.name + "_bus" + b);
                    go.transform.SetParent(_bus[b], false);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = mr.sharedMaterials;
                    r.shadowCastingMode = mr.shadowCastingMode; r.receiveShadows = mr.receiveShadows;
                    r.lightProbeUsage = mr.lightProbeUsage; r.reflectionProbeUsage = mr.reflectionProbeUsage;
                }
                // what is left of this mesh stays here (an empty rest just hides the original)
                mf.sharedMesh = Sub(mesh.name + "_rest", verts, norms, uvs, parts[2], Matrix4x4.identity);
            }
            foreach (var t in _bus) t.gameObject.SetActive(isActiveAndEnabled);
        }

        /// <summary>
        /// The wheels' outer faces sit flush with the body's sides (x ±1.25 in the original), so the two fight for the same
        /// pixels and the wheels show as broken black wedges. A part that is only wheels (low and the bus's full width) has
        /// its outer rims moved 3 cm out. The bus's local x is across it (the bus object is unrotated when parked).
        /// </summary>
        static void UnflushWheels(Mesh m)
        {
            var b = m.bounds;
            if (b.max.y > 1.2f || b.size.x < 2.3f) return;
            var v = new List<Vector3>(); m.GetVertices(v);
            for (int i = 0; i < v.Count; i++)
            {
                var p = v[i];
                if (Mathf.Abs(p.x) > 1.0f) { p.x += Mathf.Sign(p.x) * 0.03f; v[i] = p; }
            }
            m.SetVertices(v);
            m.RecalculateBounds();
        }

        static Mesh Sub(string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> idx, Matrix4x4 m)
        {
            var map = new Dictionary<int, int>();
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt = new List<int>(idx.Count);
            bool hasN = n.Count == v.Count, hasU = uv.Count == v.Count;
            foreach (var i in idx)
            {
                if (!map.TryGetValue(i, out var j))
                {
                    j = nv.Count; map[i] = j;
                    nv.Add(m.MultiplyPoint3x4(v[i]));
                    if (hasN) nn.Add(m.MultiplyVector(n[i]).normalized);
                    if (hasU) nu.Add(uv[i]);
                }
                nt.Add(j);
            }
            var mesh = new Mesh { name = name };
            if (nv.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(nv);
            if (hasN) mesh.SetNormals(nn);
            if (hasU) mesh.SetUVs(0, nu);
            mesh.SetTriangles(nt, 0);
            mesh.RecalculateBounds();
            if (!hasN) mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>"on" (parked), "departing" (driving out from the bay) or "departed" (gone). The same state again is a no-op.</summary>
        public void SetState(string v)
        {
            var want = v == "departing" ? State.Departing : v == "departed" ? State.Departed : State.Parked;
            Split(); // once, on the first state (the parked buses get their unflushed wheels too)
            if (want == _state) return;
            _state = want; _t = 0;
            Place();
        }

        void Place()
        {
            if (!_split) return;
            for (int b = 0; b < _bus.Count; b++)
            {
                var t = _bus[b];
                if (!t) continue;
                bool shown = _state == State.Parked || (_state == State.Departing && !(_t > 1 && BusRoute.At(b, _t).z < -105));
                if (t.gameObject.activeSelf != (shown && isActiveAndEnabled)) t.gameObject.SetActive(shown && isActiveAndEnabled);
                var (x, z, drop, yaw) = _state == State.Departing ? BusRoute.At(b, _t) : (BusRoute.Parked[b].x, BusRoute.Parked[b].z, 0.0, 0.0);
                t.SetPositionAndRotation(new Vector3((float)x, (float)-drop, (float)-z), Quaternion.Euler(0, (float)(-yaw * Mathf.Rad2Deg), 0));
            }
        }

        void OnEnable() { Place(); }

        void OnDisable()
        {
            foreach (var t in _bus) if (t) t.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            foreach (var t in _bus) if (t) Destroy(t.gameObject);
        }

        void Update()
        {
            if (_state != State.Departing) return;
            _t += Time.deltaTime;
            Place();
        }
    }
}
