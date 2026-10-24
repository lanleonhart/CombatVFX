// Stand-ins for the game's weapon effect scripts. The game's effect prefabs carry components of these exact class names
// (root namespace, assembly Assembly-CSharp), so when a prefab loads here its serialized fields land in these classes
// instead of being dropped as "missing script". Only Unity-serializable fields are declared; the studio drives the
// visuals itself (StockEffectPlayer). Field names and defaults come from the decompiled game (BattleTech 1.9.1).
using UnityEngine;

public enum AudioSwitch_weapon_type
{
    ac_auto, ac_rotary, ac_single, laser_large, laser_medium, laser_small, lrm, melee, mg, ppc, gauss, srm,
    ac10_rotary, ac10_single, ac2_ac5_auto, ac20_rotary, ac20_single, coil_large, coil_medium, coil_small, inferno,
    lbx_ac10, lbx_ac2, lbx_ac20, lbx_ac5, flamer, narc, tag, thumper, mortar,
}

public class WeaponEffect : MonoBehaviour
{
    public enum WeaponEffectState { NotStarted, PreFiring, Firing, WaitingForImpact, Complete }

    public int hitIndex;
    public bool subEffect;
    public WeaponEffectState currentState;
    public float attackSequenceNextDelayMin;
    public float attackSequenceNextDelayMax;
    public float preFireDuration;
    public float projectileSpeed;
    public AudioSwitch_weapon_type weaponImpactType;
    public GameObject preFireVFXPrefab;
    public GameObject muzzleFlashVFXPrefab;
    public GameObject projectilePrefab;
    public GameObject projectile;
    public string impactVFXBase = "";
    public string[] impactVFXVariations;
    public string armorDamageVFXName;
    public string structureDamageVFXName;
    public string terrainHitVFXBase;
    public string buildingHitOverlayVFXName;
    public bool shotsDestroyFlimsyObjects;
    public string preFireSFX = "";
}

public class LaserEffect : WeaponEffect
{
    public string beamStartSFX;
    public string beamStopSFX;
    public string pulseSFX;
    public float pulseDelay;
    public AnimationCurve laserAnim;
    public float lightIntensity = 3500000f;
    public float lightRadius = 100f;
}

public class PulseLaserEffect : LaserEffect { }

public class BallisticEffect : WeaponEffect
{
    public float shotDelay;
    public float burstDelay;
    public float spreadAngle;
    public GameObject bulletPrefab;
    public string firstShotSFX;
    public string middleShotSFX;
    public string lastShotSFX;
}

public class GaussEffect : BallisticEffect { }

public class BulletEffect : WeaponEffect
{
    public int bulletIdx;
}

public class BurstBallisticEffect : WeaponEffect
{
    public float impactTime = 0.5f;
    public GameObject accurateProjectilePrefab;
    public GameObject inaccurateProjectilePrefab;
    public string preFireSoundEvent;
    public string projectileSoundEvent;
    public string fireCompleteStopEvent;
}

public class LBXEffect : WeaponEffect
{
    public GameObject accurateProjectilePrefab;
    public GameObject inaccurateProjectilePrefab;
    public string preFireSoundEvent;
    public string projectileSoundEvent;
    public string fireCompleteStopEvent;
}

public class MissileLauncherEffect : WeaponEffect
{
    public GameObject MissilePrefab;
    public float firingInterval;
    public float volleyInterval;
    public float missileCurveStrength;
    public int missileCurveFrequency;
    public bool isSRM;
}

public class MissileEffect : WeaponEffect
{
    public Transform tubeTransform;
    public float impactLightIntensity = 1000000f;
    public int IndirectArcMinPoints = 9;
    public float ArcMinForce = 0.1f;
}

public class PPCEffect : WeaponEffect { }

public class FlamerEffect : WeaponEffect
{
    public float impactTime = 0.35f;
    public GameObject standardProjectilePrefab;
    public GameObject extendedProjectilePrefab;
}

public class COILEffect : WeaponEffect
{
    public GameObject muzzleFlashMinPrefab;
    public GameObject projectileMinPrefab;
    public GameObject muzzleFlashMedPrefab;
    public GameObject projectileMedPrefab;
    public GameObject muzzleFlashMaxPrefab;
    public GameObject projectileMaxPrefab;
}

public class MeleeEffect : WeaponEffect { }

/// <summary>The PPC's lightning: the game's own logic, which is plain Unity code, so it runs here unchanged.</summary>
[RequireComponent(typeof(LineRenderer))]
public class PPCBranch : MonoBehaviour
{
    public Vector3 startPos { get; set; }
    public Vector3 endPos { get; set; }

    public float FadeTime = 0.05f;
    public float MinTimeToNextFlash = 0.1f;
    public float MaxTimeToNextFlash = 0.3f;
    public int MinNumberLineBreaks = 10;
    public int MaxNumberLineBreaks = 30;
    public float MaxDistFromBeam = 1f;
    public Color startColor;
    public Color startColorFade;
    public Color endColor;
    public Color endColorFade;

    private LineRenderer lineRenderer;
    private float timeSinceLastBreak;
    private float timeUntilNextBreak = 9999.9f;
    private float seed = 3.3f;

    private float getNextBreakTime() { seed += 1.37f; return MinTimeToNextFlash + 337.7f * seed * Time.realtimeSinceStartup % (MaxTimeToNextFlash - MinTimeToNextFlash); }
    private int getNumBreaks() { seed += 2.23f; return MinNumberLineBreaks + 3739 * (int)seed % Mathf.Max(1, MaxNumberLineBreaks - MinNumberLineBreaks); }
    private Vector3 getRandomOffset()
    {
        seed += 3.71f;
        float x = 313.1f * seed % (MaxDistFromBeam * 2f) - MaxDistFromBeam;
        float y = 739.3f * seed % (MaxDistFromBeam * 2f) - MaxDistFromBeam;
        float z = 947.7f * seed % (MaxDistFromBeam * 2f) - MaxDistFromBeam;
        return new Vector3(x, y, z);
    }
    private float getRange(float maxDist) { seed += 0.37f; return 7923f * seed % Mathf.Max(0.001f, maxDist); }

    public void Init(float i_seed) { lineRenderer = GetComponent<LineRenderer>(); seed = i_seed; timeUntilNextBreak = getNextBreakTime(); }

    private void generateBreaks()
    {
        int numBreaks = Mathf.Max(2, getNumBreaks());
        lineRenderer.positionCount = numBreaks;
        lineRenderer.SetPosition(0, startPos);
        lineRenderer.SetPosition(numBreaks - 1, endPos);
        Vector3 a = endPos - startPos;
        float magnitude = a.magnitude;
        float a2 = magnitude / numBreaks * 2f;
        float num = 0f;
        a.Normalize();
        for (int i = 1; i < numBreaks - 1; i++)
        {
            num += getRange(Mathf.Min(a2, magnitude - num));
            lineRenderer.SetPosition(i, startPos + a * num + getRandomOffset());
        }
        timeSinceLastBreak = 0f;
        timeUntilNextBreak = getNextBreakTime();
    }

    private void Update()
    {
        float num = timeSinceLastBreak;
        timeSinceLastBreak += Time.deltaTime;
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
        if (num >= FadeTime)
        {
            if (timeSinceLastBreak > timeUntilNextBreak) generateBreaks();
            return;
        }
        if (timeSinceLastBreak >= FadeTime) { lineRenderer.startColor = startColorFade; lineRenderer.endColor = endColorFade; return; }
        float t = timeSinceLastBreak / FadeTime;
        lineRenderer.startColor = Color.Lerp(startColor, startColorFade, t);
        lineRenderer.endColor = Color.Lerp(endColor, endColorFade, t);
    }
}
