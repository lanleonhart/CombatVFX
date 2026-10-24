// Plays a recipe's drawn layers: the parts the CombatVFX mod will add on top of (or instead of) a stock effect.
// Everything is built from LineRenderers, particle systems with code-drawn textures, primitive meshes, lights and the
// game's own VFX prefabs, so each layer maps onto something the CombatVFX mod can do in a mission with the same numbers.
using System.Collections;
using System.Collections.Generic;
using BattleTech.Rendering;
using UnityEngine;

namespace GNFX
{
    public class RecipePlayer : MonoBehaviour
    {
        public Material Additive, Unlit, Blended, Lit;
        public GameAssets Assets;
        public StockEffectPlayer Stock;      // resolves the parts of the game's own effects
        public Sounds Audio;
        public bool Playing;
        public Vector3? TrackPos;          // the first bolt in flight, for the follow camera
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        private static Mesh _cube, _cylinder;

        public void Clear()
        {
            StopAllCoroutines(); Playing = false; TrackPos = null;
            foreach (GameObject g in _spawned) if (g != null) Destroy(g);
            _spawned.Clear();
        }

        public void Play(Recipe r, Vector3 muzzle, Vector3 target, float pre = 0f)
        {
            Clear();
            StartCoroutine(Run(r, muzzle, target, pre));
        }

        private GameObject Make(string name) { var g = new GameObject(name); _spawned.Add(g); return g; }

        // ------------------------------------------------------------------ textures drawn in code

        public static Texture2D Tex(string kind)
        {
            if (_tex.TryGetValue(kind, out Texture2D t)) return t;
            const int S = 64; t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f, dy = (y + 0.5f) / S - 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy) * 2f, a = 0f;
                switch (kind)
                {
                    case "star":
                        float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy), diag = Mathf.Abs(ax - ay);
                        a = Mathf.Max(Mathf.Clamp01(1f - ay * 40f) * (1f - ax * 2f), Mathf.Clamp01(1f - ax * 40f) * (1f - ay * 2f), Mathf.Clamp01(1f - diag * 25f) * (1f - d) * 0.7f, Mathf.Clamp01(1f - d * 3f));
                        break;
                    case "spark": a = Mathf.Clamp01(1f - Mathf.Abs(dy) * 16f) * Mathf.Clamp01(1f - Mathf.Abs(dx) * 2f); break;
                    case "ring": a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.75f) * 8f); break;
                    case "smoke":
                        float n = Mathf.PerlinNoise(x * 0.11f + 3.7f, y * 0.11f + 1.3f) * 0.6f + Mathf.PerlinNoise(x * 0.31f, y * 0.31f) * 0.4f;
                        a = Mathf.Clamp01(1f - d) * Mathf.Clamp01(n * 1.6f - 0.2f); break;
                    case "disc": a = d < 0.9f ? 1f : Mathf.Clamp01((1f - d) * 10f); break;
                    default: a = d < 0.35f ? 1f : Mathf.Clamp01(1f - (d - 0.35f) / 0.65f); a *= a; break;
                }
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px); t.Apply();
            _tex[kind] = t;
            return t;
        }

        private static Mesh Cube { get { if (_cube == null) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); _cube = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g); } return _cube; } }
        private static Mesh Cylinder { get { if (_cylinder == null) { var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); _cylinder = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g); } return _cylinder; } }

        /// <summary>The particle shaders read _TintColor (×2) and ignore Material.color; set both so every material kind takes the colour.</summary>
        private static void Tint(Material m, Color c)
        {
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * 0.5f));
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        // ------------------------------------------------------------------ parts

        private LineRenderer Line(string name, Color c, float width, Transform parent = null)
        {
            GameObject g = Make(name); if (parent != null) g.transform.SetParent(parent, false);
            var lr = g.AddComponent<LineRenderer>();
            lr.material = new Material(Additive); lr.startColor = c; lr.endColor = c;
            lr.useWorldSpace = true; lr.positionCount = 2; lr.widthMultiplier = width; lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.alignment = LineAlignment.View; lr.textureMode = LineTextureMode.Stretch;
            return lr;
        }

        private static void Alpha(LineRenderer lr, float a) { Color c = lr.startColor; c.a = a; lr.startColor = c; lr.endColor = c; }

        /// <summary>A camera-facing or fixed quad with one of the code textures.</summary>
        private GameObject Sprite(string name, string texture, Color c, bool additive)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = name; _spawned.Add(g); Destroy(g.GetComponent<Collider>());
            var r = g.GetComponent<Renderer>(); r.material = new Material(additive ? Additive : Blended) { mainTexture = Tex(texture) }; Tint(r.material, c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return g;
        }

        private static void SpriteAlpha(GameObject g, Color c, float a) { var r = g.GetComponent<Renderer>(); c.a = a; Tint(r.material, c); }

        /// <summary>An empty particle system the layer emits into by hand.</summary>
        private ParticleSystem MakePs(Layer l, Vector3 origin, Material mat, string texture, ParticleSystemRenderMode mode, float gravity, bool fadeOut = true, bool shrink = true)
        {
            GameObject g = Make("particles " + l.kind);
            g.transform.position = origin;
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = Mathf.Max(64, l.count * 4); main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = l.life; main.startSpeed = 0f; main.startSize = l.size; main.startColor = Recipe.Col(l.color);
            main.gravityModifier = gravity / 9.81f;
            var em = ps.emission; em.enabled = false;
            if (fadeOut)
            {
                var col = ps.colorOverLifetime; col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
                col.color = grad;
            }
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, shrink ? AnimationCurve.Linear(0f, 1f, 1f, 0.4f) : AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.material = new Material(mat); if (texture != null) r.material.mainTexture = Tex(texture);
            r.renderMode = mode; r.velocityScale = 0.08f; r.lengthScale = 1.2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            if (mode == ParticleSystemRenderMode.Mesh) { r.mesh = l.kind == "casing" ? Cylinder : Cube; r.alignment = ParticleSystemRenderSpace.World; r.material.color = Recipe.Col(l.color); }
            ps.Play();
            return ps;
        }

        private static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color? color = null, bool spin = false)
        {
            var ep = new ParticleSystem.EmitParams { position = pos, velocity = vel, startSize = size, startLifetime = life };
            if (color.HasValue) ep.startColor = color.Value;
            if (spin) { ep.rotation3D = Random.insideUnitSphere * 180f; ep.angularVelocity3D = Random.insideUnitSphere * 540f; }
            ps.Emit(ep, 1);
        }

        /// <summary>A random direction inside a cone of half-angle spreadDeg about dir, or on the sphere (rising if asked).</summary>
        private static Vector3 Dir(Vector3? dir, float spreadDeg, bool rising)
        {
            if (dir.HasValue)
            {
                float a = (Random.value - 0.5f) * 2f * spreadDeg * Mathf.Deg2Rad, b = Random.value * Mathf.PI * 2f, s = Mathf.Sin(a);
                return Quaternion.FromToRotation(Vector3.forward, dir.Value) * new Vector3(s * Mathf.Cos(b), s * Mathf.Sin(b), Mathf.Cos(a));
            }
            Vector3 v = Random.onUnitSphere; if (rising) v.y = Mathf.Abs(v.y) * 0.8f + 0.2f;
            return v;
        }

        private ParticleSystem Particles(Layer l, Vector3 origin, Vector3? dir, bool rising)
        {
            var ps = MakePs(l, origin, Additive, l.texture, l.texture == "spark" ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard, l.kind == "burst" ? l.gravity : 2f);
            for (int i = 0; i < l.count; i++) Emit(ps, origin, Dir(dir, l.spread, rising) * l.speed * (0.4f + Random.value * 0.8f), l.size * (0.7f + Random.value * 0.6f), l.life * (0.7f + Random.value * 0.6f));
            return ps;
        }

        private GameObject BoltMesh(Layer l)
        {
            GameObject g = Make("bolt");
            GameObject m;
            Color c = Recipe.Col(l.color);
            switch (l.shape)
            {
                case "blade":
                    m = GameObject.CreatePrimitive(PrimitiveType.Cube); m.transform.localScale = new Vector3(l.width, l.width * 0.35f, l.length);
                    var h = GameObject.CreatePrimitive(PrimitiveType.Cube); h.transform.SetParent(g.transform, false); h.transform.localScale = new Vector3(l.width * 0.6f, l.width * 0.6f, l.length * 0.15f); h.transform.localPosition = new Vector3(0f, 0f, -l.length * 0.55f);
                    h.GetComponent<Renderer>().material = new Material(Unlit) { color = new Color(0.85f, 0.85f, 0.85f) }; Destroy(h.GetComponent<Collider>());
                    break;
                case "ball": m = GameObject.CreatePrimitive(PrimitiveType.Sphere); m.transform.localScale = Vector3.one * l.width; c = Color.white; break;
                case "ring": m = GameObject.CreatePrimitive(PrimitiveType.Sphere); m.transform.localScale = new Vector3(l.width, l.width, l.width * 0.15f); break;
                default: m = GameObject.CreatePrimitive(PrimitiveType.Capsule); m.transform.localScale = new Vector3(l.width, l.length * 0.5f, l.width); m.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); c = Color.white; break;
            }
            m.transform.SetParent(g.transform, false);
            Destroy(m.GetComponent<Collider>());
            m.GetComponent<Renderer>().material = new Material(Unlit) { color = c };
            return g;
        }

        private static float FadeAt(string kind, float u)
        {
            switch (kind)
            {
                case "linear": return 1f - u;
                case "hold": return u < 0.7f ? 1f : (1f - u) / 0.3f;
                case "pulse": return Mathf.Max(0f, Mathf.Sin(u * Mathf.PI));
                case "flicker": return (0.65f + 0.35f * Mathf.Sin(u * 80f)) * (1f - u * u);
                case "in": return u * u;
                default: return 1f - u * u;
            }
        }

        // ------------------------------------------------------------------ playback

        private sealed class Live
        {
            public Recipe.Item It; public bool Built;
            public LineRenderer[] Lines; public GameObject[] Bolts; public Offs[] Offs; public ParticleSystem Ps; public GameObject Ring; public Light Light;
            public GameObject Obj, Halo; public Vector3[] Pts; public int Roll = -1; public float Emitted; public Color Color;
        }
        public struct Offs { public float Side, Up, EndSide, EndUp; }

        private Vector3 _m, _ip, _dir, _side; private float _range;
        private Recipe _recipe;
        private Vector3 _shakeOffset;      // the stage camera re-places itself every frame, so the shake is added after it

        private Vector3 PathPoint(Layer l, int i, float d, Offs[] offs)
        {
            float s = Mathf.Clamp01(d / _range), bump = 4f * s * (1f - s);
            Vector3 p = Vector3.Lerp(_m, _ip, s);
            if (l.arc > 0f) p.y += l.arc * bump;
            if (offs != null && i < offs.Length) { Offs o = offs[i]; p += _side * (o.Side * bump + o.EndSide * s); p.y += o.Up * bump + o.EndUp * s; }
            return p;
        }

        /// <summary>Where an anchored layer sits: the muzzle, the impact point, the ground under it, or the first bolt in flight.</summary>
        private Vector3 AnchorPos(Layer l, float tl)
        {
            switch (l.anchor)
            {
                case "muzzle": return _m;
                case "ground": return new Vector3(_ip.x, 0.15f, _ip.z);
                case "bolt": foreach (Recipe.Item it in CurrentItems) if (it.L.kind == "bolt") return PathPoint(it.L, 0, Mathf.Min(_range, tl * it.L.speed), null); return _ip;
                default: return _ip;
            }
        }

        /// <summary>Turn a quad to face the camera, lie flat on the ground, or face the beam.</summary>
        private void Face(GameObject g, Layer l, Camera cam)
        {
            switch (l.facing)
            {
                case "flat": g.transform.rotation = Quaternion.Euler(90f, 0f, 0f); break;
                case "beam": g.transform.rotation = Quaternion.LookRotation(l.anchor == "muzzle" ? -_dir : _dir); break;
                default: if (cam != null) g.transform.rotation = Quaternion.LookRotation(g.transform.position - cam.transform.position); break;
            }
        }

        private IEnumerator Run(Recipe r, Vector3 muzzle, Vector3 target, float pre)
        {
            Playing = true;
            _recipe = r;
            _m = muzzle; _ip = target; _dir = (target - muzzle).normalized; _side = Vector3.Cross(_dir, Vector3.up).normalized; _range = Vector3.Distance(muzzle, target);
            float impact, end;
            List<Recipe.Item> items = r.Timeline(_range, pre, out impact, out end);
            CurrentItems = items;
            var live = new List<Live>();
            foreach (Recipe.Item it in items) live.Add(new Live { It = it });
            float t = 0f;
            Camera cam = Camera.main;
            bool fireSounded = Audio == null || string.IsNullOrEmpty(r.sound);
            bool impactSounded = string.IsNullOrEmpty(r.impactSound);
            while (t <= end + 1f)
            {
                _shakeOffset = Vector3.zero;
                if (!fireSounded && t >= pre) { fireSounded = true; Audio.Play(r.sound, _m); }
                if (!impactSounded && t >= impact) { impactSounded = true; Audio.Play(r.impactSound, _ip); }
                foreach (Live o in live)
                {
                    if (t < o.It.Start) continue;
                    float tl = t - o.It.Start, u = Mathf.Clamp01(tl / Mathf.Max(0.001f, o.It.End - o.It.Start));
                    if (!o.Built) Build(o, impact, cam);
                    Step(o, tl, u, impact, cam);
                }
                if (cam != null && _shakeOffset != Vector3.zero) cam.transform.position += _shakeOffset;
                t += Time.deltaTime;
                yield return null;
            }
            Playing = false; TrackPos = null;
        }

        private void Build(Live o, float impact, Camera cam)
        {
            Layer l = o.It.L; o.Built = true; o.Color = Recipe.Col(l.color);
            switch (l.kind)
            {
                case "beam":
                {
                    int segs = l.jitter > 0f ? 14 : 1;
                    o.Lines = new LineRenderer[3];
                    float[] w = { l.width * 0.35f, l.width, l.width * (1f + l.glow) };
                    Color[] c = { Recipe.Col(l.core, 0.95f), Recipe.Col(l.color, 0.75f), Recipe.Col(l.color, 0.22f) };
                    for (int i = 0; i < 3; i++) { o.Lines[i] = Line("beam", c[i], w[i]); o.Lines[i].positionCount = segs + 1; }
                    break;
                }
                case "bolt":
                {
                    o.Bolts = new GameObject[l.count]; o.Offs = new Offs[l.count]; o.Lines = new LineRenderer[l.count * 2];
                    for (int i = 0; i < l.count; i++)
                    {
                        o.Bolts[i] = BoltMesh(l); o.Bolts[i].SetActive(false);
                        o.Lines[i * 2] = Line("glow", Recipe.Col(l.color, 0.55f), l.width * (1f + l.glow * 0.5f)); o.Lines[i * 2 + 1] = Line("halo", Recipe.Col(l.color, 0.22f), l.width * (1f + l.glow * 1.5f));
                        o.Lines[i * 2].enabled = o.Lines[i * 2 + 1].enabled = false;
                        float R() => (Random.value * 2f - 1f) * l.scatter;
                        o.Offs[i] = new Offs { Side = R(), Up = R() * 0.5f, EndSide = R() * 0.3f, EndUp = Mathf.Abs(R()) * 0.25f };
                    }
                    break;
                }
                case "trail":
                    o.Lines = new[] { Line("trail", Recipe.Col(l.color, l.opacity), l.width), Line("trail glow", Recipe.Col(l.color, l.opacity * 0.4f), l.width * 3f) };
                    foreach (LineRenderer lr in o.Lines) lr.positionCount = 5;
                    break;
                case "flash": o.Ps = Particles(l, _m, _dir, false); break;
                case "burst":
                    o.Ps = Particles(l, _ip, null, true);
                    if (l.ring > 0f) { o.Ring = Sprite("shock ring", "ring", Recipe.Col(l.color, 0.8f), true); o.Ring.transform.position = _ip; }
                    break;
                case "light":
                {
                    GameObject g = Make("light"); o.Light = g.AddComponent<Light>();
                    o.Light.type = LightType.Point; o.Light.color = o.Color; o.Light.range = l.radius; o.Light.intensity = 0f; o.Light.shadows = LightShadows.None;
                    break;
                }
                // ---- new kinds
                case "ring":
                    o.Obj = Sprite("ring", "ring", Recipe.Col(l.color, 0.9f), true); o.Obj.transform.position = AnchorPos(l, 0f); Face(o.Obj, l, cam);
                    break;
                case "orb":
                {
                    o.Obj = GameObject.CreatePrimitive(PrimitiveType.Sphere); o.Obj.name = "orb"; _spawned.Add(o.Obj); Destroy(o.Obj.GetComponent<Collider>());
                    o.Obj.GetComponent<Renderer>().material = new Material(Unlit) { color = Recipe.Col(l.core) };
                    o.Halo = Sprite("orb halo", "soft", Recipe.Col(l.color, 0.7f), true);
                    break;
                }
                case "glow": o.Obj = Sprite("glow", l.texture, Recipe.Col(l.color, 0.9f), true); break;
                case "scorch": o.Obj = Sprite("scorch", l.texture == "soft" ? "soft" : "disc", Recipe.Col(l.color, l.opacity), false); o.Obj.transform.position = AnchorPos(l, 0f) - _dir * 0.3f; if (l.anchor == "ground") o.Obj.transform.position = AnchorPos(l, 0f) + Vector3.up * 0.05f; Face(o.Obj, l, cam); break;
                case "arc":
                {
                    // the main bolt plus `count` branches, each a jagged line re-rolled `rate` times a second
                    int n = 1 + Mathf.Max(0, l.count);
                    o.Lines = new LineRenderer[n * 2]; o.Pts = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        float w = i == 0 ? l.width : l.width * 0.5f;
                        o.Lines[i * 2] = Line("arc core", Recipe.Col(l.core, 0.95f), w * 0.4f); o.Lines[i * 2 + 1] = Line("arc glow", Recipe.Col(l.color, 0.5f), w * (1f + l.glow));
                        o.Lines[i * 2].positionCount = o.Lines[i * 2 + 1].positionCount = i == 0 ? 13 : 6;
                    }
                    break;
                }
                case "smoke": o.Ps = MakePs(l, AnchorPos(l, 0f), Blended, "smoke", ParticleSystemRenderMode.Billboard, -0.6f, true, false); break;
                case "embers": o.Ps = MakePs(l, AnchorPos(l, 0f), Additive, "soft", ParticleSystemRenderMode.Billboard, l.gravity); break;
                case "debris": o.Ps = MakePs(l, AnchorPos(l, 0f), Lit ?? Unlit, null, ParticleSystemRenderMode.Mesh, l.gravity, false, false); break;
                case "casing": o.Ps = MakePs(l, _m, Lit ?? Unlit, null, ParticleSystemRenderMode.Mesh, l.gravity, false, false); break;
                case "charge": o.Ps = MakePs(l, _m, Additive, l.texture, ParticleSystemRenderMode.Billboard, 0f, false, true); break;
                case "pulse": o.Ps = MakePs(l, _m, Additive, l.texture, ParticleSystemRenderMode.Billboard, 0f, false, false); break;
                case "spray": o.Ps = MakePs(l, _m, Additive, l.texture, ParticleSystemRenderMode.Billboard, l.gravity, true, false); break;
                case "helix":
                {
                    int strands = Mathf.Max(1, l.count);
                    o.Lines = new LineRenderer[strands];
                    for (int i = 0; i < strands; i++) { o.Lines[i] = Line("helix", Recipe.Col(l.color, 0.8f), l.width); o.Lines[i].positionCount = 48; }
                    break;
                }
                case "vfx":
                case "part":
                {
                    GameObject prefab = l.kind == "part" ? (Stock != null ? Stock.PartPrefab(l.effect, l.part) : null) : Assets != null && Assets.Ready && !string.IsNullOrEmpty(l.prefab) ? Assets.Prefab(l.prefab) : null;
                    if (prefab == null) break;
                    o.Obj = Instantiate(prefab); o.Obj.name = "vfx " + l.prefab; _spawned.Add(o.Obj);
                    ShaderFix.Apply(o.Obj);
                    o.Obj.transform.position = AnchorPos(l, 0f);
                    o.Obj.transform.rotation = l.facing == "beam" ? Quaternion.LookRotation(l.anchor == "muzzle" ? _dir : -_dir) : l.facing == "flat" ? Quaternion.identity : Quaternion.LookRotation(_dir);
                    o.Obj.transform.localScale = Vector3.one * Mathf.Max(0.05f, l.size);
                    o.Obj.SetActive(true);
                    if (l.tintMode == "recipe" && _recipe != null && _recipe.MatchColor.HasValue) Recolor.Apply(o.Obj, _recipe.MatchColor.Value);
                    else if (l.tintMode == "custom") Recolor.Apply(o.Obj, Recipe.Col(l.color));
                    foreach (ParticleSystem ps in o.Obj.GetComponentsInChildren<ParticleSystem>(true)) { ps.Stop(true); ps.Clear(true); ps.Play(true); }
                    foreach (BTLightAnimator la in o.Obj.GetComponentsInChildren<BTLightAnimator>(true)) { la.StopAnimation(); la.PlayAnimation(); }
                    break;
                }
                case "shake": break;
            }
        }

        private void Step(Live o, float tl, float u, float impact, Camera cam)
        {
            Layer l = o.It.L;
            switch (l.kind)
            {
                case "beam":
                {
                    float period = l.duration + l.gap; int pi = Mathf.Min(l.count - 1, Mathf.FloorToInt(tl / period)); float tp = tl - pi * period;
                    float a = tp <= l.duration ? FadeAt(l.fade, Mathf.Clamp01(tp / l.duration)) : 0f;
                    Vector3 end = _ip;
                    if (l.reach == "sweep") end = Vector3.Lerp(_m, _ip, Mathf.Clamp01(tp / Mathf.Min(0.25f, l.duration * 0.5f)));
                    int segs = o.Lines[0].positionCount - 1;
                    if (segs > 1)
                    {
                        int roll = Mathf.FloorToInt(tl * 24f);
                        if (roll != o.Roll || o.Pts == null)
                        {
                            o.Roll = roll; o.Pts = new Vector3[segs + 1];
                            for (int k = 0; k <= segs; k++) { float s = (float)k / segs, bump = Mathf.Sin(Mathf.PI * s) * l.jitter; Vector3 p = Vector3.Lerp(_m, end, s); p += _side * ((Random.value * 2f - 1f) * bump); p.y += (Random.value * 2f - 1f) * bump; o.Pts[k] = p; }
                        }
                        foreach (LineRenderer lr in o.Lines) lr.SetPositions(o.Pts);
                    }
                    else foreach (LineRenderer lr in o.Lines) { lr.SetPosition(0, _m); lr.SetPosition(1, end); }
                    float[] op = { 0.95f, 0.75f, 0.22f };
                    for (int i = 0; i < 3; i++) Alpha(o.Lines[i], op[i] * a);
                    break;
                }
                case "bolt":
                {
                    for (int i = 0; i < l.count; i++)
                    {
                        float d = Mathf.Max(0f, (tl - i * l.gap) * l.speed);
                        bool show = tl >= i * l.gap && d < _range;
                        o.Bolts[i].SetActive(show); o.Lines[i * 2].enabled = o.Lines[i * 2 + 1].enabled = show;
                        if (!show) continue;
                        Vector3 p = PathPoint(l, i, d, o.Offs), ahead = PathPoint(l, i, Mathf.Min(_range, d + 1f), o.Offs);
                        Vector3 tangent = (ahead - p).sqrMagnitude > 0.0001f ? (ahead - p).normalized : _dir;
                        o.Bolts[i].transform.position = p; o.Bolts[i].transform.rotation = Quaternion.LookRotation(tangent);
                        if (i == 0) TrackPos = p;
                        if (l.spin > 0f) o.Bolts[i].transform.Rotate(Vector3.right, tl * l.spin * 360f, Space.Self);
                        Vector3 tail = p - tangent * l.length * 0.5f, head = p + tangent * l.length * 0.5f;
                        o.Lines[i * 2].SetPosition(0, tail); o.Lines[i * 2].SetPosition(1, head); o.Lines[i * 2 + 1].SetPosition(0, tail); o.Lines[i * 2 + 1].SetPosition(1, head);
                    }
                    break;
                }
                case "trail":
                {
                    Layer bolt = null; foreach (Recipe.Item it in CurrentItems) if (it.L.kind == "bolt") { bolt = it.L; break; }
                    float dist = bolt != null ? Mathf.Min(_range, Mathf.Max(0f, tl * bolt.speed)) : _range;
                    float fade = dist >= _range ? Mathf.Max(0f, 1f - (tl - (impact - o.It.Start)) / 0.4f) : 1f;
                    float back = Mathf.Max(0f, dist - l.length), seg = (dist - back) / 4f;
                    for (int k = 0; k <= 4; k++) { float dd = back + seg * k; Vector3 p = bolt != null ? PathPoint(bolt, 0, dd, null) : Vector3.Lerp(_m, _ip, dd / _range); o.Lines[0].SetPosition(k, p); o.Lines[1].SetPosition(k, p); }
                    Alpha(o.Lines[0], l.opacity * fade); Alpha(o.Lines[1], l.opacity * 0.4f * fade);
                    break;
                }
                case "burst":
                    if (o.Ring != null)
                    {
                        float r = l.ring * Mathf.Min(1f, tl / 0.35f); o.Ring.transform.localScale = new Vector3(r, r, 1f);
                        if (cam != null) o.Ring.transform.rotation = Quaternion.LookRotation(o.Ring.transform.position - cam.transform.position);
                        SpriteAlpha(o.Ring, o.Color, 0.8f * Mathf.Max(0f, 1f - tl / 0.4f));
                    }
                    break;
                case "light":
                {
                    o.Light.transform.position = AnchorPos(l, tl) + Vector3.up * 1.5f;
                    o.Light.intensity = l.intensity * FadeAt("ease out", u) * 0.5f;
                    break;
                }
                // ---- new kinds
                case "ring":
                {
                    float grow = Mathf.Min(1f, tl / Mathf.Max(0.02f, l.duration * 0.6f)); float r = Mathf.Max(0.01f, l.size * Mathf.Sin(grow * Mathf.PI * 0.5f));
                    o.Obj.transform.position = AnchorPos(l, tl); o.Obj.transform.localScale = new Vector3(r * 2f, r * 2f, 1f); Face(o.Obj, l, cam);
                    SpriteAlpha(o.Obj, o.Color, 0.9f * FadeAt(l.fade, u));
                    break;
                }
                case "orb":
                {
                    float grow = l.fade == "in" ? u : Mathf.Min(1f, tl / Mathf.Max(0.02f, l.duration * 0.25f));
                    float s = Mathf.Max(0.01f, l.size * grow * (0.9f + 0.1f * Mathf.Sin(tl * 40f)));
                    Vector3 p = AnchorPos(l, tl);
                    o.Obj.transform.position = p; o.Obj.transform.localScale = Vector3.one * s;
                    Color core = Recipe.Col(l.core); core.a = l.fade == "in" ? 1f : FadeAt(l.fade, u); o.Obj.GetComponent<Renderer>().material.color = core; o.Obj.SetActive(core.a > 0.02f);
                    o.Halo.transform.position = p; o.Halo.transform.localScale = Vector3.one * s * (1.5f + l.glow); if (cam != null) o.Halo.transform.rotation = Quaternion.LookRotation(p - cam.transform.position);
                    SpriteAlpha(o.Halo, o.Color, 0.7f * (l.fade == "in" ? u : FadeAt(l.fade, u)));
                    break;
                }
                case "glow":
                {
                    Vector3 p = AnchorPos(l, tl);
                    float s = l.size * (1f + l.glow * 0.1f * Mathf.Sin(tl * 60f));
                    o.Obj.transform.position = p; o.Obj.transform.localScale = new Vector3(s, s, 1f); Face(o.Obj, l, cam);
                    SpriteAlpha(o.Obj, o.Color, 0.9f * FadeAt(l.fade, u));
                    break;
                }
                case "scorch":
                {
                    float s = l.size * Mathf.Min(1f, tl / 0.1f);
                    o.Obj.transform.localScale = new Vector3(s, s, 1f);
                    SpriteAlpha(o.Obj, o.Color, l.opacity * (u < 0.6f ? 1f : (1f - u) / 0.4f));
                    break;
                }
                case "arc":
                {
                    float a = FadeAt(l.fade, u);
                    int roll = Mathf.FloorToInt(tl * Mathf.Max(1f, l.rate));
                    if (roll != o.Roll)
                    {
                        o.Roll = roll;
                        Vector3 from, to;
                        if (l.reach == "fork") { from = AnchorPos(l, tl); to = from + Random.onUnitSphere * l.length; } else { from = _m; to = _ip; }
                        var main = new Vector3[13];
                        for (int k = 0; k < 13; k++) { float s = k / 12f, bump = Mathf.Sin(Mathf.PI * s) * l.jitter; Vector3 p = Vector3.Lerp(from, to, s); p += _side * ((Random.value * 2f - 1f) * bump); p.y += (Random.value * 2f - 1f) * bump; main[k] = p; }
                        o.Lines[0].SetPositions(main); o.Lines[1].SetPositions(main);
                        for (int b = 1; b < o.Lines.Length / 2; b++)
                        {
                            Vector3 root = main[Random.Range(2, 11)], tip = root + Random.onUnitSphere * l.length * (0.4f + Random.value * 0.6f);
                            var pts = new Vector3[6];
                            for (int k = 0; k < 6; k++) { float s = k / 5f; Vector3 p = Vector3.Lerp(root, tip, s) + Random.insideUnitSphere * l.jitter * 0.4f * Mathf.Sin(Mathf.PI * s); pts[k] = p; }
                            o.Lines[b * 2].SetPositions(pts); o.Lines[b * 2 + 1].SetPositions(pts);
                        }
                    }
                    for (int i = 0; i < o.Lines.Length; i++) Alpha(o.Lines[i], (i % 2 == 0 ? 0.95f : 0.5f) * a * (i < 2 ? 1f : 0.7f));
                    break;
                }
                case "smoke":
                {
                    // emitted over the first third, drifting up and growing
                    float budget = l.count * Mathf.Clamp01(tl / Mathf.Max(0.05f, l.life * 0.3f));
                    Vector3 p = AnchorPos(l, tl);
                    while (o.Emitted < budget) { o.Emitted++; Vector3 v = Dir(null, 0f, true) * l.speed * (0.3f + Random.value * 0.7f) + Vector3.up * l.speed * 0.5f; Emit(o.Ps, p + Random.insideUnitSphere * l.size * 0.3f, v, l.size * (0.6f + Random.value * 0.8f), l.life * (0.6f + Random.value * 0.8f), new Color(o.Color.r, o.Color.g, o.Color.b, l.opacity)); }
                    break;
                }
                case "embers":
                {
                    if (o.Emitted == 0f) { Vector3 p = AnchorPos(l, tl); for (int i = 0; i < l.count; i++) Emit(o.Ps, p, Dir(null, 0f, true) * l.speed * (0.3f + Random.value * 0.9f), l.size * (0.5f + Random.value), l.life * (0.5f + Random.value)); o.Emitted = 1f; }
                    break;
                }
                case "debris":
                {
                    if (o.Emitted == 0f) { Vector3 p = AnchorPos(l, tl); Vector3 back = l.anchor == "muzzle" ? _dir : -_dir; for (int i = 0; i < l.count; i++) Emit(o.Ps, p, Dir(back + Vector3.up * 0.6f, l.spread, false) * l.speed * (0.3f + Random.value * 0.9f), l.size * (0.4f + Random.value * 1.2f), l.life * (0.6f + Random.value * 0.8f), o.Color, true); o.Emitted = 1f; }
                    break;
                }
                case "casing":
                {
                    // one casing per `gap` seconds out of the right side of the gun, tumbling
                    float due = Mathf.Min(l.count, 1f + tl / Mathf.Max(0.02f, l.gap));
                    while (o.Emitted < due) { o.Emitted++; Vector3 v = (_side * 1f + Vector3.up * 1.2f + Random.insideUnitSphere * 0.4f).normalized * l.speed; Emit(o.Ps, _m + _side * 0.5f, v, l.size, l.life, o.Color, true); }
                    break;
                }
                case "charge":
                {
                    // particles born on a sphere of `radius` around the muzzle, arriving at the muzzle as they die
                    float budget = l.count * Mathf.Clamp01(tl / Mathf.Max(0.05f, l.duration * 0.7f));
                    while (o.Emitted < budget)
                    {
                        o.Emitted++;
                        float life = Mathf.Max(0.05f, (l.duration - tl) * (0.5f + Random.value * 0.5f));
                        Vector3 p = _m + Random.onUnitSphere * l.radius * (0.5f + Random.value * 0.5f);
                        Emit(o.Ps, p, (_m - p) / life, l.size * (0.5f + Random.value), life);
                    }
                    break;
                }
                case "pulse":
                {
                    float flight = _range / Mathf.Max(1f, l.speed);
                    float due = Mathf.Min(l.count, 1f + tl / Mathf.Max(0.01f, l.gap));
                    while (o.Emitted < due && tl <= l.duration) { o.Emitted++; Emit(o.Ps, _m, _dir * l.speed, l.size, flight); }
                    break;
                }
                case "spray":
                {
                    float budget = l.count * Mathf.Clamp01(tl / Mathf.Max(0.02f, l.duration));
                    while (o.Emitted < budget) { o.Emitted++; Emit(o.Ps, _m, Dir(_dir, l.spread, false) * l.speed * (0.6f + Random.value * 0.6f), l.size * (0.6f + Random.value * 0.8f), l.life * (0.7f + Random.value * 0.6f)); }
                    break;
                }
                case "helix":
                {
                    float a = FadeAt(l.fade, u);
                    int n = o.Lines[0].positionCount, strands = o.Lines.Length;
                    Vector3 up = Vector3.Cross(_side, _dir).normalized;
                    float reach = l.reach == "sweep" ? Mathf.Clamp01(tl / Mathf.Max(0.05f, l.duration * 0.5f)) : 1f;
                    for (int s = 0; s < strands; s++)
                    {
                        var pts = new Vector3[n];
                        for (int k = 0; k < n; k++)
                        {
                            float f = (float)k / (n - 1) * reach, ang = f * l.turns * Mathf.PI * 2f + tl * l.spin * Mathf.PI * 2f + s * Mathf.PI * 2f / strands;
                            pts[k] = Vector3.Lerp(_m, _ip, f) + (_side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * l.radius * Mathf.Sin(Mathf.PI * Mathf.Min(1f, f * 4f + 0.001f) * 0.5f);
                        }
                        o.Lines[s].SetPositions(pts); Alpha(o.Lines[s], 0.8f * a);
                    }
                    break;
                }
                case "vfx":
                case "part":
                    if (o.Obj != null && l.anchor == "bolt") o.Obj.transform.position = AnchorPos(l, tl);
                    break;
                case "shake":
                    _shakeOffset += Random.insideUnitSphere * l.intensity * 0.1f * FadeAt("ease out", u);
                    break;
            }
        }

        private List<Recipe.Item> CurrentItems = new List<Recipe.Item>();
        public void SetItems(List<Recipe.Item> items) { CurrentItems = items; }
    }
}
