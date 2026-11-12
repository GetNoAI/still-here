using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StillHere
{
    /// Test hooks. Nothing here runs unless called from the Editor (script eval) during Play mode.
    public partial class Game
    {
        /// Step the simulation deterministically for `sec` seconds while holding a move/turn input, e.g. Advance(1.2f, 0, 1, 0).
        public string Advance(float sec, float moveX = 0, float moveY = 0, float turnRate = 0)
        {
            manual = true;
            input.debugMove = new Vector2(moveX, moveY); input.debugTurnRate = turnRate;
            int n = Mathf.RoundToInt(sec * 60);
            for (int i = 0; i < n; i++) Step(1f / 60f);
            input.debugMove = Vector2.zero; input.debugTurnRate = 0;
            fx.SetNow(now); fx.Flush();
            return State();
        }
        public string DoReach() { P.lastReach = -99; return Reach() ?? "(nothing)"; }
        public void Teleport(float x, float z, float yaw, float pitch = -0.12f) { P.x = x; P.z = z; P.yaw = yaw; P.pitch = pitch; P.glance = null; P.vx = P.vz = 0; UpdateCamera(); }
        public void TurnTo(float yaw) { for (int i = 0; i < 600 && Mathf.Abs(WrapA(yaw - P.yaw)) > 0.03f; i++) { input.debugTurnRate = WrapA(yaw - P.yaw) > 0 ? 1.9f : -1.9f; manual = true; Step(1f / 60f); } input.debugTurnRate = 0; }
        /// Walk forward until the predicate-ish condition (axis: 'x' or 'z', greater/less than value) or max seconds.
        public string WalkUntil(char axis, bool greater, float value, float max = 8)
        {
            float t = 0;
            while (t < max)
            {
                float v = axis == 'x' ? P.x : P.z; if (greater ? v > value : v < value) break;
                Advance(0.1f, 0, 1); t += 0.1f;
            }
            return State();
        }
        /// Test walker: turn toward a point and walk there using only the normal move/turn inputs. Sidesteps if blocked.
        public bool GoTo(float x, float z, float max = 14)
        {
            float t = 0, stall = 0; manual = true;
            while (t < max)
            {
                float dx = x - P.x, dz = z - P.z, d = Hyp(dx, dz);
                if (d < 0.12f) return true;
                if (P.busy) { Advance(0.25f); t += 0.25f; continue; }
                TurnTo(Mathf.Atan2(dx, dz));
                float bx = P.x, bz = P.z;
                Advance(0.1f, 0, Mathf.Min(1f, d * 2.5f + 0.35f)); t += 0.1f;
                if (Hyp(P.x - bx, P.z - bz) < 0.02f) stall += 0.1f; else stall = 0;
                if (stall > 0.5f) { Advance(0.7f, UnityEngine.Random.value < 0.5f ? 1 : -1, -0.2f); t += 0.7f; stall = 0; }
            }
            return false;
        }
        public void SkipIntro() { timers.Clear(); P.busy = false; SetStage("seated"); cats[0].nextMeow = now + 6; }
        public void BeginForTest(bool silent) { manual = true; au.SetSilent(silent); if (!started) StartGame(); }

        public string State()
        {
            var sb = new StringBuilder();
            sb.Append($"t={now:F1} stage={stage} pos=({P.x:F2},{P.z:F2}) yaw={P.yaw:F2} pitch={P.pitch:F2} eye={P.eye:F2} busy={P.busy} bumps={P.bumps} falls={P.falls} tin={hasTin} portions={portions} filled={filled[0]},{filled[1]} line={au.lastLine} live={fx.LiveCount(now)}");
            foreach (var c in cats) sb.Append($" | {c.name}:{c.mode}({c.x:F2},{c.z:F2}){(c.asleep ? " asleep" : "")}");
            return sb.ToString();
        }

        /// Run both cats for a while with the player hopping about, and report anything that leaves the room or sticks.
        public string SimCats(float seconds, string stg)
        {
            bool wasSilent = au.silent; au.SetSilent(true); manual = true; headless = true;
            string saveStage = stage; stage = stg; if (stg == "tin" && cats[1].asleep) { cats[1].asleep = false; cats[1].y = 0; cats[1].x = -0.95f; cats[1].z = -0.68f; CatMode(cats[1], "feet"); }
            int steps = Mathf.RoundToInt(seconds * 30), outside = 0, inside = 0; float[] still = new float[2], maxStill = new float[2];
            var last = new Vector2[2]; for (int k = 0; k < 2; k++) last[k] = new Vector2(cats[k].x, cats[k].z);
            var modes = new Dictionary<string, int>();
            for (int i = 0; i < steps; i++)
            {
                now += 1f / 30f;
                if (i % 300 == 0) { var r = W.CollideCircle(Rand(-1.8f, 1.8f), Rand(-1.6f, 1.0f), PR, W.colPlayer); P.x = r.x; P.z = r.z; P.lastMove = now; }
                for (int k = 0; k < 2; k++)
                {
                    var c = cats[k]; if (c.asleep) continue;
                    UpdateCat(c, 1f / 30f);
                    if (Mathf.Abs(c.x) > 2.4f || Mathf.Abs(c.z) > 1.9f) outside++;
                    foreach (var cb in W.colCat) if (c.x > cb.b.x + 0.02f && c.x < cb.b.z - 0.02f && c.z > cb.b.y + 0.02f && c.z < cb.b.w - 0.02f) { inside++; break; }
                    float m = Hyp(c.x - last[k].x, c.z - last[k].y); last[k] = new Vector2(c.x, c.z);
                    if (m < 1e-4f && c.mode != "lead" && c.mode != "eat") still[k] += 1f / 30f; else still[k] = 0;
                    maxStill[k] = Mathf.Max(maxStill[k], still[k]);
                    modes[c.name + ":" + c.mode] = (modes.TryGetValue(c.name + ":" + c.mode, out var v) ? v : 0) + 1;
                }
            }
            stage = saveStage; au.SetSilent(wasSilent); headless = false;
            var sb = new StringBuilder($"sim {seconds}s stage={stg}: outsideFrames={outside} insideFurnitureFrames={inside} longestStill P={maxStill[0]:F1}s B={maxStill[1]:F1}s modes:");
            foreach (var kv in modes) sb.Append($" {kv.Key}={kv.Value / 30}s");
            return sb.ToString();
        }

        /// Place a looping test sound at a spot relative to the head and report what reaches each ear.
        AudioDirector.Emitter probeLoop;
        public void ProbeAt(string id, float right, float up, float forward)
        {
            var t = cam.transform; Vector3 p = t.position + t.right * right + t.up * up + t.forward * forward;
            if (probeLoop == null) probeLoop = au.Loop(id, p, 0.8f, 0.6f, 1.1f, false);
            probeLoop.Pos = p; probeLoop.baseVol = 0.8f; au.probe.Read();
        }
        public string ProbeRead() { var v = au.probe.Read(); return $"L={v.x:F4} R={v.y:F4} bright={(v.x > 1e-6f ? v.z / v.x : 0):F3}"; }
        public void ProbeStop() { if (probeLoop != null) probeLoop.baseVol = 0; }
    }
}
