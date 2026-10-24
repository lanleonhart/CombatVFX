// Recolours one of the game's spawned VFX objects (a pre-fire charge, a muzzle flash, an impact) to a target hue while
// keeping each material's and particle's own brightness. The game pools these objects and hands them to other weapons
// later, so nothing is written into shared materials: renderers get a MaterialPropertyBlock, particles and lights are
// changed on the instance, and an FxTinted marker remembers the originals so the next, untinted use puts them back.
using System.Collections.Generic;
using UnityEngine;

namespace CombatVFX.Fx
{
    public class FxTinted : MonoBehaviour
    {
        internal struct Ps { public ParticleSystem System; public ParticleSystem.MinMaxGradient Start, Over, Trail; public bool HadOver, HadTrail; }
        internal struct Li { public Light Light; public Color Color; }
        internal struct Bl { public BattleTech.Rendering.BTLight Light; public Color Color; }
        internal readonly List<Ps> Particles = new List<Ps>();
        internal readonly List<Li> Lights = new List<Li>();
        internal readonly List<Bl> BtLights = new List<Bl>();
        internal readonly List<Renderer> Renderers = new List<Renderer>();

        /// <summary>Put the object back the way the game made it.</summary>
        internal void Restore()
        {
            foreach (Renderer r in Renderers) if (r != null) r.SetPropertyBlock(null);
            foreach (Ps p in Particles)
            {
                if (p.System == null) continue;
                var main = p.System.main; main.startColor = p.Start;
                if (p.HadOver) { var col = p.System.colorOverLifetime; col.color = p.Over; }
                if (p.HadTrail) { var tr = p.System.trails; tr.colorOverLifetime = p.Trail; }
            }
            foreach (Li l in Lights) if (l.Light != null) l.Light.color = l.Color;
            foreach (Bl b in BtLights) if (b.Light != null) b.Light.lightColor = b.Color;
            Destroy(this);
        }
    }

    internal static class FxRecolor
    {
        /// <summary>Set by the weapon-effect prefixes just before the game spawns a pre-fire, flash or impact; consumed by the spawn.</summary>
        internal static Color? Pending;

        public static Color Shift(Color old, Color target)
        {
            float max = Mathf.Max(old.r, Mathf.Max(old.g, old.b));
            if (max < 1e-4f) return old;
            float min = Mathf.Min(old.r, Mathf.Min(old.g, old.b));
            if ((max - min) / max < 0.15f) return old;
            float tmax = Mathf.Max(target.r, Mathf.Max(target.g, target.b));
            if (tmax < 1e-4f) return old;
            return new Color(target.r / tmax * max, target.g / tmax * max, target.b / tmax * max, old.a);
        }

        /// <summary>Tint a spawned object; an earlier tint on the same pooled object is undone first.</summary>
        public static void Apply(GameObject g, Color target)
        {
            if (g == null) return;
            Untint(g);
            var mark = g.AddComponent<FxTinted>();
            var mpb = new MaterialPropertyBlock();
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
            {
                Material m = r.sharedMaterial;
                if (m == null) continue;
                bool any = false;
                mpb.Clear();
                if (m.HasProperty("_ColorBB")) { Color bb = m.GetColor("_ColorBB"); if (bb.maxColorComponent > 0.001f) { mpb.SetColor("_ColorBB", Shift(bb, target)); any = true; } }
                if (!any && m.HasProperty("_TintColor")) { mpb.SetColor("_TintColor", Shift(m.GetColor("_TintColor"), target)); any = true; }
                if (any) { r.SetPropertyBlock(mpb); mark.Renderers.Add(r); }
            }
            foreach (ParticleSystem ps in g.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main; var col = ps.colorOverLifetime; var tr = ps.trails;
                var rec = new FxTinted.Ps { System = ps, Start = main.startColor, Over = col.color, Trail = tr.colorOverLifetime, HadOver = col.enabled, HadTrail = tr.enabled };
                mark.Particles.Add(rec);
                main.startColor = ShiftGradient(rec.Start, target);
                if (col.enabled) col.color = ShiftGradient(rec.Over, target);
                if (tr.enabled) tr.colorOverLifetime = ShiftGradient(rec.Trail, target);
            }
            foreach (Light l in g.GetComponentsInChildren<Light>(true)) { mark.Lights.Add(new FxTinted.Li { Light = l, Color = l.color }); l.color = Shift(l.color, target); }
            foreach (BattleTech.Rendering.BTLight bl in g.GetComponentsInChildren<BattleTech.Rendering.BTLight>(true)) { mark.BtLights.Add(new FxTinted.Bl { Light = bl, Color = bl.lightColor }); bl.lightColor = Shift(bl.lightColor, target); }
        }

        /// <summary>Undo a tint left on a pooled object from an earlier shot.</summary>
        public static void Untint(GameObject g)
        {
            if (g == null) return;
            foreach (FxTinted mark in g.GetComponentsInChildren<FxTinted>(true)) mark.Restore();
        }

        public static ParticleSystem.MinMaxGradient ShiftGradient(ParticleSystem.MinMaxGradient g, Color target)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Shift(g.color, target));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Shift(g.colorMin, target), Shift(g.colorMax, target));
                case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(ShiftKeys(g.gradient, target));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(ShiftKeys(g.gradientMin, target), ShiftKeys(g.gradientMax, target));
            }
            return g;
        }

        private static Gradient ShiftKeys(Gradient src, Color target)
        {
            if (src == null) return null;
            var keys = src.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Shift(keys[i].color, target);
            var g = new Gradient(); g.SetKeys(keys, src.alphaKeys); g.mode = src.mode;
            return g;
        }
    }
}
