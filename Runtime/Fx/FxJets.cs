// Jets recipes in the game: a 'Mech whose chassis carries the tag vfx_jets_<recipe> gets one GN particle emitter on a
// bone of its model (the Effects Studio's Jets tab writes the recipe), breathing an idle stream and bursting when the
// 'Mech jumps. With hideStock the game's own jump-jet exhausts are stopped as soon as the game starts them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BattleTech;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CombatVFX.Fx
{
    [Serializable]
    public class JetsRecipe
    {
        public string name = "GN drive";
        public int version = 1;
        public string bone = "j_COCKPIT";
        public Vector3 offset = new Vector3(0f, 1.5f, -3f);
        public Vector3 direction = new Vector3(0f, 0.2f, -1f);
        public string color = "#7cf2c8", alt = "#b4ff78", gold = "#ffde8c", core = "#dcfff0";
        public float goldMix = 0.35f;
        public float density = 1f, size = 1f, speed = 1f, drift = 1f, twinkle = 1f, lifetime = 1f;
        public float idle = 0.4f;
        public float glow = 1f;
        public float spread = 18f;
        public float burst = 2.6f;
        public bool hideStock = true;
        public string tags = "";
        public List<string> chassis = new List<string>();   // chassis def ids this plays on, besides any chassis tagged vfx_jets_<name>
        [NonSerialized] public string File;
    }

    internal static class FxJetsLibrary
    {
        internal const string TagPrefix = "vfx_jets_";
        private static readonly Dictionary<string, JetsRecipe> _recipes = new Dictionary<string, JetsRecipe>(StringComparer.OrdinalIgnoreCase);
        private static FileSystemWatcher _watcher;
        private static volatile bool _dirty = true;
        private static readonly HashSet<string> _loggedChassis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static string Dir => Path.Combine(ModEntry.ModDirectory ?? ".", "jets");
        internal static int Count { get { EnsureLoaded(); return _recipes.Count; } }

        internal static void EnsureLoaded()
        {
            if (!_dirty) return;
            _dirty = false; _recipes.Clear();
            try
            {
                if (!Directory.Exists(Dir)) { Watch(); return; }
                foreach (string file in Directory.GetFiles(Dir, "*.json"))
                {
                    try { var r = JsonUtility.FromJson<JetsRecipe>(System.IO.File.ReadAllText(file)); if (r != null) { if (r.chassis == null) r.chassis = new List<string>(); r.File = file; _recipes[Path.GetFileNameWithoutExtension(file)] = r; } }
                    catch (Exception e) { ModEntry.Log("[Jets] bad recipe " + Path.GetFileName(file) + ": " + e.Message); }
                }
                if (_recipes.Count > 0) ModEntry.Log("[Jets] " + _recipes.Count + " jets recipes: " + string.Join(", ", _recipes.Select(kv => kv.Key + (kv.Value.chassis.Count > 0 ? " [" + string.Join(", ", kv.Value.chassis.ToArray()) + "]" : " [tag only]")).OrderBy(k => k).ToArray()));
                Watch();
            }
            catch (Exception e) { ModEntry.Log("[Jets] " + e.Message); }
        }

        private static void Watch()
        {
            if (_watcher != null) return;
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                _watcher = new FileSystemWatcher(Dir, "*.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                _watcher.Changed += (s, e) => _dirty = true; _watcher.Created += (s, e) => _dirty = true; _watcher.Deleted += (s, e) => _dirty = true; _watcher.Renamed += (s, e) => _dirty = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        /// <summary>The jets recipe a 'Mech's chassis tag names, or null.</summary>
        internal static JetsRecipe ForMech(Mech mech)
        {
            EnsureLoaded();
            if (_recipes.Count == 0) return null;
            string chassisId = mech?.MechDef?.ChassisID ?? mech?.MechDef?.Chassis?.Description?.Id;
            foreach (JetsRecipe r in _recipes.Values) foreach (string c in r.chassis) if (!string.IsNullOrEmpty(c) && string.Equals(c.Trim(), chassisId, StringComparison.OrdinalIgnoreCase)) return r;
            var tags = mech?.MechDef?.Chassis?.ChassisTags;
            if (tags != null)
                foreach (string tag in tags)
                {
                    if (tag == null || !tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    JetsRecipe r;
                    if (_recipes.TryGetValue(tag.Substring(TagPrefix.Length), out r)) return r;
                }
            if (chassisId != null && _loggedChassis.Add(chassisId)) ModEntry.Log("[Jets] no jets recipe for chassis " + chassisId + " (tags: " + (tags != null ? string.Join(", ", tags.ToArray()) : "none") + "); recipes assign by chassis id or by tag vfx_jets_<recipe>");
            return null;
        }
    }

    /// <summary>The GN emitter on one 'Mech: idle stream, jump burst, drive glow.</summary>
    public class FxJetEmitter : MonoBehaviour
    {
        public JetsRecipe Recipe;
        private ParticleSystem _sparks, _stars; private GameObject _glow; private Material _glowMat;
        private float _burstLeft, _acc;
        private static Material _additive;

        public static FxJetEmitter Attach(GameObject mechRoot, JetsRecipe r)
        {
            Transform bone = null;
            foreach (Transform t in mechRoot.GetComponentsInChildren<Transform>(true)) if (t.name == r.bone) { bone = t; break; }
            if (bone == null) bone = mechRoot.transform;
            var go = new GameObject("CombatVFX_jets_" + r.name);
            go.transform.SetParent(bone, false);
            go.transform.localPosition = r.offset;
            go.transform.localRotation = Quaternion.LookRotation(r.direction.sqrMagnitude > 0.001f ? r.direction.normalized : Vector3.back);
            var e = go.AddComponent<FxJetEmitter>(); e.Recipe = r; e.Build();
            return e;
        }

        private static Material Additive() { if (_additive == null) { Shader s = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive") ?? Shader.Find("Sprites/Default"); _additive = new Material(s); } return _additive; }

        private void Build()
        {
            _sparks = MakeSystem("sparks", Recipe.color, Recipe.alt, "soft", 0.55f);
            _stars = MakeSystem("gold stars", Recipe.gold, Recipe.gold, "star", 1f);
            _glow = GameObject.CreatePrimitive(PrimitiveType.Quad); _glow.name = "drive glow"; Destroy(_glow.GetComponent<Collider>());
            _glow.transform.SetParent(transform, false);
            var r = _glow.GetComponent<Renderer>(); _glowMat = new Material(Additive()) { mainTexture = FxPlayer.Tex("soft") }; r.material = _glowMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private ParticleSystem MakeSystem(string name, string colA, string colB, string texture, float sizeMul)
        {
            var g = new GameObject(name); g.transform.SetParent(transform, false);
            var ps = g.AddComponent<ParticleSystem>(); ps.Stop();
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.maxParticles = 4000; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f * Recipe.lifetime, 4.5f * Recipe.lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * Recipe.size * sizeMul, 0.42f * Recipe.size * sizeMul);
            main.startColor = new ParticleSystem.MinMaxGradient(FxRecipe.Col(colA), FxRecipe.Col(colB));
            main.gravityModifier = -0.04f * Recipe.drift;
            var em = ps.emission; em.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient(); grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            AnimationCurve a = new AnimationCurve(), b = new AnimationCurve();
            for (int i = 0; i <= 16; i++) { float u = i / 16f; a.AddKey(u, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(u * 31f)) * Recipe.twinkle + (1f - Recipe.twinkle) * 0.3f); b.AddKey(u, 0.5f + 0.5f * Mathf.Abs(Mathf.Cos(u * 23f + 1f)) * Recipe.twinkle + (1f - Recipe.twinkle) * 0.3f); }
            sol.size = new ParticleSystem.MinMaxCurve(1f, a, b);
            var noise = ps.noise; noise.enabled = Recipe.drift > 0.01f; noise.strength = 0.35f * Recipe.drift; noise.frequency = 0.6f; noise.scrollSpeed = 0.4f; noise.damping = true;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.dampen = 0.35f; lim.limit = 2f;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.material = new Material(Additive()) { mainTexture = FxPlayer.Tex(texture) }; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        public void Jump() { _burstLeft = Recipe.burst; }
        public void Land() { _burstLeft = Mathf.Min(_burstLeft, 0.3f); }

        private void Update()
        {
            if (_sparks == null) return;
            float dt = Time.deltaTime;
            if (_burstLeft > 0f) _burstLeft -= dt;
            float burstK = _burstLeft > 0f ? Mathf.Clamp01(_burstLeft / Mathf.Max(0.3f, Recipe.burst * 0.25f)) * Mathf.Clamp01((Recipe.burst - _burstLeft) * 6f + 0.2f) : 0f;
            float rate = (Recipe.idle * 18f + burstK * 260f) * Recipe.density;
            _acc += rate * dt;
            Vector3 origin = transform.position; Vector3 dir = transform.forward;
            float spread = Recipe.spread * Mathf.Deg2Rad;
            while (_acc >= 1f)
            {
                _acc -= 1f;
                bool gold = Random.value < Recipe.goldMix;
                float a = (Random.value - 0.5f) * 2f * spread, b = Random.value * Mathf.PI * 2f, s = Mathf.Sin(a);
                Vector3 v = Quaternion.FromToRotation(Vector3.forward, dir) * new Vector3(s * Mathf.Cos(b), s * Mathf.Sin(b), Mathf.Cos(a));
                float sp = (burstK > 0f ? 4f + Random.value * 9f * burstK : 0.4f + Random.value * 0.8f) * Recipe.speed;
                var ep = new ParticleSystem.EmitParams { position = origin + Random.insideUnitSphere * 0.25f, velocity = v * sp };
                (gold ? _stars : _sparks).Emit(ep, 1);
            }
            if (_glow != null)
            {
                Camera cam = Camera.main;
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 2.2f);
                float k = (Recipe.idle > 0f ? 0.5f : 0f) + burstK * 0.9f;
                float sz = (1.6f + burstK * 1.4f) * Recipe.glow * pulse;
                _glow.transform.localScale = new Vector3(sz, sz, 1f);
                if (cam != null) _glow.transform.rotation = Quaternion.LookRotation(_glow.transform.position - cam.transform.position);
                Color c = FxRecipe.Col(Recipe.core); c.a = Mathf.Clamp01(k * Recipe.glow);
                _glowMat.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * 0.5f));
                _glow.SetActive(c.a > 0.01f);
            }
        }
    }

    /// <summary>When a 'Mech's model is set up, give it its GN emitter if its chassis asks for one.</summary>
    [HarmonyPatch(typeof(MechRepresentation), "Init", new[] { typeof(Mech), typeof(Transform), typeof(bool) })]
    internal static class MechRepresentation_Init_Jets
    {
        private static void Postfix(MechRepresentation __instance, Mech mech)
        {
            try
            {
                JetsRecipe r = FxJetsLibrary.ForMech(mech);
                if (r == null || __instance.GetComponentInChildren<FxJetEmitter>(true) != null) return;
                FxJetEmitter.Attach(__instance.gameObject, r);
                ModEntry.Log("[Jets] '" + r.name + "' on " + (mech.DisplayName ?? mech.MechDef?.ChassisID) + " at " + r.bone + " " + r.offset);
            }
            catch (Exception e) { ModEntry.Log("[Jets] init: " + e.Message); }
        }
    }

    /// <summary>The game starts its jet exhausts: stop them when the recipe says so and burst ours.</summary>
    [HarmonyPatch(typeof(JumpjetRepresentation), "ActivateJets")]
    internal static class JumpjetRepresentation_Activate_Jets
    {
        private static readonly FieldInfo Parent = AccessTools.Field(typeof(GameRepresentation), "parentCombatant");

        private static void Postfix(JumpjetRepresentation __instance)
        {
            try
            {
                var mech = Parent?.GetValue(__instance) as Mech;
                if (mech == null || mech.GameRep == null) return;
                var emitter = mech.GameRep.GetComponentInChildren<FxJetEmitter>(true);
                if (emitter == null) return;
                if (emitter.Recipe.hideStock)
                {
                    foreach (ParticleSystem ps in __instance.jumpjetParticles) if (ps != null) { ps.Stop(true); ps.Clear(true); }
                    var light = __instance.GetComponentInChildren<BattleTech.Rendering.BTLight>(true); if (light != null) light.enabled = false;
                }
                emitter.Jump();
            }
            catch (Exception e) { ModEntry.Log("[Jets] activate: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(JumpjetRepresentation), "DeactivateJets")]
    internal static class JumpjetRepresentation_Deactivate_Jets
    {
        private static readonly FieldInfo Parent = AccessTools.Field(typeof(GameRepresentation), "parentCombatant");

        private static void Postfix(JumpjetRepresentation __instance)
        {
            try
            {
                var mech = Parent?.GetValue(__instance) as Mech;
                var emitter = mech?.GameRep?.GetComponentInChildren<FxJetEmitter>(true);
                if (emitter != null) emitter.Land();
            }
            catch { }
        }
    }
}
