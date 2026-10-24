using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BattleTech;
using UnityEngine;

namespace CombatVFX.Combat
{
    // ──────────────────────────────────────────────────────────────────────────
    //  LaserReflector
    //
    //  Any mech carrying a component tagged "be_reflective" gains the ability
    //  to deflect directed-energy weapons (lasers, beams).
    //
    //  On beam hit:
    //    - The LineRenderer is shortened to the mech's surface point
    //    - A reflected beam LineRenderer fans out in the reflected direction
    //    - Radial energy splinters spawn at the impact point
    //    - A "[Reflected]" floatie appears on the mech
    //
    //  The "be_reflective" tag on any UpgradeDef in the mech's inventory
    //  enables this behaviour for that mech. Stripped armor (zero total armor)
    //  disables reflection — same rule as ballistic deflection.
    // ──────────────────────────────────────────────────────────────────────────

    internal static class LaserReflectorShared
    {
        internal const string ReflectiveTag = "Component_Reflective";

        // GUIDs of mechs that have at least one reflective component equipped
        internal static readonly HashSet<string> ReflectiveMechGuids = new HashSet<string>();

        // Instance IDs of beams already reflected this combat — prevent re-processing
        internal static readonly HashSet<int> ReflectedBeams = new HashSet<int>();

        // Cached beamRenderer field from LaserEffect
        internal static FieldInfo BeamRendererField;

        // Reuse BallisticDeflector field cache (StartPosField, EndPosField, etc.)
        // They're already populated by DeflectorShared.EnsureFields().

        internal static void EnsureBeamField()
        {
            if (BeamRendererField != null) return;
            BeamRendererField = AccessTools.Field(typeof(LaserEffect), "beamRenderer");
            ModEntry.Log($"[BELaser] beamRenderer field found={BeamRendererField != null}");
        }

        internal static void ClearOnCombatEnd()
        {
            ReflectiveMechGuids.Clear();
            ReflectedBeams.Clear();
            ModEntry.Log("[BELaser] Combat ended — reflective state cleared");
        }

        // ── Beam VFX: reflected LineRenderer ─────────────────────────────────
        internal static void SpawnReflectedBeam(Vector3 origin, Vector3 direction, Color laserColor)
        {
            try
            {
                var shader = Shader.Find("Legacy Shaders/Particles/Additive")
                          ?? Shader.Find("Particles/Additive")
                          ?? Shader.Find("Standard");

                var go = new GameObject("BEReflectedBeam");
                go.transform.position = origin;

                var lr = go.AddComponent<LineRenderer>();
                lr.material      = new Material(shader);
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.startColor    = Color.white;
                lr.endColor      = new Color(laserColor.r, laserColor.g, laserColor.b, 0f);
                lr.startWidth    = 0.25f;
                lr.endWidth      = 0.03f;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.SetPosition(0, origin);
                lr.SetPosition(1, origin + direction * 40f);

                go.AddComponent<BETracerFader>();
                ModEntry.Log($"[BELaser] Reflected beam  origin={origin:F1}  dir={direction:F2}");
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BELaser] ERROR spawning reflected beam: {ex}");
            }
        }

        // ── Beam VFX: radial energy splinters at impact ───────────────────────
        internal static void SpawnSurfaceImpact(Vector3 surfacePoint, Vector3 normal, Color laserColor)
        {
            try
            {
                var shader = Shader.Find("Legacy Shaders/Particles/Additive")
                          ?? Shader.Find("Particles/Additive")
                          ?? Shader.Find("Standard");

                // Build tangent basis so splinters spread along the surface
                Vector3 t1 = Vector3.Cross(normal, Vector3.up);
                if (t1.sqrMagnitude < 0.01f) t1 = Vector3.Cross(normal, Vector3.forward);
                t1 = t1.normalized;
                Vector3 t2 = Vector3.Cross(normal, t1).normalized;

                var root = new GameObject("BELaserImpact");
                root.transform.position = surfacePoint;

                // Radial splinter lines fanning out from impact
                const int Lines = 12;
                for (int i = 0; i < Lines; i++)
                {
                    float angle  = i * (360f / Lines) + UnityEngine.Random.Range(-15f, 15f);
                    float rad    = angle * Mathf.Deg2Rad;
                    Vector3 dir  = (t1 * Mathf.Cos(rad) + t2 * Mathf.Sin(rad)).normalized;
                    float length = UnityEngine.Random.Range(3f, 9f);

                    var lineGO = new GameObject($"Splinter_{i}");
                    lineGO.transform.SetParent(root.transform, false);
                    var lr = lineGO.AddComponent<LineRenderer>();
                    lr.material      = new Material(shader);
                    lr.useWorldSpace = true;
                    lr.positionCount = 2;
                    lr.startWidth    = UnityEngine.Random.Range(0.04f, 0.16f);
                    lr.endWidth      = 0.01f;
                    lr.startColor    = Color.white;
                    lr.endColor      = new Color(laserColor.r, laserColor.g, laserColor.b, 0.4f);
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.SetPosition(0, surfacePoint);
                    lr.SetPosition(1, surfacePoint + dir * length);
                    lineGO.AddComponent<BETracerFader>();
                }

                // Bright flash particle at the core of impact
                var ps   = new GameObject("Flash");
                ps.transform.SetParent(root.transform, false);
                ps.transform.position = surfacePoint;
                var pSys = ps.AddComponent<ParticleSystem>();
                var rend = ps.GetComponent<ParticleSystemRenderer>();
                rend.material   = new Material(shader);
                rend.renderMode = ParticleSystemRenderMode.Billboard;

                var main = pSys.main;
                main.loop            = false;
                main.playOnAwake     = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime   = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                main.startSpeed      = new ParticleSystem.MinMaxCurve(2f, 8f);
                main.startSize       = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
                main.startColor      = new ParticleSystem.MinMaxGradient(
                    Color.white, new Color(laserColor.r, laserColor.g, laserColor.b, 0.8f));
                main.gravityModifier = 0f;
                main.maxParticles    = 20;

                var em = pSys.emission;
                em.enabled      = true;
                em.rateOverTime = 0f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });
                pSys.Play();

                root.AddComponent<BERicochetAutoDestroy>();
                ModEntry.Log($"[BELaser] Surface impact VFX  point={surfacePoint:F1}");
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BELaser] ERROR spawning surface impact: {ex}");
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Mech.InitStats — register reflective mechs
    // ──────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(Mech), "InitStats")]
    public static class Mech_InitStats_BELaser_Patch
    {
        public static void Postfix(Mech __instance)
        {
            try
            {
                bool isReflective = false;
                foreach (var compRef in __instance.allComponents)
                {
                    var tags = compRef?.componentDef?.ComponentTags;
                    if (tags == null) continue;
                    foreach (var tag in tags)
                    {
                        if (tag == LaserReflectorShared.ReflectiveTag)
                        {
                            isReflective = true;
                            break;
                        }
                    }
                    if (isReflective) break;
                }

                if (isReflective)
                {
                    LaserReflectorShared.ReflectiveMechGuids.Add(__instance.GUID);
                    ModEntry.Log($"[BELaser] Reflective mech registered: {__instance.DisplayName} ({__instance.GUID})");
                }
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BELaser] ERROR in InitStats: {ex}");
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  WeaponEffect.Update — intercept beams aimed at reflective mechs
    // ──────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(WeaponEffect), "Update")]
    public static class WeaponEffect_Update_BELaser_Patch
    {
        public static void Postfix(WeaponEffect __instance)
        {
            try
            {
                if (LaserReflectorShared.ReflectiveMechGuids.Count == 0) return;

                // Only process beams — lasers have a LineRenderer on the projectile transform
                // or are a direct LaserEffect subclass.
                DeflectorShared.EnsureFields();
                LaserReflectorShared.EnsureBeamField();

                var projTr = DeflectorShared.ProjectileTransformField?.GetValue(__instance) as Transform;
                bool isBeam = (__instance is LaserEffect)
                           || (projTr != null && projTr.GetComponent<LineRenderer>() != null);
                if (!isBeam) return;

                var state = (WeaponEffect.WeaponEffectState)
                    DeflectorShared.CurrentStateField.GetValue(__instance);
                if (state != WeaponEffect.WeaponEffectState.Firing) return;

                float t  = (float)DeflectorShared.TField.GetValue(__instance);
                int   id = __instance.GetInstanceID();

                if (t >= 1f) return;
                if (LaserReflectorShared.ReflectedBeams.Contains(id)) return;

                Vector3 startPos = (Vector3)DeflectorShared.StartPosField.GetValue(__instance);
                Vector3 endPos   = (Vector3)DeflectorShared.EndPosField.GetValue(__instance);

                Vector3 rayDir    = endPos - startPos;
                float totalDist   = rayDir.magnitude;
                if (totalDist < 0.001f) return;
                rayDir = rayDir / totalDist;

                float radius = Settings.Instance.DeflectRadius;

                foreach (var mech in DeflectorShared.AllMechs)
                {
                    if (mech == null || mech.IsDead) continue;
                    if (!LaserReflectorShared.ReflectiveMechGuids.Contains(mech.GUID)) continue;

                    // No armor = no reflection (same rule as ballistic deflection)
                    float totalArmor = 0f;
                    foreach (ArmorLocation loc in System.Enum.GetValues(typeof(ArmorLocation)))
                    {
                        if (loc == ArmorLocation.None || loc == ArmorLocation.Invalid) continue;
                        totalArmor += mech.GetCurrentArmor(loc);
                    }
                    if (totalArmor <= 0f)
                    {
                        ModEntry.Log($"[BELaser] {mech.DisplayName} has no armor — cannot reflect");
                        continue;
                    }

                    Vector3 mechPos = DeflectorShared.MechSphereCenter(mech);

                    // Ray-sphere intersection
                    Vector3 surfacePoint = DeflectorShared.RaySphereIntersect(startPos, rayDir, mechPos, radius);

                    // Fallback: if beam endpoint is very close to this mech, treat as a direct hit
                    if (surfacePoint == Vector3.zero && Vector3.Distance(endPos, mechPos) < radius + 8f)
                        surfacePoint = endPos - rayDir * radius;

                    if (surfacePoint == Vector3.zero) continue;

                    float hitDist = Vector3.Distance(startPos, surfacePoint);
                    if (hitDist > totalDist + 2f) continue; // sphere is beyond the beam's reach

                    // ── Shorten the beam to the surface ──────────────────────────
                    LineRenderer beamRenderer = null;
                    if (__instance is LaserEffect && LaserReflectorShared.BeamRendererField != null)
                        beamRenderer = LaserReflectorShared.BeamRendererField.GetValue(__instance) as LineRenderer;
                    if (beamRenderer == null && projTr != null)
                        beamRenderer = projTr.GetComponent<LineRenderer>();

                    if (beamRenderer != null)
                        beamRenderer.SetPosition(1, surfacePoint);

                    // Clamp the endPos so game damage is applied at the surface
                    DeflectorShared.EndPosField.SetValue(__instance, surfacePoint);
                    LaserReflectorShared.ReflectedBeams.Add(id);

                    // ── Reflect and spawn VFX ─────────────────────────────────────
                    Vector3 normal     = (surfacePoint - mechPos).normalized;
                    Vector3 reflectDir = Vector3.Reflect(rayDir, normal);

                    Color laserColor = beamRenderer != null ? beamRenderer.startColor : Color.cyan;
                    LaserReflectorShared.SpawnReflectedBeam(surfacePoint, reflectDir, laserColor);
                    LaserReflectorShared.SpawnSurfaceImpact(surfacePoint, normal, laserColor);

                    // Floatie on the deflecting mech
                    DeflectorShared.ShowFloatie(mech, "[Reflected]",
                        FloatieMessage.MessageNature.Neutral);

                    ModEntry.Log($"[BELaser] BEAM REFLECT  {__instance.GetType().Name} id={id}" +
                                 $"  mech={mech.DisplayName}  surface={surfacePoint:F1}" +
                                 $"  reflect={reflectDir:F2}  lr={(beamRenderer != null ? "ok" : "null")}");
                    break;
                }
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BELaser] ERROR in WeaponEffect patch: {ex}");
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Combat end — clear reflective state
    // ──────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(CombatGameState), "OnCombatGameDestroyed")]
    public static class CombatGameState_OnCombatGameDestroyed_BELaser_Patch
    {
        public static void Postfix()
        {
            try { LaserReflectorShared.ClearOnCombatEnd(); }
            catch (Exception ex)
            { ModEntry.Log($"[BELaser] ERROR in combat end: {ex}"); }
        }
    }
}
