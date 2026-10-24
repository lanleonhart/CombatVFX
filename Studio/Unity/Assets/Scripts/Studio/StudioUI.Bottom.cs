// Bottom of the window: the timeline strip (every layer as a block from its start to its end, the impact moment, where
// the game would play its sounds, the current time, and a scrub bar) and the stage row (range, the two 'Mechs, camera).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GNFX
{
    public partial class StudioUI
    {
        private int _dragLayer = -1; private float _dragOffset; private bool _scrubbing;

        private void Timeline(Rect r)
        {
            float x0 = r.x + 118f, x1 = r.xMax - 16f, width = x1 - x0;
            bool recipeMode = _mode == 1 || _mode == 2;
            float pre = recipeMode ? Pre() : 0f;
            float length = Mathf.Max(0.5f, !recipeMode ? Playback.Length : Mathf.Max(_recipe.Length(Rig.Range, pre), Playback.Now + 0.2f));
            float pps = width / length;
            GUI.Label(new Rect(r.x + 10, r.y + 6, 100, 18), "TIMELINE", _cap);
            GUI.Label(new Rect(r.x + 10, r.y + 24, 104, 18), Playback.Now.ToString("0.00") + " / " + length.ToString("0.0") + " s", _small);
            GUI.Label(new Rect(r.x + 10, r.y + 42, 104, 18), "drag to scrub · blocks to move", _small);
            // ruler
            float rulerY = r.y + 8f;
            for (float s = 0f; s <= length + 0.001f; s += length > 4f ? 1f : 0.5f)
            {
                float x = x0 + s * pps;
                GUI.DrawTexture(new Rect(x, rulerY, 1, 6), _line);
                GUI.Label(new Rect(x + 2, rulerY - 4, 40, 14), s.ToString("0.#"), _small);
            }
            GUI.DrawTexture(new Rect(x0, rulerY + 6, width, 1), _line);
            Event e = Event.current;
            // blocks
            float impact = 0f, end;
            List<Recipe.Item> items = !recipeMode ? new List<Recipe.Item>() : _recipe.Timeline(Rig.Range, pre, out impact, out end);
            float blockY = r.y + 20f, blockH = 14f;
            if (pre > 0f) { GUI.DrawTexture(new Rect(x0, r.y + 16, pre * pps, r.height - 22), Solid(new Color(1f, 1f, 1f, 0.05f))); GUI.Label(new Rect(x0 + 2, r.yMax - 18, 80, 14), "pre-fire", _small); }
            if (recipeMode && _recipe.HasBase)
            {
                float flight = Mathf.Min(1.5f, 0.3f + Rig.Range / 300f);
                var br = new Rect(x0 + pre * pps, blockY, flight * pps, blockH);
                GUI.DrawTexture(br, Solid(new Color(0.5f, 0.5f, 0.45f, 0.5f)));
                GUI.Label(new Rect(br.x + 4, br.y - 1, 240, 16), "base: " + _recipe.baseEffect.Replace("WeaponEffect-Weapon_", ""), _small);
                GUI.Label(new Rect(x0 - 6, blockY + blockH - 2, 20, 14), "♪", _small);     // the game's pre-fire sound
                GUI.Label(new Rect(br.xMax - 6, blockY + blockH - 2, 20, 14), "♪", _small);
                blockY += blockH + 4f;
            }
            else if (!recipeMode)
            {
                var br = new Rect(x0, blockY, Mathf.Min(width, (_mode == 3 ? Jets.Recipe.burst : 1.6f) * pps), blockH);
                GUI.DrawTexture(br, Solid(new Color(0.5f, 0.5f, 0.45f, 0.5f)));
                GUI.Label(new Rect(br.x + 4, br.y - 1, 300, 16), _mode == 3 ? "jump burst · " + Jets.Recipe.name : (_effect ?? "").Replace("WeaponEffect-", ""), _small);
            }
            int lanes = Mathf.Max(1, items.Count);
            float laneH = Mathf.Clamp((r.yMax - 6f - blockY) / lanes, 6f, blockH);
            for (int i = 0; i < _recipe.layers.Count && recipeMode; i++)
            {
                Layer l = _recipe.layers[i];
                int idx = items.FindIndex(x => x.L == l);
                if (idx < 0) continue;
                Recipe.Item it = items[idx];
                var br = new Rect(x0 + it.Start * pps, blockY + idx * laneH, Mathf.Max(3f, (it.End - it.Start) * pps), laneH - 1f);
                Color c = KindColor(l.kind); c.a = i == _layer ? 1f : 0.7f;
                GUI.DrawTexture(br, Solid(c));
                if (laneH >= 10f) GUI.Label(new Rect(br.x + 3, br.y - 2, 260, 14), LayerLabel(l), new GUIStyle(_small) { normal = { textColor = Color.black }, fontSize = 10, clipping = TextClipping.Clip });
                // drag to move the start
                if (e.type == EventType.MouseDown && br.Contains(e.mousePosition)) { _dragLayer = i; _dragOffset = e.mousePosition.x - br.x; _layer = i; _tab = 0; e.Use(); }
            }
            if (_dragLayer >= 0 && _dragLayer < _recipe.layers.Count)
            {
                if (e.type == EventType.MouseDrag)
                {
                    Layer l = _recipe.layers[_dragLayer];
                    float anchored = items.Find(it => it.L == l).Start - l.start;      // the pre-fire shift and the impact wait
                    l.start = Mathf.Max(0f, (float)Math.Round((e.mousePosition.x - _dragOffset - x0) / pps - anchored, 2));
                    e.Use();
                }
                if (e.type == EventType.MouseUp) { _dragLayer = -1; Fire(); e.Use(); }
            }
            // impact and now markers
            if (recipeMode && impact > 0f)
            {
                float x = x0 + impact * pps;
                GUI.DrawTexture(new Rect(x, r.y + 6, 1, r.height - 12), Solid(new Color(1f, 0.4f, 0.3f, 0.9f)));
                GUI.Label(new Rect(x + 3, r.yMax - 18, 60, 14), "impact", new GUIStyle(_small) { normal = { textColor = new Color(1f, 0.5f, 0.4f) } });
            }
            float nowX = x0 + Mathf.Min(length, Playback.Now) * pps;
            GUI.DrawTexture(new Rect(nowX, r.y + 4, 2, r.height - 8), _gold);
            // scrub on the ruler band or anywhere not on a block
            var scrubRect = new Rect(x0, r.y, width, r.height);
            if (e.type == EventType.MouseDown && scrubRect.Contains(e.mousePosition) && _dragLayer < 0) { _scrubbing = true; Seek((e.mousePosition.x - x0) / pps); e.Use(); }
            if (_scrubbing && e.type == EventType.MouseDrag) { Seek((e.mousePosition.x - x0) / pps); e.Use(); }
            if (_scrubbing && e.type == EventType.MouseUp) { _scrubbing = false; e.Use(); }
        }

        /// <summary>Jump the shot to a moment: forward runs fast to it; backward re-fires first.</summary>
        private void Seek(float t)
        {
            t = Mathf.Max(0f, t);
            if (t < Playback.Now - 0.01f || !AnyPlaying) { Fire(); }
            Playback.SeekTo = t;
        }

        private void Bottom(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Range", _small, GUILayout.Width(44));
            float range = GUILayout.HorizontalSlider(Rig.Range, 30f, 900f, GUILayout.Width(140));
            GUILayout.Label(Mathf.RoundToInt(range) + " m", _small, GUILayout.Width(48));
            if (Mathf.Abs(range - Rig.Range) > 0.5f) { Rig.Range = Mathf.Round(range / 10f) * 10f; Rig.PlaceMechs(Rig.ShooterId, Rig.TargetId); Rig.Frame(Rig.CameraMode == "free" ? "shoulder" : Rig.CameraMode); }
            GUILayout.Space(10);
            if (GUILayout.Button("Shooter: " + Short(Rig.ShooterId), _pickShooter ? _btnOn : _btn, GUILayout.Width(200))) { _pickShooter = !_pickShooter; _pickTarget = false; }
            if (GUILayout.Button("Target: " + Short(Rig.TargetId), _pickTarget ? _btnOn : _btn, GUILayout.Width(200))) { _pickTarget = !_pickTarget; _pickShooter = false; }
            GUILayout.Space(10);
            GUILayout.Label("Camera", _small, GUILayout.Width(50));
            for (int i = 0; i < SceneRig.CameraModes.Length; i++)
                if (GUILayout.Button(SceneRig.CameraNames[i], Rig.CameraMode == SceneRig.CameraModes[i] ? _btnOn : _btnSmall)) Rig.Frame(SceneRig.CameraModes[i]);
            GUILayout.FlexibleSpace();
            GUILayout.Label("drag to orbit · wheel to zoom · space fires · P pauses · . steps · Ctrl+Z undo", _small);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            if (_pickShooter || _pickTarget) MechPicker(new Rect(r.x + 260f, r.y - TL - 330f, 420f, 320f));
        }

        private static string Short(string id) => string.IsNullOrEmpty(id) ? "—" : id.Replace("chrPrfMech_", "").Replace("chrprfmech_", "").Replace("Base-001", "").Replace("base-001", "");

        private void MechPicker(Rect r)
        {
            GUI.Box(r, GUIContent.none, _box);
            MouseOverPanel = MouseOverPanel || r.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
            GUILayout.Label((_pickShooter ? "SHOOTER" : "TARGET") + " · " + _mechs.Count + " 'Mechs from the game and its mods", _cap);
            _mechFilter = GUILayout.TextField(_mechFilter, _field);
            _sm = GUILayout.BeginScrollView(_sm);
            foreach (string id in _mechs)
            {
                if (!string.IsNullOrEmpty(_mechFilter) && id.IndexOf(_mechFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button(Short(id), _btn))
                {
                    if (_pickShooter) Rig.PlaceMechs(id, Rig.TargetId); else Rig.PlaceMechs(Rig.ShooterId, id);
                    ApplyHint();
                    if (_mode == 3) { Jets.Attach(Rig.Shooter, Rig.ShooterId); FocusEmitter(); }
                    Rig.Frame(Rig.CameraMode == "free" ? "shoulder" : Rig.CameraMode); _pickShooter = _pickTarget = false;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
