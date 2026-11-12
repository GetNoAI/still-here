using System;
using System.Collections.Generic;
using UnityEngine;

namespace StillHere
{
    /// Pre-baked point clouds of the cat in single frames of its real animations.
    /// Points are in cat-local space: +Z is the way the cat faces, Y up, origin on the floor under its middle.
    public class CatPoseSet : ScriptableObject
    {
        [Serializable]
        public class Pose
        {
            public string name;
            public Vector3[] p;
            public Vector3[] n;
        }

        public List<Pose> poses = new List<Pose>();
        Dictionary<string, Pose> map;

        public Pose Get(string name)
        {
            if (map == null) { map = new Dictionary<string, Pose>(); foreach (var q in poses) map[q.name] = q; }
            return map.TryGetValue(name, out var r) ? r : null;
        }

        public int Count(string prefix)
        {
            int c = 0; foreach (var q in poses) if (q.name.StartsWith(prefix)) c++; return c;
        }
    }
}
