// The components inside a stock effect's pieces (the particle systems, line renderers and lights of its pre-fire,
// muzzle flash, projectile and impact prefabs), listed for the editor and adjusted on a spawned copy: on or off,
// size, lifetime, speed, count, gravity, width and light multipliers, and a colour of their own.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GNFX
{
    [Serializable]
    public class ComponentTweak
    {
        public string piece = "flash";      // prefire | flash | projectile | impact
        public string path = "";            // the component's object path inside the piece, "" for the root
        public bool on = true;
        public float size = 1f, life = 1f, speed = 1f, count = 1f, gravity = 1f, width = 1f, intensity = 1f;
        public string color = "";           // "" = stock

        public bool IsStock => on && Mathf.Abs(size - 1f) < 1e-4f && Mathf.Abs(life - 1f) < 1e-4f && Mathf.Abs(speed - 1f) < 1e-4f && Mathf.Abs(count - 1f) < 1e-4f && Mathf.Abs(gravity - 1f) < 1e-4f && Mathf.Abs(width - 1f) < 1e-4f && Mathf.Abs(intensity - 1f) < 1e-4f && string.IsNullOrEmpty(color);
    }

    public static class ComponentTweaks
    {
        public class Found { public string Path, Type, Summary; public bool Particle, Line, Light; }

        public static string PathOf(Transform t, Transform root)
        {
            if (t == root) return "";
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null && p != root; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        private static Transform Find(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            return root.Find(path);
        }

        /// <summary>Everything adjustable inside a prefab, with a one-line description of what it is now.</summary>
        public static List<Found> List(GameObject prefab)
        {
            var list = new List<Found>();
            if (prefab == null) return list;
            Transform root = prefab.transform;
            foreach (ParticleSystem ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main; var em = ps.emission;
                float count = em.rateOverTime.constantMax; int bursts = em.burstCount; float burstN = 0f;
                for (int i = 0; i < bursts; i++) { var b = em.GetBurst(i); burstN += b.maxCount; }
                list.Add(new Found { Path = PathOf(ps.transform, root), Type = "Particles", Particle = true,
                    Summary = (burstN > 0 ? burstN.ToString("0") + " burst" : count.ToString("0") + "/s") + " · size " + main.startSize.constantMax.ToString("0.##") + " · " + main.startLifetime.constantMax.ToString("0.##") + " s" + (main.startSpeed.constantMax > 0.01f ? " · " + main.startSpeed.constantMax.ToString("0.#") + " m/s" : "") + (ps.gameObject.activeSelf ? "" : " · off in the prefab") });
            }
            foreach (LineRenderer lr in prefab.GetComponentsInChildren<LineRenderer>(true))
                list.Add(new Found { Path = PathOf(lr.transform, root), Type = "Line", Line = true, Summary = "width " + lr.widthMultiplier.ToString("0.##") + " m" });
            foreach (TrailRenderer tr in prefab.GetComponentsInChildren<TrailRenderer>(true))
                list.Add(new Found { Path = PathOf(tr.transform, root), Type = "Trail", Line = true, Summary = "width " + tr.widthMultiplier.ToString("0.##") + " · " + tr.time.ToString("0.##") + " s" });
            foreach (Light l in prefab.GetComponentsInChildren<Light>(true))
                list.Add(new Found { Path = PathOf(l.transform, root), Type = "Light", Light = true, Summary = "intensity " + l.intensity.ToString("0.#") + " · range " + l.range.ToString("0") });
            foreach (BattleTech.Rendering.BTLight l in prefab.GetComponentsInChildren<BattleTech.Rendering.BTLight>(true))
                list.Add(new Found { Path = PathOf(l.transform, root), Type = "Game light", Light = true, Summary = "intensity " + l.intensity.ToString("0") + " · radius " + l.radius.ToString("0") });
            return list;
        }

        /// <summary>Apply the tweaks for one piece to a freshly spawned copy of it.</summary>
        public static void Apply(GameObject spawned, string piece, List<ComponentTweak> tweaks)
        {
            if (spawned == null || tweaks == null) return;
            Transform root = spawned.transform;
            foreach (ComponentTweak t in tweaks)
            {
                if (t == null || t.piece != piece || t.IsStock) continue;
                Transform target = Find(root, t.path);
                if (target == null) continue;
                if (!t.on) { target.gameObject.SetActive(false); continue; }
                Color? col = string.IsNullOrEmpty(t.color) ? (Color?)null : Recipe.Col(t.color);
                foreach (ParticleSystem ps in target.GetComponents<ParticleSystem>())
                {
                    var main = ps.main;
                    if (Mathf.Abs(t.size - 1f) > 1e-4f) main.startSizeMultiplier *= t.size;
                    if (Mathf.Abs(t.life - 1f) > 1e-4f) main.startLifetimeMultiplier *= t.life;
                    if (Mathf.Abs(t.speed - 1f) > 1e-4f) main.startSpeedMultiplier *= t.speed;
                    if (Mathf.Abs(t.gravity - 1f) > 1e-4f) main.gravityModifierMultiplier *= t.gravity;
                    if (Mathf.Abs(t.count - 1f) > 1e-4f)
                    {
                        var em = ps.emission; em.rateOverTimeMultiplier *= t.count; em.rateOverDistanceMultiplier *= t.count;
                        int n = em.burstCount; var bursts = new ParticleSystem.Burst[n]; em.GetBursts(bursts);
                        for (int i = 0; i < n; i++) { bursts[i].minCount = (short)Mathf.Clamp(Mathf.RoundToInt(bursts[i].minCount * t.count), 0, 10000); bursts[i].maxCount = (short)Mathf.Clamp(Mathf.RoundToInt(bursts[i].maxCount * t.count), 0, 10000); }
                        em.SetBursts(bursts);
                        main.maxParticles = Mathf.Max(main.maxParticles, Mathf.CeilToInt(main.maxParticles * t.count));
                    }
                }
                foreach (LineRenderer lr in target.GetComponents<LineRenderer>()) lr.widthMultiplier *= t.width;
                foreach (TrailRenderer tr in target.GetComponents<TrailRenderer>()) tr.widthMultiplier *= t.width;
                foreach (Light l in target.GetComponents<Light>()) l.intensity *= t.intensity;
                foreach (BattleTech.Rendering.BTLight l in target.GetComponents<BattleTech.Rendering.BTLight>()) l.intensity *= t.intensity;
                if (col.HasValue) Recolor.Apply(target.gameObject, col.Value);
            }
        }
    }
}
