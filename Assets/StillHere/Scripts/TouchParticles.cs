using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace StillHere
{
    /// The only thing ever drawn: a ring buffer of tiny camera-facing motes.
    /// Emission is scheduled by birth time, so a touch can spread outward from the point of contact.
    public class TouchParticles : MonoBehaviour
    {
        public const int Max = 36000;

        [StructLayout(LayoutKind.Sequential)]
        struct Vtx { public Vector3 pos; public Vector2 corner; public Vector4 data; public Vector4 seed; }

        NativeArray<Vtx> verts;
        Mesh mesh;
        Material mat;
        int head;
        int dirtyMin = int.MaxValue, dirtyMax = -1;
        readonly float[] birth = new float[Max];
        readonly float[] life = new float[Max];
        static readonly Vector2[] Corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        static readonly int NowId = Shader.PropertyToID("_SHNow"), WanderId = Shader.PropertyToID("_SHWander");
        System.Random rnd = new System.Random(11);

        public float wander = 0.0075f;   // metres each mote strays from its anchor

        void Awake()
        {
            verts = new NativeArray<Vtx>(Max * 4, Allocator.Persistent);
            for (int i = 0; i < Max; i++)
            {
                birth[i] = -1e6f;
                for (int k = 0; k < 4; k++) verts[i * 4 + k] = new Vtx { corner = Corners[k], data = new Vector4(-1e6f, 1, 0, 0) };
            }
            mesh = new Mesh { name = "TouchParticles", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(Max * 4,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4));
            mesh.SetVertexBufferData(verts, 0, 0, Max * 4, 0, MeshUpdateFlags.DontRecalculateBounds);
            var idx = new int[Max * 6];
            for (int i = 0; i < Max; i++)
            {
                int v = i * 4, t = i * 6;
                idx[t] = v; idx[t + 1] = v + 2; idx[t + 2] = v + 1; idx[t + 3] = v; idx[t + 4] = v + 3; idx[t + 5] = v + 2;
            }
            mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);

            mat = Resources.Load<Material>("TouchParticleMat");
            if (!mat) { var sh = Shader.Find("StillHere/TouchParticle"); if (sh) mat = new Material(sh); }
            if (!mat) Debug.LogError("[StillHere] touch particle shader missing");
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        void OnDestroy() { if (verts.IsCreated) verts.Dispose(); if (mesh) Destroy(mesh); }

        public void Emit(Vector3 p, float birthTime, float lifeTime, float size, float bright)
        {
            int i = head; head = (head + 1) % Max;
            birth[i] = birthTime; life[i] = lifeTime;
            var data = new Vector4(birthTime, lifeTime, size, bright);
            var seed = new Vector4((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble());
            for (int k = 0; k < 4; k++) verts[i * 4 + k] = new Vtx { pos = p, corner = Corners[k], data = data, seed = seed };
            if (i < dirtyMin) dirtyMin = i;
            if (i > dirtyMax) dirtyMax = i;
        }

        public void SetNow(float now)
        {
            Shader.SetGlobalFloat(NowId, now);
            Shader.SetGlobalFloat(WanderId, wander);
        }

        public void Flush()
        {
            if (dirtyMax < 0) return;
            int start = dirtyMin * 4, count = (dirtyMax - dirtyMin + 1) * 4;
            mesh.SetVertexBufferData(verts, start, start, count, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers);
            dirtyMin = int.MaxValue; dirtyMax = -1;
        }

        void LateUpdate() { Flush(); }

        public int LiveCount(float now)
        {
            int n = 0;
            for (int i = 0; i < Max; i++) { float a = now - birth[i]; if (a > 0 && a < life[i]) n++; }
            return n;
        }

        // ---------------------------------------------------------------- shape sampling (x, y, z, brightness)

        public static void SampleBox(Vector3 mn, Vector3 mx, Vector3 p, float R, List<Vector4> outp, float dens)
        {
            Vector3 lo = Vector3.zero, hi = Vector3.zero;
            for (int a = 0; a < 3; a++)
            {
                lo[a] = Mathf.Max(mn[a], p[a] - R); hi[a] = Mathf.Min(mx[a], p[a] + R);
                if (lo[a] > hi[a] + 1e-6f) return;
            }
            bool fin = !float.IsInfinity(R); float R2 = R * R; const float e = 1e-5f;
            for (int a = 0; a < 3; a++)            // 12 edges: the outline
            {
                int b = (a + 1) % 3, c = (a + 2) % 3;
                for (int ib = 0; ib < 2; ib++) for (int ic = 0; ic < 2; ic++)
                {
                    float vb = ib == 0 ? mn[b] : mx[b], vc = ic == 0 ? mn[c] : mx[c];
                    if (vb < lo[b] - e || vb > hi[b] + e || vc < lo[c] - e || vc > hi[c] + e) continue;
                    float len = hi[a] - lo[a]; int n = Mathf.Max(1, Mathf.FloorToInt(len / 0.018f * dens));
                    for (int i = 0; i <= n; i++)
                    {
                        var q = Vector3.zero;
                        q[a] = lo[a] + len * i / n + Random.Range(-0.005f, 0.005f);
                        q[b] = vb + Random.Range(-0.002f, 0.002f); q[c] = vc + Random.Range(-0.002f, 0.002f);
                        if (!fin || (q - p).sqrMagnitude <= R2) outp.Add(new Vector4(q.x, q.y, q.z, 1));
                    }
                }
            }
            for (int a = 0; a < 3; a++)            // faces: sparse fill
            {
                int b = (a + 1) % 3, c = (a + 2) % 3;
                for (int ia = 0; ia < 2; ia++)
                {
                    float v = ia == 0 ? mn[a] : mx[a];
                    if (v < lo[a] - e || v > hi[a] + e) continue;
                    float w = hi[b] - lo[b], h = hi[c] - lo[c]; int n = Mathf.FloorToInt(w * h * 190 * dens);
                    for (int i = 0; i < n; i++)
                    {
                        var q = Vector3.zero; q[a] = v; q[b] = lo[b] + Random.value * w; q[c] = lo[c] + Random.value * h;
                        if (!fin || (q - p).sqrMagnitude <= R2) outp.Add(new Vector4(q.x, q.y, q.z, 0.4f));
                    }
                }
            }
        }

        public static void SampleCyl(Vector3 c, float r, float h, Vector3 p, float R, List<Vector4> outp, float dens)
        {
            bool fin = !float.IsInfinity(R); float R2 = R * R;
            void Push(float x, float y, float z, float b) { if (!fin || (new Vector3(x, y, z) - p).sqrMagnitude <= R2) outp.Add(new Vector4(x, y, z, b)); }
            int nr = Mathf.Max(10, Mathf.FloorToInt(2 * Mathf.PI * r / 0.014f * dens));
            for (int iy = 0; iy < 2; iy++)
                for (int i = 0; i < nr; i++) { float a = i / (float)nr * 2 * Mathf.PI; Push(c.x + Mathf.Cos(a) * r, c.y + iy * h, c.z + Mathf.Sin(a) * r, 1); }
            for (int k = 0; k < 8; k++)
            {
                float a = k / 8f * 2 * Mathf.PI;
                for (float y = 0; y <= h; y += 0.02f) Push(c.x + Mathf.Cos(a) * r, c.y + y, c.z + Mathf.Sin(a) * r, 0.55f);
            }
            int ns = Mathf.FloorToInt(2 * Mathf.PI * r * h * 220 * dens);
            for (int i = 0; i < ns; i++) { float a = Random.value * 2 * Mathf.PI; Push(c.x + Mathf.Cos(a) * r, c.y + Random.value * h, c.z + Mathf.Sin(a) * r, 0.4f); }
            int nt = Mathf.FloorToInt(Mathf.PI * r * r * 300 * dens);
            for (int i = 0; i < nt; i++) { float a = Random.value * 2 * Mathf.PI, rr = r * Mathf.Sqrt(Random.value); Push(c.x + Mathf.Cos(a) * rr, c.y + h, c.z + Mathf.Sin(a) * rr, 0.35f); }
        }

        public static void SamplePart(Part part, Vector3 p, float R, List<Vector4> outp, float dens)
        {
            if (part.cyl) SampleCyl(part.c, part.r, part.h, p, R, outp, dens);
            else SampleBox(part.min, part.max, p, R, outp, dens);
        }
    }
}
