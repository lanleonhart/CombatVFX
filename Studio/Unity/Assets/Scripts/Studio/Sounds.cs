// Studio-only sound: a WAV or OGG the user drops into recipes\sounds, played at the muzzle on fire and at the impact
// point on hit. The game's own weapon sounds are Wwise events the studio cannot play, and in the game Unity audio is
// silent, so this is a timing aid for the designer rather than something the CombatVFX mod will ship.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace GNFX
{
    public class Sounds : MonoBehaviour
    {
        public string Dir;
        public float Volume = 0.8f;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        public List<string> Files()
        {
            try { if (Directory.Exists(Dir)) return Directory.GetFiles(Dir).Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).OrderBy(f => f).ToList(); }
            catch { }
            return new List<string>();
        }

        public void Play(string file, Vector3 pos)
        {
            if (string.IsNullOrEmpty(file)) return;
            AudioClip clip;
            if (_clips.TryGetValue(file, out clip)) { if (clip != null) AudioSource.PlayClipAtPoint(clip, pos, Volume); return; }
            StartCoroutine(Load(file, c => { if (c != null) AudioSource.PlayClipAtPoint(c, pos, Volume); }));
        }

        private IEnumerator Load(string file, Action<AudioClip> then)
        {
            string path = Path.Combine(Dir ?? "", file);
            if (!File.Exists(path)) { _clips[file] = null; yield break; }
            AudioType type = file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.WAV;
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), type))
            {
                yield return req.SendWebRequest();
                if (req.isNetworkError || req.isHttpError) { Debug.Log("[Studio] sound failed " + file + ": " + req.error); _clips[file] = null; yield break; }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                _clips[file] = clip;
                then(clip);
            }
        }
    }
}
