// Recolours one of the game's spawned VFX objects (a pre-fire charge, a muzzle flash, an impact) to a target hue while
// keeping each material's and particle's own brightness: the hue moves, the energy stays. Greys and whites are left
// alone, so a white-hot core stays white. Works on the studio's swapped materials through ShaderFix's remembered bases.
using UnityEngine;

namespace GNFX
{
    public static class Recolor
    {
        /// <summary>The old colour's magnitude with the target's hue; greys (little saturation) come back unchanged.</summary>
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

        public static void Apply(GameObject g, Color target)
        {
            if (g == null) return;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = r.sharedMaterials;
                Material[] inst = r.materials;                     // per-instance copies; the spawned object is destroyed with them
                for (int i = 0; i < inst.Length && i < shared.Length; i++)
                {
                    if (inst[i] == null) continue;
                    ShaderFix.Base b = ShaderFix.BaseOf(shared[i]);
                    if (b != null) { ShaderFix.SetBB(inst[i], b, Shift(b.BB, target)); if (b.Mult == false && b.BB.maxColorComponent < 0.01f) Tint(inst[i], b.TintProp, target); }
                    else if (inst[i].HasProperty("_TintColor")) inst[i].SetColor("_TintColor", Shift(inst[i].GetColor("_TintColor"), target));
                    else if (inst[i].HasProperty("_Color")) inst[i].SetColor("_Color", Shift(inst[i].GetColor("_Color"), target));
                }
            }
            foreach (ParticleSystem ps in g.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main; main.startColor = ShiftGradient(main.startColor, target);
                var col = ps.colorOverLifetime; if (col.enabled) col.color = ShiftGradient(col.color, target);
                var trails = ps.trails; if (trails.enabled) trails.colorOverLifetime = ShiftGradient(trails.colorOverLifetime, target);
            }
            foreach (Light l in g.GetComponentsInChildren<Light>(true)) l.color = Shift(l.color, target);
            foreach (BattleTech.Rendering.BTLight bl in g.GetComponentsInChildren<BattleTech.Rendering.BTLight>(true)) bl.lightColor = Shift(bl.lightColor, target);
        }

        private static void Tint(Material m, string prop, Color target)
        {
            if (!m.HasProperty(prop)) return;
            Color c = m.GetColor(prop);
            m.SetColor(prop, Shift(c, target));
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
