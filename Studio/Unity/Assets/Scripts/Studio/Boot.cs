// Starts the studio: finds the game, indexes its bundles, builds the stage, places two 'Mechs and shows the interface.
// Settings live in studio.json next to the executable.
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GNFX
{
    public class Boot : MonoBehaviour
    {
        public static Boot Instance;
        [Serializable] public class Settings { public string gamePath = ""; public string recipesPath = ""; public string shooter = ""; public string target = ""; public string backdrop = "dusk"; public string camera = "shoulder"; }
        public Settings Config = new Settings();
        public GameAssets Assets = new GameAssets();
        private SceneRig _rig; private StudioUI _ui;

        private string SettingsPath => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "studio.json");

        private void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 120;
            try { if (File.Exists(SettingsPath)) Config = JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings(); } catch { }
            if (string.IsNullOrEmpty(Config.gamePath) || !File.Exists(Path.Combine(Config.gamePath, "BattleTech.exe"))) Config.gamePath = GameAssets.DetectGamePath() ?? Config.gamePath;
            if (string.IsNullOrEmpty(Config.recipesPath)) Config.recipesPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "recipes");
            if (string.IsNullOrEmpty(Config.backdrop)) Config.backdrop = "dusk";
            if (string.IsNullOrEmpty(Config.camera)) Config.camera = "shoulder";
            Save();
            Debug.Log("[Studio] game path " + Config.gamePath);
            if (!string.IsNullOrEmpty(Config.gamePath)) Assets.Index(Config.gamePath);
            foreach (string line in Assets.Log) Debug.Log("[Studio] " + line);
            if (!string.IsNullOrEmpty(Assets.Error)) Debug.Log("[Studio] index error: " + Assets.Error);

            _rig = gameObject.AddComponent<SceneRig>();
            _rig.Assets = Assets; _rig.Ground = Resources.Load<Material>("GNFX_Ground"); _rig.Sky = Resources.Load<Material>("GNFX_Sky");
            _rig.Backdrop = Config.backdrop;
            _rig.Build();
            _rig.Cam.gameObject.AddComponent<Bloom>();
            var audio = gameObject.AddComponent<Sounds>(); audio.Dir = Path.Combine(Config.recipesPath, "sounds");
            StockEffectPlayer stock = NewStock(), stock2 = NewStock();
            RecipePlayer recipes = NewRecipes(audio), recipes2 = NewRecipes(audio);
            recipes.Stock = stock; recipes2.Stock = stock2;
            _rig.Follow = () => recipes.TrackPos ?? stock.TrackPos;
            PlaceDefaultMechs();
            Debug.Log("[Studio] mechs placed: " + _rig.ShooterId + " -> " + _rig.TargetId + ", muzzle " + _rig.Muzzle + ", impact " + _rig.Impact);
            _ui = gameObject.AddComponent<StudioUI>();
            _ui.Assets = Assets; _ui.Rig = _rig; _ui.Stock = stock; _ui.Stock2 = stock2; _ui.Recipes = recipes; _ui.Recipes2 = recipes2; _ui.Audio = audio; _ui.RecipesDir = Config.recipesPath;
            _ui.Init();
            // command line: -effect <id> | -recipe <file> | -shot <png> [-shotAt <seconds>] -quit, for checking the studio without a screen
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-effect") _ui.SelectEffect(args[i + 1]);
                if (args[i] == "-recipe") _ui.SelectRecipe(args[i + 1]);
                if (args[i] == "-shot") _shot = args[i + 1];
                if (args[i] == "-shotAt") float.TryParse(args[i + 1], out _shotAt);
                if (args[i] == "-range") { float r; if (float.TryParse(args[i + 1], out r)) { _rig.Range = r; _rig.PlaceMechs(_rig.ShooterId, _rig.TargetId); _rig.Frame("shoulder"); } }
                if (args[i] == "-camera") _rig.Frame(args[i + 1]);
                if (args[i] == "-backdrop") _rig.SetBackdrop(args[i + 1]);
            }
            _quit = Array.IndexOf(args, "-quit") >= 0;
            Invoke("FirstFire", 0.5f);
            if (_shot != null) Invoke("Shot", 0.5f + _shotAt);
        }

        private StockEffectPlayer NewStock() { var s = gameObject.AddComponent<StockEffectPlayer>(); s.Assets = Assets; return s; }
        private RecipePlayer NewRecipes(Sounds audio)
        {
            var r = gameObject.AddComponent<RecipePlayer>();
            r.Additive = Resources.Load<Material>("GNFX_Additive"); r.Unlit = Resources.Load<Material>("GNFX_Unlit"); r.Blended = Resources.Load<Material>("GNFX_Blended");
            r.Lit = Resources.Load<Material>("GNFX_Ground"); r.Assets = Assets; r.Audio = audio;
            return r;
        }

        private void Update() { Playback.Tick(); }

        private string _shot; private float _shotAt = 0.35f; private bool _quit;
        private void FirstFire() { _ui.Fire(); }
        private void Shot()
        {
            try { ScreenCapture.CaptureScreenshot(_shot); Debug.Log("[Studio] screenshot " + _shot); } catch (Exception e) { Debug.Log("[Studio] screenshot failed: " + e.Message); }
            if (_quit) Invoke("Quit", 0.6f);
        }
        private void Quit() { Application.Quit(); }

        private void PlaceDefaultMechs()
        {
            var mechs = Assets.Ready ? Assets.MechIds : new System.Collections.Generic.List<string>();
            string Pick(string wanted, params string[] prefer)
            {
                if (!string.IsNullOrEmpty(wanted) && mechs.Contains(wanted)) return wanted;
                foreach (string p in prefer) { string hit = mechs.FirstOrDefault(m => m.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0); if (hit != null) return hit; }
                return mechs.FirstOrDefault();
            }
            string shooter = Pick(Config.shooter, "orionBase", "marauderBase", "atlasBase"), target = Pick(Config.target, "atlasBase", "highlanderBase", "awesomeBase");
            _rig.PlaceMechs(shooter, target);
            _rig.Frame(Array.IndexOf(SceneRig.CameraModes, Config.camera) >= 0 ? Config.camera : "shoulder");
            Config.shooter = shooter ?? ""; Config.target = target ?? ""; Save();
        }

        public void SetGamePath(string path)
        {
            Config.gamePath = path; Save();
            Assets.Index(path);
            if (Assets.Ready) PlaceDefaultMechs();
        }

        private void Save() { try { File.WriteAllText(SettingsPath, JsonUtility.ToJson(Config, true)); } catch { } }

        private void OnApplicationQuit()
        {
            if (_rig != null) { Config.shooter = _rig.ShooterId ?? ""; Config.target = _rig.TargetId ?? ""; Config.backdrop = _rig.Backdrop; if (_rig.CameraMode != "free") Config.camera = _rig.CameraMode; }
            Save();
        }
    }
}
