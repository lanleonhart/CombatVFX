// What CustomAmmoCategories does to a stock effect when a weapon's JSON carries ProjectileScale, ColorsTable,
// ColorChangeRule and ColorSpeedChange, done the same way here so a recipe previews as the weapon will fire in game:
// the projectile object is scaled (particles in hierarchy mode, trails widened and lengthened), and the beam and trail
// materials get the table's colour written into _ColorBB, changing over the flight by the rule.
using System.Collections.Generic;
using UnityEngine;

namespace GNFX
{
    public static class EffectMods
    {
        public static readonly string[] Rules = { "None", "Linear", "Random", "RandomOnce", "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7" };

        /// <summary>Apply a recipe's CAC knobs to one spawned projectile; flightDuration is the seconds the projectile lives.</summary>
        public static void Apply(GameObject proj, Recipe r, float flightDuration)
        {
            if (proj == null || r == null) return;
            if (r.scale != Vector3.one)
            {
                foreach (ParticleSystem ps in proj.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                proj.transform.localScale = r.scale;
                foreach (TrailRenderer tr in proj.GetComponentsInChildren<TrailRenderer>(true)) { tr.widthMultiplier = r.scale.x; tr.time *= r.scale.y; }
            }
            if (r.colors != null && r.colors.Count > 0 && r.colorRule != "None") proj.AddComponent<ColorDriver>().Init(r, flightDuration);
        }

        /// <summary>A ColorsTable entry as CAC turns it into a colour: the html colour times its intensity, alpha kept.</summary>
        public static Color EntryColor(ColorEntry e)
        {
            Color c = Recipe.Col(e.C);
            float i = e.I > 0f ? e.I : 1f;
            return new Color(c.r * i, c.g * i, c.b * i, c.a);
        }
    }

    public class ColorDriver : MonoBehaviour
    {
        private struct Target { public Material Mat; public ShaderFix.Base Base; public bool Trail; }
        private readonly List<Target> _targets = new List<Target>();
        private readonly List<Color> _table = new List<Color>();
        private string _rule = "None";
        private float _speed, _rate, _t;
        private Color _cur, _next;
        private bool _anim;
        private int _index;

        public void Init(Recipe r, float flightDuration)
        {
            foreach (LineRenderer lr in GetComponentsInChildren<LineRenderer>(true)) Add(lr, false);
            foreach (TrailRenderer tr in GetComponentsInChildren<TrailRenderer>(true)) Add(tr, true);
            foreach (ColorEntry e in r.colors) _table.Add(EffectMods.EntryColor(e));
            _rule = r.colorRule ?? "None"; _speed = r.colorSpeed; _rate = 1f / Mathf.Max(0.05f, flightDuration);
            if (_targets.Count == 0 || _table.Count == 0) return;
            // CAC's PreCacheImpacts
            _anim = _speed > 1e-4f && _table.Count > 1;
            if (_table.Count == 1) { _cur = _table[0]; Set(_cur); _anim = false; }
            else if (_rule == "RandomOnce") { _anim = false; _cur = Next(); Set(_cur); }
            else if (_rule.StartsWith("t")) { int n; int.TryParse(_rule.Substring(1), out n); _anim = false; _index = n % _table.Count; _cur = _table[_index]; Set(_cur); }
            else { _cur = Next(); _next = Next(); Set(_cur); }
        }

        private void Add(Renderer ren, bool trail)
        {
            if (ren == null || ren.sharedMaterial == null) return;
            ShaderFix.Base b = ShaderFix.BaseOf(ren.sharedMaterial);
            if (b == null) return;                      // a built-in shader ignores _ColorBB in the game too
            _targets.Add(new Target { Mat = ren.material, Base = b, Trail = trail });
        }

        private Color Next()
        {
            if (_rule == "Random" || _rule == "RandomOnce") _index = Random.Range(0, _table.Count);
            else _index = (_index + 1) % _table.Count;
            return _table[_index];
        }

        /// <summary>Beams take the colour as is; bullet trails get CAC's brightening (each channel ÷ max × 8 + 1).</summary>
        private void Set(Color c)
        {
            float max = Mathf.Max(c.maxColorComponent, 1e-4f);
            var bright = new Color(c.r / max * 8f + 1f, c.g / max * 8f + 1f, c.b / max * 8f + 1f, 1f);
            foreach (Target t in _targets) ShaderFix.SetBB(t.Mat, t.Base, t.Trail ? bright : c);
        }

        private void Update()
        {
            if (!_anim) return;
            if (_t > 1f) { _cur = _next; _t = 0f; _next = Next(); }
            Set(Color.Lerp(_cur, _next, _t));
            _t += _rate * Time.deltaTime * _speed;
        }
    }
}
