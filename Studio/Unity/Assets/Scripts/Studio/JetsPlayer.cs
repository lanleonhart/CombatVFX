// Jets: replaces a 'Mech's jump-jet exhaust with GN particles from one emitter placed on the model. The emitter hangs
// off a bone of the loaded prefab at an offset, breathes a faint idle stream, and bursts when the 'Mech jumps. The
// stock jet components (chrPrfComp_<base>_<location>_jumpjet, attached by the game to the cockpit and calf bones) can
// be shown for reference or removed. Saved as a jets recipe the CombatVFX mod plays on chassis tagged vfx_jets_<name>.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GNFX
{
    [Serializable]
    public class JetsRecipe
    {
        public string name = "GN drive";
        public int version = 1;
        public string bone = "j_COCKPIT";                 // the bone the emitter hangs off (the game's torso attach on most rigs)
        public Vector3 offset = new Vector3(0f, 1.5f, -3f); // metres from the bone, in the bone's space
        public Vector3 direction = new Vector3(0f, 0.2f, -1f); // emission direction in the bone's space (back and slightly up)
        public string color = "#7cf2c8", alt = "#b4ff78", gold = "#ffde8c", core = "#dcfff0";
        public float goldMix = 0.35f;
        public float density = 1f, size = 1f, speed = 1f, drift = 1f, twinkle = 1f, lifetime = 1f;
        public float idle = 0.4f;                          // idle stream strength, 0 = off
        public float glow = 1f;                            // drive glow sprite
        public float spread = 18f;                         // cone half-angle in degrees
        public float burst = 2.6f;                         // seconds the jump burst lasts
        public bool hideStock = true;                      // remove the game's own jet exhausts
        public string tags = "";
        public List<string> chassis = new List<string>();  // chassis def ids this plays on (besides any chassis tagged vfx_jets_<name>)

        public static JetsRecipe Parse(string json) { var r = JsonUtility.FromJson<JetsRecipe>(json); if (r == null) throw new Exception("not a jets recipe"); if (r.chassis == null) r.chassis = new List<string>(); return r; }
        public string ToJson() => JsonUtility.ToJson(this, true);
    }

    public class JetsPlayer : MonoBehaviour
    {
        public GameAssets Assets;
        public Material Additive, Blended;
        public JetsRecipe Recipe = new JetsRecipe();
        public GameObject Mech;
        public bool ShowStock;
        public bool Jumping => _burstLeft > 0f;
        public Transform Emitter => _root != null ? _root.transform : null;
        public List<string> StockFound = new List<string>();

        private GameObject _root, _glow;
        private ParticleSystem _sparks, _stars;
        private readonly List<GameObject> _stock = new List<GameObject>();
        private readonly List<ParticleSystem> _stockPs = new List<ParticleSystem>();
        private float _burstLeft, _emitAcc;
        private Transform _bone;

        /// <summary>All bones of the loaded 'Mech the emitter can hang off: the game's attach bones first, then the rest.</summary>
        public static List<string> Bones(GameObject mech)
        {
            var all = mech != null ? mech.GetComponentsInChildren<Transform>(true).Select(t => t.name).Where(n => n.StartsWith("j_")).Distinct().ToList() : new List<string>();
            var first = new[] { "j_COCKPIT", "j_Torso", "j_Head", "j_LCalf", "j_RCalf", "j_LForearm", "j_RForearm", "j_Pelvis", "j_Spine" };
            return first.Where(all.Contains).Concat(all.Where(n => !first.Contains(n)).OrderBy(n => n)).ToList();
        }

        public static string PrefabBase(string mechId)
        {
            Match m = Regex.Match(mechId ?? "", @"chrprfmech_(.+?)base-\d+", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Attach to a 'Mech (the stage's shooter), find the stock jets, build the GN emitter.</summary>
        public void Attach(GameObject mech, string mechId)
        {
            Clear();
            Mech = mech;
            if (mech == null) return;
            StockFound.Clear();
            string pb = PrefabBase(mechId);
            if (pb != null && Assets != null && Assets.Ready)
            {
                foreach (var slot in new[] { new[] { "centertorso", "j_COCKPIT" }, new[] { "leftleg", "j_LCalf" }, new[] { "rightleg", "j_RCalf" } })
                {
                    string id = "chrPrfComp_" + pb + "_" + slot[0] + "_jumpjet";
                    GameObject prefab = Assets.Prefab(id);
                    if (prefab == null) continue;
                    Transform at = Find(mech, slot[1]) ?? mech.transform;
                    GameObject g = Instantiate(prefab); g.name = id; g.transform.SetParent(at, false); g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity;
                    ShaderFix.Apply(g);
                    var systems = g.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (ParticleSystem ps in systems) { var main = ps.main; main.playOnAwake = false; ps.Stop(true); ps.Clear(true); }
                    _stock.Add(g); _stockPs.AddRange(systems);
                    int emitters = g.transform.Cast<Transform>().Count();
                    StockFound.Add(slot[0] + " · " + emitters + " emitter" + (emitters == 1 ? "" : "s") + " on " + at.name);
                }
            }
            Rebuild();
        }

        private static Transform Find(GameObject mech, string bone)
        {
            foreach (Transform t in mech.GetComponentsInChildren<Transform>(true)) if (t.name == bone) return t;
            return null;
        }

        /// <summary>Build (or rebuild after a recipe change) the emitter on its bone.</summary>
        public void Rebuild()
        {
            if (_root != null) Destroy(_root); _root = null; _glow = null; _sparks = _stars = null;
            if (Mech == null) return;
            _bone = Find(Mech, Recipe.bone) ?? Mech.transform;
            _root = new GameObject("GN emitter"); _root.transform.SetParent(_bone, false);
            _root.transform.localPosition = Recipe.offset;
            _root.transform.localRotation = Quaternion.LookRotation(Recipe.direction.sqrMagnitude > 0.001f ? Recipe.direction.normalized : Vector3.back);
            _sparks = MakeSystem("sparks", Recipe.color, Recipe.alt, "soft", 0.55f);
            _stars = MakeSystem("gold stars", Recipe.gold, Recipe.gold, "star", 1f);
            _glow = GameObject.CreatePrimitive(PrimitiveType.Quad); _glow.name = "drive glow"; Destroy(_glow.GetComponent<Collider>());
            _glow.transform.SetParent(_root.transform, false);
            var r = _glow.GetComponent<Renderer>(); r.material = new Material(Additive) { mainTexture = RecipePlayer.Tex("soft") }; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (GameObject g in _stock) g.SetActive(ShowStock && !Recipe.hideStock);
        }

        private ParticleSystem MakeSystem(string name, string colA, string colB, string texture, float sizeMul)
        {
            var g = new GameObject(name); g.transform.SetParent(_root.transform, false);
            var ps = g.AddComponent<ParticleSystem>(); ps.Stop();
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.maxParticles = 4000; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f * Recipe.lifetime, 4.5f * Recipe.lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * Recipe.size * sizeMul, 0.42f * Recipe.size * sizeMul);
            main.startColor = new ParticleSystem.MinMaxGradient(GNFX.Recipe.Col(colA), GNFX.Recipe.Col(colB));
            main.gravityModifier = -0.04f * Recipe.drift;
            var em = ps.emission; em.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient(); grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            // twinkle: size wobbles along the particle's life, a different random curve per particle
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            AnimationCurve a = new AnimationCurve(), b = new AnimationCurve();
            for (int i = 0; i <= 16; i++) { float u = i / 16f; a.AddKey(u, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(u * 31f)) * Recipe.twinkle + (1f - Recipe.twinkle) * 0.3f); b.AddKey(u, 0.5f + 0.5f * Mathf.Abs(Mathf.Cos(u * 23f + 1f)) * Recipe.twinkle + (1f - Recipe.twinkle) * 0.3f); }
            sol.size = new ParticleSystem.MinMaxCurve(1f, a, b);
            var noise = ps.noise; noise.enabled = Recipe.drift > 0.01f; noise.strength = 0.35f * Recipe.drift; noise.frequency = 0.6f; noise.scrollSpeed = 0.4f; noise.damping = true;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.dampen = 0.35f; lim.limit = 2f;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.material = new Material(Additive) { mainTexture = RecipePlayer.Tex(texture) }; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        public void Jump() { _burstLeft = Recipe.burst; foreach (ParticleSystem ps in _stockPs) if (ps != null && ps.gameObject.activeInHierarchy) { ps.Clear(true); ps.Play(true); } }

        private void Update()
        {
            if (_root == null || _sparks == null) return;
            float dt = Time.deltaTime;
            if (_burstLeft > 0f) { _burstLeft -= dt; if (_burstLeft <= 0f) foreach (ParticleSystem ps in _stockPs) if (ps != null) ps.Stop(true); }
            float burstK = _burstLeft > 0f ? Mathf.Clamp01(_burstLeft / Mathf.Max(0.3f, Recipe.burst * 0.25f)) * Mathf.Clamp01((Recipe.burst - _burstLeft) * 6f + 0.2f) : 0f;
            float rate = (Recipe.idle * 18f + burstK * 260f) * Recipe.density;
            _emitAcc += rate * dt;
            Vector3 origin = _root.transform.position; Vector3 dir = _root.transform.forward;
            float spread = Recipe.spread * Mathf.Deg2Rad;
            while (_emitAcc >= 1f)
            {
                _emitAcc -= 1f;
                bool gold = UnityEngine.Random.value < Recipe.goldMix;
                float a = (UnityEngine.Random.value - 0.5f) * 2f * spread, b = UnityEngine.Random.value * Mathf.PI * 2f, s = Mathf.Sin(a);
                Vector3 v = Quaternion.FromToRotation(Vector3.forward, dir) * new Vector3(s * Mathf.Cos(b), s * Mathf.Sin(b), Mathf.Cos(a));
                float sp = (burstK > 0f ? 4f + UnityEngine.Random.value * 9f * burstK : 0.4f + UnityEngine.Random.value * 0.8f) * Recipe.speed;
                var ep = new ParticleSystem.EmitParams { position = origin + UnityEngine.Random.insideUnitSphere * 0.25f, velocity = v * sp };
                (gold ? _stars : _sparks).Emit(ep, 1);
            }
            // the drive glow: a soft sprite at the emitter, pulsing, brighter in the burst
            if (_glow != null)
            {
                Camera cam = Camera.main;
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 2.2f);
                float k = (Recipe.idle > 0f ? 0.5f : 0f) + burstK * 0.9f;
                float sz = (1.6f + burstK * 1.4f) * Recipe.glow * pulse;
                _glow.transform.localScale = new Vector3(sz, sz, 1f);
                if (cam != null) _glow.transform.rotation = Quaternion.LookRotation(_glow.transform.position - cam.transform.position);
                Color c = GNFX.Recipe.Col(Recipe.core); c.a = Mathf.Clamp01(k * Recipe.glow);
                var m = _glow.GetComponent<Renderer>().material; m.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * 0.5f));
                _glow.SetActive(c.a > 0.01f);
            }
        }

        public void Clear()
        {
            if (_root != null) Destroy(_root); _root = null;
            foreach (GameObject g in _stock) if (g != null) Destroy(g);
            _stock.Clear(); _stockPs.Clear(); StockFound.Clear(); _burstLeft = 0f;
        }
    }
}
