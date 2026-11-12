using System.Collections.Generic;
using UnityEngine;

namespace StillHere
{
    /// The kitchen. Coordinates in the *W helpers are the web prototype's (x east, z south); World flips z once.
    /// Room: 5 m (x -2.5..2.5) by 4 m. Counters run along the north wall, the back door is in the west wall,
    /// the radiator is on the south wall, the clock on the east wall, the table in the middle.
    public partial class Game
    {
        static Part B(float x0, float y0, float z0, float x1, float y1, float z1) => World.BoxW(x0, y0, z0, x1, y1, z1);
        static Part C(float cx, float cy, float cz, float r, float h) => World.CylW(cx, cy, cz, r, h);
        static Vector4 Col(float x0, float z0, float x1, float z1) => World.ColW(x0, z0, x1, z1);
        static List<Part> Parts(params Part[] p) => new List<Part>(p);
        static List<Vector4> Cols(params Vector4[] c) => new List<Vector4>(c);
        static Part[] Legs(float x0, float z0, float x1, float z1, float top, float w = 0.045f) => new[]
        {
            B(x0, 0, z0, x0 + w, top, z0 + w), B(x1 - w, 0, z0, x1, top, z0 + w), B(x0, 0, z1 - w, x0 + w, top, z1), B(x1 - w, 0, z1 - w, x1, top, z1)
        };

        public static readonly Vector2 BowlP = new Vector2(-2.15f, -1.75f), BowlB = new Vector2(0.95f, -1.78f), WaterBowl = new Vector2(-2.15f, -1.38f);
        public static readonly Vector3 SleepSpot = new Vector3(-0.95f, 0.46f, -0.2f);   // Biscuit, on the other chair

        void BuildKitchen()
        {
            var wall = L("wall_1", "wall_2", "wall_3");
            W.Add(new WObj { id = "wallN", mat = Mat.Wall, R = 0.75f, parts = Parts(B(-2.5f, 0, -2.15f, 2.5f, 2.4f, -2)), col = Cols(Col(-3, -2.5f, 3, -2)), catBlock = true, lines = wall });
            W.Add(new WObj { id = "wallS", mat = Mat.Wall, R = 0.75f, parts = Parts(B(-2.5f, 0, 2, 2.5f, 2.4f, 2.15f)), col = Cols(Col(-3, 2, 3, 2.5f)), catBlock = true, lines = wall });
            W.Add(new WObj { id = "wallE", mat = Mat.Wall, R = 0.75f, parts = Parts(B(2.5f, 0, -2, 2.65f, 2.4f, 2)), col = Cols(Col(2.5f, -2.5f, 3, 2.5f)), catBlock = true,
                reach = (o, h) => { if (h.p.z > -1.0f && h.p.z < 0.4f) Talk("clock", L("clock"), 1, 8); else Talk("wall", wall); }, lines = wall });
            W.Add(new WObj { id = "wallW", mat = Mat.Wall, R = 0.75f, parts = Parts(B(-2.65f, 0, -2, -2.5f, 2.4f, 2)), col = Cols(Col(-3, -2.5f, -2.5f, 2.5f)), catBlock = true, lines = wall });
            var doorLines = L("door_1", "door_2");
            W.Add(new WObj { id = "door", mat = Mat.Door, R = 0.7f, parts = Parts(B(-2.52f, 0, 0.6f, -2.49f, 2.05f, 1.5f), B(-2.5f, 0.02f, 0.95f, -2.47f, 0.24f, 1.15f)),
                col = Cols(Col(-2.6f, 0.6f, -2.49f, 1.5f)), catBlock = true,
                reach = (o, h) => { if (h.p.y < 0.3f) { Sfx("hand_wood", h.p, 0.8f); Talk("flap", L("flap")); } else Talk("door", doorLines); },
                bump = (o, p, sp) => Talk("door", doorLines) });
            W.Add(new WObj { id = "floor", mat = Mat.Floor, R = 0.4f, parts = Parts(B(-2.5f, -0.1f, -2, 2.5f, 0, 2)), noBump = true,
                reach = (o, h) => { if (SurfaceAt(h.p.x, h.p.z) == "rug") Talk("rug", L("rug_1"), 0); else Talk("floor", L("floor_1"), 0); } });

            // counters along the north wall
            WObj Cup(string id, float x0, float x1, bool hingeLeft) => W.Add(new WObj
            {
                id = id, mat = Mat.Wood, R = 0.75f, parts = Parts(B(x0, 0, -2, x1, 0.9f, -1.4f)), col = Cols(Col(x0, -2.1f, x1, -1.4f)), catBlock = true,
                hingeLeft = hingeLeft, x0 = x0, x1 = x1, reach = CupReach, bump = (o, p, sp) => Talk(o.id + "b", L("cup_bump"), 1, 7)
            });
            Cup("cupA", -2.5f, -1.5f, true); Cup("cupB", -1.5f, -0.5f, false); Cup("cupC", -0.5f, 0.4f, false);
            W.Add(new WObj { id = "breadbin", mat = Mat.Wood, R = 0.4f, small = true, parts = Parts(B(-2.2f, 0.9f, -1.95f, -1.75f, 1.15f, -1.62f)), lines = L("bread_1", "bread_2") });
            W.Add(new WObj { id = "oven", mat = Mat.Metal, R = 0.65f, parts = Parts(B(0.4f, 0, -2, 1.0f, 0.9f, -1.4f)), col = Cols(Col(0.4f, -2.1f, 1.0f, -1.4f)), catBlock = true,
                reach = (o, h) => { if (h.Top) Burn(h.p); else Talk("oven", L("oven")); }, bump = (o, p, sp) => Talk("oven", L("oven")) });
            W.Add(new WObj { id = "kettle", mat = Mat.Metal, R = 0.35f, small = true, parts = Parts(C(0.7f, 0.9f, -1.7f, 0.1f, 0.22f)), reach = (o, h) => Burn(h.p) });
            W.Add(new WObj { id = "bin", mat = Mat.Metal, R = 0.45f, small = true, parts = Parts(C(1.35f, 0, -1.75f, 0.17f, 0.45f)), col = Cols(Col(1.18f, -1.92f, 1.52f, -1.58f)), catBlock = true,
                lines = L("bin"), bump = (o, p, sp) => { Sfx("bin", p, 0.8f); Talk("bin", L("bin")); } });
            W.Add(new WObj { id = "fridge", mat = Mat.Metal, R = 0.8f, parts = Parts(B(1.7f, 0, -2, 2.5f, 1.85f, -1.35f)), col = Cols(Col(1.7f, -2.1f, 2.6f, -1.35f)), catBlock = true,
                reach = (o, h) =>
                {
                    if (fridgeState == 0) { fridgeState = 1; Talk("fridge0", L("fridge_1")); return; }
                    if (fridgeState == 1)
                    {
                        fridgeState = 2; Sfx("fridge_open", h.p); if (fridgeLoop != null) fridgeLoop.baseVol = 0.9f;
                        Say("fridge_2", 1); foreach (var c in cats) if (!c.asleep) Meow(c, "insistent", 1.2f + c.idx * 0.6f);
                        var hp = h.p; Later(3.0f, () => { Sfx("fridge_close", hp); if (fridgeLoop != null) fridgeLoop.baseVol = 0.4f; fridgeState = 3; });
                        return;
                    }
                    if (fridgeState == 3) Talk("fridge2", L("fridge_3", "fridge_1"));
                },
                bump = (o, p, sp) => Talk("fridgeb", L("fridge_1")) });

            // table and chairs
            var tableParts = Parts(B(-0.6f, 0.71f, -0.2f, 0.6f, 0.76f, 0.6f)); tableParts.AddRange(Legs(-0.58f, -0.18f, 0.58f, 0.58f, 0.71f));
            W.Add(new WObj { id = "table", mat = Mat.Wood, R = 0.9f, parts = tableParts, col = Cols(Col(-0.6f, -0.2f, 0.6f, 0.6f)), lines = L("table_1", "table_2"), bump = TableBump });
            W.Add(new WObj { id = "glass", mat = Mat.Glass, R = 0.25f, small = true, parts = Parts(C(0.45f, 0.76f, -0.08f, 0.035f, 0.12f)),
                reach = (o, h) => Talk("glass", L("glass_1", "glass_2")) });
            var mine = Parts(B(-0.22f, 0.42f, 0.85f, 0.22f, 0.46f, 1.25f), B(-0.22f, 0.46f, 1.21f, 0.22f, 0.95f, 1.25f)); mine.AddRange(Legs(-0.21f, 0.86f, 0.21f, 1.24f, 0.42f, 0.035f));
            W.Add(new WObj { id = "chairMine", mat = Mat.Wood, R = 0.6f, parts = mine, col = Cols(Col(-0.22f, 0.85f, 0.22f, 1.25f)), lines = L("chair_mine_1", "chair_mine_2") });
            var other = Parts(B(-1.15f, 0.42f, 0.0f, -0.75f, 0.46f, 0.4f), B(-1.15f, 0.46f, 0.0f, -1.11f, 0.95f, 0.4f)); other.AddRange(Legs(-1.14f, 0.01f, -0.76f, 0.39f, 0.42f, 0.035f));
            W.Add(new WObj { id = "chairOther", mat = Mat.Wood, R = 0.6f, parts = other, col = Cols(Col(-1.15f, 0.0f, -0.75f, 0.4f)),
                reach = (o, h) => OtherChair(), bump = (o, p, sp) => OtherChair() });
            W.Add(new WObj { id = "radiator", mat = Mat.Metal, R = 0.6f, parts = Parts(B(-0.5f, 0.15f, 1.88f, 0.5f, 0.8f, 1.98f)), col = Cols(Col(-0.5f, 1.86f, 0.5f, 2.1f)), catBlock = true, lines = L("radiator") });
            W.Add(new WObj { id = "bucket", mat = Mat.Metal, R = 0.45f, small = true, parts = Parts(C(1.15f, 0, -0.65f, 0.17f, 0.3f)), col = Cols(Col(0.98f, -0.82f, 1.32f, -0.48f)), catBlock = true,
                reach = (o, h) => { Sfx("bucket", BucketPos(), 0.35f); Talk("bucketr", L("bucket_r")); },
                bump = (o, p, sp) => { Sfx("bucket", p, 1); Talk("bucket", L("bucket_1", "bucket_2", "bucket_r"), 2, 4); PushBucket(sp); if (sp > 0.55f) Stumble(0.7f, true); } });

            // bowls: Pudding's by the back door, Biscuit's by the radiator, water beside Pudding's
            W.Add(new WObj { id = "bowlP", mat = Mat.Ceramic, R = 0.3f, small = true, parts = Parts(World.Cyl(BowlP.x, 0, BowlP.y, 0.11f, 0.06f)), reach = (o, h) => BowlReach(0) });
            W.Add(new WObj { id = "bowlB", mat = Mat.Ceramic, R = 0.3f, small = true, parts = Parts(World.Cyl(BowlB.x, 0, BowlB.y, 0.11f, 0.06f)), reach = (o, h) => BowlReach(1) });
            W.Add(new WObj { id = "waterBowl", mat = Mat.Ceramic, R = 0.3f, small = true, parts = Parts(World.Cyl(WaterBowl.x, 0, WaterBowl.y, 0.1f, 0.06f)),
                reach = (o, h) => Talk("water", waterSpilt ? L("water_empty") : L("water_1")) });
        }

        Vector3 BucketPos() { var c = W["bucket"].parts[0].c; return new Vector3(c.x, 0.3f, c.z); }
        void PushBucket(float sp)
        {
            var b = W["bucket"]; var c = b.parts[0].c; float dx = c.x - P.x, dz = c.z - P.z, d = Hyp(dx, dz); if (d < 1e-4f) d = 1;
            float k = 0.08f + sp * 0.12f, nx = Mathf.Clamp(c.x + dx / d * k, -2.2f, 2.2f), nz = Mathf.Clamp(c.z + dz / d * k, -1.8f, 1.15f);
            b.parts = Parts(World.Cyl(nx, 0, nz, 0.17f, 0.3f)); b.col = Cols(new Vector4(nx - 0.17f, nz - 0.17f, nx + 0.17f, nz + 0.17f)); W.RebuildCols();
        }

        /// The chair nobody sits in. Until he wakes, Biscuit is asleep on it.
        void OtherChair()
        {
            var b = cats[1];
            if (b.asleep)
            {
                EmitCat(b, "sleep_0", new Vector3(SleepSpot.x, SleepSpot.y + 0.05f, SleepSpot.z + 0.2f), 2.8f, 1.5f, true);
                if (b.purrLoop != null) b.purrHold = now + 4;
                if (!Once("sleeper", "chair_other_cat", 2)) Talk("chairOther", L("chair_other_1"), 1, 6);
            }
            else if (!Once("warmSeat", "chair_other_empty", 1)) Talk("chairOther", L("chair_other_1", "chair_other_2"), 1, 6);
        }

        void TableBump(WObj o, Vector3 p, float sp)
        {
            var gp = new Vector3(0.45f, 0.82f, 0.08f);
            if (glass == "table")
            {
                glass = "wobble"; Sfx("glass_clink", gp, 1);
                if (W["glass"] != null) TouchObject(W["glass"], gp, new TouchOpt { R = 0.3f, noGlance = true });
                Say("glass_wobble", 2);
            }
            else if (glass == "wobble" && sp > 0.25f)
            {
                glass = "broken"; W.Remove("glass"); Sfx("glass_clink", gp, 0.8f);
                Later(0.42f, () =>
                {
                    var fp = new Vector3(0.62f, 0.02f, 0.38f); Sfx("glass_smash", fp); Rumble(0.5f, 0.3f, 0.2f);
                    hasShards = true; shards = new Vector2(0.62f, 0.38f); shardsR = 0.42f;
                    scratch.Clear();
                    for (int i = 0; i < 160; i++) { float a = Rand(0, 6.283f), r = Mathf.Sqrt(UnityEngine.Random.value) * 0.42f; scratch.Add(new Vector4(0.62f + Mathf.Cos(a) * r, 0.005f, 0.38f + Mathf.Sin(a) * r, Rand(0.4f, 1))); }
                    EmitSet(scratch, fp, float.PositiveInfinity, new TouchOpt { life = 1.6f, speed = 2.5f });
                    foreach (var c in cats) if (!c.asleep) { CatFlee(c); Meow(c, "yowl", 0.3f + c.idx * 0.2f); }
                    Say("glass_smash_1", 3);
                    Later(3.5f, () => Say("glass_smash_2", 1));
                });
            }
            else Talk("table", o.lines);
        }

        // ------------------------------------------------------------------ cupboards
        void CupReach(WObj o, Hit hit)
        {
            // the sink itself is on top of cupB; anywhere else on a unit, your hand finds its way down to the door
            if (hit.Top && o.id == "cupB" && hit.p.x > -1.35f && hit.p.x < -0.65f && !o.open)
                if (Talk("sink", L("sink_1", "sink_2"), 1, 4)) return;
            if (hit.Top && !o.open && said.Add("wt" + o.id)) Say("worktop", 0);
            if (!o.open) { OpenCup(o, hit.p); return; }
            FeelCup(o, hit.p);
        }
        void OpenCup(WObj o, Vector3 p)
        {
            o.open = true; Sfx("creak_open", p);
            float x = o.hingeLeft ? o.x0 : o.x1 - 0.03f;
            W.Add(new WObj { id = o.id + "Door", mat = Mat.Wood, R = 0.5f, parts = Parts(B(x, 0.05f, -1.4f, x + 0.03f, 0.85f, -0.95f)), col = Cols(Col(x - 0.01f, -1.4f, x + 0.04f, -0.95f)), catBlock = true,
                reach = (d, h) => CloseCup(o), bump = (d, q, sp) => Talk(o.id + "door", L("cup_door_1", "cup_door_2"), 2, 4) });
            if (o.id == "cupA") Say("cupA_open", 1);
            if (o.id == "cupB") Say("cupB_open", 1);
            if (o.id == "cupC") { Say("cupC_open", 1); progT = Mathf.Max(progT, now - 20); }
        }
        void CloseCup(WObj o)
        {
            var d = W[o.id + "Door"]; var p = d != null ? new Vector3(d.parts[0].min.x, 0.5f, 1.2f) : new Vector3(o.x0, 0.5f, 1.4f);
            Sfx("creak_close", p); W.Remove(o.id + "Door"); o.open = false; Talk("shut", L("cup_shut"), 0);
        }
        void FeelCup(WObj o, Vector3 p)
        {
            Sfx("rummage", p, 0.9f); o.feels++;
            if (o.id == "cupA") { Sfx("pans", p, 0.8f, 0.25f); Talk("feelA", L("cupA_feel")); }
            else if (o.id == "cupB") Talk("feelB", L("cupB_feel"));
            else if (o.id == "cupC")
            {
                if (hasTin) { Talk("feelC2", L("cupC_more")); return; }
                if (o.feels == 1) Say("cupC_beans", 1); else TakeTin(p);
            }
        }
    }
}
