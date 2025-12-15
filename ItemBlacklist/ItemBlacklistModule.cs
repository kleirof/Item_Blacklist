//#define DEBUG_MODE

using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ItemBlacklist
{
    [BepInDependency("etgmodding.etg.mtgapi")]
    [BepInPlugin(GUID, NAME, VERSION)]
    public class ItemBlacklistModule : BaseUnityPlugin
    {
        public const string GUID = "kleirof.etg.itemblacklist";
        public const string NAME = "Item Blacklist";
        public const string VERSION = "1.0.2";
        public const string TEXT_COLOR = "#AD8CFE";

        internal Dictionary<string, AmmonomiconPokedexEntry> ammonomiconDictionary = new Dictionary<string, AmmonomiconPokedexEntry>();
        internal Dictionary<string, WeightedGameObject> weightDictionary = new Dictionary<string, WeightedGameObject>();
        internal Dictionary<string, float> weightValueDictionary = new Dictionary<string, float>();
        internal static ItemBlacklistModule instance;

        internal HashSet<string> blacklist = new HashSet<string>();
        internal const string BLACKLIST_PATH = "blacklist.json";

        internal const string FINISHED_GUN_GUID = "90ff5de1e6af41d1baa820c6c0fc7647";

        public void Start()
        {
            instance = this;

            ETGModMainBehaviour.WaitForGameManagerStart(GMStart);
        }

        public void GMStart(GameManager g)
        {
            Log($"{NAME} v{VERSION} started successfully.", TEXT_COLOR);

            Harmony harmony = new Harmony(GUID);
            harmony.PatchAll();

            g.StartCoroutine(DelayInitialize());
        }

        public static void Log(string text, string color = "FFFFFF")
        {
            ETGModConsole.Log($"<color={color}>{text}</color>");
        }

        private IEnumerator DelayInitialize()
        {
            yield return null;
            yield return null;
            yield return null;

            Dictionary<int, WeightedGameObject> weightIDDictionary = new Dictionary<int, WeightedGameObject>();
            List<WeightedGameObject> gunWeights = GameManager.Instance?.RewardManager?.GunsLootTable?.defaultItemDrops?.elements;
            List<WeightedGameObject> itemWeights = GameManager.Instance?.RewardManager?.ItemsLootTable?.defaultItemDrops?.elements;

            yield return null;
            yield return null;
            AmmonomiconController.EnsureExistence();
            yield return null;
            yield return null;
            while (AmmonomiconController.Instance?.m_extantPageMap == null)
            {
                yield return null;
            }
            while (!AmmonomiconController.Instance.m_extantPageMap.ContainsKey(AmmonomiconPageRenderer.PageType.GUNS_LEFT) || !AmmonomiconController.Instance.m_extantPageMap.ContainsKey(AmmonomiconPageRenderer.PageType.ITEMS_LEFT))
            {
                yield return null;
            }
            List<AmmonomiconPokedexEntry> gunAmmonomicons = AmmonomiconController.Instance.m_extantPageMap[AmmonomiconPageRenderer.PageType.GUNS_LEFT]?.m_pokedexEntries;
            List<AmmonomiconPokedexEntry> itemAmmonomicons = AmmonomiconController.Instance.m_extantPageMap[AmmonomiconPageRenderer.PageType.ITEMS_LEFT]?.m_pokedexEntries;
            if (gunWeights == null || itemWeights == null || gunAmmonomicons == null || itemAmmonomicons == null)
                yield break;

            foreach (var weight in gunWeights)
            {
                PickupObject pickupObject = weight?.gameObject?.GetComponent<PickupObject>();
                if (pickupObject != null)
                {
                    weightIDDictionary[pickupObject.PickupObjectId] = weight;
                    string guid = pickupObject.encounterTrackable?.TrueEncounterGuid ?? pickupObject.GetComponent<EncounterTrackable>()?.TrueEncounterGuid;
                    if (string.IsNullOrEmpty(guid))
                        continue;
                    weightDictionary[guid] = weight;
                    weightValueDictionary[guid] = weight.weight;
                }
            }
            foreach (var weight in itemWeights)
            {
                PickupObject pickupObject = weight?.gameObject?.GetComponent<PickupObject>();
                if (pickupObject != null)
                {
                    weightIDDictionary[pickupObject.PickupObjectId] = weight;
                    string guid = pickupObject.encounterTrackable?.TrueEncounterGuid ?? pickupObject.GetComponent<EncounterTrackable>()?.TrueEncounterGuid;
                    if (string.IsNullOrEmpty(guid))
                        continue;
                    weightDictionary[guid] = weight;
                    weightValueDictionary[guid] = weight.weight;
                }
            }
            foreach (var entry in gunAmmonomicons)
            {
                if (!string.IsNullOrEmpty(entry?.linkedEncounterTrackable?.myGuid) && weightIDDictionary.ContainsKey(entry.linkedEncounterTrackable.pickupObjectId))
                {
                    ammonomiconDictionary[entry.linkedEncounterTrackable.myGuid] = entry;
                }
            }
            foreach (var entry in itemAmmonomicons)
            {
                if (!string.IsNullOrEmpty(entry?.linkedEncounterTrackable?.myGuid) && weightIDDictionary.ContainsKey(entry.linkedEncounterTrackable.pickupObjectId))
                {
                    ammonomiconDictionary[entry.linkedEncounterTrackable.myGuid] = entry;
                }
            }
            foreach (var entry in ammonomiconDictionary)
            {
                if (!string.IsNullOrEmpty(entry.Key) && entry.Value != null)
                {
                    if (entry.Value.linkedEncounterTrackable != null && weightIDDictionary.TryGetValue(entry.Value.linkedEncounterTrackable.pickupObjectId, out var weighted))
                    {
                        weightDictionary[entry.Key] = weighted;
                        weightValueDictionary[entry.Key] = weighted.weight;
                    }
                }
            }

            LoadBlacklist();

#if DEBUG_MODE
            DebugSetAll();
#endif

            yield break;
        }

#if DEBUG_MODE
        private void DebugSetAll()
        {
            foreach (var entry in ammonomiconDictionary)
            {
                if (string.IsNullOrEmpty(entry.Key) || entry.Value == null)
                    continue;
                blacklist.Add(entry.Key);
                UpdateSavedEntry(entry.Value);
            }
        }
#endif

        internal void RollbackWeight(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;
            if (!weightDictionary.TryGetValue(guid, out var weightedGameObject))
                return;
            if (!weightValueDictionary.TryGetValue(guid, out var value))
                return;
            weightedGameObject.weight = value;
        }

        internal void StoreWeight(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;
            if (!weightDictionary.TryGetValue(guid, out var weightedGameObject))
                return;
            if (weightedGameObject == null)
                return;
            if (weightedGameObject.weight <= Mathf.Epsilon)
                return;
            weightValueDictionary[guid] = weightedGameObject.weight;
        }

        internal void SetWeightsToZero()
        {
            foreach (string guid in blacklist)
            {
                if (string.IsNullOrEmpty(guid))
                    continue;
                if (!weightDictionary.TryGetValue(guid, out var weighted))
                    continue;
                if (weighted == null)
                    continue;
                StoreWeight(guid);
                weighted.weight = 0f;
            }
        }

        private void UpdateSavedEntry(AmmonomiconPokedexEntry ammonomiconEntry)
        {
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
            dfSlicedSprite dfSlicedSprite = ammonomiconEntry?.transform?.Find("Sliced Sprite")?.GetComponent<dfSlicedSprite>();
            if (dfSlicedSprite == null || blacklist == null)
                return;

            Color32 color = dfSlicedSprite.Color;
            color.g = 153;
            color.b = 153;
            dfSlicedSprite.Color = color;
        }

        public void SaveBlacklist()
        {
            try
            {
                string filePath = Path.Combine(ETGMod.FolderPath(instance), BLACKLIST_PATH);

                var oldSet = File.Exists(filePath)
                    ? new HashSet<string>(JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(filePath)) ?? new List<string>())
                    : new HashSet<string>();

                var newSet = new HashSet<string>();

                foreach (var guid in oldSet)
                {
                    if (string.IsNullOrEmpty(guid))
                        continue;

                    if (weightDictionary.ContainsKey(guid))
                    {
                        if (blacklist.Contains(guid))
                            newSet.Add(guid);
                    }
                    else
                    {
                        newSet.Add(guid);
                    }
                }

                foreach (var guid in blacklist)
                {
                    if (!string.IsNullOrEmpty(guid) && weightDictionary.ContainsKey(guid))
                        newSet.Add(guid);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(filePath, JsonConvert.SerializeObject(newSet.ToList(), Formatting.Indented));

                Debug.Log($"Blacklist保存：{newSet.Count} 项。 Blacklist save: {newSet.Count} items");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Blacklist保存失败: {ex.Message}. Blacklist save failed: {ex.Message}");
            }
        }

        public void LoadBlacklist()
        {
            try
            {
                string filePath = Path.Combine(ETGMod.FolderPath(instance), BLACKLIST_PATH);

                if (!File.Exists(filePath))
                {
                    Debug.Log("黑名单文件不存在。 Blacklist file does not exist");
                    blacklist.Clear();
                    return;
                }

                string json = File.ReadAllText(filePath);
                var fileList = JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();

                blacklist.Clear();
                foreach (var guid in fileList)
                {
                    if (!string.IsNullOrEmpty(guid) && weightDictionary.ContainsKey(guid))
                    {
                        blacklist.Add(guid);
                    }
                }

                foreach (var item in blacklist)
                {
                    if (ammonomiconDictionary.TryGetValue(item, out var entry))
                        UpdateSavedEntry(entry);
                }

                Debug.Log($"Blacklist加载：文件中有 {fileList.Count} 项，加载了 {blacklist.Count} 项有效条目。 " +
                         $"Blacklist load: {fileList.Count} in file, {blacklist.Count} valid items loaded");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Blacklist加载失败: {ex.Message}. Blacklist load failed: {ex.Message}");
                blacklist.Clear();
            }
        }

        private void OnApplicationQuit()
        {
            SaveBlacklist();
        }
    }
}
