// Plays one of the game's own weapon effect prefabs the way the game's WeaponEffect classes do, with the parts the
// prefab carries: pre-fire and muzzle flash particles, the projectile prefab (mesh, particles, lights, PPC branches),
// the laser's line renderer and fade curve, missiles on a curved path, and the impact prefab named by the effect.
// What the game decides from combat state (hits, surfaces, audio) is replaced by the studio's settings.
using System;
using System.Collections;
using System.Collections.Generic;
using BattleTech.Rendering;
using UnityEngine;

namespace GNFX
{
    public class StockEffectPlayer : MonoBehaviour
    {
        public GameAssets Assets;
        public bool Indirect;              // missiles climb over cover
        public float Spread = 0f;          // extra metres of scatter on every projectile
        public string Status = "";
        public bool Playing;
        public Recipe Mods;                // a recipe's CAC knobs applied to every projectile; null in plain stock mode
        public Vector3? TrackPos;          // where the latest projectile is, for the follow camera
        public string ArmorVfx = "none";   // none | armor | structure: also play the effect's armour- or structure-hit VFX at the impact
        private float SpeedMul => Mods != null && Mods.speedMul > 0f ? Mods.speedMul : 1f;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Coroutine> _flights = new List<Coroutine>();
        private int _pending;

        public void Clear()
        {
            StopAllCoroutines(); _flights.Clear(); _pending = 0; Playing = false; TrackPos = null;
            foreach (GameObject g in _spawned) if (g != null) Destroy(g);
            _spawned.Clear();
        }

        public void Play(string effectId, Vector3 muzzle, Vector3 target, int shots, int perShot)
        {
            Clear();
            StartCoroutine(Run(effectId, muzzle, target, Mathf.Max(1, shots), Mathf.Max(1, perShot)));
        }

        private GameObject Track(GameObject g) { if (g != null) { _spawned.Add(g); ShaderFix.Apply(g); } return g; }

        // ------------------------------------------------------------------ the parts of a stock effect

        public static readonly string[] Parts = { "prefire", "flash", "impact", "armor", "structure", "terrain", "building" };
        public static readonly string[] PartNames = { "Pre-fire (charge)", "Muzzle flash", "Impact", "Armour hit", "Structure hit", "Terrain hit", "Building hit" };

        /// <summary>The effect script on a stock effect's prefab, without instantiating it.</summary>
        public WeaponEffect Effect(string effectId)
        {
            GameObject prefab = Assets != null && !string.IsNullOrEmpty(effectId) ? Assets.Prefab(effectId) : null;
            return prefab != null ? prefab.GetComponent<WeaponEffect>() : null;
        }

        /// <summary>The sub-effect a launcher fires (bullet or missile), which carries its own flash and impact.</summary>
        private static WeaponEffect Sub(WeaponEffect fx)
        {
            var be = fx as BallisticEffect; if (be != null && be.bulletPrefab != null) return be.bulletPrefab.GetComponent<WeaponEffect>();
            var me = fx as MissileLauncherEffect; if (me != null && me.MissilePrefab != null) return me.MissilePrefab.GetComponent<WeaponEffect>();
            return null;
        }

        public float PrefireOf(string effectId) { WeaponEffect fx = Effect(effectId); return fx != null ? Mathf.Max(0f, fx.preFireDuration) : 0f; }

        /// <summary>The impact variation names of a stock effect (its own, or its bullet's or missile's), or null.</summary>
        public string[] ImpactVariations(string effectId) { WeaponEffect fx = Effect(effectId); if (fx == null) return null; WeaponEffect sub = Sub(fx); return !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXVariations : sub?.impactVFXVariations; }

        /// <summary>One part of a stock effect as a prefab: its pre-fire, muzzle flash, impact (a random variation), armour, structure, terrain or building hit.</summary>
        public GameObject PartPrefab(string effectId, string part)
        {
            WeaponEffect fx = Effect(effectId);
            if (fx == null) return null;
            WeaponEffect sub = Sub(fx);
            switch (part)
            {
                case "prefire": return fx.preFireVFXPrefab ?? sub?.preFireVFXPrefab;
                case "flash": return fx.muzzleFlashVFXPrefab ?? sub?.muzzleFlashVFXPrefab;
                default:
                    if (part != null && part.StartsWith("impact:"))
                    {
                        string bn = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXBase : sub?.impactVFXBase;
                        return string.IsNullOrEmpty(bn) ? null : Assets.Prefab(bn + "_" + part.Substring(7)) ?? Assets.Prefab(bn);
                    }
                    return null;
                case "impact":
                {
                    string baseName = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXBase : sub?.impactVFXBase;
                    string[] vars = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXVariations : sub?.impactVFXVariations;
                    if (string.IsNullOrEmpty(baseName)) return null;
                    GameObject g = vars != null && vars.Length > 0 ? Assets.Prefab(baseName + "_" + vars[UnityEngine.Random.Range(0, vars.Length)]) : null;
                    return g ?? Assets.Prefab(baseName);
                }
                case "armor": return Assets.Prefab(fx.armorDamageVFXName ?? sub?.armorDamageVFXName);
                case "structure": return Assets.Prefab(fx.structureDamageVFXName ?? sub?.structureDamageVFXName);
                case "terrain": { string b = fx.terrainHitVFXBase ?? sub?.terrainHitVFXBase; return Assets.Prefab(b) ?? Assets.Prefab(b + "_grass") ?? Assets.Prefab(b + "_dirt"); }
                case "building": return Assets.Prefab(fx.buildingHitOverlayVFXName ?? sub?.buildingHitOverlayVFXName);
            }
            return null;
        }

        /// <summary>What a stock effect is made of, one line per part, for the anatomy panel.</summary>
        public List<string> Anatomy(string effectId)
        {
            var lines = new List<string>();
            WeaponEffect fx = Effect(effectId);
            if (fx == null) { lines.Add("Not loaded."); return lines; }
            WeaponEffect sub = Sub(fx);
            lines.Add("Type: " + fx.GetType().Name + (sub != null ? " firing " + sub.GetType().Name : ""));
            lines.Add("Pre-fire: " + Name(fx.preFireVFXPrefab ?? sub?.preFireVFXPrefab) + (fx.preFireDuration > 0f ? ", " + fx.preFireDuration.ToString("0.##") + " s" : "") + (string.IsNullOrEmpty(fx.preFireSFX) ? "" : ", sound " + fx.preFireSFX));
            lines.Add("Muzzle flash: " + Name(fx.muzzleFlashVFXPrefab ?? sub?.muzzleFlashVFXPrefab));
            lines.Add("Projectile: " + Name(fx.projectilePrefab ?? sub?.projectilePrefab) + (fx.projectileSpeed > 0f ? (fx is LaserEffect ? ", " + fx.projectileSpeed.ToString("0.##") + " s beam" : ", " + fx.projectileSpeed.ToString("0") + " m/s") : ""));
            var le = fx as LaserEffect; if (le != null) lines.Add("Light: intensity " + le.lightIntensity.ToString("0") + ", radius " + le.lightRadius.ToString("0") + (string.IsNullOrEmpty(le.beamStartSFX) ? "" : ", sounds " + le.beamStartSFX + " / " + le.beamStopSFX));
            string ib = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXBase : sub?.impactVFXBase; string[] iv = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXVariations : sub?.impactVFXVariations;
            lines.Add("Impact: " + (ib ?? "none") + (iv != null && iv.Length > 0 ? " × " + iv.Length + " variations (" + string.Join(", ", iv) + ")" : ""));
            lines.Add("Armour hit: " + (fx.armorDamageVFXName ?? sub?.armorDamageVFXName ?? "none") + "   Structure hit: " + (fx.structureDamageVFXName ?? sub?.structureDamageVFXName ?? "none"));
            lines.Add("Terrain hit: " + (fx.terrainHitVFXBase ?? sub?.terrainHitVFXBase ?? "none") + "   Building hit: " + (fx.buildingHitOverlayVFXName ?? sub?.buildingHitOverlayVFXName ?? "none"));
            return lines;
        }

        private static string Name(GameObject g) => g != null ? g.name : "none";

        /// <summary>The colour a stock effect's projectile carries in its materials' blackbody tint, normalised; null when it has none.</summary>
        public Color? ProjectileColor(string effectId)
        {
            GameObject prefab = Assets != null ? Assets.Prefab(effectId) : null;
            WeaponEffect fx = prefab != null ? prefab.GetComponent<WeaponEffect>() : null;
            if (fx == null) return null;
            var candidates = new List<GameObject>();
            if (fx.projectilePrefab != null) candidates.Add(fx.projectilePrefab);
            var be = fx as BallisticEffect; if (be != null && be.bulletPrefab != null) { var bw = be.bulletPrefab.GetComponent<WeaponEffect>(); if (bw != null && bw.projectilePrefab != null) candidates.Add(bw.projectilePrefab); }
            var me = fx as MissileLauncherEffect; if (me != null && me.MissilePrefab != null) { var mw = me.MissilePrefab.GetComponent<WeaponEffect>(); if (mw != null && mw.projectilePrefab != null) candidates.Add(mw.projectilePrefab); }
            Color best = Color.black; float bestMax = 0f;
            foreach (GameObject g in candidates)
                foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        ShaderFix.Base b = ShaderFix.BaseOf(m);
                        Color bb = b != null ? b.BB : m.HasProperty("_ColorBB") ? m.GetColor("_ColorBB") : Color.black;
                        float max = Mathf.Max(bb.r, Mathf.Max(bb.g, bb.b));
                        float min = Mathf.Min(bb.r, Mathf.Min(bb.g, bb.b));
                        float sat = max > 0.001f ? (max - min) / max : 0f;      // prefer the most coloured material (trails are grey multipliers)
                        if (max > 0.01f && sat * max > bestMax) { bestMax = sat * max; best = bb; }
                    }
            if (bestMax <= 0f) return null;
            float top = Mathf.Max(best.r, Mathf.Max(best.g, best.b));
            return new Color(best.r / top, best.g / top, best.b / top, 1f);
        }

        /// <summary>Instantiate a VFX prefab, aim it, and play every particle system and light animator on it.</summary>
        private GameObject Spawn(GameObject prefab, Vector3 pos, Vector3 lookAt, Transform parent = null)
        {
            if (prefab == null) return null;
            GameObject g = Track(Instantiate(prefab));
            g.name = prefab.name;
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            if ((lookAt - pos).sqrMagnitude > 0.001f) g.transform.LookAt(lookAt);
            g.SetActive(true);
            PlayAll(g);
            return g;
        }

        private static void PlayAll(GameObject g)
        {
            ParticleSystem root = g.GetComponent<ParticleSystem>();
            if (root != null) { root.Stop(true); root.Clear(true); root.Play(true); }
            else foreach (ParticleSystem ps in g.GetComponentsInChildren<ParticleSystem>(true)) { ps.Stop(true); ps.Clear(true); ps.Play(true); }
            foreach (BTLightAnimator la in g.GetComponentsInChildren<BTLightAnimator>(true)) { la.StopAnimation(); la.PlayAnimation(); }
        }

        /// <summary>The impact prefab the effect names: its base plus one of its variations, from the game's vfx bundle.</summary>
        private void Impact(WeaponEffect fx, WeaponEffect fallback, Vector3 pos, Vector3 from)
        {
            string baseName = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXBase : fallback?.impactVFXBase;
            string[] vars = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXVariations : fallback?.impactVFXVariations;
            if (string.IsNullOrEmpty(baseName)) return;
            string name = baseName;
            if (vars != null && vars.Length > 0) name = baseName + "_" + vars[UnityEngine.Random.Range(0, vars.Length)];
            GameObject prefab = Assets.Prefab(name) ?? Assets.Prefab(baseName);
            if (prefab == null) { Status = "No impact prefab " + name; return; }
            Spawn(prefab, pos, from);
            string extra = ArmorVfx == "armor" ? (fx.armorDamageVFXName ?? fallback?.armorDamageVFXName) : ArmorVfx == "structure" ? (fx.structureDamageVFXName ?? fallback?.structureDamageVFXName) : null;
            if (!string.IsNullOrEmpty(extra)) { GameObject p2 = Assets.Prefab(extra); if (p2 != null) Spawn(p2, pos, from); }
        }

        private static Vector3 Jitter(Vector3 p, float amount)
        {
            if (amount <= 0f) return p;
            return p + new Vector3(UnityEngine.Random.Range(-amount, amount), UnityEngine.Random.Range(-amount, amount), UnityEngine.Random.Range(-amount, amount));
        }

        private IEnumerator Run(string effectId, Vector3 muzzle, Vector3 target, int shots, int perShot)
        {
            Playing = true; Status = "";
            GameObject prefab = Assets.Prefab(effectId);
            if (prefab == null) { Status = "Could not load " + effectId; Playing = false; yield break; }
            GameObject inst = Track(Instantiate(prefab));
            inst.name = effectId; inst.transform.position = muzzle; inst.SetActive(true);
            WeaponEffect fx = inst.GetComponent<WeaponEffect>();
            if (fx == null) { Status = effectId + ": the prefab's effect script did not bind (no WeaponEffect component)"; Playing = false; yield break; }
            Status = fx.GetType().Name + " · " + (fx.projectilePrefab != null ? fx.projectilePrefab.name : "no projectile") + " · impact " + (string.IsNullOrEmpty(fx.impactVFXBase) ? "none" : fx.impactVFXBase);

            if (fx is LaserEffect) yield return Laser((LaserEffect)fx, inst, muzzle, target, shots);
            else if (fx is BallisticEffect) yield return Ballistic((BallisticEffect)fx, muzzle, target, shots, perShot);
            else if (fx is BurstBallisticEffect) yield return Burst((BurstBallisticEffect)fx, muzzle, target, shots);
            else if (fx is LBXEffect) yield return Lbx((LBXEffect)fx, muzzle, target, shots * Mathf.Max(perShot, 1));
            else if (fx is MissileLauncherEffect) yield return Missiles((MissileLauncherEffect)fx, muzzle, target, shots);
            else if (fx is FlamerEffect) yield return Flamer((FlamerEffect)fx, muzzle, target);
            else if (fx is COILEffect) yield return Coil((COILEffect)fx, muzzle, target);
            else if (fx is MeleeEffect) Impact(fx, null, target, muzzle);
            else yield return Projectile(fx, fx.projectilePrefab, fx.muzzleFlashVFXPrefab, muzzle, target, null);

            while (_pending > 0) yield return null;
            Playing = false;
        }

        // ------------------------------------------------------------------ lasers

        private IEnumerator Laser(LaserEffect fx, GameObject inst, Vector3 muzzle, Vector3 target, int shots)
        {
            for (int s = 0; s < shots; s++)
            {
                if (fx.projectilePrefab == null) { Status = "Laser without a projectile prefab"; yield break; }
                GameObject proj = Track(Instantiate(fx.projectilePrefab));
                proj.name = fx.projectilePrefab.name; proj.transform.position = muzzle; proj.SetActive(true);
                LineRenderer lr = proj.GetComponent<LineRenderer>() ?? proj.GetComponentInChildren<LineRenderer>(true);
                if (lr == null) { Status = "Laser projectile has no LineRenderer"; yield break; }
                lr.useWorldSpace = true; lr.positionCount = 2; lr.SetPosition(0, muzzle); lr.SetPosition(1, target); lr.enabled = true;
                lr.gameObject.SetActive(true);
                Color c0 = lr.startColor, c1 = lr.endColor;
                BTLight light = lr.GetComponentInChildren<BTLight>(true);
                if (light != null)
                {
                    light.gameObject.SetActive(true); light.enabled = true;
                    light.transform.position = (muzzle + target) * 0.5f; light.length = Vector3.Distance(muzzle, target);
                }
                foreach (MeshRenderer mr in proj.GetComponentsInChildren<MeshRenderer>(true)) mr.gameObject.SetActive(true);
                PlayAll(proj);
                EffectMods.Apply(proj, Mods, fx.projectileSpeed > 0f ? fx.projectileSpeed : 1f);
                Spawn(fx.preFireVFXPrefab, muzzle, target);
                if (fx.preFireDuration > 0f) yield return new WaitForSeconds(fx.preFireDuration);
                Spawn(fx.muzzleFlashVFXPrefab, muzzle, target);
                Impact(fx, null, target, muzzle);
                float duration = fx.projectileSpeed > 0f ? fx.projectileSpeed : 1f;    // for lasers the game stores seconds here
                float t = 0f;
                while (t < 1f)
                {
                    float a = fx.laserAnim != null && fx.laserAnim.length > 0 ? fx.laserAnim.Evaluate(t) : 1f - t;
                    c0.a = a; c1.a = a; lr.startColor = c0; lr.endColor = c1;
                    if (light != null) { light.intensity = a * fx.lightIntensity; light.radius = fx.lightRadius; }
                    t += Time.deltaTime / duration;
                    yield return null;
                }
                lr.enabled = false;
                if (light != null) { light.intensity = 0f; light.enabled = false; }
                if (s < shots - 1) yield return new WaitForSeconds(Mathf.Max(0.1f, fx.attackSequenceNextDelayMin));
            }
        }

        // ------------------------------------------------------------------ projectiles

        /// <summary>A projectile prefab from muzzle to target at the effect's speed; impact on arrival.</summary>
        private IEnumerator Projectile(WeaponEffect fx, GameObject projectilePrefab, GameObject flashPrefab, Vector3 muzzle, Vector3 target, WeaponEffect launcher)
        {
            Spawn(fx.preFireVFXPrefab ?? launcher?.preFireVFXPrefab, muzzle, target);
            if (fx.preFireDuration > 0f) yield return new WaitForSeconds(fx.preFireDuration);
            Spawn(flashPrefab, muzzle, target);
            if (projectilePrefab == null) { Impact(fx, launcher, target, muzzle); yield break; }
            GameObject proj = Track(Instantiate(projectilePrefab));
            proj.name = projectilePrefab.name; proj.transform.position = muzzle; proj.transform.LookAt(target); proj.SetActive(true);
            foreach (MeshRenderer mr in proj.GetComponentsInChildren<MeshRenderer>(true)) mr.gameObject.SetActive(true);
            foreach (BTLight l in proj.GetComponentsInChildren<BTLight>(true)) l.gameObject.SetActive(true);
            PPCBranch[] branches = proj.GetComponentsInChildren<PPCBranch>(true);
            for (int i = 0; i < branches.Length; i++) { branches[i].gameObject.SetActive(true); branches[i].Init(3.3f + i * 1.7f); }
            PlayAll(proj);
            float speed = (fx.projectileSpeed > 0f ? fx.projectileSpeed : launcher != null && launcher.projectileSpeed > 0f ? launcher.projectileSpeed : 200f) * SpeedMul;
            float dist = Vector3.Distance(muzzle, target), duration = Mathf.Min(4f, dist / speed), t = 0f;
            EffectMods.Apply(proj, Mods, duration);
            while (t < 1f)
            {
                Vector3 p = Vector3.Lerp(muzzle, target, t);
                proj.transform.position = p; TrackPos = p;
                foreach (PPCBranch b in branches) { b.startPos = muzzle; b.endPos = p; }
                t += Time.deltaTime / duration;
                yield return null;
            }
            proj.transform.position = target;
            Impact(fx, launcher, target, muzzle);
            foreach (ParticleSystem ps in proj.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true);
            foreach (MeshRenderer mr in proj.GetComponentsInChildren<MeshRenderer>()) mr.enabled = false;
            foreach (PPCBranch b in branches) b.gameObject.SetActive(false);
            foreach (BTLight l in proj.GetComponentsInChildren<BTLight>()) l.enabled = false;
        }

        private IEnumerator Flight(WeaponEffect fx, GameObject projectilePrefab, GameObject flashPrefab, Vector3 muzzle, Vector3 target, WeaponEffect launcher)
        {
            _pending++;
            yield return Projectile(fx, projectilePrefab, flashPrefab, muzzle, target, launcher);
            _pending--;
        }

        private IEnumerator Ballistic(BallisticEffect fx, Vector3 muzzle, Vector3 target, int shots, int perShot)
        {
            if (fx.bulletPrefab == null) { yield return Projectile(fx, fx.projectilePrefab, fx.muzzleFlashVFXPrefab, muzzle, target, null); yield break; }
            BulletEffect be = fx.bulletPrefab.GetComponent<BulletEffect>();
            for (int s = 0; s < shots; s++)
            {
                for (int p = 0; p < perShot; p++)
                {
                    Vector3 end = Jitter(target, fx.spreadAngle + Spread);
                    WeaponEffect bullet = be != null ? (WeaponEffect)be : fx;
                    StartCoroutine(Flight(bullet, bullet.projectilePrefab ?? fx.projectilePrefab, fx.muzzleFlashVFXPrefab ?? bullet.muzzleFlashVFXPrefab, muzzle, end, fx));
                    if (p < perShot - 1 && fx.burstDelay > 0f) yield return new WaitForSeconds(fx.burstDelay);
                }
                if (s < shots - 1) yield return new WaitForSeconds(fx.shotDelay > 0f ? fx.shotDelay : fx.burstDelay);
            }
        }

        private IEnumerator Burst(BurstBallisticEffect fx, Vector3 muzzle, Vector3 target, int shots)
        {
            for (int s = 0; s < shots; s++)
            {
                StartCoroutine(Flight(fx, fx.accurateProjectilePrefab ?? fx.projectilePrefab, fx.muzzleFlashVFXPrefab, muzzle, Jitter(target, Spread), null));
                if (s < shots - 1) yield return new WaitForSeconds(Mathf.Max(0.15f, fx.impactTime));
            }
        }

        private IEnumerator Lbx(LBXEffect fx, Vector3 muzzle, Vector3 target, int pellets)
        {
            Spawn(fx.muzzleFlashVFXPrefab, muzzle, target);
            for (int i = 0; i < pellets; i++)
            {
                bool accurate = UnityEngine.Random.value < 0.7f;
                GameObject prefab = (accurate ? fx.accurateProjectilePrefab : fx.inaccurateProjectilePrefab) ?? fx.accurateProjectilePrefab ?? fx.projectilePrefab;
                StartCoroutine(Flight(fx, prefab, null, muzzle, Jitter(target, 4f + Spread), null));
            }
            yield return null;
        }

        private IEnumerator Coil(COILEffect fx, Vector3 muzzle, Vector3 target)
        {
            yield return Projectile(fx, fx.projectileMedPrefab ?? fx.projectilePrefab, fx.muzzleFlashMedPrefab ?? fx.muzzleFlashVFXPrefab, muzzle, target, null);
        }

        private IEnumerator Flamer(FlamerEffect fx, Vector3 muzzle, Vector3 target)
        {
            GameObject prefab = fx.standardProjectilePrefab ?? fx.projectilePrefab;
            Spawn(fx.muzzleFlashVFXPrefab, muzzle, target);
            GameObject proj = Spawn(prefab, muzzle, target);
            float duration = fx.projectileSpeed > 0f && fx.projectileSpeed < 10f ? fx.projectileSpeed : 1.2f;
            EffectMods.Apply(proj, Mods, duration);
            yield return new WaitForSeconds(Mathf.Min(fx.impactTime, duration));
            Impact(fx, null, target, muzzle);
            yield return new WaitForSeconds(Mathf.Max(0f, duration - fx.impactTime));
            if (proj != null) foreach (ParticleSystem ps in proj.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true);
        }

        // ------------------------------------------------------------------ missiles

        private IEnumerator Missiles(MissileLauncherEffect fx, Vector3 muzzle, Vector3 target, int shots)
        {
            if (fx.MissilePrefab == null) { yield return Projectile(fx, fx.projectilePrefab, fx.muzzleFlashVFXPrefab, muzzle, target, null); yield break; }
            MissileEffect me = fx.MissilePrefab.GetComponent<MissileEffect>();
            for (int i = 0; i < shots; i++)
            {
                Vector3 end = Jitter(target, 1.5f + Spread);
                StartCoroutine(MissileFlight(fx, me, muzzle, end));
                if (i < shots - 1) yield return new WaitForSeconds(fx.firingInterval > 0f ? fx.firingInterval : 0.08f);
            }
        }

        private IEnumerator MissileFlight(MissileLauncherEffect fx, MissileEffect me, Vector3 muzzle, Vector3 end)
        {
            _pending++;
            WeaponEffect eff = me != null ? (WeaponEffect)me : fx;
            GameObject projPrefab = eff.projectilePrefab ?? fx.projectilePrefab;
            Spawn(eff.muzzleFlashVFXPrefab ?? fx.muzzleFlashVFXPrefab, muzzle, end);
            if (projPrefab == null) { Impact(eff, fx, end, muzzle); _pending--; yield break; }
            GameObject proj = Track(Instantiate(projPrefab));
            proj.name = projPrefab.name; proj.transform.position = muzzle; proj.transform.LookAt(end); proj.SetActive(true);
            foreach (MeshRenderer mr in proj.GetComponentsInChildren<MeshRenderer>(true)) mr.gameObject.SetActive(true);
            PlayAll(proj);
            Vector3[] pts = MissilePath(fx, muzzle, end);
            float speed = (eff.projectileSpeed > 0f ? eff.projectileSpeed : 150f) * SpeedMul;
            float total = PathLength(pts), duration = Mathf.Min(4f, total / speed), t = 0f;
            EffectMods.Apply(proj, Mods, duration);
            Vector3 last = muzzle;
            while (t < 1f)
            {
                Vector3 p = Spline.Point(pts, t);
                proj.transform.position = p; TrackPos = p;
                if ((p - last).sqrMagnitude > 0.0001f) proj.transform.rotation = Quaternion.LookRotation(p - last);
                last = p;
                t += Time.deltaTime / duration;
                yield return null;
            }
            proj.transform.position = end;
            Impact(eff, fx, end, muzzle);
            foreach (ParticleSystem ps in proj.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true);
            foreach (MeshRenderer mr in proj.GetComponentsInChildren<MeshRenderer>()) mr.enabled = false;
            _pending--;
        }

        /// <summary>The game's missile path: a few control points, each lifted by a random amount up to the launcher's curve strength; SRMs stay low, LRMs keep 25 m off the ground, indirect fire climbs high.</summary>
        private Vector3[] MissilePath(MissileLauncherEffect fx, Vector3 start, Vector3 end)
        {
            float force = fx.missileCurveStrength;
            int n = Mathf.Max(2, UnityEngine.Random.Range(2, Mathf.Max(3, fx.missileCurveFrequency)));
            if (force < 0.1f || fx.missileCurveFrequency < 1) { force = 0f; n = 2; }
            if (Indirect) { n = Mathf.Max(n, 9); }
            var pts = new Vector3[n + 1];
            pts[0] = start; pts[n] = end;
            Vector3 axis = end - start;
            float minHeight = fx.isSRM ? 0f : 25f, range = axis.magnitude;
            for (int i = 1; i < n; i++)
            {
                float s = (float)i / n;
                Vector3 a = Vector3.Lerp(start, end, s);
                Vector3 lift = Vector3.up * UnityEngine.Random.Range(-force, force);
                lift = Quaternion.AngleAxis(UnityEngine.Random.Range(0, 360), axis) * lift;
                if (lift.y < 0f) lift.y = 0f;
                Vector3 p = a + lift;
                if (Indirect) p.y += Mathf.Sin(s * Mathf.PI) * Mathf.Max(60f, range * 0.35f);
                if (p.y < minHeight) p.y = minHeight;
                pts[i] = p;
            }
            return pts;
        }

        private static float PathLength(Vector3[] pts)
        {
            float len = 0f; Vector3 prev = pts[0];
            for (int i = 1; i <= 40; i++) { Vector3 p = Spline.Point(pts, i / 40f); len += Vector3.Distance(prev, p); prev = p; }
            return Mathf.Max(1f, len);
        }
    }

    /// <summary>A Catmull-Rom curve through control points, parameterised 0..1 by segment.</summary>
    public static class Spline
    {
        public static Vector3 Point(Vector3[] p, float t)
        {
            int segs = p.Length - 1;
            if (segs < 1) return p[0];
            float f = Mathf.Clamp01(t) * segs;
            int i = Mathf.Min(segs - 1, Mathf.FloorToInt(f));
            float u = f - i;
            Vector3 p0 = p[Mathf.Max(0, i - 1)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(p.Length - 1, i + 2)];
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
        }
    }
}
