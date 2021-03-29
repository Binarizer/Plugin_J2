using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using BepInEx;
using BepInEx.Configuration;
using Heluo;
using Heluo.Events;
using Heluo.Data;
using Heluo.Utility;

namespace J2
{
    // bug修复
    public class HookFeaturesAndFixes : IHook
    {
        static ConfigEntry<bool> fixWeaponSlots;
        static ConfigEntry<bool> fixInTeamExp;
        static ConfigEntry<bool> fixNpcExpWaste;

        public void OnRegister(BaseUnityPlugin plugin)
        {
            fixWeaponSlots = plugin.Config.Bind("bug修正", "武器孔数修正", false, "原设定为一般武器也带孔");
            fixInTeamExp = plugin.Config.Bind("游戏设定", "在队队友可获得属性成长", false, "在队成员升级时可获得不在队成长值");
            fixNpcExpWaste = plugin.Config.Bind("bug修正", "NPC不再浪费经验", false, "原版会因你设的秘籍系数而浪费经验到不存在的秘籍中，导致秘籍经验拉高游戏难度下降");
        }
        public void OnUpdate()
        {
        }

        // 武器孔数修正
        static int GetHoleCount(InventoryLevel level)
        {
            switch (level)
            {
                case InventoryLevel.White:
                    return 0;
                case InventoryLevel.Green:
                    return UnityEngine.Random.Range(0, 100) % 2;
                case InventoryLevel.Blue:
                    return UnityEngine.Random.Range(0, 100) % 2 + 1;
                case InventoryLevel.Purple:
                case InventoryLevel.Special:
                    return 2;
                default:
                    return 0;
            }
        }
        [HarmonyPostfix, HarmonyPatch(typeof(InventoryEx), "ItemId", MethodType.Setter)]
        public static void InventoryExPatch(ref InventoryEx __instance)
        {
            if (fixWeaponSlots.Value)
            {
                try
                {
                    var equipInventoryItem = __instance.Item as EquipInventoryItem;
                    if (equipInventoryItem.ParentType == InventoryType.Weapon)
                    {
                        __instance.QuenchHoleCount = GetHoleCount(__instance.Level);
                        __instance.QuenchEffect = new List<string>
                        {
                            null,
                            null
                        };
                    }
                }
                catch
                {
                }
            }
        }

        // 在队队友可获得属性成长 + NPC不浪费经验
        [HarmonyPrefix, HarmonyPatch(typeof(NpcExpEventHandler), "OnEvent", new Type[] { typeof(NpcExpEventArgs) })]
        public static bool ExpPatch1(ref NpcExpEventHandler __instance, NpcExpEventArgs e)
        {
            var t = Traverse.Create(__instance);
            var characterList = t.Field("characterList").GetValue<List<Character>>();
            var playData = t.Field("playData").GetValue<GameData>();
            var dataManager = t.Field("dataManager").GetValue<IDataProvider>();
            if (e.Exp > 0)
            {
                if (!characterList.HasData())
                {
                    return false;
                }
                GameDifficulty difficulty = playData.Difficulty;
                List<string> list = new List<string>();
                foreach (Character character in characterList)
                {
                    if (!Game.Teammates.Contains(character.Id))
                    {
                        NpcGrowingItem npcGrowingItem = dataManager.Get<NpcGrowingItem>(character.Id);
                        if (npcGrowingItem != null)
                        {
                            //float expPercentage = __instance.GetExpPercentage(npcGrowingItem.ExpPercentage, difficulty, npcGrowingItem.ManualLearnTalent);
                            float expPercentage = t.Method("GetExpPercentage", npcGrowingItem.ExpPercentage, difficulty, npcGrowingItem.ManualLearnTalent).GetValue<float>();
                            int count = Mathf.RoundToInt((float)e.Exp * expPercentage);
                            if (character.Level >= npcGrowingItem.MaxLevel)
                            {
                                list.Add(character.Id);
                            }
                            else
                            {
                                int dLevel = character.Level;
                                character.AddExp(count, false, false);
                                dLevel = character.Level - dLevel;
                                if (dLevel > 0 && npcGrowingItem.ManualLearnTalent == 0)
                                {
                                    for (int i = 0; i < dLevel; i++)
                                    {
                                        t.Method("LearnTalent", character).GetValue();// __instance.LearnTalent(character);
                                    }
                                }
                            }
                        }
                    }
                }
                if (list.HasData())
                {
                    using (List<string>.Enumerator enumerator2 = list.GetEnumerator())
                    {
                        while (enumerator2.MoveNext())
                        {
                            string id = enumerator2.Current;
                            int num = characterList.FindIndex((Character c) => c.Id == id);
                            if (num != -1)
                            {
                                characterList.RemoveAt(num);
                            }
                        }
                    }
                }
            }
            return false;
        }
        static int cacheCharacterLevel;
        [HarmonyPrefix, HarmonyPatch(typeof(Upgradeable), "AddExp", new Type[] { typeof(int) })]
        public static bool ExpPatch2(ref Upgradeable __instance, ref int count)
        {
            if (__instance.GetType() == typeof(Character))
            {
                Character c = __instance as Character;
                if (fixNpcExpWaste.Value && c.SkillTree.CurrentSkillTree == null)
                {
                    // 没有修炼秘籍，加回全部经验
                    count = (int)(count / (1.0f - Game.PlayData.SkillExpDistributionRate));
                }
                cacheCharacterLevel = c.Level;
            }
            return true;    // 继续执行提升等级
        }
        [HarmonyPostfix, HarmonyPatch(typeof(Upgradeable), "AddExp", new Type[] { typeof(int) })]
        public static void ExpPatch3(ref Upgradeable __instance, ref int count)
        {
            if (__instance.GetType() == typeof(Character))
            {
                Character c = __instance as Character;
                if (c.Level != cacheCharacterLevel && (fixInTeamExp.Value || !Game.Teammates.Contains(c.Id)))
                {
                    // 队伍没有，或者在队成长开启
                    int num3 = c.Level - cacheCharacterLevel;
                    NpcGrowingItem npcGrowingItem = Game.Data.Get<NpcGrowingItem>(c.Id);
                    if (npcGrowingItem != null)
                    {
                        int num4 = (Game.PlayData.Difficulty == GameDifficulty.Master) ? 2 : 1;
                        foreach (KeyValuePair<CharacterProperty, int> keyValuePair in npcGrowingItem.Property)
                        {
                            if (keyValuePair.Value > 0)
                            {
                                c.AddProperty(keyValuePair.Key, keyValuePair.Value * num3 * num4, false);
                            }
                        }
                    }
                }
            }
        }
    }
}
