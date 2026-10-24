// The stage: ground, sky and light for four backdrops, an orbit camera with presets (including one that rides with the
// shot), and the 'Mechs loaded from the game's own prefabs. The shooter's muzzle comes from the rig's weapon hardpoint
// that matches the weapon family (chrPrfWeap_<mech>_<location>_<weapon>_*/..._fire), the hit point from the target's
// location objects (Head_whole, LTorso_whole and so on). A second shooter can stand beside the first for comparisons.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GNFX
{
    public class SceneRig : MonoBehaviour
    {
        public GameAssets Assets;
        public Material Ground, Sky;
        public Camera Cam;
        public GameObject Shooter, Target, Shooter2;
        public string ShooterId, TargetId;
        public float Range = 240f;
        public Vector3 Muzzle, Impact, Muzzle2;
        public string CameraMode = "shoulder";
        public string Backdrop = "dusk";
        public string HitLocation = "ct";
        public string WeaponHint = "laser";
        public bool Compare;
        public Func<Vector3?> Follow;            // where the shot is right now, for the "follow" camera
        public static readonly string[] Locations = { "ct", "head", "lt", "rt", "la", "ra", "ll", "rl" };
        public static readonly string[] LocationNames = { "Centre torso", "Head", "Left torso", "Right torso", "Left arm", "Right arm", "Left leg", "Right leg" };
        // orbit
        private float _theta = -Mathf.PI / 2f - 0.22f, _phi = 1.32f, _dist = 60f;
        private Vector3 _focus = new Vector3(0f, 8f, 60f);
        private Vector3 _lastMouse; private bool _drag;
        private Light _sun; private GameObject _ground;

        public void Build()
        {
            _sun = new GameObject("Sun").AddComponent<Light>(); _sun.type = LightType.Directional; _sun.shadows = LightShadows.Soft; _sun.shadowStrength = 0.7f;
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane); _ground.name = "Ground"; _ground.transform.localScale = new Vector3(400f, 1f, 400f);
            if (Ground != null) { Ground.mainTexture = GridTexture(); Ground.mainTextureScale = new Vector2(400f, 400f); _ground.GetComponent<Renderer>().material = Ground; }
            var camGo = new GameObject("Camera"); Cam = camGo.AddComponent<Camera>(); camGo.tag = "MainCamera"; camGo.AddComponent<AudioListener>();
            Cam.fieldOfView = 42f; Cam.nearClipPlane = 0.3f; Cam.farClipPlane = 6000f; Cam.allowHDR = true; Cam.allowMSAA = false;
            Cam.renderingPath = RenderingPath.Forward;
            SetBackdrop(Backdrop);
        }

        public bool Deferred
        {
            get { return Cam != null && Cam.renderingPath == RenderingPath.DeferredShading; }
            set { if (Cam != null) Cam.renderingPath = value ? RenderingPath.DeferredShading : RenderingPath.Forward; }
        }

        /// <summary>Sky, sun, ambient, fog and ground for a time of day, or a neutral grey reference stage.</summary>
        public void SetBackdrop(string name)
        {
            Backdrop = name;
            Color ground; Color sky, equator, groundAmb, fog; Color sunCol; float sunInt; Vector3 sunDir; bool skybox = true; float exposure = 1.1f, atmosphere = 1f; Color tint;
            switch (name)
            {
                case "day":
                    ground = new Color(0.42f, 0.40f, 0.34f); sky = new Color(0.62f, 0.72f, 0.88f); equator = new Color(0.45f, 0.45f, 0.42f); groundAmb = new Color(0.22f, 0.2f, 0.17f);
                    fog = new Color(0.66f, 0.72f, 0.80f); sunCol = new Color(1f, 0.97f, 0.9f); sunInt = 1.25f; sunDir = new Vector3(55f, -30f, 0f); tint = new Color(0.5f, 0.6f, 0.8f); break;
                case "night":
                    ground = new Color(0.09f, 0.09f, 0.08f); sky = new Color(0.08f, 0.1f, 0.14f); equator = new Color(0.06f, 0.06f, 0.07f); groundAmb = new Color(0.02f, 0.02f, 0.02f);
                    fog = new Color(0.02f, 0.025f, 0.035f); sunCol = new Color(0.55f, 0.65f, 0.9f); sunInt = 0.18f; sunDir = new Vector3(40f, 20f, 0f); skybox = false; tint = Color.black; break;
                case "grey":
                    ground = new Color(0.5f, 0.5f, 0.5f); sky = new Color(0.5f, 0.5f, 0.5f); equator = new Color(0.42f, 0.42f, 0.42f); groundAmb = new Color(0.3f, 0.3f, 0.3f);
                    fog = new Color(0.5f, 0.5f, 0.5f); sunCol = Color.white; sunInt = 0.9f; sunDir = new Vector3(50f, -20f, 0f); skybox = false; tint = Color.grey; break;
                default: // dusk
                    ground = new Color(0.3f, 0.26f, 0.22f); sky = new Color(0.45f, 0.36f, 0.4f); equator = new Color(0.5f, 0.32f, 0.2f); groundAmb = new Color(0.12f, 0.1f, 0.09f);
                    fog = new Color(0.32f, 0.22f, 0.2f); sunCol = new Color(1f, 0.62f, 0.35f); sunInt = 0.9f; sunDir = new Vector3(12f, -60f, 0f); tint = new Color(0.9f, 0.45f, 0.3f); atmosphere = 1.6f; exposure = 0.9f; break;
            }
            if (Ground != null) Ground.color = ground;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = groundAmb;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 300f; RenderSettings.fogEndDistance = name == "night" ? 2200f : 3500f; RenderSettings.fogColor = fog;
            _sun.color = sunCol; _sun.intensity = sunInt; _sun.transform.rotation = Quaternion.Euler(sunDir);
            RenderSettings.sun = _sun;
            if (skybox && Sky != null)
            {
                RenderSettings.skybox = Sky; Cam.clearFlags = CameraClearFlags.Skybox;
                if (Sky.HasProperty("_SkyTint")) Sky.SetColor("_SkyTint", tint);
                if (Sky.HasProperty("_Exposure")) Sky.SetFloat("_Exposure", exposure);
                if (Sky.HasProperty("_AtmosphereThickness")) Sky.SetFloat("_AtmosphereThickness", atmosphere);
                if (Sky.HasProperty("_GroundColor")) Sky.SetColor("_GroundColor", ground);
            }
            else { RenderSettings.skybox = null; Cam.clearFlags = CameraClearFlags.SolidColor; Cam.backgroundColor = fog; }
        }

        private static Texture2D GridTexture()
        {
            const int S = 64; var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { bool line = x < 2 || y < 2; px[y * S + x] = line ? new Color(0.78f, 0.78f, 0.74f) : new Color(1f, 1f, 1f); }
            t.SetPixels(px); t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ 'Mechs

        public void PlaceMechs(string shooterId, string targetId)
        {
            ShooterId = shooterId; TargetId = targetId;
            if (Shooter != null) Destroy(Shooter); if (Target != null) Destroy(Target); if (Shooter2 != null) Destroy(Shooter2);
            Shooter = LoadMech(shooterId, Vector3.zero, 0f);
            Target = LoadMech(targetId, new Vector3(0f, 0f, Range), 180f);
            if (Compare) Shooter2 = LoadMech(shooterId, new Vector3(-18f, 0f, 0f), 0f);
            Refresh();
        }

        public void SetCompare(bool on)
        {
            Compare = on;
            if (Shooter2 != null) { Destroy(Shooter2); Shooter2 = null; }
            if (on && !string.IsNullOrEmpty(ShooterId)) Shooter2 = LoadMech(ShooterId, new Vector3(-18f, 0f, 0f), 0f);
            Refresh();
        }

        /// <summary>Recompute muzzle and impact after the weapon hint or hit location changed.</summary>
        public void Refresh()
        {
            if (Shooter == null) return;
            Muzzle = FindMuzzle(Shooter, WeaponHint);
            Impact = FindLocation(Target, HitLocation);
            Muzzle2 = Shooter2 != null ? FindMuzzle(Shooter2, WeaponHint) : Muzzle + new Vector3(-18f, 0f, 0f);
        }

        /// <summary>The weapon family word used in the rig's hardpoint names, from a stock effect id.</summary>
        public static string HintFor(string effectId)
        {
            string e = (effectId ?? "").ToLowerInvariant();
            if (e.Contains("laser") || e.Contains("tag")) return "laser";
            if (e.Contains("ppc") || e.Contains("coil")) return "ppc";
            if (e.Contains("gauss")) return "gauss";
            if (e.Contains("machinegun")) return "mg";
            if (e.Contains("flamer")) return "flamer";
            if (e.Contains("narc")) return "narc";
            if (e.Contains("lb")) return "lbx";
            var m = System.Text.RegularExpressions.Regex.Match(e, @"(lrm|srm)(\d+)");
            if (m.Success) return "missile" + m.Groups[2].Value;
            if (e.Contains("srminferno")) return "missile6";
            if (e.Contains("ultra")) return "uac";
            m = System.Text.RegularExpressions.Regex.Match(e, @"ac(\d+)");
            if (m.Success) return "ac" + m.Groups[1].Value;
            return "laser";
        }

        private GameObject LoadMech(string id, Vector3 pos, float yaw)
        {
            GameObject prefab = Assets != null && Assets.Ready ? Assets.Prefab(id) : null;
            GameObject g;
            if (prefab != null)
            {
                g = Instantiate(prefab); g.name = id;
                foreach (Animator a in g.GetComponentsInChildren<Animator>(true)) a.enabled = true;
            }
            else g = Silhouette(id);
            g.transform.position = pos; g.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return g;
        }

        /// <summary>A blocky stand-in when the game's prefabs are not available.</summary>
        private static GameObject Silhouette(string name)
        {
            var g = new GameObject(name ?? "Mech");
            float h = 16f, w = 10f, d = 6f;
            GameObject Box(float x, float y, float z, float sx, float sy, float sz, string n) { var b = GameObject.CreatePrimitive(PrimitiveType.Cube); b.name = n; b.transform.SetParent(g.transform, false); b.transform.localPosition = new Vector3(x, y, z); b.transform.localScale = new Vector3(sx, sy, sz); b.GetComponent<Renderer>().material.color = new Color(0.33f, 0.36f, 0.31f); return b; }
            float legH = h * 0.48f, torsoH = h * 0.36f;
            Box(-w * 0.22f, legH / 2f, 0f, w * 0.22f, legH, d * 0.7f, "LLeg_whole"); Box(w * 0.22f, legH / 2f, 0f, w * 0.22f, legH, d * 0.7f, "RLeg_whole");
            Box(0f, legH + torsoH / 2f, 0f, w * 0.62f, torsoH, d, "Torso_whole"); Box(0f, legH + torsoH + h * 0.06f, d * 0.1f, w * 0.26f, h * 0.12f, d * 0.6f, "Head_whole");
            Box(-w * 0.43f, legH + torsoH * 0.47f, 0f, w * 0.18f, torsoH * 0.9f, d * 0.6f, "LArm_whole"); Box(w * 0.43f, legH + torsoH * 0.47f, 0f, w * 0.18f, torsoH * 0.9f, d * 0.6f, "RArm_whole");
            var gun = Box(w * 0.43f, legH + torsoH * 0.62f, d * 0.75f, w * 0.1f, w * 0.1f, d * 1.2f, "silhouette_fire");
            return g;
        }

        /// <summary>The rig's weapon muzzle: a "_fire" transform under the hardpoint of the wanted weapon family, right arm first.</summary>
        public static Vector3 FindMuzzle(GameObject mech, string hint)
        {
            Transform[] all = mech.GetComponentsInChildren<Transform>(true);
            Transform best = null; int bestScore = -1;
            hint = (hint ?? "").ToLowerInvariant();
            foreach (Transform t in all)
            {
                string n = t.name.ToLowerInvariant();
                if (!n.Contains("_fire")) continue;
                int score = 1;
                string chain = Chain(t);
                if (hint.Length > 0 && chain.Contains("_" + hint + "_")) score += 16;
                else if (hint.StartsWith("missile") && chain.Contains("_missile")) score += 8;
                else if (hint.StartsWith("ac") && chain.Contains("_ac")) score += 8;
                else if (hint == "uac" && chain.Contains("_uac")) score += 8;
                if (chain.Contains("right_arm") || chain.Contains("rightarm")) score += 4; else if (chain.Contains("arm")) score += 2;
                if (chain.Contains("torso")) score += 1;
                if (score > bestScore) { bestScore = score; best = t; }
            }
            if (best != null) return best.position;
            Bounds b = BoundsOf(mech);
            return b.center + mech.transform.forward * b.extents.z + mech.transform.right * b.extents.x * 0.6f;
        }

        private static string Chain(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            for (Transform p = t; p != null; p = p.parent) sb.Append(p.name.ToLowerInvariant()).Append('/');
            return sb.ToString();
        }

        /// <summary>The hit point on the target: the renderer bounds centre of its location object, pushed toward the shooter.</summary>
        public static Vector3 FindLocation(GameObject mech, string loc)
        {
            string wanted;
            switch (loc)
            {
                case "head": wanted = "head_whole"; break;
                case "lt": wanted = "ltorso_whole"; break;
                case "rt": wanted = "rtorso_whole"; break;
                case "la": wanted = "larm_whole"; break;
                case "ra": wanted = "rarm_whole"; break;
                case "ll": wanted = "lleg_whole"; break;
                case "rl": wanted = "rleg_whole"; break;
                default: wanted = "torso_whole"; break;
            }
            foreach (Transform t in mech.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.ToLowerInvariant() != wanted) continue;
                Renderer[] rs = t.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds; foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                return b.center + mech.transform.forward * b.extents.z * 0.7f;
            }
            return FindChest(mech);
        }

        public static Vector3 FindChest(GameObject mech)
        {
            Bounds b = BoundsOf(mech);
            Vector3 c = b.center; c.y = b.min.y + b.size.y * 0.62f;
            return c + mech.transform.forward * b.extents.z * 0.6f;
        }

        public static Bounds BoundsOf(GameObject g)
        {
            Renderer[] rs = g.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(g.transform.position + Vector3.up * 8f, new Vector3(10f, 16f, 6f));
            Bounds b = rs[0].bounds; foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------ camera

        public static readonly string[] CameraModes = { "shoulder", "muzzle", "follow", "side", "target" };
        public static readonly string[] CameraNames = { "Over the shoulder", "At the muzzle", "Follow the shot", "Side", "At the target" };

        public void Frame(string mode)
        {
            CameraMode = mode;
            Vector3 mid = new Vector3(0f, 8f, Range / 2f);
            Vector3 fwd = (Impact - Muzzle).normalized;
            switch (mode)
            {
                case "side": Place(mid + new Vector3(Range * 0.55f + 30f, 12f, -10f), mid); break;
                case "shoulder": Place(Muzzle + new Vector3(9f, 10f, -24f), Muzzle + fwd * Mathf.Min(90f, Range * 0.4f)); break;
                case "muzzle": Place(Muzzle + new Vector3(2.6f, 1.4f, -5.5f), Muzzle + fwd * 25f); break;
                case "follow": Place(Muzzle + new Vector3(14f, 6f, -10f), Muzzle + fwd * 12f); break;
                case "target": Place(Impact + new Vector3(28f, 10f, 34f), Impact); break;
            }
        }

        /// <summary>Look at a point from a little above and to the side, at a distance.</summary>
        public void FocusOn(Vector3 point, float dist, float side) { CameraMode = "free"; Place(point + new Vector3(dist * side, dist * 0.3f, -dist * 0.8f), point); }

        /// <summary>Set the orbit so the camera sits at a point looking at a focus.</summary>
        private void Place(Vector3 cam, Vector3 focus)
        {
            _focus = focus;
            Vector3 d = cam - focus; _dist = Mathf.Max(1f, d.magnitude);
            _phi = Mathf.Acos(Mathf.Clamp(d.y / _dist, -1f, 1f)); _theta = Mathf.Atan2(d.z, d.x);
        }

        private void Update()
        {
            if (Cam == null) return;
            bool overUi = StudioUI.MouseOverPanel;
            if (Input.GetMouseButtonDown(0) && !overUi) { _drag = true; _lastMouse = Input.mousePosition; if (CameraMode != "follow") CameraMode = "free"; }
            if (Input.GetMouseButtonUp(0)) _drag = false;
            if (_drag && Input.GetMouseButton(0))
            {
                Vector3 d = Input.mousePosition - _lastMouse; _lastMouse = Input.mousePosition;
                _theta -= d.x * 0.006f; _phi = Mathf.Clamp(_phi + d.y * 0.006f, 0.15f, 1.55f);
            }
            if (!overUi && Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f) { _dist = Mathf.Clamp(_dist * (Input.mouseScrollDelta.y < 0 ? 1.12f : 0.89f), 3f, 1500f); if (CameraMode != "follow") CameraMode = "free"; }
            if (CameraMode == "follow" && Follow != null)
            {
                Vector3? p = Follow();
                if (p.HasValue) _focus = Vector3.Lerp(_focus, p.Value, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
            }
            Cam.transform.position = _focus + new Vector3(_dist * Mathf.Sin(_phi) * Mathf.Cos(_theta), _dist * Mathf.Cos(_phi), _dist * Mathf.Sin(_phi) * Mathf.Sin(_theta));
            Cam.transform.LookAt(_focus);
        }
    }
}
