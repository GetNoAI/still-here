using UnityEngine;

namespace StillHere
{
    /// Pudding (small, bell on her collar, awake from the start) and Biscuit (big old tom, dull tag instead of a bell,
    /// asleep on the other chair until the tin comes out). Each has their own bowl. You never see them unless they touch you.
    public class Cat
    {
        public string name; public int idx; public bool isB;
        public float x, z, y, h, spd;
        public string mode = "feet", prevMode = "feet";
        public float modeT, orbitA, orbitR = 0.6f, orbitDir = 1;
        public float purr, purrHold, nextMeow = 1e9f, bellAcc, lastRub = -99, lastUnder = -99, stuckT, fx, fz, wx, wz, walkPhase, stillT, nextShot, crossDir = 1;
        public bool dodgeRolled, asleep, stole;
        public float leadClock;      // time since she last went to show you the way (rubs and dodges don't reset it)
        public int rubShots;
        public Vector2 bowl, eatPt, leadPt, asidePt;
        public AudioDirector.Emitter purrLoop, eatLoop, snoreLoop;
        public Vector3 Mouth => new Vector3(x, y + 0.25f, z);
    }

    public partial class Game
    {
        [System.NonSerialized] public Cat[] cats;

        void InitCats()
        {
            var p = new Cat { name = "Pudding", idx = 0, x = 0.05f, z = -0.45f, orbitA = 1.2f, orbitR = 0.6f, orbitDir = 1,
                bowl = BowlP, eatPt = new Vector2(-1.9f, -1.75f), leadPt = new Vector2(-1.85f, -1.62f), asidePt = new Vector2(-1.75f, -1.05f) };
            var b = new Cat { name = "Biscuit", idx = 1, isB = true, x = SleepSpot.x, z = SleepSpot.z, y = SleepSpot.y, h = 0, asleep = true, mode = "sleep", orbitA = 4f, orbitR = 0.78f, orbitDir = -1,
                bowl = BowlB, eatPt = new Vector2(1.22f, -1.76f), leadPt = new Vector2(0.95f, -1.42f), asidePt = new Vector2(1.55f, -1.25f) };
            cats = new[] { p, b };
            foreach (var c in cats)
            {
                c.purrLoop = au.Loop(c.isB ? "purr_b" : "purr_p", c.Mouth, 0, 0.45f, 1.1f, false);
                c.eatLoop = au.Loop("cat_eat", c.Mouth, 0, 0.45f, 1.2f, false);
            }
            b.snoreLoop = au.Loop("snore", b.Mouth, 0.5f, 0.3f, 1.6f, false);
        }

        public void CatMode(Cat c, string m)
        {
            if (c.mode == m) return;
            if (m == "wander") c.prevMode = c.mode;
            c.mode = m; c.modeT = now;
        }

        public void Meow(Cat c, string kind = "normal", float delay = 0)
        {
            if (au.silent) return;
            string id; float pitch = 1, vol = 1;
            if (c.isB) id = kind == "insistent" ? "meow_b2" : kind == "mrrp" ? "meow_b3" : kind == "yowl" ? "yowl" : "meow_b1";
            else id = kind == "insistent" ? "meow_p2" : kind == "mrrp" ? "meow_p3" : kind == "yowl" ? "yowl" : Pick("meow_p1", "meow_p4");
            if (kind == "yowl") { pitch = c.isB ? 0.85f : 1.1f; vol = 1.1f; }
            au.Play(id, c.Mouth, vol, pitch, delay, 0.5f, 1.1f);
        }

        void CatFlee(Cat c)
        {
            float dx = c.x - P.x, dz = c.z - P.z, d = Hyp(dx, dz); if (d < 1e-4f) { dx = 1; d = 1; }
            c.fx = Mathf.Clamp(c.x + dx / d * 1.6f, -2.2f, 2.2f); c.fz = Mathf.Clamp(c.z + dz / d * 1.6f, -1.8f, 1.2f);
            c.rubShots = 0; CatMode(c, "flee");
        }

        /// Where she waits for you; she steps aside as you arrive so you can stand there.
        Vector2 LeadPoint(Cat c)
        {
            Vector2 b, aside;
            if (stage == "tin") { b = c.leadPt; aside = c.asidePt; }
            else { b = new Vector2(-0.05f, 1.2f); aside = new Vector2(0.55f, 1.15f); }
            return Hyp(P.x - b.x, P.z - b.y) < 0.9f ? aside : b;
        }

        Vector2 CatTarget(Cat c)
        {
            Vector2 f = Fwd(), r = Rgt();
            switch (c.mode)
            {
                case "feet": return new Vector2(Mathf.Clamp(P.x + Mathf.Cos(c.orbitA) * c.orbitR, -2.3f, 2.3f), Mathf.Clamp(P.z + Mathf.Sin(c.orbitA) * c.orbitR, -1.8f, 1.25f));
                case "lead": return LeadPoint(c);
                case "rub": return new Vector2(P.x + f.x * 0.42f - r.x * 0.36f * c.crossDir, P.z + f.y * 0.42f - r.y * 0.36f * c.crossDir);
                case "cross": return new Vector2(P.x + f.x * 0.42f + r.x * 0.5f * c.crossDir, P.z + f.y * 0.42f + r.y * 0.5f * c.crossDir);
                case "flee": return new Vector2(c.fx, c.fz);
                case "wander": return new Vector2(c.wx, c.wz);
                case "eat": return c.eatPt;
                case "steal": { var o = cats[1 - c.idx]; return o.bowl + new Vector2(o.isB ? -0.28f : 0.1f, o.isB ? 0.12f : 0.3f); }
                default: return new Vector2(c.x, c.z);
            }
        }

        void CatBrain(Cat c)
        {
            if (c.asleep) return;
            float inMode = now - c.modeT, dP = Hyp(c.x - P.x, c.z - P.z);
            if (stage == "end" || filled[c.idx]) { if (c.mode != "eat" && c.mode != "flee") CatMode(c, "eat"); if (c.mode == "flee" && inMode > 1.2f) CatMode(c, "eat"); return; }
            if (c.mode == "flee" && inMode > 1.6f) CatMode(c, "feet");
            if (c.mode == "wander" && inMode > 2.5f) CatMode(c, c.prevMode == "wander" || c.prevMode == "cross" || c.prevMode == "rub" ? "feet" : c.prevMode);
            if (c.mode == "rub" && (inMode > 7 || (P.speed > 0.3f && dP > 0.7f))) CatMode(c, "feet");
            if (c.mode == "cross" && c.rubShots <= 0 && inMode > 1.6f) CatMode(c, "feet");
            if (c.mode == "steal")
            {
                var o = cats[1 - c.idx]; var t = CatTarget(c);
                if (Hyp(c.x - t.x, c.z - t.y) < 0.3f || inMode > 7)
                {
                    c.stole = true; au.Play("hiss", o.Mouth, 0.9f, o.isB ? 0.9f : 1.05f);
                    if (dP < 3.2f) Say("wrong_cat", 1);
                    CatFlee(c);
                }
                return;
            }
            if (stage == "up" || stage == "tin")
            {
                var lp = LeadPoint(c); float dL = Hyp(P.x - lp.x, P.z - lp.y);
                // one bowl filled, mine still empty: have a go at theirs first
                if (stage == "tin" && filled[1 - c.idx] && !c.stole && c.mode != "flee" && now - lastFeedT > 6f) { CatMode(c, "steal"); return; }
                float leadDelay = Mathf.Max(6, 22 - hintI * 5) + c.idx * 4;
                if (c.mode == "feet" && c.leadClock > leadDelay && dP < 3.5f) { c.leadClock = 0; CatMode(c, "lead"); }
                if (c.mode == "lead" && inMode > 18 + c.idx * 5 && dL > 2.3f && hintI < 4) CatMode(c, "feet");
                if (c.mode == "feet" && now - P.lastMove > 3.5f && now - c.lastRub > 16 && now - lastRubAny > 7 && dP < 1.5f && !P.busy) { c.crossDir = UnityEngine.Random.value < 0.5f ? 1 : -1; CatMode(c, "rub"); }
            }
        }
        float lastRubAny = -99;

        /// The single frame of real cat movement that matches what this cat is doing right now.
        public string CatPose(Cat c)
        {
            if (c.asleep) return "sleep_0";
            if (c.mode == "eat" && Hyp(c.x - c.eatPt.x, c.z - c.eatPt.y) < 0.2f) return c.isB ? "eat_1" : "eat_0";
            if (c.spd > 1.2f) return "run_" + (Mathf.FloorToInt(c.walkPhase * 4) % 4);
            if (c.spd > 0.1f) return "walk_" + (Mathf.FloorToInt(c.walkPhase * 6) % 6);
            return c.stillT > 1.2f ? (((int)(now / 9) + c.idx) % 2 == 0 ? "sit_0" : "sit_1") : (c.idx == 0 ? "idle_0" : "idle_1");
        }

        /// Draw the cat as felt: a rim-lit point cloud of one animation frame, spreading from where it touched you.
        public void EmitCat(Cat c, string pose, Vector3? contact = null, float life = 1.8f, float bright = 1, bool noGlance = false, float spread = 0.9f)
        {
            if (headless || poses == null) return;
            var ps = poses.Get(pose); if (ps == null) return;
            float sh = Mathf.Sin(c.h), ch = Mathf.Cos(c.h), scale = c.isB ? 1.12f : 0.95f;
            Vector3 camP = cam.transform.position, ct = contact ?? new Vector3(c.x, c.y + 0.2f, c.z);
            int n = ps.p.Length; float keep = Mathf.Min(1f, 1300f / n);
            for (int i = 0; i < n; i++)
            {
                if (keep < 1 && UnityEngine.Random.value > keep) continue;
                Vector3 lp = ps.p[i] * scale, ln = ps.n[i];
                var w = new Vector3(c.x + lp.x * ch + lp.z * sh, c.y + lp.y, c.z - lp.x * sh + lp.z * ch);
                var nw = new Vector3(ln.x * ch + ln.z * sh, ln.y, -ln.x * sh + ln.z * ch);
                float rim = 1 - Mathf.Abs(Vector3.Dot(nw, (w - camP).normalized));
                float b = 0.07f + 0.93f * rim * rim * rim;       // bright silhouette, dim interior
                fx.Emit(w, now + (w - ct).magnitude / spread + UnityEngine.Random.value * 0.06f, life * Rand(0.85f, 1.15f), Rand(0.002f, 0.0032f), Mathf.Min(1.6f, b * bright * 1.35f));
            }
            if (!noGlance) GlanceTo(new Vector3(c.x, c.y + 0.2f, c.z));
        }

        /// The cat walks across your shins. You feel it as three successive frames of its walk.
        void StartCross(Cat c, float dir)
        {
            Vector2 f = Fwd(), r = Rgt();
            c.crossDir = dir >= 0 ? 1 : -1;
            if (stage == "intro") { c.x = P.x + f.x * 0.42f - r.x * 0.36f * c.crossDir; c.z = P.z + f.y * 0.42f - r.y * 0.36f * c.crossDir; }
            c.h = Mathf.Atan2(r.x * c.crossDir, r.y * c.crossDir);
            c.lastRub = now; lastRubAny = now; c.purrHold = now + 6; c.rubShots = 3; c.nextShot = now;
            au.Play("fur", c.Mouth, 1); Rumble(0.15f, 0.05f, 0.4f);
            CatMode(c, "cross");
            if (stage != "intro" && UnityEngine.Random.value < 0.55f) Say(Pick("rub_1", "rub_2", "rub_3", "rub_4"), 0);
        }

        void Stroke(Cat c)
        {
            c.purrHold = now + 6; au.Play("fur", c.Mouth, 1); Meow(c, "mrrp", 0.2f);
            EmitCat(c, CatPose(c), new Vector3(c.x, c.y + 0.32f, c.z), 2.4f);
            Rumble(0.1f, 0.05f, 0.5f);
            Talk("stroke" + c.idx, c.isB ? L("stroke_b_1", "stroke_b_2") : L("stroke_p_1", "stroke_p_2"), 1, 4);
        }

        public void WakeBiscuit()
        {
            var b = cats[1]; if (!b.asleep) return;
            b.asleep = false; b.y = 0; b.x = -0.95f; b.z = -0.68f; b.h = Mathf.PI; b.spd = 0;
            if (b.snoreLoop != null) b.snoreLoop.baseVol = 0;
            au.Play("cat_land", new Vector3(b.x, 0.05f, b.z), 1); Meow(b, "mrrp", 0.5f);
            CatMode(b, "feet"); b.modeT = now; b.nextMeow = now + 3;
            Later(1.6f, () => Say("biscuit_wake_1", 3));
            Later(1.7f, () => Say("biscuit_wake_2", 3));
        }

        void UpdateCat(Cat c, float dt)
        {
            if (c.asleep)
            {
                if (c.snoreLoop != null) c.snoreLoop.Pos = c.Mouth;
                c.purr = Mathf.Lerp(c.purr, now < c.purrHold ? 0.5f : 0, Damp(1.5f, dt));
                if (c.purrLoop != null) { c.purrLoop.Pos = c.Mouth; c.purrLoop.baseVol = c.purr; }
                return;
            }
            if (c.mode != "lead") c.leadClock += dt;
            CatBrain(c);
            // usually they slip out of your way; now and then they don't
            {
                float rx = c.x - P.x, rz = c.z - P.z, dist = Hyp(rx, rz), sp = P.speed;
                if (dist > 1.0f) c.dodgeRolled = false;
                else if (sp > 0.3f && !c.dodgeRolled && c.mode != "flee" && c.mode != "eat" && c.mode != "cross")
                {
                    float vx = P.vx / sp, vz = P.vz / sp, ahead = rx * vx + rz * vz, lat = rx * vz - rz * vx;
                    if (ahead > 0 && Mathf.Abs(lat) < 0.4f && dist < 0.85f)
                    {
                        c.dodgeRolled = true;
                        if (UnityEngine.Random.value < 0.8f)
                        {
                            float s = lat >= 0 ? 1 : -1;
                            c.wx = Mathf.Clamp(c.x + vz * 0.7f * s, -2.2f, 2.2f); c.wz = Mathf.Clamp(c.z - vx * 0.7f * s, -1.8f, 1.25f); CatMode(c, "wander");
                        }
                    }
                }
            }
            c.orbitA += dt * c.orbitDir * (0.3f + 0.25f * Mathf.Sin(now * 0.21f + c.idx * 2));
            Vector2 t = CatTarget(c);
            float dx = t.x - c.x, dz = t.y - c.z, d = Hyp(dx, dz);
            bool parked = (c.mode == "lead" || c.mode == "eat") && d < 0.1f;
            float cap = c.mode == "flee" ? 1.7f : c.mode == "cross" ? 0.5f : c.mode == "steal" ? 1.3f : 0.95f;
            float want = (d < 0.06f || parked) ? 0 : Mathf.Min(cap, d * 1.8f + (c.mode == "cross" ? 0.3f : 0));
            if (want > 0 && c.mode != "cross") c.h += Mathf.Clamp(WrapA(Mathf.Atan2(dx, dz) - c.h), -5 * dt, 5 * dt);
            c.spd = Mathf.Lerp(c.spd, want, Damp(5, dt));
            float ox = c.x, oz = c.z;
            var r = W.CollideCircle(c.x + Mathf.Sin(c.h) * c.spd * dt, c.z + Mathf.Cos(c.h) * c.spd * dt, CatR, W.colCat);
            float nx = r.x, nz = r.z;
            float pdx = nx - P.x, pdz = nz - P.z, pd = Hyp(pdx, pdz), minD = (c.mode == "rub" || c.mode == "cross") ? 0.2f : 0.3f;
            if (pd < minD && pd > 1e-4f) { nx = P.x + pdx / pd * minD; nz = P.z + pdz / pd * minD; }
            var o = cats[1 - c.idx];
            if (!o.asleep) { float qx = nx - o.x, qz = nz - o.z, qd = Hyp(qx, qz); if (qd < 0.26f && qd > 1e-4f) { nx = o.x + qx / qd * 0.26f; nz = o.z + qz / qd * 0.26f; } }
            c.x = Mathf.Clamp(nx, -2.36f, 2.36f); c.z = Mathf.Clamp(nz, -1.86f, 1.86f);
            float moved = Hyp(c.x - ox, c.z - oz);
            c.walkPhase = (c.walkPhase + moved / (c.spd > 1.2f ? 0.7f : 0.42f)) % 1f;
            c.stillT = c.spd < 0.08f ? c.stillT + dt : 0;
            if (want > 0.3f && moved < want * dt * 0.25f) c.stuckT += dt; else c.stuckT = Mathf.Max(0, c.stuckT - dt);
            if (c.stuckT > 1.2f) { c.stuckT = 0; c.wx = Rand(-2, 2); c.wz = Rand(-1.7f, 1f); CatMode(c, "wander"); }

            // collar: Pudding's bright bell, Biscuit's dull tag
            c.bellAcc += moved;
            if (c.bellAcc > (c.isB ? 0.3f : 0.22f)) { c.bellAcc = 0; if (UnityEngine.Random.value < 0.8f) au.Play(c.isB ? "bell_b" : "bell_p", c.Mouth, c.spd > 1 ? 1.5f : 1, 1, 0, 0.5f, 1.1f); }

            // brushing against your legs
            if (c.mode == "rub" && d < 0.14f && now - c.lastRub > 5) StartCross(c, c.crossDir);
            if (c.rubShots > 0 && now >= c.nextShot)
            {
                Vector2 f = Fwd();
                // one frame at a time, like a flick-book: each fades as the next arrives
                EmitCat(c, CatPose(c), new Vector3(P.x + f.x * 0.25f, 0.2f, P.z + f.y * 0.25f), 0.8f, 1, c.rubShots < 3, 3f);
                c.rubShots--; c.nextShot = now + 0.55f;
            }

            // voice
            if (now > c.nextMeow && !P.busy)
            {
                if (stage == "seated" || stage == "up" || stage == "tin")
                {
                    var lp = LeadPoint(c); bool atLead = c.mode == "lead" && Hyp(c.x - lp.x, c.z - lp.y) < 0.3f;
                    Meow(c, atLead || hintI > 2 ? "insistent" : Pick("normal", "normal", "mrrp"));
                    c.nextMeow = now + (atLead ? Rand(3.5f, 6) : Rand(7, 13) - Mathf.Min(hintI, 4)) + (c.isB ? 1.5f : 0);
                }
                else c.nextMeow = now + 5;
            }

            bool eating = c.mode == "eat" && d < 0.14f && filled[c.idx] && now < eatUntil;
            float nearP = Hyp(c.x - P.x, c.z - P.z);
            float tgt = now < c.purrHold ? 0.9f : filled[c.idx] ? 0.7f : (nearP < 0.8f && (stage == "seated" || stage == "intro") ? 0.25f : 0);
            c.purr = Mathf.Lerp(c.purr, tgt, Damp(1.5f, dt));
            if (c.purrLoop != null) { c.purrLoop.Pos = c.Mouth; c.purrLoop.baseVol = c.purr * 0.9f; }
            if (c.eatLoop != null) { c.eatLoop.Pos = c.Mouth; c.eatLoop.baseVol = Mathf.MoveTowards(c.eatLoop.baseVol, eating ? 0.8f : 0, dt * 2); }
        }
    }
}
