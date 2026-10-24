// The game's effects are authored in HDR (a laser's _ColorBB is 5, an AC round's is several hundred) and rely on the
// game's bloom to turn that into a glow. This is a small bloom of the same shape: threshold with a soft knee, a blur
// pyramid, and an additive composite, so the stock effects read in the studio the way they do in a mission.
using System.Collections.Generic;
using UnityEngine;

namespace GNFX
{
    [RequireComponent(typeof(Camera))]
    public class Bloom : MonoBehaviour
    {
        public static bool On = true;
        public static float Threshold = 1.0f, Intensity = 0.6f;
        public static int Levels = 5;
        private Material _m;

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (!On) { Graphics.Blit(src, dst); return; }
            if (_m == null)
            {
                Shader s = Shader.Find("GNFX/Bloom");
                if (s == null) { Graphics.Blit(src, dst); return; }
                _m = new Material(s);
            }
            _m.SetFloat("_Threshold", Threshold);
            var fmt = RenderTextureFormat.ARGBHalf;
            int w = Mathf.Max(1, src.width / 2), h = Mathf.Max(1, src.height / 2);
            RenderTexture bright = RenderTexture.GetTemporary(w, h, 0, fmt);
            Graphics.Blit(src, bright, _m, 0);
            var levels = new List<RenderTexture>();
            RenderTexture cur = bright;
            for (int i = 0; i < Levels; i++)
            {
                w = Mathf.Max(1, w / 2); h = Mathf.Max(1, h / 2);
                RenderTexture a = RenderTexture.GetTemporary(w, h, 0, fmt), b = RenderTexture.GetTemporary(w, h, 0, fmt);
                _m.SetVector("_Offset", new Vector4(1f / w, 0f, 0f, 0f)); Graphics.Blit(cur, a, _m, 1);
                _m.SetVector("_Offset", new Vector4(0f, 1f / h, 0f, 0f)); Graphics.Blit(a, b, _m, 1);
                RenderTexture.ReleaseTemporary(a);
                levels.Add(b); cur = b;
            }
            RenderTexture.ReleaseTemporary(bright);
            // add the levels up from the smallest, so every level contributes at its own blur radius
            RenderTexture sum = levels[levels.Count - 1];
            _m.SetFloat("_Intensity", 1f);
            for (int i = levels.Count - 2; i >= 0; i--)
            {
                RenderTexture t = RenderTexture.GetTemporary(levels[i].width, levels[i].height, 0, fmt);
                _m.SetTexture("_Bloom", sum); Graphics.Blit(levels[i], t, _m, 2);
                if (sum != levels[levels.Count - 1]) RenderTexture.ReleaseTemporary(sum);
                sum = t;
            }
            _m.SetFloat("_Intensity", Intensity / Mathf.Max(1, levels.Count));
            _m.SetTexture("_Bloom", sum); Graphics.Blit(src, dst, _m, 2);
            if (sum != levels[levels.Count - 1]) RenderTexture.ReleaseTemporary(sum);
            foreach (RenderTexture l in levels) RenderTexture.ReleaseTemporary(l);
        }
    }
}
