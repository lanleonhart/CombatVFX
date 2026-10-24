// Plays one of the game's stock weapon effects on its own, as a layer of a recipe: pre-fire, muzzle flash, the
// projectile from start to end (a laser's beam with its fade curve, a bullet or PPC bolt at its speed, a missile on an
// arc) and the impact, all from the game's pooled prefabs, with a scale, a colour and a speed of the layer's own. The
// game's WeaponEffect itself needs a weapon and a hit to run, so this drives the same prefabs the way the Effects
// Studio does.
using System.Collections;
using System.Collections.Generic;
using BattleTech;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CombatVFX.Fx
{
    public class FxStockShot : MonoBehaviour
    {
        private readonly List<KeyValuePair<string, GameObject>> _pooled = new List<KeyValuePair<string, GameObject>>();

        public static FxStockShot Play(string effectId, Vector3 start, Vector3 end, Vector3 scale, Color? tint, float speedMul, bool prefire, bool flash, bool impact, int shots)
        {
            var go = new GameObject("CombatVFX_shot_" + effectId);
            var s = go.AddComponent<FxStockShot>();
            s.StartCoroutine(s.Run(effectId, start, end, scale, tint, speedMul, prefire, flash, impact, Mathf.Max(1, shots)));
            return s;
        }

        private GameObject Spawn(string id, Vector3 pos, Vector3 lookAt, Color? tint, Vector3? scale)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dm = UnityGameInstance.BattleTechGame?.DataManager;
            if (dm == null) return null;
            GameObject g = null;
            try { g = dm.PooledInstantiate(id, BattleTechResourceType.Prefab); } catch { }
            if (g == null) { FxLibrary.RequestPrefab(id); return null; }
            _pooled.Add(new KeyValuePair<string, GameObject>(id, g));
            g.transform.position = pos;
            if ((lookAt - pos).sqrMagnitude > 0.001f) g.transform.rotation = Quaternion.LookRotation(lookAt - pos);
            if (scale.HasValue && scale.Value != Vector3.one)
            {
                foreach (ParticleSystem ps in g.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                g.transform.localScale = scale.Value;
                foreach (TrailRenderer tr in g.GetComponentsInChildren<TrailRenderer>(true)) { tr.widthMultiplier = scale.Value.x; }
            }
            g.SetActive(true);
            if (tint.HasValue) FxRecolor.Apply(g, tint.Value);
            foreach (ParticleSystem ps in g.GetComponentsInChildren<ParticleSystem>(true)) { ps.Stop(true); ps.Clear(true); ps.Play(true); }
            return g;
        }

        private IEnumerator Run(string effectId, Vector3 start, Vector3 end, Vector3 scale, Color? tint, float speedMul, bool prefire, bool flash, bool impact, int shots)
        {
            // read the effect's own numbers and pieces off its prefab
            string projectile = null, prefireName = null, flashName = null, impactBase = null; string[] impactVars = null;
            float preDur = 0f, speed = 200f; bool laser = false, missile = false; AnimationCurve anim = null; float lightIntensity = 0f, lightRadius = 0f;
            FxLibrary.WithEffect(effectId, (fx, sub) =>
            {
                WeaponEffect p = sub != null && sub.projectilePrefab != null ? sub : fx;
                projectile = p.projectilePrefab != null ? p.projectilePrefab.name : null;
                prefireName = (fx.preFireVFXPrefab ?? sub?.preFireVFXPrefab)?.name; flashName = (fx.muzzleFlashVFXPrefab ?? sub?.muzzleFlashVFXPrefab)?.name;
                impactBase = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXBase : sub?.impactVFXBase; impactVars = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXVariations : sub?.impactVFXVariations;
                preDur = fx.preFireDuration; speed = p.projectileSpeed > 0f ? p.projectileSpeed : fx.projectileSpeed;
                var le = fx as LaserEffect; if (le != null) { laser = true; anim = le.laserAnim; lightIntensity = le.lightIntensity; lightRadius = le.lightRadius; }
                missile = fx is MissileLauncherEffect;
            });
            if (projectile == null && flashName == null) { Destroy(gameObject, 0.1f); yield break; }
            Vector3 dir = (end - start).normalized; float range = Vector3.Distance(start, end);
            if (prefire && prefireName != null) Spawn(prefireName, start, end, tint, null);
            if (preDur > 0f) yield return new WaitForSeconds(preDur);
            for (int shot = 0; shot < shots; shot++)
            {
                if (flash && flashName != null) Spawn(flashName, start, end, tint, null);
                Vector3 target = shots > 1 ? end + Random.insideUnitSphere * 1.5f : end;
                GameObject proj = projectile != null ? Spawn(projectile, start, target, tint, scale) : null;
                if (proj != null)
                {
                    if (laser)
                    {
                        var lr = proj.GetComponent<LineRenderer>() ?? proj.GetComponentInChildren<LineRenderer>(true);
                        var light = proj.GetComponentInChildren<BattleTech.Rendering.BTLight>(true);
                        if (light != null) { light.gameObject.SetActive(true); light.transform.position = (start + target) * 0.5f; }
                        if (impact && impactBase != null) Spawn(Impact(impactBase, impactVars), target, start, tint, null);
                        float dur = speed > 0f ? speed / Mathf.Max(0.1f, speedMul) : 1f, t = 0f;        // for lasers the game keeps seconds in projectileSpeed
                        Color c0 = lr != null ? lr.startColor : Color.white, c1 = lr != null ? lr.endColor : Color.white;
                        while (t < 1f)
                        {
                            if (lr != null) { lr.useWorldSpace = true; lr.positionCount = 2; lr.SetPosition(0, start); lr.SetPosition(1, target); float a = anim != null ? anim.Evaluate(t) : 1f - t; Color a0 = c0, a1 = c1; a0.a = a; a1.a = a; lr.startColor = a0; lr.endColor = a1; if (light != null) light.intensity = lightIntensity * a; }
                            t += Time.deltaTime / dur; yield return null;
                        }
                        if (lr != null) lr.enabled = false;
                        if (light != null) light.enabled = false;
                    }
                    else
                    {
                        float sp = Mathf.Max(10f, speed * speedMul), dur = Mathf.Min(4f, range / sp), t = 0f;
                        Vector3 mid = (start + target) * 0.5f + Vector3.up * (missile ? Mathf.Min(40f, range * 0.25f) : 0f);
                        Vector3 last = start;
                        while (t < 1f)
                        {
                            Vector3 p = missile ? Vector3.Lerp(Vector3.Lerp(start, mid, t), Vector3.Lerp(mid, target, t), t) : Vector3.Lerp(start, target, t);
                            proj.transform.position = p;
                            if ((p - last).sqrMagnitude > 0.0001f) proj.transform.rotation = Quaternion.LookRotation(p - last);
                            last = p; t += Time.deltaTime / dur; yield return null;
                        }
                        foreach (ParticleSystem ps in proj.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true);
                        foreach (Renderer r in proj.GetComponentsInChildren<MeshRenderer>()) r.enabled = false;
                        if (impact && impactBase != null) Spawn(Impact(impactBase, impactVars), target, start, tint, null);
                    }
                }
                else if (impact && impactBase != null) Spawn(Impact(impactBase, impactVars), target, start, tint, null);
                if (shot < shots - 1) yield return new WaitForSeconds(0.12f);
            }
            yield return new WaitForSeconds(4f);
            Destroy(gameObject);
        }

        private static string Impact(string b, string[] vars) => vars != null && vars.Length > 0 ? b + "_" + vars[Random.Range(0, vars.Length)] : b;

        private void OnDestroy()
        {
            var dm = UnityGameInstance.BattleTechGame?.DataManager;
            foreach (var kv in _pooled)
            {
                if (kv.Value == null) continue;
                FxRecolor.Untint(kv.Value);
                kv.Value.transform.localScale = Vector3.one;
                kv.Value.SetActive(false);
                try { if (dm != null) dm.PoolGameObject(kv.Key, kv.Value); else Destroy(kv.Value); } catch { Destroy(kv.Value); }
            }
        }
    }
}
