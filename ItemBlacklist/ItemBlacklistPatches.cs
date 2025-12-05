using UnityEngine;
using HarmonyLib;

namespace ItemBlacklist
{
    public static class ItemBlacklistPatches
    {
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
    }
}
