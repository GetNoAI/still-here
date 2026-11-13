using System;
using UnityEngine;

namespace StillHere
{
    public partial class Game
    {
        // ------------------------------------------------------------------ getting up, stumbling, falling
        public void StandUp()
        {
            if (stage != "seated" || P.busy) return;
            P.busy = true;
            Sfx("chair_scrape", new Vector3(0, 0.1f, -1.05f)); SelfSfx("knees", 1, 0, 0.5f); SelfSfx("breath", 0.7f, 0, 0.2f);
            Say("stand", 2);
            P.eyeT = EyeStand; P.anim = new MoveAnim { x0 = P.x, z0 = P.z, x1 = 0.55f, z1 = -1.15f, t0 = now + 0.4f, dur = 1.8f };
            Later(2.4f, () => { P.busy = false; P.anim = null; SetStage("up"); SelfSfx("step_rug", 1, 1); });
        }

        public void Stumble(float sev = 1, bool quiet = false)
        {
            if (P.busy) return;
            bool chain = now - P.stumbleT < 4;
            P.stumbleT = now;
            if (chain) { Fall(); return; }
            P.wobbleT0 = now; P.wobbleAmp = 0.13f * sev; P.vx *= 0.15f; P.vz *= 0.15f;
            for (int i = 0; i < 3; i++) SelfSfx("heartbeat", 0.8f - i * 0.15f, 0, i * 0.8f); SelfSfx("breath", 0.8f); Rumble(0.6f, 0.4f, 0.25f, Haptics.Feel.Knock);
            if (!quiet && !Once("stumble1", "stumble_1", 2) && UnityEngine.Random.value < 0.5f) Say(Pick("stumble_2", "stumble_3"), 2);
        }

        public void Fall()
        {
            if (P.busy) return;
            P.busy = true; P.falls++; P.vx = P.vz = 0; P.wobbleT0 = -99;
            Sfx("fall", new Vector3(P.x, 0.1f, P.z), 1); Rumble(1, 1, 0.5f, Haptics.Feel.Heavy);
            P.eyeT = EyeFloor; P.rollT = 0.42f; Muffle(520, 8); for (int i = 0; i < 8; i++) SelfSfx("heartbeat", 1f - i * 0.09f, 0, i * 0.85f);
            int k = Mathf.Min(P.falls, 3);
            Say("fall_" + k + "a", 3);
            Later(2.5f, () => { foreach (var c in cats) if (!c.asleep && !filled[c.idx]) { CatMode(c, "rub"); break; } });
            Later(5.0f, () => Say("fall_" + k + "b", 2));
            Later(6.2f, () => { P.eyeT = EyeStand; P.rollT = 0; SelfSfx("knees"); SelfSfx("breath", 1); });
            Later(8.6f, () => { Muffle(22000, 1.2f); P.busy = false; P.stumbleT = -99; });
        }

        public void Burn(Vector3 p)
        {
            if (P.busy) return;
            Sfx("sizzle", p); Rumble(0.9f, 0.9f, 0.3f, Haptics.Feel.Burn);
            Say("burn", 3); P.flinch = 1; Muffle(1800, 30); Later(0.8f, () => Muffle(22000, 2));
            var f = Fwd(); var r = W.CollideCircle(P.x - f.x * 0.15f, P.z - f.y * 0.15f, PR, W.colPlayer); P.x = r.x; P.z = r.z;
            Stumble(0.8f, true);
            Later(2.6f, () => Once("kettle", "kettle", 1));
        }

        void Bump(WObj o, float cx, float cz, float sp)
        {
            if (o.noBump || now - o.lastBump < 1.1f) return;
            o.lastBump = now; P.bumps++;
            var p = new Vector3(cx, W.ContactY(o, cx, cz), cz);
            float hard = Mathf.Clamp(sp / Walk, 0.3f, 1.2f);
            if (o.mat == Mat.Wall || o.mat == Mat.Door) Sfx("pat_wall", p, 0.9f * hard);
            else if (o.mat == Mat.Metal) Sfx("hand_metal", p, 1.2f * hard);
            else Sfx("knock_wood", p, 0.9f * hard);
            if (sp > 0.45f) Sfx("bump_soft", p, 0.6f * hard);
            TouchObject(o, p);
            Rumble(0.25f + hard * 0.3f, 0.1f, 0.12f, Haptics.Feel.Knock);
            if (o.bump != null) o.bump(o, p, sp); else if (o.lines != null) Talk(o.id, o.lines, 1, 6);
            if (sp > 0.7f && UnityEngine.Random.value < 0.35f) Say(Pick("ow_1", "ow_2"), 2);
            if (P.bumps == 4) Later(1.2f, () => Say("slowly", 1));
        }

        // ------------------------------------------------------------------ per-frame
        void UpdatePlayer(float dt, InputFrame f)
        {
            if (f.turn != 0) P.yaw += f.turn;                 // turning never interrupts a glance
            if (f.tilt != 0)
            {
                if (P.glance != null) { P.pitch = P.glance.back; P.glance = null; }
                P.pitch = Mathf.Clamp(P.pitch + f.tilt, -1.3f, 0.9f); P.lastLook = now;
            }
            if (P.glance != null)                             // look toward the touch, hold while it glows, then look back
            {
                float t = now - P.glance.t0;
                P.pitch = Mathf.Lerp(P.pitch, t < 1.7f ? P.glance.pitch : P.glance.back, Damp(t < 1.7f ? 4 : 2.5f, dt));
                if (t > 3.2f) { P.pitch = P.glance.back; P.glance = null; }
            }
            if (P.turn != null)
            {
                float t = Ease((now - P.turn.t0) / P.turn.dur);
                P.yaw = Mathf.Lerp(P.turn.y0, P.turn.y1, t); P.pitch = Mathf.Lerp(P.turn.p0, P.turn.p1, t);
                if (t >= 1) P.turn = null;
            }
            if (P.anim != null) { float t = Ease((now - P.anim.t0) / P.anim.dur); P.x = Mathf.Lerp(P.anim.x0, P.anim.x1, t); P.z = Mathf.Lerp(P.anim.z0, P.anim.z1, t); }
            if (stage == "seated" && !P.busy && f.move.magnitude > 0.3f) StandUp();

            float speed = 0;
            if (!P.busy && stage != "seated" && stage != "pre" && stage != "intro")
            {
                Vector2 fw = Fwd(), rt = Rgt(); float mx = f.move.x, mz = f.move.y, ml = Hyp(mx, mz);
                float slow = (now - P.stumbleT < 1.2f ? 0.3f : 1f) * (mz < 0 ? 0.6f : 1f);     // backwards is slower
                float tvx = (fw.x * mz + rt.x * mx) * Walk * slow, tvz = (fw.y * mz + rt.y * mx) * Walk * slow;
                float k = Damp(5, dt);
                P.vx = Mathf.Lerp(P.vx, tvx, k); P.vz = Mathf.Lerp(P.vz, tvz, k);
                speed = Hyp(P.vx, P.vz);
                var res = W.CollideCircle(P.x + P.vx * dt, P.z + P.vz * dt, PR, W.colPlayer);
                if (res.hit) { if (speed > 0.18f) Bump(res.o, res.cx, res.cz, speed); P.vx *= 0.3f; P.vz *= 0.3f; }
                float moved = Hyp(res.x - P.x, res.z - P.z);
                P.x = res.x; P.z = res.z;
                if (ml > 0.1f) P.lastMove = now;

                P.stepAcc += moved;
                string sf = SurfaceAt(P.x, P.z);
                if (sf != P.surface) OnSurface(sf, speed);
                P.surface = sf;
                if (P.stepAcc > 0.5f)
                {
                    P.stepAcc = 0; P.foot = -P.foot;
                    SelfSfx(sf == "rug" ? "step_rug" : sf == "wet" ? "step_wet" : sf == "glass" ? "step_glass" : "step_tile", 0.8f + speed * 0.3f, P.foot);
                }
                // pressing on across a wet floor ends badly; stopping, or backing out slowly, is safe
                if (sf == "wet" && speed > 0.2f) { P.wetT += dt; if (P.wetT > 1.0f) { P.wetT = 0; Stumble(1); } } else P.wetT = Mathf.Max(0, P.wetT - dt);
                KickCheck();
                foreach (var c in cats)
                {
                    if (c.asleep) continue;
                    float cdx = c.x - P.x, cdz = c.z - P.z, cd = Hyp(cdx, cdz);
                    if (speed > 0.3f && cd < 0.34f && (cdx * P.vx + cdz * P.vz) / (cd * speed + 1e-6f) > 0.2f && now - c.lastUnder > 15 && now - lastUnderAny > 6 && stage != "fed")
                    {
                        c.lastUnder = now; lastUnderAny = now;
                        Meow(c, "yowl"); EmitCat(c, c.isB ? "jump_0" : "hit_0", null, 1.2f);
                        CatFlee(c);
                        Later(0.35f, () => EmitCat(c, "run_" + UnityEngine.Random.Range(0, 4), null, 1.0f, 0.8f, true));
                        Say(c.isB ? "under_3" : Pick("under_1", "under_2"), 3);
                        Stumble(1.1f);
                    }
                }
                // walking eases your gaze back to the horizon (for keyboard and touch players)
                if (speed > 0.2f && now - P.lastLook > 2.0f && P.glance == null) P.pitch = Mathf.Lerp(P.pitch, -0.12f, Damp(0.9f, dt));
            }
            P.speed = speed;
            P.eye = Mathf.Lerp(P.eye, P.eyeT, Damp(P.eyeT < P.eye ? 7 : 1.6f, dt));
            P.roll = Mathf.Lerp(P.roll, P.rollT, Damp(4, dt));
            P.bob += speed * dt * 7.5f;
            P.flinch = Mathf.Max(0, P.flinch - dt * 3);
        }
        float lastUnderAny = -99;

        public string SurfaceAt(float x, float z)
        {
            if (hasShards && Hyp(x - shards.x, z - shards.y) < shardsR) return "glass";
            foreach (var w in wets) if (Hyp(x - w.x, z - w.y) < w.z) return "wet";
            if (x > -1.2f && x < 1.2f && z > -1.5f && z < 0.6f) return "rug";
            return "tile";
        }
        void OnSurface(string sf, float sp)
        {
            if (sf == "wet")
            {
                SelfSfx("step_wet", 1.3f); Rumble(0.3f, 0.6f, 0.15f, Haptics.Feel.Soft);
                if (!Once("wet1", "wet_1", 2)) Say("wet_2", 1);
                if (sp > 0.45f) Stumble(0.9f, true);
            }
            if (sf == "glass")
            {
                SelfSfx("step_glass", 1.2f); Rumble(0.4f, 0.6f, 0.12f, Haptics.Feel.Firm);
                Once("glass1", "glass_step", 3);
                Stumble(0.6f, true);
            }
        }

        void KickCheck()
        {
            if (now - kickT < 1.5f || P.speed < 0.15f && Hyp(P.vx, P.vz) < 0.15f) return;
            foreach (var id in new[] { "waterBowl", "bowlP", "bowlB" })
            {
                var o = W[id]; var c = o.parts[0].c;
                if (Hyp(P.x - c.x, P.z - c.z) >= 0.29f) continue;
                kickT = now; var p = new Vector3(c.x, 0.05f, c.z);
                TouchObject(o, p, new TouchOpt { R = 0.3f }); Sfx("bowl_clink", p, 1);
                if (id == "waterBowl")
                {
                    if (!waterSpilt) { waterSpilt = true; Sfx("water_spill", p); wets.Add(new Vector3(c.x + 0.25f, c.z, 0.25f)); Say("water_spill", 2); if (SurfaceAt(P.x, P.z) == "wet") P.surface = "wet"; }
                    else Talk("wbk", L("water_empty"));
                }
                else
                {
                    int who = id == "bowlP" ? 0 : 1;
                    if (portions > 0 && !filled[who] && !P.busy) { Say("bowl_kick", 2); Later(1.4f, () => Feed(who)); }
                    else Talk("fbk", L("bowl_kick"));
                }
                return;
            }
        }

        // ------------------------------------------------------------------ reaching out
        /// Reach with your hands. Returns the id of what was touched (for tests).
        public string Reach()
        {
            if (stage == "end") { UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex); return null; }
            // nothing to reach for until you're on your feet: the opening narration plays undisturbed
            if (!started || P.busy || stage == "pre" || stage == "intro" || stage == "seated") return null;
            if (now - P.lastReach < 0.45f) return null;
            P.lastReach = now;
            // arms, not eyes: reach from the chest, angled a little below where you're facing
            bool standing = P.eye > 1.3f;
            Vector3 o = cam.transform.position - new Vector3(0, standing ? 0.28f : 0.15f, 0);
            float rp = Mathf.Clamp((P.glance != null ? P.glance.back : P.pitch) - (standing ? 0.32f : 0.05f), -1.45f, 0.7f);
            Vector3 d = new Vector3(Mathf.Sin(P.yaw) * Mathf.Cos(rp), Mathf.Sin(rp), Mathf.Cos(P.yaw) * Mathf.Cos(rp));
            Func<Vector3, bool> okH = q => Hyp(q.x - P.x, q.z - P.z) <= 1.0f;
            const float maxT = 1.9f;
            Vector2 fw = Fwd();

            Hit hit = W.RayWorld(o, d, maxT); if (hit.valid && !okH(hit.p)) hit.valid = false;
            // whatever you're pressed up against wins, wherever you happen to be looking
            {
                WObj po = null; float pdd = 0; Vector3 pp = default;
                for (float dd = PR; dd <= 0.75f && po == null; dd += 0.04f)
                {
                    float px = P.x + fw.x * dd, pz = P.z + fw.y * dd;
                    foreach (var c in W.colPlayer)
                        if (px >= c.b.x && px <= c.b.z && pz >= c.b.y && pz <= c.b.w) { po = c.o; pdd = dd; pp = new Vector3(px, W.ContactY(c.o, px, pz), pz); break; }
                }
                if (po != null)
                {
                    float hd = hit.valid ? Hyp(hit.p.x - P.x, hit.p.z - P.z) : 1e9f;
                    bool sameAndClose = hit.valid && hit.o == po && hd <= pdd + 0.5f;     // e.g. the table top in front of you
                    if (!sameAndClose && hd > pdd + 0.05f) hit = new Hit { valid = true, o = po, t = pdd, p = pp, n = new Vector3(-fw.x, 0, -fw.y), part = po.parts[0] };
                }
            }
            Hit small = W.FatRay(o, d, maxT, ob => ob.small); if (small.valid && !okH(small.p)) small.valid = false;
            if (!small.valid)
            {   // bending to feel for small things near your feet, roughly in front
                float bestD = 1e9f;
                foreach (var ob in W.objects)
                {
                    if (!ob.small) continue;
                    var pt = ob.parts[0]; if (pt.max.y > 0.5f) continue;
                    float cx = (pt.min.x + pt.max.x) * 0.5f, cz = (pt.min.z + pt.max.z) * 0.5f, dx = cx - P.x, dz = cz - P.z, hd = Hyp(dx, dz);
                    if (hd > 0.85f || hd < 1e-3f) continue;
                    if ((dx * fw.x + dz * fw.y) / hd < 0.82f) continue;                    // within ~35 degrees of facing
                    if (hd < bestD) { bestD = hd; small = new Hit { valid = true, o = ob, part = pt, t = hd, p = new Vector3(cx, pt.max.y, cz), n = Vector3.up }; }
                }
                bool hitIsShell = hit.valid && (hit.o.id == "floor" || hit.o.id.StartsWith("wall"));
                if (small.valid && hit.valid && !hitIsShell && Hyp(hit.p.x - P.x, hit.p.z - P.z) < bestD) small.valid = false;
                if (small.valid && hit.valid && hitIsShell) hit = small;
            }
            // a cat: a soft sphere around its body
            Cat hitCat = null; float catT = 0;
            foreach (var c in cats)
            {
                if (c.asleep) continue;
                Vector3 cc = new Vector3(c.x, 0.22f, c.z), oc = cc - o; float t = Vector3.Dot(oc, d);
                if (t <= 0 || t >= maxT) continue;
                if ((o + d * t - cc).magnitude < 0.27f && Hyp(cc.x - P.x, cc.z - P.z) < 1.05f && (hitCat == null || t < catT)) { hitCat = c; catT = t; }
            }
            if (hitCat != null && (!hit.valid || catT < hit.t + 0.2f || hit.o.id == "floor") && (!small.valid || catT < small.t + 0.1f)) { Stroke(hitCat); return "cat" + (hitCat.isB ? "B" : "P"); }
            if (small.valid && (!hit.valid || hit.o.id == "floor" || small.t < hit.t + 0.12f)) hit = small;
            // a cat near your feet, roughly in front, with no furniture in the way: your hand finds it
            if (!hit.valid || hit.o.id == "floor" || hit.o.id.StartsWith("wall") || hit.o.id == "door")
            {
                Cat near = null; float nd = 0.9f;
                foreach (var c in cats)
                {
                    if (c.asleep) continue;
                    float dx = c.x - P.x, dz = c.z - P.z, hd = Hyp(dx, dz);
                    if (hd < nd && hd > 1e-3f && (dx * fw.x + dz * fw.y) / hd > 0.75f) { near = c; nd = hd; }
                }
                if (near != null) { Stroke(near); return "cat" + (near.isB ? "B" : "P"); }
            }

            SelfSfx("reach", 1);
            if (!hit.valid)
            {
                if (now - P.lastWhoosh > 7 && UnityEngine.Random.value < 0.6f) { Say(Pick("nothing_1", "nothing_2"), 0); P.lastWhoosh = now; }
                return null;
            }
            var obj = hit.o; obj.touches++;
            switch (obj.mat)
            {
                case Mat.Wood: Sfx("hand_wood", hit.p, 1, 0.08f); break;
                case Mat.Metal: Sfx("hand_metal", hit.p, 0.8f, 0.08f); break;
                case Mat.Glass: Sfx("glass_clink", hit.p, 0.5f, 0.08f); break;
                case Mat.Ceramic: Sfx("bowl_clink", hit.p, 0.6f, 0.08f); break;
                default: Sfx("pat_wall", hit.p, 0.6f, 0.08f); break;
            }
            TouchObject(obj, hit.p);
            // what your fingertips meet: soft wood and walls, firm metal and china
            Rumble(0.15f, 0.1f, 0.06f, obj.mat == Mat.Metal || obj.mat == Mat.Glass || obj.mat == Mat.Ceramic ? Haptics.Feel.Firm : Haptics.Feel.Soft);
            if (obj.reach != null) obj.reach(obj, hit); else if (obj.lines != null) Talk(obj.id, obj.lines);
            return obj.id;
        }
    }
}
