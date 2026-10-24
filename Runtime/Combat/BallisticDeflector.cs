using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BattleTech;
using UnityEngine;

namespace CombatVFX.Combat
{
    // ──────────────────────────────────────────────────────────────────────────
    //  BallisticDeflector
    //
    //  Universal system — applies to every mech in combat, no components needed.
    //  All tuning lives in settings.json.
    //
    //  Deflect chance formula (Proposal A — multiplicative scalars):
    //    chance = BaseDeflectChance
    //           × DamageScalar(weaponDamage)     — heavy rounds harder to deflect
    //           × DistanceScalar(distance)        — close shots harder to deflect
    //           × WeightClass.DeflectChanceMultiplier
    //           × ArmorScalar                     — stripped mech deflects less
    //           + GlancingAngleBonus × sin(2θ)    — bell curve, peaks at 45°
    //    clamped to [MinDeflectChance, MaxDeflectChance]
    //    (or 1.0 when TestMode = true)
    //
    //  DamageScalar  = clamp((ReferenceDamage / weaponDamage)^DamagePower, 0.1, MaxDamageScalar)
    //  DistanceScalar = lerp(CloseRangeScalar, FarRangeScalar, clamp01(distance / MaxRangeRef))
    //  ArmorScalar    = clamp01(currentTotalArmor / classAverageTotalArmor)
    //
    //  On deflect:
    //    - Projectile endPos is redirected away from the mech
    //    - Ricochet VFX tier selected by weapon damage (light / medium / heavy)
    //    - DamageLocation intercepted — damage × WeightClass.DamageFraction applied
    //
    //  Weight class brackets (MaxTonnage):
    //    Light    ≤35T  — 0.60× chance, 15% dmg fraction
    //    Medium   ≤55T  — 1.00× chance, 15% dmg fraction
    //    Heavy    ≤75T  — 1.35× chance, 12% dmg fraction
    //    Assault  >75T  — 1.70× chance,  8% dmg fraction
    //
    //  Missiles excluded — standard impact retained.
    //  Beams/energy excluded — handled by GN Field or ignored.
    // ──────────────────────────────────────────────────────────────────────────

    internal static class DeflectorShared
    {
        // WeaponEffect field cache
        internal static FieldInfo EndPosField;
        internal static FieldInfo StartPosField;
        internal static FieldInfo TField;
        internal static FieldInfo CurrentPosField;
        internal static FieldInfo CurrentStateField;
        internal static FieldInfo ProjectileTransformField;
        internal static FieldInfo WeaponField;

        internal static readonly HashSet<int> Deflected  = new HashSet<int>();
        internal static readonly HashSet<int> Diagnosed  = new HashSet<int>();
        internal static readonly List<Mech>   AllMechs   = new List<Mech>();

        // Set true while we are calling DamageLocation ourselves (ground secondary hit)
        // so the patch doesn't intercept and double-scale it.
        internal static bool SuppressDeflectPatch = false;

        // Cached MethodInfo for calling DamageLocation dynamically (ground secondary hits)
        internal static MethodInfo DamageLocationMethod;

        internal struct PendingDeflect
        {
            public float   tHit;
            public Vector3 surfacePoint;
            public Vector3 newEndPos;
            public string  mechGuid;
            public float   damageFraction;   // from weight class
            public float   weaponDamage;     // for VFX tier + log
        }

        internal static readonly Dictionary<int, PendingDeflect> PendingDeflects =
            new Dictionary<int, PendingDeflect>();

        internal struct PendingGroundDeflect
        {
            public float        tHit;           // normalized t to trigger at (0.95 = just before landing)
            public Vector3      groundPoint;     // terrain impact point
            public Vector3      groundNormal;    // terrain surface normal at impact
            public Vector3      newEndPos;       // reflected trajectory endpoint
            public string       secondaryMechGuid; // empty = no secondary hit
            public float        secondaryDamage;
            public ArmorLocation secondaryLoc;  // leg hit (ground deflects come from below)
        }

        internal static readonly Dictionary<int, PendingGroundDeflect> PendingGroundDeflects =
            new Dictionary<int, PendingGroundDeflect>();

        // Keyed by mech GUID — set at deflect execution, consumed by DamageLocation
        internal struct DeflectOutcome
        {
            public float damageFraction;
            public float timestamp;
        }

        internal static readonly Dictionary<string, DeflectOutcome> RecentDeflects =
            new Dictionary<string, DeflectOutcome>();

        // ── Combat statistics (reset each combat) ─────────────────────────────
        internal static int   StatShotsProcessed   = 0;
        internal static int   StatMechDeflects      = 0;
        internal static int   StatGroundDeflects    = 0;
        internal static float StatDamageReduced     = 0f;
        internal static float StatDamageApplied     = 0f;  // damage that DID get through on deflects

        internal static void EnsureFields()
        {
            if (EndPosField != null) return;
            EndPosField              = AccessTools.Field(typeof(WeaponEffect), "endPos");
            StartPosField            = AccessTools.Field(typeof(WeaponEffect), "startPos");
            TField                   = AccessTools.Field(typeof(WeaponEffect), "t");
            CurrentPosField          = AccessTools.Field(typeof(WeaponEffect), "currentPos");
            CurrentStateField        = AccessTools.Field(typeof(WeaponEffect), "currentState");
            ProjectileTransformField = AccessTools.Field(typeof(WeaponEffect), "projectileTransform");
            WeaponField              = AccessTools.Field(typeof(WeaponEffect), "weapon");

            bool ok = EndPosField != null && StartPosField != null && TField != null
                   && CurrentPosField != null && CurrentStateField != null
                   && ProjectileTransformField != null;
            ModEntry.Log($"[BEDeflect] Field cache built — ok={ok} weaponField={(WeaponField != null ? "ok" : "MISSING")}");
        }

        internal static void ClearOnCombatEnd()
        {
            float deflectRate = StatShotsProcessed > 0
                ? (float)(StatMechDeflects + StatGroundDeflects) / StatShotsProcessed * 100f
                : 0f;
            float savedRatio = (StatDamageReduced + StatDamageApplied) > 0f
                ? StatDamageReduced / (StatDamageReduced + StatDamageApplied) * 100f
                : 0f;

            ModEntry.Log("═══════════════════════════════════════════════════════");
            ModEntry.Log("[BEDeflect] COMBAT SUMMARY");
            ModEntry.Log("═══════════════════════════════════════════════════════");
            ModEntry.Log($"  Shots processed      : {StatShotsProcessed}");
            ModEntry.Log($"  Mech deflections     : {StatMechDeflects}");
            ModEntry.Log($"  Ground deflections   : {StatGroundDeflects}");
            ModEntry.Log($"  Total deflect rate   : {deflectRate:F1}%");
            ModEntry.Log($"  Damage reduced (saved): {StatDamageReduced:F1}");
            ModEntry.Log($"  Damage applied (thru) : {StatDamageApplied:F1}");
            ModEntry.Log($"  Damage saved ratio   : {savedRatio:F1}%");
            ModEntry.Log("═══════════════════════════════════════════════════════");

            Deflected.Clear();
            Diagnosed.Clear();
            PendingDeflects.Clear();
            PendingGroundDeflects.Clear();
            RecentDeflects.Clear();
            AllMechs.Clear();
            SuppressDeflectPatch    = false;
            StatShotsProcessed      = 0;
            StatMechDeflects        = 0;
            StatGroundDeflects      = 0;
            StatDamageReduced       = 0f;
            StatDamageApplied       = 0f;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        internal static Vector3 RaySphereIntersect(Vector3 origin, Vector3 dir,
                                                    Vector3 center, float radius)
        {
            Vector3 oc  = origin - center;
            float   b   = Vector3.Dot(oc, dir);
            float   c   = Vector3.Dot(oc, oc) - radius * radius;
            float   d   = b * b - c;
            if (d < 0f) return Vector3.zero;
            float sqrtD = Mathf.Sqrt(d);
            float t1 = -b - sqrtD;
            float t2 = -b + sqrtD;
            if (t1 > 0f) return origin + dir * t1;
            if (t2 > 0f) return origin + dir * t2;
            return Vector3.zero;
        }

        // ── Weapon property reader ────────────────────────────────────────────
        internal static float GetWeaponDamage(WeaponEffect instance)
        {
            if (WeaponField == null) return 15f;
            try
            {
                var weapon = WeaponField.GetValue(instance) as Weapon;
                return weapon != null ? Mathf.Max(1f, weapon.DamagePerShot) : 15f;
            }
            catch { return 15f; }
        }

        // ── Mech sphere center — offset from foot position to torso height ────
        // mech.CurrentPosition is at ground/foot level. Weapon shots are aimed at
        // the torso (~4m up). Without this offset the ray misses the sphere on
        // short-to-medium range shots aimed at head-on torso locations.
        internal static Vector3 MechSphereCenter(Mech mech) =>
            mech.CurrentPosition + Vector3.up * 4f;

        // ── Roll deflect — Proposal A multiplicative formula ──────────────────
        //
        // chance = BaseDeflectChance
        //        × DamageScalar    — (ReferenceDamage/weaponDamage)^DamagePower
        //        × DistanceScalar  — lerp(close, far, dist/MaxRangeRef)
        //        × weightMultiplier
        //        × armorScalar     — currentTotalArmor/classAvgArmor, 0-1
        //        + GlancingAngleBonus × sin(2θ)
        //
        // sin(2θ) bell curve: 0 at head-on, peaks at 45°, 0 at pure graze.
        // Computed from 3D surface normal — works in full 360°.
        internal static bool RollDeflect(
            float   weaponDamage,
            float   distance,
            float   weightMultiplier,
            float   armorScalar,
            Vector3 incomingDir,
            Vector3 surfaceNormal,
            out float finalChance,
            out float angleFactor,
            out float damageScalar,
            out float distanceScalar)
        {
            var s = Settings.Instance;

            if (s.TestMode)
            {
                finalChance = 1.0f; angleFactor = 1.0f;
                damageScalar = 1.0f; distanceScalar = 1.0f;
                return true;
            }

            // DamageScalar — heavier rounds harder to deflect
            damageScalar = Mathf.Pow(s.ReferenceDamage / Mathf.Max(1f, weaponDamage), s.DamagePower);
            damageScalar = Mathf.Clamp(damageScalar, 0.1f, s.MaxDamageScalar);

            // DistanceScalar — close shots more direct, harder to deflect
            float distT    = Mathf.Clamp01(distance / s.MaxRangeRef);
            distanceScalar = Mathf.Lerp(s.CloseRangeScalar, s.FarRangeScalar, distT);

            // Angle factor — sin(2θ) bell curve, peaks at 45°
            float cosTheta = Mathf.Abs(Vector3.Dot(incomingDir, surfaceNormal));
            float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
            angleFactor    = 2f * sinTheta * cosTheta;

            float chance = s.BaseDeflectChance
                         * damageScalar
                         * distanceScalar
                         * weightMultiplier
                         * armorScalar
                         + s.GlancingAngleBonus * angleFactor;

            finalChance = Mathf.Clamp(chance, s.MinDeflectChance, s.MaxDeflectChance);
            return UnityEngine.Random.value < finalChance;
        }

        // ── Queue deferred deflect ────────────────────────────────────────────
        internal static void QueueDeferredDeflect(int id, Vector3 startPos, Vector3 endPos,
                                                   string typeName, WeaponEffect instance)
        {
            var s = Settings.Instance;

            Vector3 dir     = endPos - startPos;
            float totalDist = dir.magnitude;
            if (totalDist < 0.001f) return;
            dir = dir / totalDist;

            float radius       = s.DeflectRadius;
            float weaponDamage = GetWeaponDamage(instance);

            StatShotsProcessed++;
            ModEntry.Log($"───────────────────────────────────────────────────────");
            ModEntry.Log($"[BEDeflect] SHOT #{StatShotsProcessed} — {typeName} id={id}  dmg={weaponDamage:F1}" +
                         $"  dist={totalDist:F0}m  start={startPos:F1}  end={endPos:F1}");

            foreach (var mech in AllMechs)
            {
                if (mech == null || mech.IsDead) continue;

                Vector3 mechPos     = MechSphereCenter(mech);
                float   mechTonnage = mech.MechDef?.Chassis?.Tonnage ?? 55f;
                var     wc          = s.GetWeightClass(mechTonnage);

                Vector3 surfacePoint = RaySphereIntersect(startPos, dir, mechPos, radius);
                if (surfacePoint == Vector3.zero)
                {
                    // Fallback: if the shot's endpoint is close to this mech, the round
                    // is aimed here but the ray-sphere check failed (geometry offset, endPos
                    // at a mesh contact point that lies outside our sphere). Treat as a hit
                    // and approximate the surface entry from the endpoint.
                    if (Vector3.Distance(endPos, mechPos) < radius + 8f)
                    {
                        surfacePoint = endPos - dir * radius;
                        ModEntry.Log($"[BEDeflect]   {mech.DisplayName} — sphere miss, endPos proximity fallback");
                    }
                    else
                    {
                        ModEntry.Log($"[BEDeflect]   {mech.DisplayName} — no sphere intersection, skip");
                        continue;
                    }
                }

                float hitDist = Vector3.Distance(startPos, surfacePoint);
                float tHit    = hitDist / totalDist;
                if (tHit > 1.0f)
                {
                    ModEntry.Log($"[BEDeflect]   {mech.DisplayName} — tHit={tHit:F3} > 1.0 (mech behind end), skip");
                    continue;
                }

                // Surface normal at impact point — used for deflect roll and reflect direction
                Vector3 normal = (surfacePoint - mechPos).normalized;
                if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;

                // Distance from shooter to mech — represents firing range at shot time
                float distance = Vector3.Distance(startPos, mechPos);

                // Armor scalar — how much total armor remains vs class average
                float totalCurrentArmor = 0f;
                foreach (ArmorLocation loc in System.Enum.GetValues(typeof(ArmorLocation)))
                {
                    if (loc == ArmorLocation.None || loc == ArmorLocation.Invalid) continue;
                    totalCurrentArmor += mech.GetCurrentArmor(loc);
                }
                float avgTotalArmor = s.GetAverageTotalArmor(wc.Name);
                float armorScalar   = avgTotalArmor > 0f
                    ? Mathf.Clamp01(totalCurrentArmor / avgTotalArmor)
                    : 1f;

                // Structure cannot deflect — only hardened armor can ricochet rounds
                if (totalCurrentArmor <= 0f)
                {
                    ModEntry.Log($"[BEDeflect]   {mech.DisplayName} — no armor remaining, structure cannot deflect");
                    continue;
                }

                bool deflected = RollDeflect(
                    weaponDamage, distance,
                    wc.DeflectChanceMultiplier, armorScalar,
                    dir, normal,
                    out float finalChance, out float angleFactor,
                    out float damageScalar, out float distanceScalar);

                // Detailed formula breakdown for tuning review
                float rawChance = s.BaseDeflectChance * damageScalar * distanceScalar
                                * wc.DeflectChanceMultiplier * armorScalar;
                ModEntry.Log($"[BEDeflect]   TARGET  {mech.DisplayName} ({mechTonnage:F0}T {wc.Name})  tHit={tHit:F3}");
                ModEntry.Log($"[BEDeflect]   FORMULA {s.BaseDeflectChance:F2}(base)" +
                             $" × {damageScalar:F2}(dmg={weaponDamage:F1})" +
                             $" × {distanceScalar:F2}(dist={distance:F0}m)" +
                             $" × {wc.DeflectChanceMultiplier:F2}(weight)" +
                             $" × {armorScalar:F2}(armor {totalCurrentArmor:F0}/{avgTotalArmor:F0})" +
                             $" = {rawChance:F3}  +angle {s.GlancingAngleBonus:F2}×{angleFactor:F2}={s.GlancingAngleBonus * angleFactor:F3}" +
                             $"  → clamped={finalChance:F3}");
                ModEntry.Log($"[BEDeflect]   RESULT  {(deflected ? "✓ DEFLECT" : "✗ hit")}  (roll vs {finalChance:F3})");

                if (!deflected) continue;

                StatMechDeflects++;
                Vector3 reflectDir = Vector3.Reflect(dir, normal);
                Vector3 newEndPos  = surfacePoint + reflectDir * (radius * 2f + 20f);

                PendingDeflects[id] = new PendingDeflect
                {
                    tHit           = tHit,
                    surfacePoint   = surfacePoint,
                    newEndPos      = newEndPos,
                    mechGuid       = mech.GUID,
                    damageFraction = wc.DamageFraction,
                    weaponDamage   = weaponDamage
                };

                float appliedDmg = weaponDamage * wc.DamageFraction;
                float savedDmg   = weaponDamage - appliedDmg;
                ModEntry.Log($"[BEDeflect]   QUEUED  id={id} tHit={tHit:F3} surface={surfacePoint:F1}");
                ModEntry.Log($"[BEDeflect]   DAMAGE  {weaponDamage:F1} × {wc.DamageFraction:F2}(frac)" +
                             $" = {appliedDmg:F1} applied / {savedDmg:F1} absorbed");
                return; // one deflect per projectile
            }

            ModEntry.Log($"[BEDeflect]   No mech deflect — checking ground...");
            // No mech deflect — check for ground deflect
            TryQueueGroundDeflect(id, startPos, dir, totalDist, weaponDamage);
        }

        // ── Ground deflect ────────────────────────────────────────────────────
        // Called when a projectile misses all mechs. If the shot hits terrain,
        // the round ricochets off the ground. If the reflected ray intersects a
        // mech sphere, that mech takes reduced damage (typically to the legs).
        internal static void TryQueueGroundDeflect(int id, Vector3 startPos, Vector3 dir,
                                                    float totalDist, float weaponDamage)
        {
            var s = Settings.Instance;
            if (!s.GroundDeflectEnabled)
            {
                ModEntry.Log("[BEDeflect]   GroundDeflect disabled in settings, skip");
                return;
            }

            if (dir.y >= 0.05f)
            {
                ModEntry.Log($"[BEDeflect]   Shot is not descending (dir.y={dir.y:F3}), no ground deflect");
                return;
            }

            // ── Find terrain hit ──────────────────────────────────────────────
            if (!FindTerrainHit(startPos, dir, totalDist,
                                out Vector3 groundPoint, out Vector3 groundNormal))
            {
                ModEntry.Log("[BEDeflect]   No terrain hit found within shot range");
                return;
            }

            float hitDist = Vector3.Distance(startPos, groundPoint);
            // Fire far enough before impact that the direction change is clearly visible.
            // Use 20% of the remaining distance, clamped to [3m, 20m], so short shots
            // still get a visible redirect window and very long shots don't redirect too early.
            float earlyOffset = Mathf.Clamp(hitDist * 0.20f, 3f, 20f);
            float tHit = Mathf.Clamp01((hitDist - earlyOffset) / Mathf.Max(totalDist, 0.001f));

            Vector3 reflectDir  = Vector3.Reflect(dir, groundNormal);
            float   bounceRange = s.GroundDeflectMaxBounceRange;
            Vector3 newEndPos   = groundPoint + reflectDir * bounceRange;

            // ── Check reflected ray for secondary mech hit ────────────────────
            string        secondaryGuid   = string.Empty;
            float         secondaryDamage = 0f;
            ArmorLocation secondaryLoc    = ArmorLocation.None;

            foreach (var mech in AllMechs)
            {
                if (mech == null || mech.IsDead) continue;

                Vector3 sp = RaySphereIntersect(groundPoint, reflectDir, MechSphereCenter(mech), s.DeflectRadius);
                if (sp == Vector3.zero) continue;
                if (Vector3.Distance(groundPoint, sp) > bounceRange) continue;

                // Ground ricochets come from below — always hits the legs
                secondaryGuid   = mech.GUID;
                secondaryDamage = weaponDamage * s.GroundDeflectDamageFraction;
                secondaryLoc    = UnityEngine.Random.value > 0.5f
                    ? ArmorLocation.LeftLeg : ArmorLocation.RightLeg;

                ModEntry.Log($"[BEDeflect] Ground bounce id={id} will hit {mech.DisplayName}" +
                             $" loc={secondaryLoc} dmg={secondaryDamage:F1}");
                break;
            }

            PendingGroundDeflects[id] = new PendingGroundDeflect
            {
                tHit              = tHit,
                groundPoint       = groundPoint,
                groundNormal      = groundNormal,
                newEndPos         = newEndPos,
                secondaryMechGuid = secondaryGuid,
                secondaryDamage   = secondaryDamage,
                secondaryLoc      = secondaryLoc
            };

            StatGroundDeflects++;
            ModEntry.Log($"[BEDeflect]   GROUND HIT  point={groundPoint:F1}  normal={groundNormal:F2}" +
                         $"  tHit={tHit:F3}  bounceRange={bounceRange:F0}m");
            ModEntry.Log($"[BEDeflect]   GROUND SECONDARY  " +
                         (string.IsNullOrEmpty(secondaryGuid)
                             ? "none — bounce ray missed all mechs"
                             : $"mech={secondaryGuid} loc={secondaryLoc} dmg={secondaryDamage:F1}"));
        }

        // Returns true when the ray hits terrain within maxDist.
        // groundNormal is the terrain surface normal at impact — used to compute reflection.
        // Relies on Unity's Physics.Raycast; terrain normals point mostly upward (y > 0.5).
        private static bool FindTerrainHit(Vector3 startPos, Vector3 dir, float maxDist,
                                           out Vector3 groundPoint, out Vector3 groundNormal)
        {
            groundPoint  = Vector3.zero;
            groundNormal = Vector3.up;

            // If the shot isn't angled downward at all it won't hit the ground
            if (dir.y >= 0.05f) return false;

            // Prefer Terrain.activeTerrain for clean height sampling (avoids mech/prop colliders)
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                // Parametric intersection: startPos.y + dir.y * d = terrainY(x,z)
                // Approximate with terrain height at endPos x,z (flat-terrain approximation)
                Vector3 endPos = startPos + dir * maxDist;
                float terrainY = terrain.SampleHeight(endPos) + terrain.transform.position.y;

                float d = (terrainY - startPos.y) / dir.y; // dir.y < 0 so this is positive
                if (d <= 0f || d >= maxDist) return false;

                groundPoint = startPos + dir * d;

                // Get actual normal via downward raycast at the impact point
                Vector3 castOrigin = new Vector3(groundPoint.x, groundPoint.y + 10f, groundPoint.z);
                if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 20f))
                    groundNormal = hit.normal;

                return true;
            }

            // Fallback: physics raycast (may hit mech colliders — filter by upward normal)
            if (Physics.Raycast(startPos, dir, out RaycastHit phit, maxDist))
            {
                if (phit.normal.y > 0.5f) // ground-like surface
                {
                    groundPoint  = phit.point;
                    groundNormal = phit.normal;
                    return true;
                }
            }

            return false;
        }

        // ── Combat floaties ───────────────────────────────────────────────────
        internal static void ShowFloatie(Mech mech, string text, FloatieMessage.MessageNature nature)
        {
            try
            {
                mech.Combat?.MessageCenter?.PublishMessage(
                    new FloatieMessage(mech.GUID, mech.GUID, text, nature));
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BEDeflect] Floatie error: {ex.Message}");
            }
        }

        // ── Apply secondary ground-bounce damage ──────────────────────────────
        // Called directly instead of routing through the weapon hit pipeline.
        // DamageLocation is called via cached MethodInfo so the signature mismatch
        // (which we don't control) fails gracefully at runtime rather than at compile time.
        internal static void ApplyGroundSecondaryDamage(Mech mech, ArmorLocation loc, float damage)
        {
            if (DamageLocationMethod == null)
                DamageLocationMethod = AccessTools.Method(typeof(Mech), "DamageLocation");

            if (DamageLocationMethod == null)
            {
                ModEntry.Log("[BEDeflect] DamageLocation method not found — secondary damage skipped");
                return;
            }

            SuppressDeflectPatch = true;
            try
            {
                Vector3 attackDir = Vector3.up; // coming from below (ground bounce)
                DamageLocationMethod.Invoke(mech, new object[]
                {
                    0,          // hitPoints
                    attackDir,  // attackDirection
                    loc,        // aLoc
                    null,       // weapon
                    damage,     // totalArmorDamage
                    0f,         // totalStructureDamage
                    0,          // hitIndex
                    null,       // sequence
                    false       // applyEffects
                });
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BEDeflect] Ground secondary damage invoke failed: {ex.Message}");
            }
            finally
            {
                SuppressDeflectPatch = false;
            }
        }

        // ── VFX dispatch ──────────────────────────────────────────────────────
        // VFX tier is determined by weapon damage, matching settings.json thresholds:
        //   damage < VFXLightMaxDamage  → light (MG-class sparks)
        //   damage >= VFXHeavyMinDamage → heavy (AC20/Gauss shower)
        //   otherwise                  → medium (AC5-10 ricochet)
        internal static void SpawnDeflectVFX(Vector3 surfacePoint, Vector3 deflectDir,
                                              float weaponDamage)
        {
            try
            {
                var s = Settings.Instance;
                var shader = Shader.Find("Legacy Shaders/Particles/Additive")
                          ?? Shader.Find("Particles/Additive")
                          ?? Shader.Find("Standard");

                bool light = weaponDamage < s.VFXLightMaxDamage;
                bool heavy = weaponDamage >= s.VFXHeavyMinDamage;

                var root = new GameObject("BEDeflectRicochet");
                root.transform.position = surfacePoint;

                if (light)
                {
                    // MG / Magshot — small puff of sparks, quick fade
                    BuildSparks(root, surfacePoint, shader,
                        count: 18, speedMin: 3f, speedMax: 12f,
                        sizeMin: 0.03f, sizeMax: 0.10f,
                        colorA: new Color(1.0f, 0.85f, 0.5f, 1f),
                        colorB: new Color(1.0f, 0.5f,  0.1f, 1f),
                        gravity: 0.2f);
                    BuildTracer(root, surfacePoint, deflectDir, shader,
                        length: 6f, width: 0.03f,
                        startColor: new Color(1f, 0.9f, 0.7f, 1f),
                        endColor:   new Color(1f, 0.6f, 0.2f, 0f));
                }
                else if (heavy)
                {
                    // AC20 / Heavy Gauss — heavy shower, armor fragment debris
                    BuildSparks(root, surfacePoint, shader,
                        count: 90, speedMin: 12f, speedMax: 40f,
                        sizeMin: 0.07f, sizeMax: 0.25f,
                        colorA: new Color(1.0f, 0.7f, 0.2f, 1f),
                        colorB: new Color(0.9f, 0.3f, 0.05f, 1f),
                        gravity: 0.7f);
                    // Secondary debris burst — slow heavy chunks
                    BuildSparks(root, surfacePoint, shader,
                        count: 25, speedMin: 2f, speedMax: 8f,
                        sizeMin: 0.18f, sizeMax: 0.45f,
                        colorA: new Color(0.7f, 0.45f, 0.2f, 1f),
                        colorB: new Color(0.35f, 0.15f, 0.05f, 0f),
                        gravity: 1.5f);
                    BuildTracer(root, surfacePoint, deflectDir, shader,
                        length: 18f, width: 0.15f,
                        startColor: new Color(1f, 0.8f, 0.3f, 1f),
                        endColor:   new Color(1f, 0.4f, 0.1f, 0f));
                }
                else
                {
                    // Medium — AC5/AC10 range, standard ricochet
                    BuildSparks(root, surfacePoint, shader,
                        count: 45, speedMin: 7f, speedMax: 24f,
                        sizeMin: 0.05f, sizeMax: 0.18f,
                        colorA: new Color(1.0f, 0.85f, 0.4f, 1f),
                        colorB: new Color(1.0f, 0.5f,  0.1f, 1f),
                        gravity: 0.4f);
                    BuildTracer(root, surfacePoint, deflectDir, shader,
                        length: 12f, width: 0.07f,
                        startColor: new Color(1f, 0.9f, 0.6f, 1f),
                        endColor:   new Color(1f, 0.6f, 0.2f, 0f));
                }

                root.AddComponent<BERicochetAutoDestroy>();
                string vfxTier = light ? "Light" : heavy ? "Heavy" : "Medium";
                ModEntry.Log($"[BEDeflect] VFX spawned ({vfxTier} dmg={weaponDamage:F1}) at {surfacePoint:F1}");
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BEDeflect] ERROR spawning VFX: {ex}");
            }
        }

        // ── Ground impact VFX ─────────────────────────────────────────────────
        // Dirt burst + sparks at the terrain impact point, distinct from mech ricochet.
        // groundNormal is used to orient the particle hemisphere (facing away from terrain).
        internal static void SpawnGroundDeflectVFX(Vector3 groundPoint, Vector3 groundNormal)
        {
            try
            {
                var shader = Shader.Find("Legacy Shaders/Particles/Additive")
                          ?? Shader.Find("Particles/Additive")
                          ?? Shader.Find("Standard");

                if (shader == null)
                    ModEntry.Log("[BEDeflect] WARNING: no particle shader found — ground VFX may be invisible");

                var root = new GameObject("BEGroundDeflect");
                root.transform.position = groundPoint;

                // Large dirt/soil burst — clearly visible from any camera angle
                BuildGroundDirt(root, groundPoint, groundNormal, shader);

                // Heavy sparks — bright ricochet flash
                BuildSparks(root, groundPoint, shader,
                    count: 60, speedMin: 8f, speedMax: 35f,
                    sizeMin: 0.10f, sizeMax: 0.40f,
                    colorA: new Color(1.0f, 0.85f, 0.5f, 1f),
                    colorB: new Color(1.0f, 0.55f, 0.1f, 1f),
                    gravity: 0.8f);

                root.AddComponent<BERicochetAutoDestroy>();
                ModEntry.Log($"[BEDeflect] Ground VFX spawned at {groundPoint:F1}  normal={groundNormal:F2}  shader={(shader != null ? shader.name : "NULL")}");
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BEDeflect] ERROR spawning ground VFX: {ex}");
            }
        }

        private static void BuildGroundDirt(GameObject parent, Vector3 pos, Vector3 normal, Shader shader)
        {
            var go = new GameObject("Dirt");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = pos;

            var ps   = go.AddComponent<ParticleSystem>();
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.material   = new Material(shader);
            rend.renderMode = ParticleSystemRenderMode.Billboard;

            var main = ps.main;
            main.loop            = false;
            main.playOnAwake     = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(3f, 18f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(0.50f, 0.35f, 0.15f, 0.90f),
                new Color(0.30f, 0.20f, 0.10f, 0.60f));
            main.gravityModifier = 1.2f;
            main.maxParticles    = 50;

            var emission = ps.emission;
            emission.enabled      = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 35) });

            // Orient hemisphere so dirt sprays away from the terrain surface
            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius    = 0.25f;
            // Rotate shape so the flat base aligns with the terrain normal
            Quaternion q   = Quaternion.FromToRotation(Vector3.up, normal);
            shape.rotation = q.eulerAngles;

            var col  = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(new Color(0.55f, 0.40f, 0.18f), 0f),
                    new GradientColorKey(new Color(0.28f, 0.18f, 0.08f), 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.5f, 0.5f),
                    new GradientAlphaKey(0f,   1f)
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);
            ps.Play();
        }

        private static void BuildSparks(GameObject parent, Vector3 pos, Shader shader,
            int count, float speedMin, float speedMax, float sizeMin, float sizeMax,
            Color colorA, Color colorB, float gravity)
        {
            var go   = new GameObject("Sparks");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = pos;

            var ps   = go.AddComponent<ParticleSystem>();
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.material      = new Material(shader);
            rend.renderMode    = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.15f;
            rend.lengthScale   = 1.5f;

            var main = ps.main;
            main.loop            = false;
            main.playOnAwake     = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.12f, 0.40f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize       = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor      = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.gravityModifier = gravity;
            main.maxParticles    = count + 10;

            var emission = ps.emission;
            emission.enabled      = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius    = 0.12f;
            shape.rotation  = new Vector3(-90f, 0f, 0f);

            var col  = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(new Color(colorA.r, colorA.g, colorA.b), 0f),
                    new GradientColorKey(new Color(colorB.r, colorB.g, colorB.b), 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.35f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);
            ps.Play();
        }

        private static void BuildTracer(GameObject parent, Vector3 origin, Vector3 dir,
            Shader shader, float length, float width, Color startColor, Color endColor)
        {
            var go = new GameObject("Tracer");
            go.transform.SetParent(parent.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material      = new Material(shader);
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth    = width;
            lr.endWidth      = width * 0.12f;
            lr.startColor    = startColor;
            lr.endColor      = endColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.SetPosition(0, origin);
            lr.SetPosition(1, origin + dir * length);
            go.AddComponent<BETracerFader>();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Register every mech at combat start
    // ──────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(Mech), "InitStats")]
    public static class Mech_InitStats_BEDeflect_Patch
    {
        public static void Postfix(Mech __instance)
        {
            try
            {
                DeflectorShared.AllMechs.Add(__instance);
                float tonnage = __instance.MechDef?.Chassis?.Tonnage ?? -1f;
                var   wc      = Settings.Instance.GetWeightClass(tonnage);
                ModEntry.Log($"[BEDeflect] Registered {__instance.DisplayName}" +
                             $" ({tonnage:F0}T {wc.Name}" +
                             $" ×{wc.DeflectChanceMultiplier:F2} dmgFrac={wc.DamageFraction:F2})");
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BEDeflect] ERROR in Mech_InitStats: {ex}");
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  WeaponEffect.Update — detect, queue, and execute deflects
    // ──────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(WeaponEffect), "Update")]
    public static class WeaponEffect_Update_BEDeflect_Patch
    {
        public static void Postfix(WeaponEffect __instance)
        {
            try
            {
                if (DeflectorShared.AllMechs.Count == 0) return;
                if (!IsBallisticProjectile(__instance)) return;

                DeflectorShared.EnsureFields();

                int    id       = __instance.GetInstanceID();
                string typeName = __instance.GetType().Name;

                // ── Verbose type trace (fires once per unique effect instance) ──
                // Only when asked for: this ran for every idle, pooled effect on every frame (428k lines, 38 MB in one
                // session, 2026-10-22) and was the mod's largest per-frame cost.
                if (Settings.Instance.DebugLogging &&
                    !DeflectorShared.Diagnosed.Contains(id) &&
                    !DeflectorShared.Deflected.Contains(id))
                {
                    var stateRaw = DeflectorShared.CurrentStateField.GetValue(__instance);
                    float tRaw   = (float)DeflectorShared.TField.GetValue(__instance);
                    ModEntry.Log($"[BEDeflect] TRACE {typeName} id={id}  state={stateRaw}  t={tRaw:F3}");
                }

                var state = (WeaponEffect.WeaponEffectState)
                    DeflectorShared.CurrentStateField.GetValue(__instance);
                if (state != WeaponEffect.WeaponEffectState.Firing) return;

                float t  = (float)DeflectorShared.TField.GetValue(__instance);

                if (t >= 1f) return;
                if (DeflectorShared.Deflected.Contains(id)) return;

                // ── First frame — queue ───────────────────────────────────────
                if (!DeflectorShared.Diagnosed.Contains(id))
                {
                    DeflectorShared.Diagnosed.Add(id);
                    Vector3 sp = (Vector3)DeflectorShared.StartPosField.GetValue(__instance);
                    Vector3 ep = (Vector3)DeflectorShared.EndPosField.GetValue(__instance);
                    if (Settings.Instance.DebugLogging)
                        ModEntry.Log($"[BEDeflect] DIAG {typeName} id={id} t={t:F3}" +
                                     $" | start={sp:F1} end={ep:F1}");
                    DeflectorShared.QueueDeferredDeflect(id, sp, ep, typeName, __instance);
                }

                // ── Execute mech deflect when bullet reaches surface ──────────
                if (DeflectorShared.PendingDeflects.TryGetValue(id, out var pending))
                {
                    if (t >= pending.tHit)
                    {
                        DeflectorShared.EndPosField.SetValue(__instance, pending.newEndPos);
                        DeflectorShared.Deflected.Add(id);
                        DeflectorShared.PendingDeflects.Remove(id);

                        // Record outcome for DamageLocation to consume
                        DeflectorShared.RecentDeflects[pending.mechGuid ?? ""] =
                            new DeflectorShared.DeflectOutcome
                            {
                                damageFraction = pending.damageFraction,
                                timestamp      = Time.time
                            };

                        Vector3 deflectDir = (pending.newEndPos - pending.surfacePoint).normalized;
                        DeflectorShared.SpawnDeflectVFX(pending.surfacePoint, deflectDir,
                                                         pending.weaponDamage);

                        // Combat floatie — "DEFLECTED  -X" on the deflecting mech
                        var deflectMech = DeflectorShared.AllMechs
                            .Find(m => m != null && m.GUID == pending.mechGuid);
                        if (deflectMech != null)
                        {
                            float shownDmg = pending.weaponDamage * pending.damageFraction;
                            string floatieText = $"[Deflected] {shownDmg:F0} Armor Damage";
                            DeflectorShared.ShowFloatie(deflectMech, floatieText,
                                FloatieMessage.MessageNature.Neutral);
                            ModEntry.Log($"[BEDeflect]   Floatie → \"{floatieText}\" on {deflectMech.DisplayName}");
                        }

                        ModEntry.Log($"[BEDeflect] DEFLECT {typeName} id={id}" +
                                     $" t={t:F3} surface={pending.surfacePoint:F1}" +
                                     $" | dmg ×{pending.damageFraction:F2}" +
                                     $" ({pending.weaponDamage:F0} → {pending.weaponDamage * pending.damageFraction:F1})");
                    }
                }

                // ── Execute ground deflect when bullet nears terrain ───────────
                if (DeflectorShared.PendingGroundDeflects.TryGetValue(id, out var gp))
                {
                    if (t >= gp.tHit)
                    {
                        DeflectorShared.EndPosField.SetValue(__instance, gp.newEndPos);
                        DeflectorShared.Deflected.Add(id);
                        DeflectorShared.PendingGroundDeflects.Remove(id);

                        DeflectorShared.SpawnGroundDeflectVFX(gp.groundPoint, gp.groundNormal);

                        // Apply secondary mech damage if the bounce ray hit someone
                        if (!string.IsNullOrEmpty(gp.secondaryMechGuid))
                        {
                            var target = DeflectorShared.AllMechs
                                .Find(m => m != null && m.GUID == gp.secondaryMechGuid);
                            if (target != null && !target.IsDead)
                            {
                                DeflectorShared.ApplyGroundSecondaryDamage(
                                    target, gp.secondaryLoc, gp.secondaryDamage);
                                float origDmg = gp.secondaryDamage / Settings.Instance.GroundDeflectDamageFraction;
                                DeflectorShared.ShowFloatie(target,
                                    $"{origDmg:F0} DMG  RICOCHET  -{gp.secondaryDamage:F0}",
                                    FloatieMessage.MessageNature.Neutral);
                                ModEntry.Log($"[BEDeflect] Ground secondary DAMAGE → {target.DisplayName}" +
                                             $" {gp.secondaryLoc} {gp.secondaryDamage:F1}");
                            }
                        }

                        ModEntry.Log($"[BEDeflect] GROUND DEFLECT {typeName} id={id}" +
                                     $" t={t:F3} point={gp.groundPoint:F1}");
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BEDeflect] ERROR in WeaponEffect patch: {ex}");
            }
        }

        private static bool IsBallisticProjectile(WeaponEffect instance)
        {
            // Hard type exclusions — these are never physical projectiles
            if (instance is MissileEffect) return false;
            if (instance is LaserEffect)   return false;

            // Name-based exclusions — energy weapons that reach the class name check
            string name = instance.GetType().Name;
            if (name.Contains("Beam")    || name.Contains("Laser")  ||
                name.Contains("Pulse")   || name.Contains("PPC")    ||
                name.Contains("Missile") || name.Contains("Flamer") ||
                name.Contains("Plasma")  || name.Contains("Flam"))
                return false;

            // Default: treat as a physical projectile. Gauss, MagShot, and any
            // other solid-round weapons whose class name doesn't match the explicit
            // allowlist above still need deflection — returning true here catches them.
            return true;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  DamageLocation — scale damage on deflect
    //
    //  Armor already influenced deflect *chance* via armorScalar in RollDeflect.
    //  Here we simply apply the fixed DamageFraction from the mech's weight class.
    //  e.g. Atlas deflects AC20 (80 dmg): 80 × 0.08 = 6.4 damage applies.
    // ──────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(Mech), "DamageLocation")]
    public static class Mech_DamageLocation_BEDeflect_Patch
    {
        // Window kept short (300 ms) so that all DamageLocation calls from the
        // same weapon volley are reduced (multi-cluster, multi-pellet weapons call
        // DamageLocation once per hit), but a new attack 500 ms later is not affected.
        // We do NOT remove the entry on first call — we let it expire naturally.
        private const float OutcomeWindow = 0.3f;

        public static bool Prefix(Mech __instance, ref float totalArmorDamage)
        {
            // Ground secondary damage calls DamageLocation directly — don't intercept
            if (DeflectorShared.SuppressDeflectPatch) return true;

            try
            {
                string guid = __instance.GUID;
                if (guid == null) return true;

                if (!DeflectorShared.RecentDeflects.TryGetValue(guid, out var outcome))
                    return true;

                if (Time.time - outcome.timestamp > OutcomeWindow)
                {
                    DeflectorShared.RecentDeflects.Remove(guid);
                    return true;
                }

                float original        = totalArmorDamage;
                totalArmorDamage     *= outcome.damageFraction;
                float absorbed        = original - totalArmorDamage;
                DeflectorShared.StatDamageReduced  += absorbed;
                DeflectorShared.StatDamageApplied  += totalArmorDamage;
                // Do NOT remove — let the entry persist for the full OutcomeWindow so
                // every DamageLocation call in this volley (multi-cluster, etc.) is reduced.

                ModEntry.Log($"[BEDeflect] DamageLocation REDUCED  {__instance.DisplayName}" +
                             $"  {original:F1} × {outcome.damageFraction:F2} = {totalArmorDamage:F1}" +
                             $"  (absorbed {absorbed:F1})" +
                             $"  [session saved={DeflectorShared.StatDamageReduced:F1}]");
                return true;
            }
            catch (Exception ex)
            {
                ModEntry.Log($"[BEDeflect] ERROR in DamageLocation patch: {ex}");
            }
            return true;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Combat end cleanup
    // ──────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(CombatGameState), "OnCombatGameDestroyed")]
    public static class CombatGameState_OnCombatGameDestroyed_BEDeflect_Patch
    {
        public static void Postfix()
        {
            try { DeflectorShared.ClearOnCombatEnd(); }
            catch (Exception ex)
            { ModEntry.Log($"[BEDeflect] ERROR in combat end: {ex}"); }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  VFX MonoBehaviours
    // ──────────────────────────────────────────────────────────────────────────
    public class BERicochetAutoDestroy : MonoBehaviour
    {
        private float       _elapsed;
        private const float Duration = 2.5f;
        private void Update() { if ((_elapsed += Time.deltaTime) >= Duration) Destroy(gameObject); }
    }

    public class BETracerFader : MonoBehaviour
    {
        private LineRenderer _lr;
        private float        _elapsed;
        private const float  Duration = 0.4f;

        private void Start() { _lr = GetComponent<LineRenderer>(); }
        private void Update()
        {
            _elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(1f - (_elapsed / Duration));
            if (alpha <= 0f) { Destroy(gameObject); return; }
            if (_lr == null) return;
            var sc = _lr.startColor; sc.a = alpha; _lr.startColor = sc;
            var ec = _lr.endColor;   ec.a = 0f;    _lr.endColor   = ec;
        }
    }
}
