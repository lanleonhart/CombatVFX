// Stand-ins for the game's light components, which its effect prefabs carry. The game draws these through its own
// deferred renderer; here each one drives a plain Unity Light with the same colour and radius, and an intensity scaled
// by Studio.LightScale (the game's intensities are in the millions).
using System.Collections;
using UnityEngine;

namespace BattleTech.Rendering
{
    public class BTLight : MonoBehaviour
    {
        public enum LightTypes { Point, Spot, Area, Directional }
        public enum MinimumLightQuality { Low, Medium, High }
        public enum DistanceBehavior { None, Fade, Cull }

        public bool isInLightList;
        public Vector3 lastPosition = Vector3.zero;
        public Quaternion lastRotation = Quaternion.identity;
        public float lastIntensity;
        public float radius = 10f;
        public float intensity = 1f;
        public float attenuationOffset;
        public Color lightColor = Color.white;
        public float lightTemperature = 6500f;
        public bool useTemperature;
        public bool contributeVolumetrics;
        public float volumetricsMultiplier = 1f;
        public float maximumSmoothness = 1f;
        public float spotlightAngleInner = 35f;
        public float spotlightAngleOuter = 50f;
        public bool castShadows;
        public bool contributeSpecular = true;
        public LightTypes lightType;
        public float length = 1f;
        public float shadowBias = 0.0001f;
        public float shadowNearPlane = 0.05f;
        public float shadowFarPlane = 1f;
        public bool hasMoved;
        public bool isNightLight;
        public LayerMask layerMask = -1;
        public MinimumLightQuality quality;
        public DistanceBehavior distanceBehavior;
        public int fadeCount;

        /// <summary>The game's intensities run into the millions; this turns them into Unity light units.</summary>
        public static float Scale = 1f / 400000f;

        private Light _light;

        private Light Light
        {
            get
            {
                if (_light == null)
                {
                    _light = GetComponent<Light>();
                    if (_light == null) _light = gameObject.AddComponent<Light>();
                    _light.type = lightType == LightTypes.Spot ? LightType.Spot : LightType.Point;
                    _light.shadows = LightShadows.None;
                    _light.renderMode = LightRenderMode.ForcePixel;
                }
                return _light;
            }
        }

        private void OnEnable() { RefreshLightSettings(true); }
        private void LateUpdate() { RefreshLightSettings(false); }

        public void RefreshLightSettings(bool force)
        {
            Light l = Light;
            l.color = lightColor;
            l.range = Mathf.Max(1f, lightType == LightTypes.Area ? Mathf.Max(radius, length * 0.6f) : radius);
            l.spotAngle = spotlightAngleOuter;
            l.intensity = Mathf.Clamp(intensity * Scale, 0f, 20f);
            l.enabled = enabled && l.intensity > 0.001f;
        }
    }

    public class BTLightAnimator : MonoBehaviour
    {
        public AnimationCurve curve;
        public float multiplier = 1f;

        private BTLight _thisLight;
        private IEnumerator activeCoroutine;

        private BTLight thisLight { get { if (_thisLight == null) _thisLight = GetComponent<BTLight>(); return _thisLight; } }

        private void OnEnable() { PlayAnimation(); }

        public void PlayAnimation()
        {
            if (thisLight == null || curve == null || curve.length == 0) return;
            if (activeCoroutine != null) StopCoroutine(activeCoroutine);
            if (!gameObject.activeInHierarchy) gameObject.SetActive(true);
            activeCoroutine = AnimateLight();
            StartCoroutine(activeCoroutine);
        }

        public void StopAnimation()
        {
            if (activeCoroutine != null) { StopCoroutine(activeCoroutine); activeCoroutine = null; }
            if (thisLight != null) { thisLight.intensity = 0f; thisLight.enabled = false; }
        }

        private void OnDisable() { if (activeCoroutine != null) StopCoroutine(activeCoroutine); }

        private IEnumerator AnimateLight()
        {
            thisLight.enabled = true;
            float end = curve.keys[curve.length - 1].time, t = 0f;
            while (t < end)
            {
                thisLight.intensity = curve.Evaluate(t) * multiplier;
                thisLight.RefreshLightSettings(true);
                t += Time.deltaTime;
                yield return null;
            }
            thisLight.intensity = 0f;
            thisLight.RefreshLightSettings(true);
            thisLight.enabled = false;
            activeCoroutine = null;
        }
    }
}
