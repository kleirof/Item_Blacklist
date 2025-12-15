using UnityEngine;
using HarmonyLib;
using MonoMod.Cil;
using System.Reflection;
using Mono.Cecil.Cil;
using System;

namespace ItemBlacklist
{
    public static class ItemBlacklistPatches
    {
        public static void EmitCall<T>(this ILCursor iLCursor, string methodName, Type[] parameters = null, Type[] generics = null)
        {
            MethodInfo methodInfo = AccessTools.Method(typeof(T), methodName, parameters, generics);
            iLCursor.Emit(OpCodes.Call, methodInfo);
        }

        [HarmonyPatch(typeof(AmmonomiconPokedexEntry), nameof(AmmonomiconPokedexEntry.Awake))]
        public static class AmmonomiconPokedexEntryAwakePatch
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
                if (ammonomiconEntry.IsEquipmentPage)
                    return;
                EncounterDatabaseEntry databaseEntry = ammonomiconEntry.linkedEncounterTrackable;
                if (databaseEntry == null)
                    return;
                if (databaseEntry.pickupObjectId == -1)
                    return;
                string guid = databaseEntry.myGuid;
                var blacklist = ItemBlacklistModule.instance?.blacklist;
                var ammonomicon = ItemBlacklistModule.instance?.ammonomiconDictionary;
                dfSlicedSprite dfSlicedSprite = ammonomiconEntry?.transform?.Find("Sliced Sprite")?.GetComponent<dfSlicedSprite>();
                if (string.IsNullOrEmpty(guid) || dfSlicedSprite == null || blacklist == null || ammonomicon == null)
                    return;

                if (blacklist.Contains(guid))
                {
                    blacklist.Remove(guid);
                    Color32 color = dfSlicedSprite.Color;
                    color.g = 255;
                    color.b = 255;
                    dfSlicedSprite.Color = color;
                    ItemBlacklistModule.instance.RollbackWeight(guid);
                    if (ammonomiconEntry.m_button != null && ammonomiconEntry.m_button.HasFocus)
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
                        if (ammonomiconEntry.m_button != null && ammonomiconEntry.m_button.HasFocus)
                            AkSoundEngine.PostEvent("Play_UI_menu_select_01", GameManager.Instance.gameObject);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(LootData), nameof(LootData.GetItemForPlayer))]
        public static class LootDataGetItemForPlayerPatch
        {
            [HarmonyPrefix]
            public static void LootDataGetItemForPlayerPrefix()
            {
                ItemBlacklistModule.instance?.SetWeightsToZero();
            }
        }

        [HarmonyPatch(typeof(RewardManager), nameof(RewardManager.GetItemForPlayer))]
        public static class RewardManagerGetItemForPlayerPatch
        {
            [HarmonyPrefix]
            public static void RewardManagerGetItemForPlayerPrefix()
            {
                ItemBlacklistModule.instance?.SetWeightsToZero();
            }
        }

        [HarmonyPatch(typeof(AmmonomiconController), nameof(AmmonomiconController.CloseAmmonomicon))]
        public static class CloseAmmonomiconPatch
        {
            [HarmonyPostfix]
            public static void CloseAmmonomiconPostfix()
            {
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
    }
}
