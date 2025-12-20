using UnityEngine;
using HarmonyLib;
using MonoMod.Cil;
using System.Reflection;
using Mono.Cecil.Cil;
using System;
using System.Linq;

namespace ItemBlacklist
{
    public static class ItemBlacklistPatches
    {
        public static void EmitCall<T>(this ILCursor iLCursor, string methodName, Type[] parameters = null, Type[] generics = null)
        {
            MethodInfo methodInfo = AccessTools.Method(typeof(T), methodName, parameters, generics);
            iLCursor.Emit(OpCodes.Call, methodInfo);
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
        public class LootDataGetItemForPlayerPatchClass
        {
            [HarmonyPrefix]
            public static void LootDataGetItemForPlayerPrefix()
            {
                ItemBlacklistModule.instance?.SetWeightsToZero();
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
                ItemBlacklistModule.instance?.SetWeightsToZero();
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

        [HarmonyPatch(typeof(AmmonomiconController), nameof(AmmonomiconController.CloseAmmonomicon))]
        public class CloseAmmonomiconPatch
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
