using System.Collections.Generic;
using UnityEngine;

namespace StillHere
{
    /// What the player is never told: stand up, find the tin, and then (once the second cat wakes) fill two bowls
    /// in two different corners of the room. The old man's mutterings get more specific the longer you are stuck.
    public partial class Game
    {
        public float eatUntil, lastFeedT = -99;

        static readonly (float t, string id)[] HintSeated = { (12, "hint_seat_1"), (32, "hint_seat_2"), (60, "hint_seat_3"), (100, "hint_seat_1") };
        static readonly (float t, string id)[] HintUp = { (16, "hint_up_1"), (40, "hint_up_2"), (70, "hint_up_3"), (105, "hint_up_4"), (150, "hint_up_5"), (210, "hint_up_6"), (280, "hint_up_4") };
        static readonly (float t, string id)[] HintBoth = { (9, "hint_tin_1"), (17, "hint_tin_2"), (48, "hint_tin_3"), (78, "hint_tin_4"), (112, "hint_tin_5"), (170, "hint_tin_1"), (230, "hint_tin_2") };
        static readonly (float t, string id)[] HintOnlyP = { (2.5f, "need_p"), (30, "hint_tin_3"), (65, "hint_tin_1"), (110, "hint_tin_5"), (170, "hint_tin_3") };
        static readonly (float t, string id)[] HintOnlyB = { (2.5f, "need_b"), (30, "hint_tin_4"), (65, "hint_tin_2"), (110, "hint_tin_5"), (170, "hint_tin_4") };

        (float t, string id)[] HintList()
        {
            switch (stage)
            {
                case "seated": return HintSeated;
                case "up": return HintUp;
                case "tin": return !filled[0] && !filled[1] ? HintBoth : !filled[0] ? HintOnlyP : HintOnlyB;
                default: return null;
            }
        }
        void Hints()
        {
            var H = HintList(); if (H == null || P.busy || au.Speaking || hintI >= H.Length) return;
            if (now - progT > H[hintI].t)
            {
                Say(H[hintI].id, 1); hintI++;
                if (stage != "seated") foreach (var c in cats) if (!c.asleep && !filled[c.idx]) Meow(c, hintI > 2 ? "insistent" : "normal", 1.5f + c.idx * 0.8f);
            }
        }

        void TakeTin(Vector3 p)
        {
            hasTin = true; portions = 2;
            Sfx("tin_pick", p); Rumble(0.3f, 0.2f, 0.12f, Haptics.Feel.Firm);
            Say("tin_take", 3);
            SetStage("tin"); progT = now + 9;        // let the surprise play out before any hints
            var pud = cats[0]; Meow(pud, "mrrp", 0.5f); Meow(pud, "insistent", 1.4f); CatMode(pud, "feet");
            Later(3.4f, WakeBiscuit);                // the sound of a tin: across the room, something heavy lands on the floor
        }

        void BowlReach(int who)
        {
            if (portions > 0 && !filled[who]) { Feed(who); return; }
            if (filled[who]) Talk("bowlfull", L("bowl_full"), 0, 6);
            else Talk("bowl" + who, who == 0 ? L("bowl_p_empty", "bowl_generic") : L("bowl_b_empty", "bowl_generic"));
        }

        public void Feed(int who)
        {
            if (portions <= 0 || filled[who] || P.busy) return;
            bool first = portions == 2, last = portions == 1;
            P.busy = true; portions--; filled[who] = true; hintI = 0; progT = now; lastFeedT = now;
            var c = cats[who]; var bp = new Vector3(c.bowl.x, 0.06f, c.bowl.y);
            P.eyeT = 0.9f; P.glance = null;                 // bend down
            SelfSfx("knees"); SelfSfx("breath", 0.6f);
            if (first) Later(0.8f, () => Sfx("ring_pull", bp));
            Later(first ? 1.9f : 0.9f, () => { Sfx("scoop", bp); Haptics.Play(Haptics.Feel.Soft); TouchObject(W[who == 0 ? "bowlP" : "bowlB"], bp, new TouchOpt { R = 0.35f, life = 2.6f }); });
            Later(first ? 2.9f : 1.9f, () => Sfx("bowl_clink", bp, 0.8f));
            Later(first ? 2.2f : 1.2f, () => Say(last ? "fed_last" : who == 0 ? "fed_first_p" : "fed_first_b", 3));
            c.purrHold = now + 60; eatUntil = now + 600; CatMode(c, "eat");
            float up = first ? 3.8f : 2.8f;
            Later(up, () => { EmitCat(c, c.isB ? "eat_1" : "eat_0", null, 3f); P.eyeT = EyeStand; });
            if (!last) { Later(up + 0.6f, () => { P.busy = false; progT = now; }); return; }

            // both fed
            hasTin = false; SetStage("fed"); eatUntil = now + 36;
            Later(9, () => Say("end_1", 2));
            Later(14.5f, () => Say("end_2", 2));
            Later(17, () =>
            {   // straighten up and turn back toward the room
                P.glance = null; P.eyeT = EyeStand;
                float y = Mathf.Atan2(0.3f - P.x, 0.2f - P.z);
                P.turn = new TurnAnim { y0 = P.yaw, y1 = P.yaw + WrapA(y - P.yaw), p0 = P.pitch, p1 = -0.08f, t0 = now, dur = 4.5f };
            });
            Later(19, () => { ambOn = false; ambTarget = 0; RevealRoom(); });
            Later(26, () => { foreach (var k in cats) EmitCat(k, k.isB ? "eat_1" : "eat_0", null, 4.5f, 0.85f, true); });
            Later(30, () => Say("end_3", 2));
            Later(38, () => { foreach (var k in cats) k.purrHold = 0; masterTarget = 0; });
            Later(46, () => { stage = "end"; P.busy = false; Debug.Log("[StillHere] end"); });
        }

        /// A last, faint memory of the whole kitchen, and both cats at their bowls.
        void RevealRoom()
        {
            var centre = new Vector3(P.x, 0.2f, P.z);
            foreach (var o in W.objects)
            {
                if (o.id == "floor") continue;
                bool wall = o.mat == Mat.Wall;
                scratch.Clear();
                foreach (var part in o.parts) TouchParticles.SamplePart(part, centre, float.PositiveInfinity, scratch, wall ? 0.12f : 0.6f);
                EmitSet(scratch, centre, float.PositiveInfinity, new TouchOpt { life = 9, speed = 1.0f, bright = wall ? 0.35f : 0.55f, cap = 2200, delay0 = 0.5f });
            }
            foreach (var k in cats) EmitCat(k, k.isB ? "eat_1" : "eat_0", centre, 8f, 0.8f, true);
        }
    }
}
