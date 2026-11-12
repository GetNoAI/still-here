using System;
using System.Collections.Generic;
using UnityEngine;

namespace StillHere
{
    public enum Mat { Wall, Door, Floor, Wood, Metal, Glass, Ceramic }

    /// One invisible primitive. Cylinders stand upright; their min/max are the bounding box used for rays.
    public struct Part
    {
        public bool cyl;
        public Vector3 min, max;
        public Vector3 c;   // cylinder base centre
        public float r, h;
    }

    public struct Hit
    {
        public bool valid;
        public WObj o;
        public Part part;
        public float t;
        public Vector3 p, n;
        public bool Top => n.y > 0.5f;
    }

    /// A thing in the kitchen: never drawn, only bumped into, reached for, heard and outlined.
    public class WObj
    {
        public string id;
        public Mat mat = Mat.Wood;
        public float R = 0.8f;                       // how far a touch outline spreads
        public List<Part> parts = new List<Part>();
        public List<Vector4> col = new List<Vector4>(); // x0, z0, x1, z1 footprints
        public bool small, catBlock, noBump;
        public string[] lines;
        public Action<WObj, Hit> reach;
        public Action<WObj, Vector3, float> bump;
        public int touches;
        public float lastBump = -99;
        // cupboards
        public bool open, hingeLeft;
        public int feels;
        public float x0, x1;
    }

    public struct ColBox { public WObj o; public Vector4 b; }

    public struct CollideResult { public float x, z; public bool hit; public WObj o; public float cx, cz; }

    /// Geometry queries. The layout is authored in the web prototype's coordinates (z pointing south);
    /// the *W helpers flip z once, here, so everything else is in Unity space (+Z north, +X east).
    public class World
    {
        public readonly List<WObj> objects = new List<WObj>();
        public readonly Dictionary<string, WObj> byId = new Dictionary<string, WObj>();
        public readonly List<ColBox> colPlayer = new List<ColBox>();
        public readonly List<ColBox> colCat = new List<ColBox>();

        public static Part BoxW(float x0, float y0, float z0, float x1, float y1, float z1)
        {
            return new Part { cyl = false, min = new Vector3(x0, y0, -z1), max = new Vector3(x1, y1, -z0) };
        }
        public static Part CylW(float cx, float cy, float cz, float r, float h) { return Cyl(cx, cy, -cz, r, h); }
        public static Part Cyl(float cx, float cy, float cz, float r, float h)
        {
            return new Part { cyl = true, c = new Vector3(cx, cy, cz), r = r, h = h, min = new Vector3(cx - r, cy, cz - r), max = new Vector3(cx + r, cy + h, cz + r) };
        }
        public static Vector4 ColW(float x0, float z0, float x1, float z1) { return new Vector4(x0, -z1, x1, -z0); }

        public WObj this[string id] => byId.TryGetValue(id, out var o) ? o : null;

        public WObj Add(WObj o) { objects.Add(o); byId[o.id] = o; RebuildCols(); return o; }
        public void Remove(string id)
        {
            if (!byId.TryGetValue(id, out var o)) return;
            objects.Remove(o); byId.Remove(id); RebuildCols();
        }
        public void RebuildCols()
        {
            colPlayer.Clear(); colCat.Clear();
            foreach (var o in objects)
                foreach (var b in o.col)
                {
                    colPlayer.Add(new ColBox { o = o, b = b });
                    if (o.catBlock) colCat.Add(new ColBox { o = o, b = b });
                }
        }

        static bool RayBox(Vector3 o, Vector3 d, Vector3 mn, Vector3 mx, out float t, out int axis, out int sign)
        {
            float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity; axis = -1; sign = 0; t = 0;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(d[a]) < 1e-9f) { if (o[a] < mn[a] || o[a] > mx[a]) return false; continue; }
                float t1 = (mn[a] - o[a]) / d[a], t2 = (mx[a] - o[a]) / d[a]; int s = -1;
                if (t1 > t2) { float tt = t1; t1 = t2; t2 = tt; s = 1; }
                if (t1 > tmin) { tmin = t1; axis = a; sign = s; }
                tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) return false;
            }
            if (tmax < 0 || tmin < 0) return false;
            t = tmin; return true;
        }

        public Hit RayWorld(Vector3 o, Vector3 d, float maxT)
        {
            var best = new Hit();
            foreach (var ob in objects)
                foreach (var part in ob.parts)
                    if (RayBox(o, d, part.min, part.max, out float t, out int axis, out int sign) && t <= maxT && (!best.valid || t < best.t))
                    {
                        var n = Vector3.zero; n[axis] = sign;
                        best = new Hit { valid = true, o = ob, part = part, t = t, p = o + d * t, n = n };
                    }
            return best;
        }

        /// A forgiving reach for small things: the nearest filtered part the ray passes within `rad` of.
        public Hit FatRay(Vector3 o, Vector3 d, float maxT, Func<WObj, bool> filter, float rad = 0.16f)
        {
            var best = new Hit(); float bestScore = 1e9f;
            foreach (var ob in objects)
            {
                if (!filter(ob)) continue;
                foreach (var part in ob.parts)
                    for (float t = 0.1f; t <= maxT; t += 0.04f)
                    {
                        var p = o + d * t;
                        var c = new Vector3(Mathf.Clamp(p.x, part.min.x, part.max.x), Mathf.Clamp(p.y, part.min.y, part.max.y), Mathf.Clamp(p.z, part.min.z, part.max.z));
                        float dd = (p - c).magnitude;
                        if (dd < rad)
                        {
                            float sc = dd + t * 0.05f;
                            if (sc < bestScore) { bestScore = sc; best = new Hit { valid = true, o = ob, part = part, t = t, p = c, n = Vector3.up }; }
                            break;
                        }
                    }
            }
            return best;
        }

        public CollideResult CollideCircle(float x, float z, float r, List<ColBox> list)
        {
            var res = new CollideResult();
            for (int pass = 0; pass < 2; pass++)
                foreach (var c in list)
                {
                    float x0 = c.b.x, z0 = c.b.y, x1 = c.b.z, z1 = c.b.w;
                    float cx = Mathf.Clamp(x, x0, x1), cz = Mathf.Clamp(z, z0, z1);
                    float dx = x - cx, dz = z - cz, d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < r)
                    {
                        if (d < 1e-6f)
                        {
                            float l = x - x0, rr = x1 - x, t = z - z0, b = z1 - z, m = Mathf.Min(Mathf.Min(l, rr), Mathf.Min(t, b));
                            if (m == l) x = x0 - r; else if (m == rr) x = x1 + r; else if (m == t) z = z0 - r; else z = z1 + r;
                        }
                        else { x = cx + dx / d * r; z = cz + dz / d * r; }
                        if (!res.hit) { res.hit = true; res.o = c.o; res.cx = cx; res.cz = cz; }
                    }
                }
            res.x = x; res.z = z;
            return res;
        }

        /// Hand height at which you meet this object when you walk into it.
        public float ContactY(WObj o, float x, float z)
        {
            float top = 0;
            foreach (var part in o.parts)
                if (x >= part.min.x - 0.06f && x <= part.max.x + 0.06f && z >= part.min.z - 0.06f && z <= part.max.z + 0.06f)
                    top = Mathf.Max(top, part.max.y);
            if (top <= 0) top = 0.8f;
            return Mathf.Clamp(top, 0.05f, 0.85f) - 0.02f;
        }
    }
}
