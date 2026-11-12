using System.Collections.Generic;
using UnityEngine;

namespace StillHere
{
    /// All sound. Clips come from Resources/SFX and Resources/VO (generated with ElevenLabs).
    /// Unity's stock 3D audio only pans and attenuates, so this adds the cues a listener in the dark needs:
    /// distance roll-off and air absorption, muffling for sounds behind you, a wetter room sound with distance,
    /// and a wider image for sounds right beside you. With Steam Audio installed, direction is rendered with true HRTF.
    public class AudioDirector : MonoBehaviour
    {
        public static AudioDirector I;

        public class Emitter
        {
            public GameObject go; public AudioSource src; public AudioLowPassFilter lp;
            public float baseVol = 1, refDist = 0.6f, roll = 1.1f, fade = 1;
            public bool loop, ambient, busy;
            public Vector3 Pos { get => go.transform.position; set => go.transform.position = value; }
        }

        readonly Dictionary<string, AudioClip> sfx = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioClip> vo = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, List<string>> groups = new Dictionary<string, List<string>>();
        readonly List<Emitter> pool = new List<Emitter>();
        readonly List<Emitter> loops = new List<Emitter>();
        readonly HashSet<string> warned = new HashSet<string>();
        AudioSource self, selfL, selfR, voice;
        public Transform listener;
        public AudioProbe probe;

        /// True when an HRTF spatializer (Steam Audio) is active; otherwise sounds are panned and filtered.
#if STEAMAUDIO_ENABLED && !UNITY_WEBGL
        public static bool Hrtf => AudioSettings.GetSpatializerPluginName().Contains("Steam Audio");
#else
        public static bool Hrtf => false;      // Steam Audio is not available here (e.g. web builds): pan + filter instead
#endif
        public bool silent;                     // headless simulation: no sound, lines are only logged
        public float ambience = 1;              // faded to 0 at the ending
        public float master = 1;
        float duck = 1;
        public Vector4 tableRect;               // x0,z0,x1,z1: sounds under the table are muffled
        public readonly List<string> saidLog = new List<string>();
        public string lastLine = "";

        static readonly HashSet<string> LoopIds = new HashSet<string> { "purr_p", "purr_b", "cat_eat", "snore", "fridge_hum", "rain", "draught", "kettle", "radiator" };
        // per-clip mix trims (everything is peak-normalised first)
        static readonly Dictionary<string, float> Trim = new Dictionary<string, float>
        {
            { "clock", 0.5f }, { "drip", 0.75f }, { "step_tile", 0.32f }, { "step_rug", 0.3f }, { "step_wet", 0.5f }, { "step_glass", 0.55f }, { "reach", 0.22f },
            { "hand_wood", 0.35f }, { "hand_metal", 0.35f }, { "pat_wall", 0.4f }, { "knock_wood", 0.7f }, { "bump_soft", 0.7f },
            { "bell_p", 0.3f }, { "bell_p2", 0.3f }, { "bell_b", 0.34f }, { "fur", 0.4f }, { "knees", 0.4f }, { "breath", 0.45f }, { "heartbeat", 0.8f },
            { "meow_p", 0.75f }, { "meow_b", 0.8f }, { "yowl", 1f }, { "hiss", 0.7f }, { "cat_land", 0.8f },
            { "creak_open", 0.6f }, { "creak_close", 0.6f }, { "rummage", 0.55f }, { "pans", 0.7f }, { "tin_pick", 0.6f }, { "ring_pull", 0.7f }, { "scoop", 0.65f },
            { "bowl_clink", 0.5f }, { "glass_clink", 0.55f }, { "glass_smash", 1f }, { "bucket", 0.95f }, { "bin", 0.7f }, { "water_spill", 0.8f },
            { "fall", 1f }, { "sizzle", 0.8f }, { "chair_scrape", 0.6f }, { "fridge_open", 0.6f }, { "fridge_close", 0.6f },
        };

        void Awake()
        {
            I = this;
            foreach (var c in Resources.LoadAll<AudioClip>("VO")) vo[c.name] = c;
            foreach (var c in Resources.LoadAll<AudioClip>("SFX"))
            {
                var prepared = Prepare(c, LoopIds.Contains(c.name));
                sfx[c.name] = prepared;
                string g = c.name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                if (g != c.name) { if (!groups.TryGetValue(g, out var l)) groups[g] = l = new List<string>(); l.Add(c.name); }
            }
            { int rawN = 0; foreach (var l in audit) if (l.Contains("RAW") || l.Contains("EXCEPTION")) rawN++; Debug.Log($"[StillHere] audio ready: {sfx.Count} effects, {vo.Count} voice lines, hrtf={Hrtf}, unprepared clips={rawN}"); }
            self = Make2D("self", 0); selfL = Make2D("selfL", -0.22f); selfR = Make2D("selfR", 0.22f);
            voice = Make2D("voice", 0); voice.reverbZoneMix = 0.12f; voice.priority = 0;
            var rz = gameObject.AddComponent<AudioReverbZone>();   // one small tiled room
            rz.minDistance = 40; rz.maxDistance = 50; rz.reverbPreset = AudioReverbPreset.User;
            rz.room = -1100; rz.roomHF = -500; rz.roomLF = 0; rz.decayTime = 0.55f; rz.decayHFRatio = 0.6f;
            rz.reflections = -500; rz.reflectionsDelay = 0.007f; rz.reverb = -350; rz.reverbDelay = 0.011f;
            rz.HFReference = 5000; rz.LFReference = 250; rz.diffusion = 90; rz.density = 80;
        }

        AudioSource Make2D(string n, float pan)
        {
            var g = new GameObject("audio_" + n); g.transform.SetParent(transform, false);
            var s = g.AddComponent<AudioSource>(); s.spatialBlend = 0; s.panStereo = pan; s.playOnAwake = false; s.reverbZoneMix = 0.5f; s.dopplerLevel = 0;
            return s;
        }

        // ------------------------------------------------------------------ clip preparation

        // Generated "single" sounds often contain several events; these are cut to the first one. Value = silence (s) that ends an event.
        static readonly Dictionary<string, float> SingleGap = new Dictionary<string, float>
        {
            { "step_tile", 0.09f }, { "step_rug", 0.09f }, { "step_wet", 0.14f }, { "step_glass", 0.12f }, { "bell_p", 0.1f }, { "bell_p2", 0.2f }, { "bell_b", 0.1f },
            { "hand_metal", 0.1f }, { "pat_wall", 0.1f }, { "knock_wood", 0.14f }, { "bump_soft", 0.12f }, { "bowl_clink", 0.15f }, { "tin_pick", 0.2f },
            { "cat_land", 0.3f }, { "meow_p", 0.18f }, { "meow_b", 0.18f }, { "yowl", 0.18f }, { "hiss", 0.18f }, { "glass_clink", 0.25f },
        };
        // Hard caps (s) so nothing overstays.
        static readonly Dictionary<string, float> MaxLen = new Dictionary<string, float>
        {
            { "reach", 0.6f }, { "hand_wood", 0.55f }, { "fur", 1.2f }, { "rummage", 1.4f }, { "pans", 1.6f }, { "breath", 3.5f }, { "heartbeat", 3.5f }, { "knees", 0.8f },
            { "chair_scrape", 1.4f }, { "creak_open", 1.5f }, { "creak_close", 1.2f }, { "ring_pull", 1.8f }, { "scoop", 1.5f }, { "sizzle", 1.2f }, { "fall", 1.8f },
            { "glass_smash", 2.5f }, { "water_spill", 2.0f }, { "bucket", 2.0f }, { "bin", 1.2f }, { "fridge_open", 1.8f }, { "fridge_close", 1.0f },
            { "step_tile", 0.45f }, { "step_rug", 0.45f }, { "step_wet", 0.7f }, { "step_glass", 0.6f }, { "bell_p", 0.5f }, { "bell_p2", 0.7f }, { "bell_b", 0.5f },
            { "meow_p", 1.6f }, { "meow_b", 2.0f }, { "yowl", 1.2f }, { "hiss", 1.2f }, { "cat_land", 0.8f }, { "pat_wall", 0.4f }, { "hand_metal", 0.5f },
            { "knock_wood", 0.6f }, { "bump_soft", 0.6f }, { "bowl_clink", 0.7f }, { "tin_pick", 0.9f }, { "glass_clink", 1.0f },
        };
        static readonly HashSet<string> Rhythmic = new HashSet<string> { "clock", "drip" };   // looped beat-to-beat, not crossfaded
        public readonly List<string> audit = new List<string>();
        static string Group(string id) => id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        static bool Lookup(Dictionary<string, float> d, string id, out float v) => d.TryGetValue(id, out v) || d.TryGetValue(Group(id), out v);

        /// Mono, silence trimmed, cut to one event where needed, peak-normalised; loops get a clean seam.
        AudioClip Prepare(AudioClip raw, bool loop)
        {
            try
            {
                if (raw.loadState != AudioDataLoadState.Loaded) raw.LoadAudioData();
                int ch = raw.channels, n = raw.samples, sr = raw.frequency; if (n < 64) { audit.Add(raw.name + ": too short, raw"); return raw; }
                var d = new float[n * ch];
                if (!raw.GetData(d, 0)) { audit.Add(raw.name + ": GetData failed, RAW clip used"); return raw; }
                var m = new float[n]; float peak = 0;
                for (int i = 0; i < n; i++) { float s = 0; for (int c = 0; c < ch; c++) s += d[i * ch + c]; s /= ch; m[i] = s; float a = Mathf.Abs(s); if (a > peak) peak = a; }
                if (peak < 1e-4f) { audit.Add(raw.name + ": silent, raw"); return raw; }
                float th = peak * 0.03f; int a0 = 0, a1 = n - 1;
                while (a0 < n && Mathf.Abs(m[a0]) < th) a0++;
                while (a1 > a0 && Mathf.Abs(m[a1]) < th) a1--;
                int pad = sr / 100;
                a0 = Mathf.Max(0, a0 - pad); a1 = Mathf.Min(n - 1, a1 + (loop ? pad : sr / 4));
                // envelope in 10 ms windows, for event detection
                int win = sr / 100, nw = (a1 - a0 + 1) / win; var env = new float[Mathf.Max(1, nw)];
                for (int w = 0; w < nw; w++) { float e = 0; for (int i = 0; i < win; i++) e = Mathf.Max(e, Mathf.Abs(m[a0 + w * win + i])); env[w] = e; }
                int events = 0; { bool on = false; int quiet = 0; for (int w = 0; w < nw; w++) { if (env[w] > 0.25f * peak) { if (!on) events++; on = true; quiet = 0; } else if (env[w] < 0.06f * peak && ++quiet >= 8) on = false; } }
                string note = "";
                if (!loop && Lookup(SingleGap, raw.name, out float gap))
                {
                    // keep the first event with its whole natural decay; cut just before a second event begins
                    int need = Mathf.Max(3, Mathf.RoundToInt(gap * 100)), quiet = 0, started = -1; bool gone = false;
                    for (int w = 0; w < nw; w++)
                    {
                        if (started < 0) { if (env[w] > 0.2f * peak) started = w; continue; }
                        if (!gone) { if (env[w] < 0.06f * peak) { if (++quiet >= need && w - started > 6) gone = true; } else quiet = 0; }
                        else if (env[w] > 0.15f * peak) { a1 = Mathf.Min(a1, a0 + Mathf.Max(started + 8, w - 3) * win); note += " cut-before-2nd-event"; break; }
                    }
                }
                if (!loop && Lookup(MaxLen, raw.name, out float mx) && (a1 - a0 + 1) > mx * sr) { a1 = a0 + Mathf.RoundToInt(mx * sr); note += " capped"; }
                int len = a1 - a0 + 1; if (len < 64) { audit.Add(raw.name + ": nothing left, raw"); return raw; }
                float[] o;
                if (loop && Rhythmic.Contains(raw.name))
                {   // loop from just before the first beat to just before the last, so the rhythm carries across the seam
                    var onsets = new List<int>(); { bool on = false; int quiet = 0; for (int w = 0; w < nw; w++) { if (env[w] > 0.3f * peak) { if (!on) onsets.Add(w); on = true; quiet = 0; } else if (env[w] < 0.08f * peak && ++quiet >= 6) on = false; } }
                    int pre = sr / 60, s0 = a0, s1 = a1;
                    if (onsets.Count >= 3) { s0 = Mathf.Max(a0, a0 + onsets[0] * win - pre); s1 = Mathf.Max(s0 + sr / 2, a0 + onsets[onsets.Count - 1] * win - pre); note += " beat-loop(" + onsets.Count + " beats)"; }
                    int ol = s1 - s0; o = new float[ol]; int f = sr / 250;
                    for (int i = 0; i < ol; i++) { float s = m[s0 + i]; if (i < f) s *= i / (float)f; if (i >= ol - f) s *= (ol - 1 - i) / (float)f; o[i] = s; }
                    float g = 0.8f / peak; for (int i = 0; i < ol; i++) o[i] *= g;
                }
                else if (loop)
                {
                    int x = Mathf.Min(sr / 2, len / 4); int ol = len - x; o = new float[ol];
                    for (int i = 0; i < ol; i++)
                    {
                        float s = m[a0 + i];
                        if (i < x) { float k = i / (float)x; s = s * Mathf.Sin(k * Mathf.PI * 0.5f) + m[a0 + ol + i] * Mathf.Cos(k * Mathf.PI * 0.5f); }
                        o[i] = s;
                    }
                    double sum = 0; for (int i = 0; i < ol; i++) sum += o[i] * o[i];
                    float rms = Mathf.Sqrt((float)(sum / ol)); float g = rms > 1e-5f ? Mathf.Min(0.09f / rms, 0.95f / peak) : 1;
                    for (int i = 0; i < ol; i++) o[i] *= g;
                    note += " crossfade-loop";
                }
                else
                {
                    o = new float[len]; float pk = 0; for (int i = 0; i < len; i++) pk = Mathf.Max(pk, Mathf.Abs(m[a0 + i]));
                    float g = 0.9f / Mathf.Max(pk, 1e-4f); int fi = Mathf.Min(sr / 200, len / 2), fo = Mathf.Min(sr / 25, len / 2);
                    for (int i = 0; i < len; i++) { float s = m[a0 + i] * g; if (i < fi) s *= i / (float)fi; if (i >= len - fo) s *= (len - 1 - i) / (float)fo; o[i] = s; }
                }
                var clip = AudioClip.Create(raw.name, o.Length, 1, sr, false);
                clip.SetData(o, 0);
                audit.Add($"{raw.name}: raw {raw.length:F2}s {ch}ch, {events} event(s) -> {clip.length:F2}s mono{note}");
                return clip;
            }
            catch (System.Exception e) { audit.Add(raw.name + ": EXCEPTION " + e.Message); Debug.LogWarning("[StillHere] could not prepare clip " + raw.name + ": " + e.Message); return raw; }
        }

        AudioClip Clip(string id, out float trim)
        {
            trim = Trim.TryGetValue(id, out var t) ? t : 0.6f;
            if (groups.TryGetValue(id, out var l) && (!sfx.ContainsKey(id) || Random.value < 0.6f)) return sfx[l[Random.Range(0, l.Count)]];
            if (sfx.TryGetValue(id, out var c)) { if (!Trim.ContainsKey(id)) { string g = id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'); if (Trim.TryGetValue(g, out var tg)) trim = tg; } return c; }
            if (warned.Add(id)) Debug.LogWarning("[StillHere] missing sound: " + id);
            return null;
        }

        public string solo;     // test: only this loop is audible
        public bool HasSfx(string id) => sfx.ContainsKey(id) || groups.ContainsKey(id);
        public bool HasVo(string id) => vo.ContainsKey(id);
        public int SfxCount => sfx.Count;
        public int VoCount => vo.Count;

        // ------------------------------------------------------------------ playing

        Emitter Get()
        {
            foreach (var e in pool) if (!e.busy) return e;
            var n = NewEmitter("emitter" + pool.Count); pool.Add(n); return n;
        }
        Emitter NewEmitter(string name)
        {
            var g = new GameObject(name); g.transform.SetParent(transform, false);
            var s = g.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 1; s.dopplerLevel = 0;
            s.rolloffMode = AudioRolloffMode.Custom; s.maxDistance = 30;
            s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0, 1, 1));   // distance handled in Spatialise
            var lp = g.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 22000;
#if STEAMAUDIO_ENABLED && !UNITY_WEBGL
            if (Hrtf)
            {   // true binaural rendering (Steam Audio): ear-to-ear timing, head shadow and outer-ear filtering
                s.spatialize = true; s.spatializePostEffects = true;
                var sa = g.AddComponent<SteamAudio.SteamAudioSource>();
                sa.directBinaural = true; sa.interpolation = SteamAudio.HRTFInterpolation.Bilinear;
                sa.distanceAttenuation = false; sa.airAbsorption = false;      // distance and air are modelled in Spatialise
            }
#endif
            return new Emitter { go = g, src = s, lp = lp };
        }

        /// One-shot at a place in the room.
        public Emitter Play(string id, Vector3 pos, float vol = 1, float pitch = 1, float delay = 0, float refDist = 0.6f, float roll = 1.1f)
        {
            if (silent) return null;
            var c = Clip(id, out float trim); if (!c) return null;
            var e = Get(); e.busy = true; e.loop = false; e.ambient = false; e.baseVol = vol * trim; e.refDist = refDist; e.roll = roll; e.fade = 1;
            e.Pos = pos; e.src.clip = c; e.src.loop = false; e.src.pitch = pitch * Random.Range(0.96f, 1.04f);
            Spatialise(e);
            if (delay > 0) e.src.PlayDelayed(delay); else e.src.Play();
            e.src.SetScheduledEndTime(AudioSettings.dspTime + delay + c.length / Mathf.Max(0.1f, e.src.pitch) + 0.05);
            StartCoroutine(Release(e, delay + c.length / Mathf.Max(0.1f, e.src.pitch) + 0.15f));
            return e;
        }
        System.Collections.IEnumerator Release(Emitter e, float t) { yield return new WaitForSecondsRealtime(t); e.busy = false; }

        /// Continuous source (landmarks, purring). Volume is driven through baseVol / fade.
        public Emitter Loop(string id, Vector3 pos, float vol, float refDist = 0.7f, float roll = 1.1f, bool ambient = true)
        {
            var e = NewEmitter("loop_" + id); e.loop = true; e.ambient = ambient; e.refDist = refDist; e.roll = roll; e.Pos = pos; e.busy = true;
            var c = Clip(id, out float trim); e.baseVol = vol; e.src.loop = true; e.src.clip = c;
            loops.Add(e);
            if (c && !silent) { e.src.time = Random.value * c.length * 0.9f; Spatialise(e); e.src.Play(); }
            return e;
        }

        /// Sounds of your own body: not placed in the room. pan: -1 left foot, +1 right foot.
        public void PlaySelf(string id, float vol = 1, int foot = 0, float delay = 0, float pitch = 1)
        {
            if (silent) return;
            var c = Clip(id, out float trim); if (!c) return;
            var s = foot < 0 ? selfL : foot > 0 ? selfR : self;
            if (delay > 0) StartCoroutine(Delayed(s, c, vol * trim, delay)); else s.PlayOneShot(c, vol * trim);
        }
        System.Collections.IEnumerator Delayed(AudioSource s, AudioClip c, float v, float t) { yield return new WaitForSecondsRealtime(t); s.PlayOneShot(c, v); }

        // ------------------------------------------------------------------ the old man's voice

        string cur; int curPri = -1; float gapUntil; float curStarted;
        readonly List<(string id, int pri)> queue = new List<(string, int)>();
        public bool Speaking => !silent && (cur != null || queue.Count > 0);

        public void Say(string id, int pri = 1)
        {
            lastLine = id; saidLog.Add(id);
            if (silent) return;
            if (!vo.ContainsKey(id)) { if (warned.Add("vo:" + id)) Debug.LogWarning("[StillHere] missing voice line: " + id); return; }
            if (cur != null)
            {
                if (pri > curPri) { queue.Clear(); voice.Stop(); StartLine(id, pri); }
                else if (pri >= 1 && queue.Count < 3) queue.Add((id, pri));
                return;
            }
            if (Time.unscaledTime < gapUntil && queue.Count > 0) { queue.Add((id, pri)); return; }
            StartLine(id, pri);
        }
        void StartLine(string id, int pri)
        {
            cur = id; curPri = pri; curStarted = Time.unscaledTime;
            voice.clip = vo[id]; voice.volume = 1f; voice.Play();
        }
        public void StopVoice() { queue.Clear(); cur = null; curPri = -1; if (voice) voice.Stop(); }

        // ------------------------------------------------------------------ per-frame spatial model

        void Spatialise(Emitter e)
        {
            if (!listener) return;
            Vector3 to = e.Pos - listener.position; float d = to.magnitude;
            Vector3 local = d > 1e-4f ? listener.InverseTransformDirection(to / d) : Vector3.forward;
            float rear = Mathf.Clamp01(-local.z);                                   // 0 in front, 1 directly behind
            float gain = d <= e.refDist ? 1 : e.refDist / (e.refDist + e.roll * (d - e.refDist));
            float air = 1f / (1f + (d / 5f) * (d / 5f) * 1.8f);                     // highs die with distance
            bool hrtf = Hrtf;                                                       // HRTF already colours rear sounds; keep only a gentle extra nudge
            float cut = 21000f * Mathf.Lerp(1f, hrtf ? 0.5f : 0.17f, Mathf.Pow(rear, 1.15f)) * air;
            bool under = e.Pos.y < 0.65f && e.Pos.x > tableRect.x && e.Pos.x < tableRect.z && e.Pos.z > tableRect.y && e.Pos.z < tableRect.w
                         && listener.position.y > 0.9f;
            if (under) { cut *= 0.45f; gain *= 0.8f; }
            e.lp.cutoffFrequency = Mathf.Clamp(cut, 700f, 22000f);
            e.src.volume = Mathf.Clamp01(e.baseVol * e.fade * gain * (1f - (hrtf ? 0.08f : 0.22f) * rear) * (e.ambient ? ambience * duck : 1f));
            if (solo != null && e.loop && (e.src.clip == null || e.src.clip.name != solo)) e.src.volume = 0;
            e.src.spread = Mathf.Lerp(120f, 55f, Mathf.Clamp01((d - 0.25f) / 1.4f));   // never hard-panned: the far ear always hears a little
            e.src.reverbZoneMix = Mathf.Lerp(0.2f, 1.05f, Mathf.Clamp01(d / 4.5f));
        }

        void LateUpdate()
        {
            AudioListener.volume = master;
            if (cur != null && !voice.isPlaying && Time.unscaledTime - curStarted > 0.2f) { cur = null; curPri = -1; gapUntil = Time.unscaledTime + 0.28f; }
            if (cur == null && queue.Count > 0 && Time.unscaledTime >= gapUntil) { var n = queue[0]; queue.RemoveAt(0); StartLine(n.id, n.pri); }
            duck = Mathf.Lerp(duck, cur != null ? 0.55f : 1f, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 5));
            foreach (var e in pool) if (e.busy) Spatialise(e);
            foreach (var e in loops) Spatialise(e);
        }

        public void SetSilent(bool s)
        {
            silent = s;
            if (s) { foreach (var e in pool) e.src.Stop(); foreach (var e in loops) e.src.Stop(); self.Stop(); selfL.Stop(); selfR.Stop(); voice.Stop(); cur = null; queue.Clear(); }
            else foreach (var e in loops) if (e.src.clip && !e.src.isPlaying) e.src.Play();
        }
    }

    /// Measures what actually reaches each ear, so left/right, behind and distance can be checked without listening.
    public class AudioProbe : MonoBehaviour
    {
        double sumL, sumR, sumHi; long n; float prevL;
        readonly object gate = new object();

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels < 2) return;
            double l = 0, r = 0, hi = 0; float p = prevL;
            for (int i = 0; i < data.Length; i += channels)
            {
                float a = data[i], b = data[i + 1];
                l += a * a; r += b * b; float dlt = a - p; hi += dlt * dlt; p = a;
            }
            prevL = p;
            lock (gate) { sumL += l; sumR += r; sumHi += hi; n += data.Length / channels; }
        }

        /// RMS of left, right, and a crude brightness measure (first-difference energy) since the last call.
        public Vector3 Read()
        {
            lock (gate)
            {
                if (n == 0) return Vector3.zero;
                var v = new Vector3(Mathf.Sqrt((float)(sumL / n)), Mathf.Sqrt((float)(sumR / n)), Mathf.Sqrt((float)(sumHi / n)));
                sumL = sumR = sumHi = 0; n = 0; return v;
            }
        }
    }
}
