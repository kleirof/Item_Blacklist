using Alexandria.ItemAPI;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ItemBlacklist
{
    public class BanSpriteController : MonoBehaviour
    {
        private tk2dClippedSprite banSprite;
        private AmmonomiconPokedexEntry pokedexEntry;

        private static int banSpriteId = -1;

        void Awake()
        {
            CreateBanSprite();
        }

        void LateUpdate()
        {
            if (banSprite != null)
            {
                if (pokedexEntry != null)
                {
                    UpdateBanSpritePosition(banSprite, pokedexEntry);
                    pokedexEntry.UpdateClipping(banSprite);
                }
            }
        }

        private void CreateBanSprite()
        {
            if (AmmonomiconController.Instance?.CurrentLeftPageRenderer == null)
                return;

            if (banSpriteId == -1) return;

            banSprite = AmmonomiconController.Instance.CurrentLeftPageRenderer.AddSpriteToPage<tk2dClippedSprite>(
                AmmonomiconController.Instance.EncounterIconCollection,
                banSpriteId
            );

            if (banSprite != null)
            {
                banSprite.SetSprite(AmmonomiconController.Instance.EncounterIconCollection, banSpriteId);
                banSprite.transform.parent = transform;
            }

            pokedexEntry = GetComponent<AmmonomiconPokedexEntry>();
        }

        private void UpdateBanSpritePosition(tk2dClippedSprite banSprite, AmmonomiconPokedexEntry entry)
        {
            if (banSprite == null || entry == null || entry.m_childSprite == null || entry.m_bgSprite == null)
                return;

            Vector3 worldCenter = entry.m_childSprite.WorldCenter.ToVector3ZisY(0f);
            Bounds banBounds = banSprite.GetBounds();
            Vector3 banSize = banBounds.size;
            Vector3 centeredPosition = worldCenter - new Vector3(banSize.x / 2f, banSize.y / 2f, 0f);
            banSprite.transform.position = centeredPosition.WithZ(entry.m_childSprite.transform.position.z - 0.1f);
        }

        void OnDestroy()
        {
            if (banSprite != null && banSprite.gameObject != null)
                Destroy(banSprite.gameObject);
        }

        internal static void UpdateBanSprites(AmmonomiconPageRenderer pageRenderer)
        {
            List<AmmonomiconPokedexEntry> pokedexEntries = pageRenderer?.GetPokedexEntries();
            if (pokedexEntries == null)
                return;

            foreach (var pokedexEntry in pokedexEntries)
            {
                if (pokedexEntry == null || pokedexEntry.gameObject == null)
                    continue;

                var databaseEntry = pokedexEntry.linkedEncounterTrackable;
                if (databaseEntry == null)
                    continue;

                string guid = databaseEntry.myGuid;
                var blacklist = ItemBlacklistModule.instance?.blacklist;
                if (string.IsNullOrEmpty(guid) || blacklist == null)
                    continue;

                bool shouldBan = blacklist.Contains(guid);
                bool alreadyHasBan = HasBanSprite(pokedexEntry);

                if (shouldBan && !alreadyHasBan)
                    AddBanSprite(pokedexEntry);
                else if (!shouldBan && alreadyHasBan)
                    RemoveBanSprite(pokedexEntry);
            }
        }

        private static bool HasBanSprite(AmmonomiconPokedexEntry entry)
        {
            return entry != null && entry.gameObject != null &&
                   entry.gameObject.GetComponent<BanSpriteController>() != null;
        }

        private static void AddBanSprite(AmmonomiconPokedexEntry entry)
        {
            if (entry == null || entry.gameObject == null)
                return;

            entry.gameObject.AddComponent<BanSpriteController>();
        }

        private static void RemoveBanSprite(AmmonomiconPokedexEntry entry)
        {
            if (entry == null || entry.gameObject == null)
                return;

            var controller = entry.gameObject.GetComponent<BanSpriteController>();
            if (controller != null)
                Destroy(controller);
        }

        internal static void AddBanSpriteToCollection()
        {
            try
            {
                if (banSpriteId == -1)
                    banSpriteId = SpriteBuilder.AddSpriteToCollection("ItemBlacklist.ban_sprite", AmmonomiconController.ForceInstance.EncounterIconCollection);
            }
            catch (Exception ex)
            {
                Debug.LogError($"添加禁止图标失败：{ex.Message} Failed to add ban sprite: {ex.Message}");
            }
        }
    }
}