using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StillHere.EditorTools
{
    /// Samples single frames of the Realistic Cat's animation clips and stores them as point clouds,
    /// so the game can show the cat "as felt" without needing the model, its rig or its fur shaders at runtime.
    public static class CatPoseBaker
    {
        const string Root = "Assets/Realistic Cat Demo/";
        const string Prefab = Root + "Prefabs/URP/Mobile/Cat_05.prefab";
        const string Anim = Root + "Cats/Cat_05/Animations/";
        public const string OutPath = "Assets/StillHere/Resources/CatPoses.asset";
        const int Points = 1500;

        [MenuItem("StillHere/Bake Cat Poses")]
        public static void BakeMenu() { Debug.Log(Bake()); }

        public static string Bake()
        {
            var log = new StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if (!prefab) return "cat prefab not found at " + Prefab;
            var go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.SetActive(true);
            var an = go.GetComponentInChildren<Animator>(true);
            if (an) an.enabled = false;
            SkinnedMeshRenderer body = null;
            foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (body == null || s.sharedMesh.vertexCount > body.sharedMesh.vertexCount && !s.name.Contains("jaw")) body = s;
            foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.name == "cat_5") body = s;
            body.gameObject.SetActive(true);
            Transform head = null, neck = null;
            foreach (var b in body.bones)
            {
                var n = b.name.ToLowerInvariant();
                if (head == null && n.Contains("head")) head = b;
                if (neck == null && n.Contains("neck")) neck = b;
            }
            log.AppendLine("bones: " + string.Join(",", System.Array.ConvertAll(body.bones, b => b.name)));

            var set = ScriptableObject.CreateInstance<CatPoseSet>();
            var rnd = new System.Random(7);

            // name, clip, normalised time, head-down degrees
            var plan = new List<(string, string, float, float)>();
            for (int i = 0; i < 6; i++) plan.Add(("walk_" + i, "Walk", i / 6f, 0));
            for (int i = 0; i < 4; i++) plan.Add(("run_" + i, "Run", i / 4f, 0));
            plan.Add(("sit_0", "Sitting_Idle", 0.02f, 0));
            plan.Add(("sit_1", "Sitting_Idle", 0.45f, 0));
            plan.Add(("idle_0", "Idle", 0.05f, 0));
            plan.Add(("idle_1", "Idle", 0.55f, 0));
            plan.Add(("jump_0", "Jump", 0.3f, 0));
            plan.Add(("jump_1", "Jump", 0.55f, 0));
            plan.Add(("hit_0", "Get_Hit", 0.35f, 0));
            plan.Add(("sleep_0", "Dying", 1.0f, 0));
            plan.Add(("eat_0", "Idle", 0.05f, 85));
            plan.Add(("eat_1", "Sitting_Idle", 0.3f, 80));

            var baked = new Mesh();
            foreach (var (name, clipName, nt, headDown) in plan)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Anim + clipName + ".anim");
                if (!clip) { log.AppendLine("missing clip " + clipName); continue; }
                clip.SampleAnimation(go, Mathf.Clamp01(nt) * clip.length * 0.999f);
                if (headDown > 0)
                {
                    // approximate an eating posture: neck and head tipped toward the floor
                    var axis = go.transform.right;
                    if (neck) neck.Rotate(axis, headDown * 0.6f, Space.World);
                    if (head) head.Rotate(axis, headDown * 0.4f, Space.World);
                }
                body.BakeMesh(baked);
                var v = baked.vertices; var nr = baked.normals; var tr = baked.triangles;
                var l2w = Matrix4x4.TRS(body.transform.position, body.transform.rotation, Vector3.one);
                for (int i = 0; i < v.Length; i++) { v[i] = l2w.MultiplyPoint3x4(v[i]); nr[i] = l2w.MultiplyVector(nr[i]).normalized; }

                // area-weighted surface sampling
                int tc = tr.Length / 3; var cum = new float[tc]; float total = 0;
                for (int t = 0; t < tc; t++)
                {
                    Vector3 a = v[tr[t * 3]], b = v[tr[t * 3 + 1]], c = v[tr[t * 3 + 2]];
                    total += Vector3.Cross(b - a, c - a).magnitude * 0.5f; cum[t] = total;
                }
                var pts = new Vector3[Points]; var nrm = new Vector3[Points];
                Vector3 mn = Vector3.one * 1e9f, mx = Vector3.one * -1e9f;
                for (int k = 0; k < Points; k++)
                {
                    float r = (float)rnd.NextDouble() * total;
                    int lo = 0, hi = tc - 1; while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] < r) lo = mid + 1; else hi = mid; }
                    float u = (float)rnd.NextDouble(), w = (float)rnd.NextDouble(); if (u + w > 1) { u = 1 - u; w = 1 - w; }
                    int i0 = tr[lo * 3], i1 = tr[lo * 3 + 1], i2 = tr[lo * 3 + 2];
                    pts[k] = v[i0] + (v[i1] - v[i0]) * u + (v[i2] - v[i0]) * w;
                    nrm[k] = (nr[i0] + (nr[i1] - nr[i0]) * u + (nr[i2] - nr[i0]) * w).normalized;
                    mn = Vector3.Min(mn, pts[k]); mx = Vector3.Max(mx, pts[k]);
                }
                // centre on the floor under the body
                var off = new Vector3((mn.x + mx.x) * 0.5f, mn.y, (mn.z + mx.z) * 0.5f);
                for (int k = 0; k < Points; k++) pts[k] -= off;
                set.poses.Add(new CatPoseSet.Pose { name = name, p = pts, n = nrm });
                log.AppendLine($"{name}: size {(mx - mn).ToString("F2")} headZ {(head ? (head.position.z - off.z).ToString("F2") : "?")} headY {(head ? head.position.y.ToString("F2") : "?")}");
            }
            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(go);
            System.IO.Directory.CreateDirectory("Assets/StillHere/Resources");
            AssetDatabase.DeleteAsset(OutPath);
            AssetDatabase.CreateAsset(set, OutPath);
            AssetDatabase.SaveAssets();
            log.AppendLine("baked " + set.poses.Count + " poses -> " + OutPath);
            return log.ToString();
        }
    }
}
