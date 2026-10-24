// Shot time: speed, pause, single frame steps and seeking. Everything that plays (coroutines, particle systems, the
// colour driver, light animators) runs on Unity's scaled time, so one time scale drives it all. Seeking forward runs
// the shot fast until the wanted moment and pauses there; seeking backward re-fires first (the caller does that).
using UnityEngine;

namespace GNFX
{
    public static class Playback
    {
        public static float Speed = 1f;        // 1, 0.5, 0.25, 0.1
        public static bool Paused;
        public static float SeekTo = -1f;      // seconds of shot time to run to, then pause; -1 = none
        public static float Now;               // seconds since the last Fire, in shot time
        public static float Length = 2f;       // how long the current shot is expected to last (for the scrub bar)
        private static int _stepFrames;

        public static void Fired(float length) { Now = 0f; Length = Mathf.Max(0.5f, length); }
        public static void Step() { Paused = true; _stepFrames = 1; }

        /// <summary>Called once a frame before anything else; sets Time.timeScale for this frame and advances the clock.</summary>
        public static void Tick()
        {
            float scale = Paused ? 0f : Speed;
            if (_stepFrames > 0) { scale = 1f; _stepFrames--; }
            if (SeekTo >= 0f)
            {
                if (Now < SeekTo - 0.001f) scale = Mathf.Clamp((SeekTo - Now) * 4f, 1f, 8f);
                else { SeekTo = -1f; Paused = true; scale = 0f; }
            }
            Time.timeScale = scale;
            Now += Time.deltaTime;
        }
    }
}
