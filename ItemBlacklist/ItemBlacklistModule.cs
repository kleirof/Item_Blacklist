//#define DEBUG_MODE

using BepInEx;
using BepInEx.Configuration;
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
    [BepInDependency("alexandria.etgmod.alexandria")]
    [BepInPlugin(GUID, NAME, VERSION)]
    public class ItemBlacklistModule : BaseUnityPlugin
    {
        public const string GUID = "kleirof.etg.itemblacklist";
        public const string NAME = "Item Blacklist";
        public const string VERSION = "1.0.7";
        public const string TEXT_COLOR = "#AD8CFE";

        internal Dictionary<string, WeakBag<AmmonomiconPokedexEntry>> ammonomiconDictionary = new Dictionary<string, WeakBag<AmmonomiconPokedexEntry>>();
        internal static ItemBlacklistModule instance;

        internal HashSet<string> blacklist = new HashSet<string>();

        internal const string FINISHED_GUN_GUID = "90ff5de1e6af41d1baa820c6c0fc7647";

        internal Dictionary<string, WeakBag<WeightedGameObject>> weightGroups = new Dictionary<string, WeakBag<WeightedGameObject>>();
        internal WeakStrongDictionary<WeightedGameObject, float> oldWeights = new WeakStrongDictionary<WeightedGameObject, float>();
        internal HashSet<string> mainGuidSets = new HashSet<string>();
        internal HashSet<int> extraPages = new HashSet<int>();

        private ConfigEntry<string> savePath;
        private string finalSavePath;

        public void Start()
        {
            instance = this;

            savePath = Config.Bind(
                "ItemBlacklist",
                "SavePath",
                "[@BepInExConfigPath@]/blacklist_save.bl",
                "存档路径。支持以下变量：[@GameSavePath@] - 游戏存档目录；[@BepInExConfigPath@] - BepInEx配置目录；[@ModFolderPath@] - Mod所在目录。示例：[@BepInExConfigPath@]/blacklist_save.bl     Save path. The following variables are supported: [@GameSavePath@] - game save directory; [@BepInExConfigPath@] - BepInEx configuration directory; [@ModFolderPath@] - directory containing mods. Example: [@BepInExConfigPath@]/blacklist_save.bl"
            );

            ETGModMainBehaviour.WaitForGameManagerStart(GMStart);
        }

        public void GMStart(GameManager g)
        {
            Log($"{NAME} v{VERSION} started successfully.", TEXT_COLOR);

            Harmony harmony = new Harmony(GUID);
            harmony.PatchAll();

            finalSavePath = ResolveSavePath(savePath.Value);

            g.StartCoroutine(DelayInitialize());
        }

        private string ResolveSavePath(string pathWithVariables)
        {
            if (instance == null)
                return GetDefaultPath();

            if (string.IsNullOrEmpty(pathWithVariables))
                return GetDefaultPath();

            try
            {
                string resolved = pathWithVariables;

                resolved = resolved.Replace("[@GameSavePath@]", SaveManager.SavePath ?? ".");
                resolved = resolved.Replace("[@BepInExConfigPath@]", Paths.ConfigPath ?? ".");
                resolved = resolved.Replace("[@ModFolderPath@]", ETGMod.FolderPath(instance) ?? ".");

                string fullPath = Path.GetFullPath(resolved);

                return ValidateResolvedPath(fullPath) ? fullPath : GetDefaultPath();
            }
            catch
            {
                return GetDefaultPath();
            }
        }

        private static bool ValidateResolvedPath(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);

                string dir = Path.GetDirectoryName(fullPath);
                string file = Path.GetFileName(fullPath);

                return !string.IsNullOrEmpty(dir) &&
                       !string.IsNullOrEmpty(file) &&
                       !file.Any(c => Path.GetInvalidFileNameChars().Contains(c));
            }
            catch
            {
                return false;
            }
        }

        private static string GetDefaultPath()
        {
            return Path.Combine(Paths.ConfigPath ?? ".", "blacklist_save.bl");
        }

        public static void Log(string text, string color = "FFFFFF")
        {
            ETGModConsole.Log($"<color={color}>{text}</color>");
        }

        internal void AddToWeightedGameObjectGroup(string groupId, WeightedGameObject weighted)
        {
            if (!weightGroups.TryGetValue(groupId, out var bag))
            {
                bag = new WeakBag<WeightedGameObject>(4);
                weightGroups[groupId] = bag;
            }
            bag.Add(weighted);
            mainGuidSets.Add(groupId);
        }

        internal void AddToAmmonomiconPokedexEntryGroup(string groupId, AmmonomiconPokedexEntry pokedexEntry)
        {
            if (!ammonomiconDictionary.TryGetValue(groupId, out var bag))
            {
                bag = new WeakBag<AmmonomiconPokedexEntry>(4);
                ammonomiconDictionary[groupId] = bag;
            }
            bag.Add(pokedexEntry);
        }

        private IEnumerator DelayInitialize()
        {
            yield return null;
            yield return null;
            yield return null;

            List<WeightedGameObject> gunWeights = GameManager.Instance?.RewardManager?.GunsLootTable?.defaultItemDrops?.elements;
            List<WeightedGameObject> itemWeights = GameManager.Instance?.RewardManager?.ItemsLootTable?.defaultItemDrops?.elements;

            yield return null;
            yield return null;
            AmmonomiconController.EnsureExistence();
            yield return null;
            yield return null;

            BanSpriteController.AddBanSpriteToCollection();
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

            var genericLoots = Resources.FindObjectsOfTypeAll<GenericLootTable>()
                                .Where(obj => obj != null && obj.GetInstanceID() != 0)
                                .ToArray();

            foreach (var lootTable in genericLoots)
            {
                var elements = lootTable?.defaultItemDrops?.elements;
                if (elements == null)
                    continue;
                foreach (var weight in elements)
                {
                    PickupObject pickupObject = weight?.gameObject?.GetComponent<PickupObject>();
                    if (pickupObject != null)
                    {
                        string guid = pickupObject.encounterTrackable?.TrueEncounterGuid ?? pickupObject.GetComponent<EncounterTrackable>()?.TrueEncounterGuid;
                        if (string.IsNullOrEmpty(guid))
                            continue;
                        oldWeights[weight] = weight.weight;
                        AddToWeightedGameObjectGroup(guid, weight);
                    }
                }
            }
            foreach (var entry in gunAmmonomicons)
            {
                var guid = entry?.linkedEncounterTrackable?.myGuid;
                if (!string.IsNullOrEmpty(guid) && mainGuidSets.Contains(guid))
                {
                    AddToAmmonomiconPokedexEntryGroup(guid, entry);
                }
            }
            foreach (var entry in itemAmmonomicons)
            {
                var guid = entry?.linkedEncounterTrackable?.myGuid;
                if (!string.IsNullOrEmpty(guid) && mainGuidSets.Contains(guid))
                {
                    AddToAmmonomiconPokedexEntryGroup(guid, entry);
                }
            }

            LoadBlacklist();

#if DEBUG_MODE
            DebugSetAll();
#endif

            SetAllWeightsToZero();

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
            if (!weightGroups.TryGetValue(guid, out var groups))
                return;
            foreach (var weightedGameObject in groups)
            {
                if (!oldWeights.TryGetValue(weightedGameObject, out var value))
                    return;
                weightedGameObject.weight = value;
            }
        }

        internal void SetAllWeightsToZero()
        {
            foreach (string guid in blacklist)
            {
                SetWeightToZero(guid);
            }
        }

        internal void SetWeightToZero(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;
            if (!weightGroups.TryGetValue(guid, out var groups))
                return;
            foreach (var weightedGameObject in groups)
            {
                if (weightedGameObject == null)
                    continue;
                if (weightedGameObject.weight <= Mathf.Epsilon)
                    continue;
                oldWeights[weightedGameObject] = weightedGameObject.weight;
                weightedGameObject.weight = 0f;
            }
        }

        internal void UpdateSavedEntry(string guid, AmmonomiconPokedexEntry ammonomiconEntry)
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
            if (!blacklist.Contains(guid))
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
                string filePath = finalSavePath;
                if (string.IsNullOrEmpty(filePath))
                    return;

                var oldSet = File.Exists(filePath)
                    ? new HashSet<string>(JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(filePath)) ?? new List<string>())
                    : new HashSet<string>();

                var newSet = new HashSet<string>();

                foreach (var guid in oldSet)
                {
                    if (!string.IsNullOrEmpty(guid) && !mainGuidSets.Contains(guid))
                        newSet.Add(guid);
                }

                foreach (var guid in blacklist)
                {
                    if (!string.IsNullOrEmpty(guid) && mainGuidSets.Contains(guid))
                        newSet.Add(guid);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(filePath, JsonConvert.SerializeObject(newSet.ToList(), Formatting.Indented));

                Debug.Log($"Blacklist共保存 {newSet.Count} 项，保存路径 {filePath}\nBlacklist saved {newSet.Count} items, save path {filePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Blacklist保存失败: {ex.Message} Blacklist save failed: {ex.Message}");
            }
        }

        public void LoadBlacklist()
        {
            try
            {
                string filePath = finalSavePath;
                if (string.IsNullOrEmpty(filePath))
                    return;

                if (!File.Exists(filePath))
                {
                    Debug.Log("黑名单文件不存在。 Blacklist file does not exist.");
                    blacklist.Clear();
                    return;
                }

                string json = File.ReadAllText(filePath);
                var fileList = JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();

                blacklist.Clear();
                foreach (var guid in fileList)
                {
                    if (!string.IsNullOrEmpty(guid) && mainGuidSets.Contains(guid))
                    {
                        blacklist.Add(guid);
                    }
                }

                foreach (var item in blacklist)
                {
                    if (!ammonomiconDictionary.TryGetValue(item, out var group))
                        continue;
                    foreach (var ammonomicon in group)
                        UpdateSavedEntry(item, ammonomicon);
                }

                Debug.Log($"Blacklist加载，文件中有 {fileList.Count} 项，加载了 {blacklist.Count} 项有效条目，加载路径 {filePath}\n" +
                         $"Blacklist loaded, {fileList.Count} items in file, {blacklist.Count} valid items loaded, load path {filePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Blacklist加载失败: {ex.Message} Blacklist load failed: {ex.Message}");
                blacklist.Clear();
            }
        }

        private void OnApplicationQuit()
        {
            SaveBlacklist();
        }
    }
}