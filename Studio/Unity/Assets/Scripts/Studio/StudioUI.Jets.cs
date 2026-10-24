// Jets mode: the loaded 'Mech with its stock jump-jet exhausts shown or removed, and one GN particle emitter placed on
// a bone of the model with offset and direction controls, idle stream and jump burst, saved as a jets recipe.
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GNFX
{
    public partial class StudioUI
    {
        public JetsPlayer Jets;
        private System.Collections.Generic.List<string> _jetFiles = new System.Collections.Generic.List<string>();
        private string _jetsPath; private bool _pickBone; private string _boneFilter = "";
        private string JetsDir => Path.Combine(RecipesDir ?? ".", "jets");

        private void RefreshJetFiles()
        {
            _jetFiles.Clear();
            try { if (Directory.Exists(JetsDir)) _jetFiles.AddRange(Directory.GetFiles(JetsDir, "*.json").OrderBy(f => f)); } catch { }
        }

        private void EnterJets()
        {
            _mode = 3;
            RefreshJetFiles();
            if (Jets.Mech != Rig.Shooter) Jets.Attach(Rig.Shooter, Rig.ShooterId);
            Rig.Follow = null;
            FocusEmitter();
        }

        private void FocusEmitter()
        {
            if (Jets.Emitter != null) Rig.FocusOn(Jets.Emitter.position, 26f, 0.35f);
        }

        private void JetsLeft(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
            _sl = GUILayout.BeginScrollView(_sl);
            JetsRecipe j = Jets.Recipe;
            GUILayout.Label("THE 'MECH", _cap);
            if (GUILayout.Button(Short(Rig.ShooterId) + "  ·  change", _btn)) { _pickShooter = true; _pickTarget = false; }
            GUILayout.Label("The stage's shooter. Its game prefab is " + (Rig.ShooterId ?? "") + "; jets attach to its bones the way the game does.", _small);

            GUILayout.Space(8); GUILayout.Label("STOCK JUMP JETS", _cap);
            if (Jets.StockFound.Count == 0) GUILayout.Label("This prefab has no jump-jet components of its own.", _small);
            foreach (string s in Jets.StockFound) GUILayout.Label("· " + s, _small);
            bool hide = GUILayout.Toggle(j.hideStock, " Remove the stock jets (the GN emitter replaces them)", _label);
            if (hide != j.hideStock) { j.hideStock = hide; Jets.Rebuild(); }
            if (!j.hideStock) { bool show = GUILayout.Toggle(Jets.ShowStock, " Show them here for reference", _label); if (show != Jets.ShowStock) { Jets.ShowStock = show; Jets.Rebuild(); } }

            GUILayout.Space(8); GUILayout.Label("GN EMITTER · where", _cap);
            GUILayout.BeginHorizontal(); GUILayout.Label("Bone", _small, GUILayout.Width(60));
            if (GUILayout.Button(j.bone + "  ▾", _btn)) _pickBone = !_pickBone;
            GUILayout.EndHorizontal();
            if (_pickBone)
            {
                _boneFilter = GUILayout.TextField(_boneFilter, _field);
                int n = 0;
                foreach (string b in JetsPlayer.Bones(Rig.Shooter))
                {
                    if (_boneFilter.Length > 0 && b.IndexOf(_boneFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++n > 16) { GUILayout.Label("… type to narrow", _small); break; }
                    if (GUILayout.Button(b + (b == "j_COCKPIT" ? "   (the game's torso attach)" : b == "j_LCalf" || b == "j_RCalf" ? "   (leg attach)" : ""), b == j.bone ? _btnOn : _btnSmall)) { j.bone = b; _pickBone = false; Jets.Rebuild(); FocusEmitter(); }
                }
            }
            Vector3 o = j.offset;
            o.x = (float)Math.Round(Slider("Left / right m", o.x, -8f, 8f), 2); o.y = (float)Math.Round(Slider("Down / up m", o.y, -10f, 10f), 2); o.z = (float)Math.Round(Slider("Back / forward m", o.z, -10f, 10f), 2);
            if (o != j.offset) { j.offset = o; if (Jets.Emitter != null) Jets.Emitter.localPosition = o; }
            GUILayout.BeginHorizontal(); GUILayout.Label("Blows", _small, GUILayout.Width(60));
            foreach (var d in new[] { new object[] { "back", new Vector3(0f, 0.2f, -1f) }, new object[] { "down", new Vector3(0f, -1f, -0.2f) }, new object[] { "up", new Vector3(0f, 1f, -0.2f) }, new object[] { "forward", new Vector3(0f, 0.2f, 1f) } })
                if (GUILayout.Button((string)d[0], ((Vector3)d[1] - j.direction).sqrMagnitude < 0.01f ? _btnOn : _btnSmall)) { j.direction = (Vector3)d[1]; Jets.Rebuild(); }
            GUILayout.EndHorizontal();
            j.spread = (float)Math.Round(Slider("Cone °", j.spread, 2f, 60f), 1);
            if (GUILayout.Button("Look at the emitter", _btnSmall, GUILayout.Width(150))) FocusEmitter();

            GUILayout.Space(8); GUILayout.Label("GN EMITTER · look", _cap);
            bool changed = false;
            string c;
            c = ColorPicker("Particles", j.color, "jets.color"); if (c != j.color) { j.color = c; changed = true; }
            c = ColorPicker("Second tone", j.alt, "jets.alt"); if (c != j.alt) { j.alt = c; changed = true; }
            c = ColorPicker("Gold sparks", j.gold, "jets.gold"); if (c != j.gold) { j.gold = c; changed = true; }
            c = ColorPicker("Drive glow", j.core, "jets.core"); if (c != j.core) { j.core = c; changed = true; }
            j.goldMix = (float)Math.Round(Slider("Gold mix", j.goldMix, 0f, 1f), 2);
            float v;
            v = (float)Math.Round(Slider("Density", j.density, 0.2f, 3f), 2); if (v != j.density) { j.density = v; }
            v = (float)Math.Round(Slider("Spark size", j.size, 0.3f, 3f), 2); if (v != j.size) { j.size = v; changed = true; }
            v = (float)Math.Round(Slider("Thrust speed", j.speed, 0.3f, 3f), 2); if (v != j.speed) j.speed = v;
            v = (float)Math.Round(Slider("Drift", j.drift, 0f, 3f), 2); if (v != j.drift) { j.drift = v; changed = true; }
            v = (float)Math.Round(Slider("Twinkle", j.twinkle, 0f, 2f), 2); if (v != j.twinkle) { j.twinkle = v; changed = true; }
            v = (float)Math.Round(Slider("Lifetime", j.lifetime, 0.3f, 3f), 2); if (v != j.lifetime) { j.lifetime = v; changed = true; }
            j.idle = (float)Math.Round(Slider("Idle stream", j.idle, 0f, 2f), 2);
            j.glow = (float)Math.Round(Slider("Drive glow", j.glow, 0f, 3f), 2);
            j.burst = (float)Math.Round(Slider("Jump burst s", j.burst, 0.5f, 6f), 1);
            if (changed) Jets.Rebuild();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("JUMP  (space)", _btnOn, GUILayout.Width(120))) Jets.Jump();
            GUILayout.EndHorizontal();

            GUILayout.Space(8); GUILayout.Label("JETS RECIPES · " + JetsDir, _cap);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _small, GUILayout.Width(40)); j.name = GUILayout.TextField(j.name ?? "", _field);
            if (GUILayout.Button("Save", _btnSmall, GUILayout.Width(50))) SaveJets();
            GUILayout.EndHorizontal();
            foreach (string f in _jetFiles)
                if (GUILayout.Button(Path.GetFileNameWithoutExtension(f).Replace('_', ' '), f == _jetsPath ? _btnOn : _btn)) { try { Jets.Recipe = JetsRecipe.Parse(File.ReadAllText(f)); _jetsPath = f; Jets.Rebuild(); FocusEmitter(); Toast("Loaded " + Path.GetFileName(f)); } catch (Exception e) { Toast(e.Message); } }
            GUILayout.Space(6); GUILayout.Label("IN THE GAME", _cap);
            GUILayout.Label("CombatVFX plays a jets recipe on every 'Mech whose chassis carries the tag vfx_jets_" + Slug(j.name) + " (ChassisTags). The recipe file goes in Mods\\CombatVFX\\jets.", _small);
            if (GUILayout.Button("Install into Mods\\CombatVFX\\jets", _btn)) InstallJets();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void SaveJets()
        {
            try { Directory.CreateDirectory(JetsDir); string f = Path.Combine(JetsDir, Slug(Jets.Recipe.name) + ".json"); File.WriteAllText(f, Jets.Recipe.ToJson()); _jetsPath = f; RefreshJetFiles(); Toast("Saved " + Path.GetFileName(f)); }
            catch (Exception e) { Toast("Could not save: " + e.Message); }
        }

        private void InstallJets()
        {
            try
            {
                string dir = Path.Combine(Assets.GamePath, "Mods", "CombatVFX", "jets"); Directory.CreateDirectory(dir);
                string f = Path.Combine(dir, Slug(Jets.Recipe.name) + ".json"); File.WriteAllText(f, Jets.Recipe.ToJson());
                Toast("Installed " + f + " · tag a chassis vfx_jets_" + Slug(Jets.Recipe.name));
            }
            catch (Exception e) { Toast("Could not install: " + e.Message); }
        }
    }
}
