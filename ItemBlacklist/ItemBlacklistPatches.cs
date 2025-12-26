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
                if (ItemBlacklistModule.IsInBlacklist(pickupObject))
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
                if (ItemBlacklistModule.IsInBlacklist(pickupObject))
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
                if (ItemBlacklistModule.IsInBlacklist(pickupObject))
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

        [HarmonyPatch(typeof(PickupObjectDatabase), nameof(PickupObjectDatabase.GetRandomGun))]
        public class GetRandomGunPatchClass
        {
            [HarmonyILManipulator]
            public static void GetRandomGunPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (crs.TryGotoNext(MoveType.After,
                    x => x.MatchIsinst<Gun>()
                    ))
                {
                    crs.EmitCall<GetRandomGunPatchClass>(nameof(GetRandomGunPatchClass.GetRandomGunPatchCall));
                }
            }

            private static Gun GetRandomGunPatchCall(Gun orig)
            {
                if (orig == null)
                    return null;
                if (ItemBlacklistModule.IsInBlacklist(orig))
                    return null;
                return orig;
            }
        }

        [HarmonyPatch(typeof(PickupObjectDatabase), nameof(PickupObjectDatabase.GetRandomStartingGun))]
        public class GetRandomStartingGunPatchClass
        {
            [HarmonyILManipulator]
            public static void GetRandomStartingGunPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (crs.TryGotoNext(MoveType.After,
                    x => x.MatchIsinst<Gun>()
                    ))
                {
                    crs.EmitCall<GetRandomStartingGunPatchClass>(nameof(GetRandomStartingGunPatchClass.GetRandomStartingGunPatchCall));
                }
            }

            private static Gun GetRandomStartingGunPatchCall(Gun orig)
            {
                if (orig == null)
                    return null;
                if (ItemBlacklistModule.IsInBlacklist(orig))
                    return null;
                return orig;
            }
        }

        [HarmonyPatch(typeof(PickupObjectDatabase), nameof(PickupObjectDatabase.GetRandomGunOfQualities))]
        public class GetRandomGunOfQualitiesPatchClass
        {
            [HarmonyILManipulator]
            public static void GetRandomGunOfQualitiesPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (crs.TryGotoNext(MoveType.After,
                    x => x.MatchIsinst<Gun>()
                    ))
                {
                    crs.EmitCall<GetRandomGunOfQualitiesPatchClass>(nameof(GetRandomGunOfQualitiesPatchClass.GetRandomGunOfQualitiesPatchCall));
                }
            }

            private static Gun GetRandomGunOfQualitiesPatchCall(Gun orig)
            {
                if (orig == null)
                    return null;
                if (ItemBlacklistModule.IsInBlacklist(orig))
                    return null;
                return orig;
            }
        }

        [HarmonyPatch(typeof(PickupObjectDatabase), nameof(PickupObjectDatabase.GetRandomPassiveOfQualities))]
        public class GetRandomPassiveOfQualitiesPatchClass
        {
            [HarmonyILManipulator]
            public static void GetRandomPassiveOfQualitiesPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (crs.TryGotoNext(MoveType.After,
                    x => x.MatchIsinst<PassiveItem>()
                    ))
                {
                    crs.EmitCall<GetRandomPassiveOfQualitiesPatchClass>(nameof(GetRandomPassiveOfQualitiesPatchClass.GetRandomPassiveOfQualitiesPatchCall));
                }
            }

            private static PassiveItem GetRandomPassiveOfQualitiesPatchCall(PassiveItem orig)
            {
                if (orig == null)
                    return null;
                if (ItemBlacklistModule.IsInBlacklist(orig))
                    return null;
                return orig;
            }
        }

        [HarmonyPatch]
        public class GetRandomActiveOfQualitiesPatch
        {
            [HarmonyTargetMethod]
            public static MethodBase TargetMethod()
            {
                var qolConfigType = typeof(Gunfiguration.QoLConfig);

                var nestedType = qolConfigType.GetNestedType("RandomizeParadoxEquipmentBetterPatch",
                    BindingFlags.NonPublic | BindingFlags.Public);

                if (nestedType == null)
                {
                    return null;
                }

                var method = nestedType.GetMethod("GetRandomActiveOfQualities",
                    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public,
                    null,
                    new Type[] { typeof(System.Random), typeof(PickupObject.ItemQuality[]) },
                    null);

                return method;
            }

            [HarmonyILManipulator]
            public static void GetRandomPassiveOfQualitiesPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (crs.TryGotoNext(MoveType.After,
                    x => x.MatchIsinst<PlayerItem>()
                    ))
                {
                    crs.EmitCall<GetRandomActiveOfQualitiesPatch>(nameof(GetRandomActiveOfQualitiesPatch.GetRandomActiveOfQualitiesPatchCall));
                }
            }

            private static PlayerItem GetRandomActiveOfQualitiesPatchCall(PlayerItem orig)
            {
                if (orig == null)
                    return null;
                if (ItemBlacklistModule.IsInBlacklist(orig))
                    return null;
                return orig;
            }
        }

        [HarmonyPatch(typeof(LootEngine), nameof(LootEngine.DropItemWithoutInstantiating))]
        [HarmonyPatch(typeof(LootEngine), nameof(LootEngine.SpawnItem))]
        public class LootEnginePatch
        {
            private static void ReplaceIfBlacklisted(ref GameObject item)
            {
                if (ItemBlacklistModule.instance == null)
                    return;
                if (!ItemBlacklistModule.instance.rollIfBlacklisted)
                    return;

                var pickupObject = item.GetComponent<PickupObject>();
                if (pickupObject == null)
                    return;

                if (!ItemBlacklistModule.IsInBlacklist(pickupObject))
                    return;

                PickupObject replacement = null;
                var quality = pickupObject.quality;
                int oldId = pickupObject.PickupObjectId; 
                var localRandom = new System.Random(Environment.TickCount ^ oldId ^ UnityEngine.Random.Range(0, 65536));

                for (int attempt = 0; attempt < 15; attempt++)
                {
                    if (pickupObject is PassiveItem)
                    {
                        replacement = PickupObjectDatabase.GetRandomPassiveOfQualities(
                            localRandom,
                            new List<int> { oldId },
                            quality);
                    }
                    else if (pickupObject is Gun)
                    {
                        replacement = PickupObjectDatabase.GetRandomGunOfQualities(
                            localRandom,
                            new List<int> { oldId },
                            quality);
                    }
                    else if (pickupObject is PlayerItem)
                    {
                        replacement = GetRandomActiveOfQualities(
                            localRandom,
                            new List<int> { oldId },
                            quality);
                    }

                    if (replacement == null)
                        break;

                    if (!ItemBlacklistModule.IsInBlacklist(replacement))
                        break;
                    else
                    {
                        oldId = replacement.PickupObjectId;
                        replacement = null;
                    }
                }

                if (replacement != null)
                {
                    item = replacement.gameObject;
                }
            }

            public static PlayerItem GetRandomActiveOfQualities(System.Random usedRandom, List<int> excludedIDs, params PickupObject.ItemQuality[] qualities)
            {
                List<PlayerItem> list = new List<PlayerItem>();
                int totalChecked = 0;
                int blacklistedCount = 0;

                for (int i = 0; i < PickupObjectDatabase.Instance.Objects.Count; i++)
                {
                    var obj = PickupObjectDatabase.Instance.Objects[i];

                    if (obj == null || !(obj is PlayerItem))
                        continue;

                    totalChecked++;

                    if (ItemBlacklistModule.IsInBlacklist(obj))
                    {
                        blacklistedCount++;
                        continue;
                    }

                    if (obj.quality == PickupObject.ItemQuality.EXCLUDED ||
                        obj.quality == PickupObject.ItemQuality.SPECIAL)
                        continue;

                    if (obj is ContentTeaserItem)
                        continue;

                    if (Array.IndexOf(qualities, obj.quality) == -1)
                        continue;

                    if (excludedIDs != null && excludedIDs.Contains(obj.PickupObjectId))
                        continue;

                    EncounterTrackable component = obj.GetComponent<EncounterTrackable>();
                    if (component && component.PrerequisitesMet())
                    {
                        list.Add(obj as PlayerItem);
                    }
                }

                if (list.Count == 0)
                {
                    return null;
                }

                int num = usedRandom.Next(list.Count);
                var result = list[num];

                return result;
            }

            [HarmonyPrefix, HarmonyPatch(nameof(LootEngine.DropItemWithoutInstantiating))]
            public static void DropItemWithoutInstantiatingPrefix(ref GameObject item)
            {
                ReplaceIfBlacklisted(ref item);
            }

            [HarmonyPrefix, HarmonyPatch(nameof(LootEngine.SpawnItem))]
            public static void SpawnItemPrefix(ref GameObject item)
            {
                ReplaceIfBlacklisted(ref item);
            }
        }

        [HarmonyPatch]
        public class GetItemOfTypeAndQualityPatchClass
        {
            static MethodBase TargetMethod()
            {
                MethodInfo method = typeof(LootEngine).GetMethod("GetItemOfTypeAndQuality");
                return method.MakeGenericMethod(typeof(PickupObject));
            }

            [HarmonyILManipulator]
            public static void GetItemOfTypeAndQualityPatch(ILContext ctx)
            {
                ILCursor crs = new ILCursor(ctx);

                if (crs.TryGotoNext(MoveType.After,
                    x => x.MatchCallvirt<PickupObject>("PrerequisitesMet")
                    ))
                {
                    crs.Emit(OpCodes.Ldloc_2);
                    crs.Emit(OpCodes.Ldloc_1);
                    crs.EmitCall<GetItemOfTypeAndQualityPatchClass>(nameof(GetItemOfTypeAndQualityPatchClass.GetItemOfTypeAndQualityPatchCall_1));
                }
                crs.Index = 0;

                if (((Func<bool>)(() =>
                    crs.TryGotoNext(MoveType.After,
                    x => x.MatchCallvirt<PickupObject>("PrerequisitesMet")
                    ))).TheNthTime(2))
                {
                    crs.Emit(OpCodes.Ldloc_S, (byte)6);
                    crs.EmitCall<GetItemOfTypeAndQualityPatchClass>(nameof(GetItemOfTypeAndQualityPatchClass.GetItemOfTypeAndQualityPatchCall_2));
                }
            }

            private static bool GetItemOfTypeAndQualityPatchCall_1(bool orig, int i, List<WeightedGameObject> compiledRawItems)
            {
                var pickupObject = compiledRawItems?[i]?.gameObject?.GetComponent<PickupObject>();
                if (pickupObject == null)
                    return orig;
                if (ItemBlacklistModule.IsInBlacklist(pickupObject))
                    return false;
                return orig;
            }

            private static bool GetItemOfTypeAndQualityPatchCall_2(bool orig, int j)
            {
                var pickupObject = PickupObjectDatabase.Instance?.Objects[j];
                if (pickupObject == null)
                    return orig;
                if (ItemBlacklistModule.IsInBlacklist(pickupObject))
                    return false;
                return orig;
            }
        }
    }
}