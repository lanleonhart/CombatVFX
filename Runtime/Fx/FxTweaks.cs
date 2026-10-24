// Component tweaks in the game: the recipe's adjustments to the particle systems, lines and lights inside a stock
// effect's pieces, applied to the pooled copy the game spawns (pre-fire, flash, impact) or to the effect's projectile.
// Everything applied is remembered on an FxTweaked marker so the next, untweaked use of the pooled object puts it back.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CombatVFX.Fx
{
    [Serializable]
    public class FxTweak
    {
        public string piece = "flash";
        public string path = "";
        public bool on = true;
        public float size = 1f, life = 1f, speed = 1f, count = 1f, gravity = 1f, width = 1f, intensity = 1f;
        public string color = "";
        public bool IsStock => on && Mathf.Abs(size - 1f) < 1e-4f && Mathf.Abs(life - 1f) < 1e-4f && Mathf.Abs(speed - 1f) < 1e-4f && Mathf.Abs(count - 1f) < 1e-4f && Mathf.Abs(gravity - 1f) < 1e-4f && Mathf.Abs(width - 1f) < 1e-4f && Mathf.Abs(intensity - 1f) < 1e-4f && string.IsNullOrEmpty(color);
    }

    public class FxTweaked : MonoBehaviour
    {
        internal struct Ps { public ParticleSystem S; public float Size, Life, Speed, Grav, Rate, Dist; public int Max; public ParticleSystem.Burst[] Bursts; }
        internal struct Ln { public LineRenderer L; public float W; }
        internal struct Tr { public TrailRenderer T; public float W; }
        internal struct Li { public Light L; public float I; }
        internal struct Bl { public BattleTech.Rendering.BTLight L; public float I; }
        internal readonly List<Ps> P = new List<Ps>(); internal readonly List<Ln> Lines = new List<Ln>(); internal readonly List<Tr> Trails = new List<Tr>(); internal readonly List<Li> Lights = new List<Li>(); internal readonly List<Bl> BtLights = new List<Bl>();
        internal readonly List<GameObject> Hidden = new List<GameObject>();

        internal void Restore()
        {
            foreach (GameObject g in Hidden) if (g != null) g.SetActive(true);
            foreach (Ps p in P) { if (p.S == null) continue; var m = p.S.main; m.startSizeMultiplier = p.Size; m.startLifetimeMultiplier = p.Life; m.startSpeedMultiplier = p.Speed; m.gravityModifierMultiplier = p.Grav; m.maxParticles = p.Max; var e = p.S.emission; e.rateOverTimeMultiplier = p.Rate; e.rateOverDistanceMultiplier = p.Dist; e.SetBursts(p.Bursts); }
            foreach (Ln l in Lines) if (l.L != null) l.L.widthMultiplier = l.W;
            foreach (Tr t in Trails) if (t.T != null) t.T.widthMultiplier = t.W;
            foreach (Li l in Lights) if (l.L != null) l.L.intensity = l.I;
            foreach (Bl b in BtLights) if (b.L != null) b.L.intensity = b.I;
            Destroy(this);
        }
    }

    internal static class FxTweaks
    {
        /// <summary>Set by the weapon-effect prefixes just before the game spawns a piece; consumed by the spawn.</summary>
        internal static List<FxTweak> Pending; internal static string PendingPiece;

        public static void Untweak(GameObject g) { var m = g != null ? g.GetComponent<FxTweaked>() : null; if (m != null) m.Restore(); }

        public static void Apply(GameObject g, string piece, List<FxTweak> tweaks)
        {
            if (g == null || tweaks == null) return;
            Untweak(g);
            FxTweaked mark = null;
            Transform root = g.transform;
            foreach (FxTweak t in tweaks)
            {
                if (t == null || t.piece != piece || t.IsStock) continue;
                Transform target = string.IsNullOrEmpty(t.path) ? root : root.Find(t.path);
                if (target == null) continue;
                if (mark == null) mark = g.AddComponent<FxTweaked>();
                if (!t.on) { mark.Hidden.Add(target.gameObject); target.gameObject.SetActive(false); continue; }
                foreach (ParticleSystem ps in target.GetComponents<ParticleSystem>())
                {
                    var main = ps.main; var em = ps.emission;
                    int n = em.burstCount; var bursts = new ParticleSystem.Burst[n]; em.GetBursts(bursts);
                    mark.P.Add(new FxTweaked.Ps { S = ps, Size = main.startSizeMultiplier, Life = main.startLifetimeMultiplier, Speed = main.startSpeedMultiplier, Grav = main.gravityModifierMultiplier, Max = main.maxParticles, Rate = em.rateOverTimeMultiplier, Dist = em.rateOverDistanceMultiplier, Bursts = (ParticleSystem.Burst[])bursts.Clone() });
                    main.startSizeMultiplier *= t.size; main.startLifetimeMultiplier *= t.life; main.startSpeedMultiplier *= t.speed; main.gravityModifierMultiplier *= t.gravity;
                    if (Mathf.Abs(t.count - 1f) > 1e-4f)
                    {
                        em.rateOverTimeMultiplier *= t.count; em.rateOverDistanceMultiplier *= t.count;
                        for (int i = 0; i < n; i++) { bursts[i].minCount = (short)Mathf.Clamp(Mathf.RoundToInt(bursts[i].minCount * t.count), 0, 10000); bursts[i].maxCount = (short)Mathf.Clamp(Mathf.RoundToInt(bursts[i].maxCount * t.count), 0, 10000); }
                        em.SetBursts(bursts);
                        main.maxParticles = Mathf.Max(main.maxParticles, Mathf.CeilToInt(main.maxParticles * t.count));
                    }
                }
                foreach (LineRenderer l in target.GetComponents<LineRenderer>()) { mark.Lines.Add(new FxTweaked.Ln { L = l, W = l.widthMultiplier }); l.widthMultiplier *= t.width; }
                foreach (TrailRenderer tr in target.GetComponents<TrailRenderer>()) { mark.Trails.Add(new FxTweaked.Tr { T = tr, W = tr.widthMultiplier }); tr.widthMultiplier *= t.width; }
                foreach (Light l in target.GetComponents<Light>()) { mark.Lights.Add(new FxTweaked.Li { L = l, I = l.intensity }); l.intensity *= t.intensity; }
                foreach (BattleTech.Rendering.BTLight l in target.GetComponents<BattleTech.Rendering.BTLight>()) { mark.BtLights.Add(new FxTweaked.Bl { L = l, I = l.intensity }); l.intensity *= t.intensity; }
                if (!string.IsNullOrEmpty(t.color)) FxRecolor.Apply(target.gameObject, FxRecipe.Col(t.color));
            }
        }
    }
}
