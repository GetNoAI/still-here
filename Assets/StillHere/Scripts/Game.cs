using System;
using System.Collections.Generic;
using UnityEngine;

namespace StillHere
{
    /// STILL HERE. Near-total darkness: you perceive the kitchen through sound, an old man's mutterings,
    /// and faint particle outlines of whatever you (or the cats) touch. No UI of any kind.
    /// This class builds everything at runtime; the scene only needs one GameObject carrying it.
    public partial class Game : MonoBehaviour
    {
        public static Game I;
        public const float EyeStand = 1.55f, EyeSit = 1.12f, EyeFloor = 0.38f, PR = 0.25f, CatR = 0.12f, Fov = 75f, Walk = 0.85f;

        public float now;                 // game clock, seconds
        public bool manual;               // tests drive Step() themselves
        public bool headless;             // simulation only: skip visuals
        [NonSerialized] public World W = new World();
        public TouchParticles fx;
        public AudioDirector au;
        [NonSerialized] public InputHub input = new InputHub();
        public CatPoseSet poses;
        public Camera cam;
        AudioLowPassFilter earLP; float earTarget = 22000, earRate = 3;

        public class Glance { public float pitch, back, t0; }
        public class MoveAnim { public float x0, z0, x1, z1, t0, dur; }
        public class TurnAnim { public float y0, y1, p0, p1, t0, dur; }
        public class Player
        {
            public float x = 0, z = -1.05f, yaw = 0, pitch = -0.22f, roll, rollT, eye = EyeSit, eyeT = EyeSit, vx, vz, speed;
            public float stepAcc, stumbleT = -99, lastLook = -99, lastMove, lastReach = -99, wetT, lastWhoosh = -99, bob, flinch;
            public int foot = 1, falls, bumps;
            public bool busy = true;
            public string surface = "rug";
            public Glance glance; public MoveAnim anim; public TurnAnim turn;
            public float wobbleT0 = -99, wobbleAmp;
        }
        [NonSerialized] public Player P = new Player();

        // ---- game state
        public string stage = "pre";      // pre, intro, seated, up, tin, fed, end
        public bool started, hasTin, ambOn = true, waterSpilt, hasShards;
        public int portions, hintI, fridgeState;
        public float progT, kickT = -99, shardsR, ambTarget = 0, masterTarget = 1;
        public string glass = "table";
        public Vector2 shards;
        public readonly List<Vector3> wets = new List<Vector3> { new Vector3(-0.5f, 1.05f, 0.3f) };   // x, z, r
        public readonly bool[] filled = new bool[2];

        readonly List<(float t, Action fn)> timers = new List<(float, Action)>();
        readonly Dictionary<string, (int i, float t)> talk = new Dictionary<string, (int, float)>();
        readonly HashSet<string> said = new HashSet<string>();
        float headphonesT;

        // ------------------------------------------------------------------ helpers
        public static float Hyp(float a, float b) => Mathf.Sqrt(a * a + b * b);
        static float Ease(float t) => t < 0 ? 0 : t > 1 ? 1 : t * t * (3 - 2 * t);
        static float WrapA(float a) { while (a > Mathf.PI) a -= 2 * Mathf.PI; while (a < -Mathf.PI) a += 2 * Mathf.PI; return a; }
        static float Damp(float rate, float dt) => 1 - Mathf.Exp(-dt * rate);
        static T Pick<T>(params T[] a) => a[UnityEngine.Random.Range(0, a.Length)];
        static float Rand(float a, float b) => UnityEngine.Random.Range(a, b);
        public Vector2 Fwd() => new Vector2(Mathf.Sin(P.yaw), Mathf.Cos(P.yaw));     // (x, z)
        public Vector2 Rgt() => new Vector2(Mathf.Cos(P.yaw), -Mathf.Sin(P.yaw));

        public void Later(float s, Action fn) { timers.Add((now + s, fn)); }
        void SetStage(string s) { stage = s; progT = now; hintI = 0; Debug.Log("[StillHere] stage -> " + s); }

        public void Say(string id, int pri = 1) { au.Say(id, pri); }
        bool Once(string key, string id, int pri = 1) { if (!said.Add(key)) return false; Say(id, pri); return true; }
        /// Speaks ids[0], ids[1]... on successive touches, then varies among the later ones. Returns false while cooling down.
        bool Talk(string key, string[] ids, int pri = 1, float cool = 5)
        {
            talk.TryGetValue(key, out var L); if (!talk.ContainsKey(key)) L = (0, -99);
            if (now - L.t < cool) return false;
            string id = L.i < ids.Length ? ids[L.i] : ids[ids.Length > 1 ? UnityEngine.Random.Range(1, ids.Length) : 0];
            talk[key] = (L.i + 1, now);
            Say(id, pri); return true;
        }
        static string[] L(params string[] a) => a;

        void Sfx(string id, Vector3 p, float vol = 1, float delay = 0, float pitch = 1) { au.Play(id, p, vol, pitch, delay); }
        void SelfSfx(string id, float vol = 1, int foot = 0, float delay = 0) { au.PlaySelf(id, vol, foot, delay); }
        void Muffle(float cutoff, float rate = 3) { earTarget = cutoff; earRate = rate; }
        void Rumble(float strong, float weak = 0.2f, float secs = 0.15f) { input.Rumble(strong, weak, secs); }

        // ------------------------------------------------------------------ boot
        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            foreach (var c in FindObjectsByType<Camera>()) c.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>()) l.gameObject.SetActive(false);
            RenderSettings.skybox = null; RenderSettings.ambientLight = Color.black; RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            var head = new GameObject("Head");
            cam = head.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.fieldOfView = Fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 60; cam.allowHDR = false; cam.allowMSAA = false;
            head.tag = "MainCamera";
            head.AddComponent<AudioListener>();
            earLP = head.AddComponent<AudioLowPassFilter>(); earLP.cutoffFrequency = 22000;

            fx = new GameObject("TouchParticles").AddComponent<TouchParticles>();
            au = new GameObject("Audio").AddComponent<AudioDirector>();
            au.listener = head.transform;
            au.probe = head.AddComponent<AudioProbe>();
            poses = Resources.Load<CatPoseSet>("CatPoses");
            if (!poses) Debug.LogError("[StillHere] CatPoses asset missing: run StillHere > Bake Cat Poses");
            input.Enable();

#if STEAMAUDIO_ENABLED && !UNITY_WEBGL
            if (AudioDirector.Hrtf && SteamAudio.SteamAudioManager.Singleton != null) SteamAudio.SteamAudioManager.NotifyAudioListenerChangedTo(head.transform);
#endif
            BuildKitchen();
            InitCats();
            UpdateCamera();
        }

        void Update()
        {
            input.Tick();
            if (!manual) Step(Mathf.Min(0.05f, Time.deltaTime));
        }

        public void Step(float dt)
        {
            now += dt;
            fx.SetNow(now);
            var f = input.Poll(dt);
            if (!started)
            {
                UpdateCamera();
                if (now > headphonesT) { EmitHeadphones(); headphonesT = now + 2.2f; }
                if (f.any) StartGame();
                return;
            }
            for (int i = timers.Count - 1; i >= 0; i--)
                if (timers[i].t <= now) { var fn = timers[i].fn; timers.RemoveAt(i); try { fn(); } catch (Exception e) { Debug.LogException(e); } }

            if (!manual && !au.silent)
            {
                if (now >= nextTick) { tock = !tock; au.Play("clock", SrcClock, 1.8f, tock ? 0.93f : 1f, 0, 1.0f, 1.0f); nextTick += 1f; if (nextTick < now) nextTick = now + 1f; }
                if (now >= nextDrip) { if (ambOn) au.Play("drip", SrcSink, 1.4f * au.ambience, Rand(0.92f, 1.08f), 0, 0.7f, 1.0f); nextDrip = now + Rand(1.3f, 2.8f); }
            }
            if (f.reach) Reach();
            UpdatePlayer(dt, f);
            foreach (var c in cats) UpdateCat(c, dt);
            Hints();
            au.ambience = Mathf.MoveTowards(au.ambience, ambTarget, dt * (ambTarget > au.ambience ? 0.22f : 0.3f));
            au.master = Mathf.MoveTowards(au.master, masterTarget, dt * 0.25f);
            earLP.cutoffFrequency = Mathf.Lerp(earLP.cutoffFrequency, earTarget, Damp(earRate, dt));
            UpdateCamera();
        }

        void StartGame()
        {
            if (started) return;
            started = true;
            // Capture the mouse for looking around, except on phones and tablets: mobile browsers refuse pointer capture
            // and Unity's web player treats that refusal as a fatal error.
            if (!manual && !Application.isMobilePlatform && UnityEngine.InputSystem.Touchscreen.current == null) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            StartAmbience();
            ambTarget = 1;
            SetStage("intro");
            var p = cats[0]; p.x = 0.05f; p.z = -0.45f; p.h = Mathf.PI / 2; CatMode(p, "feet");
            Later(2.2f, () => Meow(p, "normal"));
            Later(4.0f, () => StartCross(p, -1));
            Later(6.6f, () => Say("intro_1", 3));
            Later(9.6f, () => Meow(p, "insistent"));
            Later(11.4f, () => Say("intro_2", 3));
            Later(14.0f, () => { P.busy = false; SetStage("seated"); p.nextMeow = now + 6; });
        }

        // ------------------------------------------------------------------ landmark sounds
        public static readonly Vector3 SrcFridge = new Vector3(2.1f, 0.4f, 1.65f), SrcSink = new Vector3(-1.0f, 0.86f, 1.75f), SrcClock = new Vector3(2.45f, 1.9f, -0.3f),
            SrcKettle = new Vector3(0.7f, 1.0f, 1.7f), SrcDoor = new Vector3(-2.45f, 1.2f, -1.05f), SrcDoorLow = new Vector3(-2.45f, 0.05f, -1.05f), SrcRadiator = new Vector3(0, 0.5f, -1.9f);
        AudioDirector.Emitter fridgeLoop;
        float nextTick, nextDrip; bool tock;
        void StartAmbience()
        {
            fridgeLoop = au.Loop("fridge_hum", SrcFridge, 0.4f, 0.8f, 1.2f);
            nextTick = now + 0.5f; nextDrip = now + 1f;     // the clock and the tap are single ticks and drips, timed here
            au.Loop("kettle", SrcKettle, 0.45f, 0.4f, 1.3f);
            au.Loop("rain", SrcDoor, 0.7f, 0.9f, 1.1f);
            au.Loop("draught", SrcDoorLow, 0.4f, 0.7f, 1.2f);
            au.Loop("radiator", SrcRadiator, 1.4f, 0.9f, 1.0f);      // the only fixed cue for Biscuit's bowl: keep it clear
            au.tableRect = new Vector4(-0.6f, -0.6f, 0.6f, 0.2f);
        }

        // ------------------------------------------------------------------ touch outlines
        public class TouchOpt { public float R = -1, density = 1, life = 2.0f, bright = 1, speed = 1.3f, delay0 = 0, sizeMul = 1; public int cap = 2600; public bool noGlance; }
        readonly List<Vector4> scratch = new List<Vector4>(8192);

        public void EmitSet(List<Vector4> pts, Vector3 p, float R, TouchOpt o)
        {
            float keep = pts.Count > o.cap ? o.cap / (float)pts.Count : 1; bool fin = !float.IsInfinity(R);
            foreach (var q in pts)
            {
                if (keep < 1 && UnityEngine.Random.value > keep) continue;
                var w = new Vector3(q.x, q.y, q.z); float d = (w - p).magnitude;
                float fall = fin ? 1 - 0.65f * d / R : 1;
                fx.Emit(w, now + o.delay0 + d / o.speed + UnityEngine.Random.value * 0.08f, o.life * Rand(0.8f, 1.2f), Rand(0.0026f, 0.0046f) * o.sizeMul, q.w * fall * o.bright);
            }
        }
        public void TouchObject(WObj o, Vector3 p, TouchOpt opt = null)
        {
            opt = opt ?? new TouchOpt();
            float R = opt.R > 0 ? opt.R : o.R;
            scratch.Clear();
            foreach (var part in o.parts) TouchParticles.SamplePart(part, p, R, scratch, opt.density);
            EmitSet(scratch, p, R, opt);
            if (!opt.noGlance) GlanceTo(p);
        }
        /// Before the game begins: a faint, breathing outline of headphones. No words.
        void EmitHeadphones()
        {
            scratch.Clear();
            var t = cam.transform;
            void Pt(float x, float y) { var w = t.TransformPoint(new Vector3(x, y, 2.4f)); scratch.Add(new Vector4(w.x, w.y, w.z, 1)); }
            for (int i = 0; i <= 70; i++) { float a = Mathf.PI * i / 70f; Pt(Mathf.Cos(a) * 0.36f, Mathf.Sin(a) * 0.34f - 0.02f); }
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 46; i++)
                {
                    float a = i / 46f * 2 * Mathf.PI;
                    Pt(s * 0.37f + Mathf.Cos(a) * 0.075f, -0.08f + Mathf.Sin(a) * 0.12f);
                    Pt(s * 0.37f + Mathf.Cos(a) * 0.04f, -0.08f + Mathf.Sin(a) * 0.075f);
                }
            EmitSet(scratch, t.TransformPoint(new Vector3(0, 0.3f, 2.4f)), float.PositiveInfinity, new TouchOpt { life = 2.4f, speed = 3, bright = 0.55f });
        }

        // ------------------------------------------------------------------ camera & glance
        /// A reflexive tilt of the head toward what was touched, so its outline is seen; then back. Never turns you.
        public void GlanceTo(Vector3 p)
        {
            var cp = cam.transform.position;
            float hd = Hyp(p.x - cp.x, p.z - cp.z), ang = Mathf.Atan2(p.y - cp.y, hd), vHalf = Fov * 0.5f * Mathf.Deg2Rad;
            float bas = P.glance != null ? P.glance.back : P.pitch, tp = bas;
            if (ang < bas - vHalf * 0.6f) tp = Mathf.Clamp(ang + vHalf * 0.45f, -1.25f, 0.4f);
            if (ang > bas + vHalf * 0.8f) tp = Mathf.Clamp(ang - vHalf * 0.4f, -1.25f, 0.6f);
            if (Mathf.Abs(tp - bas) > 0.01f) P.glance = new Glance { pitch = tp, back = bas, t0 = now };
        }
        void UpdateCamera()
        {
            float bobY = Mathf.Sin(P.bob) * 0.018f * (P.eyeT == EyeStand ? 1 : 0), bobR = Mathf.Sin(P.bob * 0.5f) * 0.006f, wob = 0;
            float wt = now - P.wobbleT0; if (wt >= 0 && wt < 1.4f) wob = Mathf.Sin(wt * 11) * P.wobbleAmp * (1 - wt / 1.4f);
            cam.transform.position = new Vector3(P.x, P.eye + bobY, P.z);
            cam.transform.rotation = Quaternion.Euler(-(P.pitch + P.flinch * 0.18f) * Mathf.Rad2Deg, P.yaw * Mathf.Rad2Deg, (P.roll + bobR + wob) * Mathf.Rad2Deg);
        }
    }
}
