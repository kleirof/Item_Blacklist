using UnityEngine;
using HarmonyLib;
using MonoMod.Cil;
using System.Reflection;
using Mono.Cecil.Cil;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ItemBlacklist
{
    public static class ItemBlacklistPatches
    {
        private static EncounterTrackable cachedTargetTrackable;

        public static void EmitCall<T>(this ILCursor iLCursor, string methodName, Type[] parameters = null, Type[] generics = null)
        {
            MethodInfo methodInfo = AccessTools.Method(typeof(T), methodName, parameters, generics);
            iLCursor.Emit(OpCodes.Call, methodInfo);
        }

        public static T GetFieldInEnumerator<T>(object instance, string fieldNamePattern)
        {
            return (T)instance.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.Name.Contains("$" + fieldNamePattern) || f.Name.Contains("<" + fieldNamePattern + ">") || f.Name == fieldNamePattern)
                .GetValue(instance);
        }

        public static bool TheNthTime(this Func<bool> predict, int n = 1)
        {
            for (int i = 0; i < n; ++i)
            {
                if (!predict())
                    return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(AmmonomiconController), nameof(AmmonomiconController.HandleTurnToNextPage), MethodType.Enumerator)]
        public class HandleTurnToNextPagePatch
        {
            [HarmonyPostfix]
            public static void HandleTurnToNextPagePostfix(ref bool __result)
            {
                if (!__result && cachedTargetTrackable != null)
                {
                    AmmonomiconPokedexEntry pokedexEntry = AmmonomiconController.Instance?.CurrentLeftPageRenderer?
                        .GetPokedexEntry(cachedTargetTrackable);

                    if (pokedexEntry != null)
                    {
                        pokedexEntry.ForceFocus();
                        AmmonomiconController.Instance.StartCoroutine(FlashPokedexEntry(pokedexEntry));
                        cachedTargetTrackable = null;
                    }
                }
            }

            private static void SetEntryColor(HashSet<string> blacklist, string guid, ref Color32 color)
            {
                if (!blacklist.Contains(guid))
                {
                    color.g = 255;
                    color.b = 255;
                }
                else
                {
                    color.g = 153;
                    color.b = 153;
                }
            }

            private static IEnumerator FlashPokedexEntry(AmmonomiconPokedexEntry pokedexEntry)
            {
                if (pokedexEntry == null) 
                    yield break;

                dfSlicedSprite dfSlicedSprite = pokedexEntry?.transform?.Find("Sliced Sprite")?.GetComponent<dfSlicedSprite>();
                if (dfSlicedSprite == null) 
                    yield break;

                var databaseEntry = pokedexEntry.linkedEncounterTrackable;
                var blacklist = ItemBlacklistModule.instance?.blacklist;
                if (databaseEntry == null || blacklist == null)
                    yield break;
                string guid = databaseEntry.myGuid;
                if (string.IsNullOrEmpty(guid))
                    yield break;

                Color32 normalColor = dfSlicedSprite.Color;
                Color32 flashColor = new Color32(254, 224, 0, 255);

                int flashCount = 5;
                float flashDuration = 0.15f;

                for (int i = 0; i < flashCount; i++)
                {
                    dfSlicedSprite.Color = flashColor;
                    yield return new WaitForSecondsRealtime(flashDuration);
                    SetEntryColor(blacklist, guid, ref normalColor);
                    dfSlicedSprite.Color = normalColor;
                    yield return new WaitForSecondsRealtime(flashDuration);
                }

                SetEntryColor(blacklist, guid, ref normalColor);
                dfSlicedSprite.Color = normalColor;
            }
        }

        [HarmonyPatch(typeof(AmmonomiconPokedexEntry), nameof(AmmonomiconPokedexEntry.Awake))]
        public class AmmonomiconPokedexEntryAwakePatch
        {
            [HarmonyPostfix]
            public static void AmmonomiconPokedexEntryAwakePostfix(AmmonomiconPokedexEntry __instance)
            {
                if (__instance?.m_button != null)
                    __instance.m_button.Click += OnEntryClick;
            }

            private static void OnEntryClick(dfControl control, dfMouseEventArgs keyEvent)
            {
                if (keyEvent.Buttons != dfMouseButtons.Right)
                    return;
                AmmonomiconPokedexEntry ammonomiconEntry = control?.GetComponent<AmmonomiconPokedexEntry>();
                if (ammonomiconEntry == null)
                    return;
                if (ammonomiconEntry.encounterState != AmmonomiconPokedexEntry.EncounterState.ENCOUNTERED)
                    return;

                var databaseEntry = ammonomiconEntry.linkedEncounterTrackable;
                if (databaseEntry == null)
                    return;
                if (databaseEntry.pickupObjectId == -1)
                    return;
                string guid = databaseEntry.myGuid;
                var ammonomicon = ItemBlacklistModule.instance?.ammonomiconDictionary;
                if (string.IsNullOrEmpty(guid) || ammonomicon == null)
                    return;

                if (!ammonomiconEntry.IsEquipmentPage)
                {

                    var blacklist = ItemBlacklistModule.instance?.blacklist;
                    var dfSlicedSprite = ammonomiconEntry?.transform?.Find("Sliced Sprite")?.GetComponent<dfSlicedSprite>();
                    if (dfSlicedSprite == null || blacklist == null)
                        return;

                    if (blacklist.Contains(guid))
                    {
                        blacklist.Remove(guid);
                        Color32 color = dfSlicedSprite.Color;
                        color.g = 255;
                        color.b = 255;
                        dfSlicedSprite.Color = color;
                        ItemBlacklistModule.instance.RollbackWeight(guid);
                        if (ammonomiconEntry.m_button != null && ammonomiconEntry.m_button.HasFocus && GameManager.HasInstance)
                            AkSoundEngine.PostEvent("Play_UI_menu_select_01", GameManager.Instance.gameObject);
                    }
                    else
                    {
                        if (ammonomicon.ContainsKey(guid))
                        {
                            blacklist.Add(guid);
                            Color32 color = dfSlicedSprite.Color;
                            color.g = 153;
                            color.b = 153;
                            dfSlicedSprite.Color = color;
                            ItemBlacklistModule.instance.SetWeightToZero(guid);
                            if (ammonomiconEntry.m_button != null && ammonomiconEntry.m_button.HasFocus && GameManager.HasInstance)
                                AkSoundEngine.PostEvent("Play_UI_menu_select_01", GameManager.Instance.gameObject);
                        }
                    }
                }
                else
                {
                    var ammonomiconController = AmmonomiconController.HasInstance ? AmmonomiconController.Instance : null;
                    if (ammonomiconController == null)
                        return;
                    if (!ammonomicon.ContainsKey(guid))
                        return;
                    var pickupObject = PickupObjectDatabase.GetById(databaseEntry.pickupObjectId);
                    if (pickupObject == null)
                        return;
                    EncounterTrackable targetTrackable = pickupObject?.gameObject?.GetComponent<EncounterTrackable>();
                    if (targetTrackable == null)
                        return;
                    cachedTargetTrackable = targetTrackable;
                    var bookmarks = ammonomiconController.m_AmmonomiconInstance?.bookmarks;
                    if (bookmarks == null)
                        return;
                    var index = pickupObject is Gun ? 1 : 2;
                    if (index >= bookmarks.Length)
                        return;
                    bookmarks[index].IsCurrentPage = true;
                    if (ammonomiconEntry.m_button != null && ammonomiconEntry.m_button.HasFocus && GameManager.HasInstance)
                        AkSoundEngine.PostEvent("Play_UI_menu_select_01", GameManager.Instance.gameObject);
                }
            }
        }

        [HarmonyPatch(typeof(LootData), nameof(LootData.GetItemForPlayer))]
        public class LootDataGetItemForPlayerPatchClass
        {
            [HarmonyPrefix]
            public static void LootDataGetItemForPlayerPrefix()
            {
                ItemBlacklistModule.instance?.SetAllWeightsToZero();
            }

            [HarmonyILManipulator]
            public static void LootDataGetItemForPlayerPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (((Func<bool>)(() =>
                    crs.TryGotoNext(MoveType.After,
                    x => x.MatchCall<UnityEngine.Object>("op_Inequality")
                    ))).TheNthTime(3))
                {
                    crs.Emit(OpCodes.Ldloc_S, (byte)11);
                    crs.EmitCall<LootDataGetItemForPlayerPatchClass>(nameof(LootDataGetItemForPlayerPatchClass.LootDataGetItemForPlayerPatchCall));
                }
            }

            private static bool LootDataGetItemForPlayerPatchCall(bool orig, PickupObject pickupObject)
            {
                if (pickupObject == null)
                    return orig;
                string guid = pickupObject.encounterTrackable?.TrueEncounterGuid ?? pickupObject.GetComponent<EncounterTrackable>()?.TrueEncounterGuid;
                if (string.IsNullOrEmpty(guid))
                    return orig;
                var blacklist = ItemBlacklistModule.instance?.blacklist;
                if (blacklist == null)
                    return orig;
                if (blacklist.Contains(guid))
                    return false;
                return orig;
            }
        }

        [HarmonyPatch(typeof(RewardManager), nameof(RewardManager.GetItemForPlayer))]
        public class RewardManagerGetItemForPlayerPatchClass
        {
            [HarmonyPrefix]
            public static void RewardManagerGetItemForPlayerPrefix()
            {
                ItemBlacklistModule.instance?.SetAllWeightsToZero();
            }

            [HarmonyILManipulator]
            public static void RewardManagerGetItemForPlayerPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (((Func<bool>)(() =>
                    crs.TryGotoNext(MoveType.After,
                    x => x.MatchCall<UnityEngine.Object>("op_Inequality")
                    ))).TheNthTime(2))
                {
                    crs.Emit(OpCodes.Ldloc_S, (byte)7);
                    crs.EmitCall<RewardManagerGetItemForPlayerPatchClass>(nameof(RewardManagerGetItemForPlayerPatchClass.RewardManagerGetItemForPlayerPatchCall));
                }
            }

            private static bool RewardManagerGetItemForPlayerPatchCall(bool orig, PickupObject pickupObject)
            {
                if (pickupObject == null)
                    return orig;
                string guid = pickupObject.encounterTrackable?.TrueEncounterGuid ?? pickupObject.GetComponent<EncounterTrackable>()?.TrueEncounterGuid;
                if (string.IsNullOrEmpty(guid))
                    return orig;
                var blacklist = ItemBlacklistModule.instance?.blacklist;
                if (blacklist == null)
                    return orig;
                if (blacklist.Contains(guid))
                    return false;
                return orig;
            }
        }

        [HarmonyPatch(typeof(GunberMuncherController), nameof(GunberMuncherController.GetItemForPlayer))]
        public class GunberMuncherControllerGetItemForPlayerPatchClass
        {
            [HarmonyPrefix]
            public static void GunberMuncherControllerGetItemForPlayerPrefix()
            {
                ItemBlacklistModule.instance?.SetAllWeightsToZero();
            }

            [HarmonyILManipulator]
            public static void GunberMuncherControllerGetItemForPlayerPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (((Func<bool>)(() =>
                    crs.TryGotoNext(MoveType.After,
                    x => x.MatchCall<UnityEngine.Object>("op_Inequality")
                    ))).TheNthTime(5))
                {
                    crs.Emit(OpCodes.Ldloc_S, (byte)9);
                    crs.EmitCall<GunberMuncherControllerGetItemForPlayerPatchClass>(nameof(GunberMuncherControllerGetItemForPlayerPatchClass.GunberMuncherControllerGetItemForPlayerPatchCall));
                }
            }

            private static bool GunberMuncherControllerGetItemForPlayerPatchCall(bool orig, PickupObject pickupObject)
            {
                if (pickupObject == null)
                    return orig;
                string guid = pickupObject.encounterTrackable?.TrueEncounterGuid ?? pickupObject.GetComponent<EncounterTrackable>()?.TrueEncounterGuid;
                if (string.IsNullOrEmpty(guid))
                    return orig;
                var blacklist = ItemBlacklistModule.instance?.blacklist;
                if (blacklist == null)
                    return orig;
                if (blacklist.Contains(guid))
                    return false;
                return orig;
            }
        }

        [HarmonyPatch(typeof(AmmonomiconController), nameof(AmmonomiconController.CloseAmmonomicon))]
        public class CloseAmmonomiconPatch
        {
            [HarmonyPostfix]
            public static void CloseAmmonomiconPostfix()
            {
                cachedTargetTrackable = null;
                ItemBlacklistModule.instance?.SetAllWeightsToZero();
                ItemBlacklistModule.instance?.SaveBlacklist();
            }
        }

        [HarmonyPatch(typeof(LootEngine), nameof(LootEngine.SpewLoot), new Type[] { typeof(GameObject), typeof(Vector3) })]
        public class SpewLootPatchClass
        {
            [HarmonyILManipulator]
            public static void SpewLootPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (crs.TryGotoNext(MoveType.After,
                    x => x.MatchLdfld<PickupObject>("PickupObjectId")
                    ))
                {
                    crs.EmitCall<SpewLootPatchClass>(nameof(SpewLootPatchClass.SpewLootPatchCall));
                }
            }

            private static int SpewLootPatchCall(int orig)
            {
                if (orig != GlobalItemIds.UnfinishedGun)
                    return orig;
                ItemBlacklistModule module = ItemBlacklistModule.instance;
                if (module == null)
                    return orig;
                if (module.blacklist.Contains(ItemBlacklistModule.FINISHED_GUN_GUID))
                    return 1;
                return orig;
            }
        }

        [HarmonyPatch(typeof(LootEngine), nameof(LootEngine.SpawnItem))]
        public class SpawnItemPatchClass
        {
            [HarmonyILManipulator]
            public static void SpawnItemPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (crs.TryGotoNext(MoveType.After,
                    x => x.MatchLdfld<PickupObject>("PickupObjectId")
                    ))
                {
                    crs.EmitCall<SpawnItemPatchClass>(nameof(SpawnItemPatchClass.SpawnItemPatchCall));
                }
            }

            private static int SpawnItemPatchCall(int orig)
            {
                if (orig != GlobalItemIds.UnfinishedGun)
                    return orig;
                ItemBlacklistModule module = ItemBlacklistModule.instance;
                if (module == null)
                    return orig;
                if (module.blacklist.Contains(ItemBlacklistModule.FINISHED_GUN_GUID))
                    return 1;
                return orig;
            }
        }

        [HarmonyPatch(typeof(LootData), nameof(LootData.GetItemsForPlayer))]
        public class GetItemsForPlayerPatchClass
        {
            [HarmonyILManipulator]
            public static void GetItemsForPlayerPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (crs.TryGotoNext(MoveType.After,
                    x => x.MatchLdfld<PickupObject>("PickupObjectId")
                    ))
                {
                    crs.EmitCall<GetItemsForPlayerPatchClass>(nameof(GetItemsForPlayerPatchClass.GetItemsForPlayerPatchCall));
                }
            }

            private static int GetItemsForPlayerPatchCall(int orig)
            {
                if (orig != GlobalItemIds.UnfinishedGun)
                    return orig;
                ItemBlacklistModule module = ItemBlacklistModule.instance;
                if (module == null)
                    return orig;
                if (module.blacklist.Contains(ItemBlacklistModule.FINISHED_GUN_GUID))
                    return 1;
                return orig;
            }
        }

        [HarmonyPatch(typeof(AmmonomiconController), nameof(AmmonomiconController.LoadPageUIAtPath))]
        public class LoadPageUIAtPathPatch
        {
            [HarmonyPostfix]
            public static void LoadPageUIAtPathPostfix(AmmonomiconController __instance, AmmonomiconPageRenderer.PageType pageType)
            {
                if ((int)pageType <= (int)AmmonomiconPageRenderer.PageType.DEATH_RIGHT)
                    return;
                var extraPages = ItemBlacklistModule.instance?.extraPages;
                var mainGuidSets = ItemBlacklistModule.instance?.mainGuidSets;
                if (extraPages == null || mainGuidSets == null)
                    return;
                if (extraPages.Contains((int)pageType))
                    return;
                extraPages.Add((int)pageType);
                foreach (var entry in __instance.m_extantPageMap?[pageType]?.m_pokedexEntries)
                {
                    var guid = entry?.linkedEncounterTrackable?.myGuid;
                    if (string.IsNullOrEmpty(guid) || !mainGuidSets.Contains(guid))
                        continue;

                    ItemBlacklistModule.instance?.AddToAmmonomiconPokedexEntryGroup(guid, entry);
                    ItemBlacklistModule.instance?.UpdateSavedEntry(guid, entry);
                }
            }
        }

        [HarmonyPatch(typeof(AmmonomiconPageRenderer), nameof(AmmonomiconPageRenderer.ConstructRectanglePageLayout), MethodType.Enumerator)]
        public class ConstructRectanglePageLayoutPatch
        {
            [HarmonyPostfix]
            public static void ConstructRectanglePageLayoutPostfix(ref bool __result, object __instance)
            {
                if (__result)
                    return;

                AmmonomiconPageRenderer self = GetFieldInEnumerator<AmmonomiconPageRenderer>(__instance, "this");
                if (self == null)
                    return;

                if (self.pageType == AmmonomiconPageRenderer.PageType.EQUIPMENT_LEFT)
                    BanSpriteController.UpdateBanSprites(self);
            }
        }
    }
}