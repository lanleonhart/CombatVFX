// Plays a recipe when a weapon tagged vfx_<recipe> fires: a postfix on WeaponEffect.Update that leaves on a hash
// lookup for every effect that is not ours, and on the first Firing frame reads the effect's start and end points and
// starts an FxPlayer. With hideStock the stock projectile object is kept inactive for the shot; damage still flows
// through the stock effect. A hotkey (the game's test tools on, or FxTestHotkey in settings.json) plays the most
// recently saved recipe from the selected 'Mech to its target so a recipe can be tuned against a live mission.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BattleTech;
using BattleTech.UI;
using HarmonyLib;
using UnityEngine;

namespace CombatVFX.Fx
{
    /// <summary>The private WeaponEffect fields the player reads, found once.</summary>
    internal static class FxReflect
    {
        internal static FieldInfo Weapon, StartPos, EndPos, CurrentState, ProjectileTransform;
        private static bool _done;

        internal static void Ensure()
        {
            if (_done) return;
            _done = true;
            Weapon = AccessTools.Field(typeof(WeaponEffect), "weapon");
            StartPos = AccessTools.Field(typeof(WeaponEffect), "startPos");
            EndPos = AccessTools.Field(typeof(WeaponEffect), "endPos");
            CurrentState = AccessTools.Field(typeof(WeaponEffect), "currentState");
            ProjectileTransform = AccessTools.Field(typeof(WeaponEffect), "projectileTransform");
            if (StartPos == null || EndPos == null || CurrentState == null) ModEntry.Log("[Fx] WeaponEffect fields not found; recipes will not play (game version changed?)");
        }
    }

    internal static class FxShared
    {
        internal static readonly HashSet<int> NotOurs = new HashSet<int>();           // effect ids with no recipe (answered once)
        internal static readonly HashSet<int> Played = new HashSet<int>();            // effect ids already played this shot
        internal static readonly HashSet<int> Hidden = new HashSet<int>();            // effect ids whose stock projectile stays hidden
        internal static readonly HashSet<string> LoggedNames = new HashSet<string>();

        internal static void Clear()
        {
            NotOurs.Clear(); Played.Clear(); Hidden.Clear(); LoggedNames.Clear();
            foreach (FxPlayer p in UnityEngine.Object.FindObjectsOfType<FxPlayer>()) UnityEngine.Object.Destroy(p.gameObject);
            FxLibrary.CombatEnded();
        }
    }

    [HarmonyPatch(typeof(WeaponEffect), "Update")]
    internal static class WeaponEffect_Update_Fx
    {
        // keeps a hidden stock projectile hidden; the stock code re-enables it as it moves
        private static void Prefix(WeaponEffect __instance)
        {
            if (FxShared.Hidden.Count == 0 || !FxShared.Hidden.Contains(__instance.GetInstanceID())) return;
            try
            {
                var projT = FxReflect.ProjectileTransform?.GetValue(__instance) as Transform;
                if (projT != null && projT.gameObject.activeSelf) projT.gameObject.SetActive(false);
            }
            catch { }
        }

        private static void Postfix(WeaponEffect __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                if (FxShared.NotOurs.Contains(id) || FxShared.Played.Contains(id)) return;
                FxReflect.Ensure();
                if (FxReflect.StartPos == null) { FxShared.NotOurs.Add(id); return; }
                Weapon weapon = FxReflect.Weapon?.GetValue(__instance) as Weapon;
                if (weapon == null) return;                                   // not known yet; ask again next frame
                FxRecipe r = FxLibrary.ForWeapon(weapon);
                if (r == null || !r.HasLayers) { FxShared.NotOurs.Add(id); return; }
                var state = (WeaponEffect.WeaponEffectState)FxReflect.CurrentState.GetValue(__instance);
                if (state != WeaponEffect.WeaponEffectState.Firing) return;
                var projT = FxReflect.ProjectileTransform?.GetValue(__instance) as Transform;
                if (projT != null && projT.gameObject.name.Contains("Dummy")) { FxShared.NotOurs.Add(id); return; }
                FxShared.Played.Add(id);
                Vector3 start = (Vector3)FxReflect.StartPos.GetValue(__instance);
                Vector3 end = (Vector3)FxReflect.EndPos.GetValue(__instance);
                if (r.hideStock && projT != null) { projT.gameObject.SetActive(false); FxShared.Hidden.Add(id); }
                FxPlayer.Play(r, start, end);
                if (FxShared.LoggedNames.Add(r.name)) ModEntry.Log("[Fx] playing recipe '" + r.name + "' for " + (weapon.defId ?? "weapon") + (r.hideStock ? " (stock projectile hidden)" : "") + "; further shots of it are not logged");
            }
            catch (Exception e) { ModEntry.Log("[Fx] weapon effect: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(WeaponEffect), "OnComplete")]
    internal static class WeaponEffect_OnComplete_Fx
    {
        private static void Postfix(WeaponEffect __instance)
        {
            int id = __instance.GetInstanceID();
            FxShared.Played.Remove(id); FxShared.Hidden.Remove(id);           // the effect object is pooled and fires again
        }
    }

    [HarmonyPatch(typeof(CombatGameState), "OnCombatGameDestroyed")]
    internal static class CombatGameState_Destroyed_Fx
    {
        private static void Postfix() { try { FxShared.Clear(); } catch { } }
    }

    /// <summary>Ctrl+Shift+E plays the most recently saved recipe from the selected 'Mech to its target (or 200 m ahead);
    /// Ctrl+Shift+R cycles which recipe the hotkey plays. With the game's test tools on, or FxTestHotkey: true in settings.json.</summary>
    public class FxTestHotkey : MonoBehaviour
    {
        private static FxTestHotkey _instance;
        private int _pick = -1;

        internal static void Ensure()
        {
            if (_instance != null || !(DebugBridge.TestToolsEnabled || FxSettings.TestHotkey)) return;
            var go = new GameObject("CombatVFX_TestHotkey");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FxTestHotkey>();
            ModEntry.Log("[Fx] test hotkey on: Ctrl+Shift+E plays the latest recipe from the selected 'Mech, Ctrl+Shift+R cycles recipes");
        }

        private void Update()
        {
            bool mod = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (!mod) return;
            if (Input.GetKeyDown(KeyCode.R))
            {
                var keys = FxLibrary.Keys;
                if (keys.Count == 0) { ModEntry.Log("[Fx] no recipes to cycle"); return; }
                _pick = (_pick + 1) % keys.Count;
                ModEntry.Log("[Fx] hotkey recipe: " + keys[_pick]);
            }
            if (Input.GetKeyDown(KeyCode.E)) Play();
        }

        private void Play()
        {
            try
            {
                var keys = FxLibrary.Keys;
                FxRecipe r = _pick >= 0 && _pick < keys.Count ? FxLibrary.Get(keys[_pick]) : FxLibrary.Latest();
                if (r == null) { ModEntry.Log("[Fx] hotkey: no recipe in " + FxLibrary.Dir); return; }
                var hud = FindObjectOfType<CombatHUD>();
                AbstractActor actor = hud?.SelectedActor;
                if (actor == null) { ModEntry.Log("[Fx] hotkey: select a 'Mech first"); return; }
                Vector3 start = actor.CurrentPosition + Vector3.up * 9f + actor.GameRep.transform.forward * 3f;
                ICombatant target = hud.SelectionHandler?.ActiveState?.AllTargetedCombatants?.FirstOrDefault();
                Vector3 end = target != null ? target.CurrentPosition + Vector3.up * 6f : start + actor.GameRep.transform.forward * 200f;
                FxPlayer.Play(r, start, end);
                ModEntry.Log("[Fx] hotkey played '" + r.name + "' from " + actor.DisplayName + (target != null ? " to " + target.DisplayName : " 200 m ahead"));
            }
            catch (Exception e) { ModEntry.Log("[Fx] hotkey: " + e.Message); }
        }
    }
}
