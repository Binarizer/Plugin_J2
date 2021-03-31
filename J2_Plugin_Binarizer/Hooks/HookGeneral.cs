using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using BepInEx;
using BepInEx.Configuration;
using Heluo;
using Heluo.UI;
using Heluo.Data;
using Heluo.Data.Graph;
using Heluo.Events;
using Heluo.Flow;
using Heluo.Audio;
using Heluo.Utility;
using Heluo.Battle;
using Heluo.Features;

namespace J2
{
    // 一般游戏设定功能
    public class HookGenerals : IHook
    {
        static ConfigEntry<float> speedValue;
        static ConfigEntry<KeyCode> speedKey;
        static ConfigEntry<KeyCode> headKey;
        static ConfigEntry<KeyCode> tailKey;
        static ConfigEntry<bool> alwaysFullMember;
        static ConfigEntry<int> alwaysMemberCount;
        static ConfigEntry<int> alwaysBattleCount;
        static ConfigEntry<bool> alwaysAmbush;
        static ConfigEntry<bool> sharePoints;
        static ConfigEntry<bool> jumpTalent;

        public void OnRegister(BaseUnityPlugin plugin)
        {
            // configs
            speedValue = plugin.Config.Bind("游戏设定", "速度值", 1.5f, "调整速度值");
            speedKey = plugin.Config.Bind("游戏设定", "速度热键", KeyCode.F2, "开关速度调节");
            alwaysAmbush = plugin.Config.Bind("游戏设定", "全难度开启切磋", false, "全难度开启切磋(需重启游戏生效)");
            sharePoints = plugin.Config.Bind("游戏设定", "共享成就感悟点", false, "全队成员可享受成就感悟点");
            jumpTalent = plugin.Config.Bind("游戏设定", "跳点天赋", false, "花费5点跳点不连接的天赋");

            alwaysFullMember = plugin.Config.Bind("队伍调整", "开放队伍人数上限", false, "开启全难度固定队友数量（不受原版4-5-6限制）");
            alwaysMemberCount = plugin.Config.Bind("队伍调整", "队伍人数上限", 6, "全难度队友数量, 用队伍调整键调整可看到后备队友");
            headKey = plugin.Config.Bind("队伍调整", "队伍调整-提到前面", KeyCode.F4, "将选中队友放到队伍前面，主角后面");
            tailKey = plugin.Config.Bind("队伍调整", "队伍调整-放到队尾", KeyCode.F3, "将选中队友放到队伍末端");
            alwaysBattleCount = plugin.Config.Bind("队伍调整", "队伍战斗可上场数", 6, "上场战斗队友人数, 最小1，最大6（超过6需要dll支持），从主角开始算起");

            // specific patches
            {
                //var harmony = new Harmony("Generals"); 
                //var assembly = Assembly.GetAssembly(typeof(BattleFactory));
                //var listTypes = assembly.GetTypes().ToList();
                //var realType = listTypes.FindAll(t => t.Name.Contains("GenerateBattleCreateInfo")).First();    // <GenerateBattleCreateInfo>d__7
                //var source = realType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
                //var il = source.GetMethodBody().GetILAsByteArray();
                //int size = il.Length;
                //MethodRental.SwapMethodBody(realType, source.MetadataToken, il, il.Length, MethodRental.JitImmediate);
                //Console.WriteLine("source=" + source);
                //var transpiler = typeof(HookGenerals).GetMethod("TeammateCountBattle1");
                //Console.WriteLine("transpiler=" + transpiler);
                //harmony.Patch(source, transpiler: new HarmonyMethod(transpiler));
            }
        }

        public void OnUpdate()
        {
            if (Input.GetKeyDown(speedKey.Value))
            {
                Console.WriteLine("按了加速键");
                Time.timeScale = Time.timeScale == 1.0f ? Math.Max(0.1f, speedValue.Value) : 1.0f;
                Console.WriteLine("Time.timeScale=" + Time.timeScale);
            }
            if (teammatesController != null)
            {
                var teammateTagIndex = Traverse.Create(teammatesController).Field("teammateTagIndex").GetValue<int>();
                if (Input.GetKeyDown(tailKey.Value) && teammateTagIndex > 0)
                {
                    ICharacter c = orderedTeammates[teammateTagIndex];
                    orderedTeammates.RemoveAt(teammateTagIndex);
                    orderedTeammates.Add(c);
                    int count = teammates.GetValue<List<ICharacter>>().Count;
                    teammates.SetValue(orderedTeammates.GetRange(0, count));
                    teammatesController.UpdateView();
                }
                if (Input.GetKeyDown(headKey.Value) && teammateTagIndex > 0)
                {
                    ICharacter c = orderedTeammates[teammateTagIndex];
                    orderedTeammates.RemoveAt(teammateTagIndex);
                    orderedTeammates.Insert(1, c);
                    int count = teammates.GetValue<List<ICharacter>>().Count;
                    teammates.SetValue(orderedTeammates.GetRange(0, count));
                    teammatesController.UpdateView();
                }
            }
        }

        // 全难度固定队友
        [HarmonyPostfix, HarmonyPatch(typeof(Teammate), MethodType.Constructor, new Type[] { typeof(IEntityManager), typeof(EventRouter), typeof(GameData) })]
        public static void TeammateConstructorPatch(ref Teammate __instance)
        {
            Console.WriteLine("TeammatesController.ctor()");
            orderedTeammates = new List<ICharacter>();
        }
        [HarmonyPostfix, HarmonyPatch(typeof(Teammate), "TeammateMaxCount", MethodType.Getter)]
        public static void TeammateCountPatch(ref int __result)
        {
            if (alwaysFullMember.Value)
                __result = alwaysMemberCount.Value;
        }
        static bool bBattle = false;
        static Traverse teammates = null;
        static List<ICharacter> orderedTeammates = new List<ICharacter>();
        static TeammatesController teammatesController;
        [HarmonyPostfix, HarmonyPatch(typeof(TeammatesController), MethodType.Constructor, new Type[] { typeof(ITeammate), typeof(IView<ITeammate>[]) })]
        public static void TeammateCountBegin(ref TeammatesController __instance, ITeammate model)
        {
            Console.WriteLine("TeammatesController.ctor()");
            teammatesController = __instance;
            teammates = Traverse.Create(__instance).Field("teammates");
            var teammatesValue = teammates.GetValue<List<ICharacter>>();
            if (orderedTeammates.Count == 0)
            {
                orderedTeammates = teammatesValue.GetRange(0, teammatesValue.Count);
            }
            else
            {
                teammates.SetValue(orderedTeammates.GetRange(0, orderedTeammates.Count));
                teammatesValue = teammates.GetValue<List<ICharacter>>();
            }
            if (teammatesValue.Count > 6)
            {
                teammatesValue.RemoveRange(6, teammatesValue.Count - 6);
            }
        }
        [HarmonyPostfix, HarmonyPatch(typeof(TeammatesController), "OnClose")]
        public static void TeammateCountClose(ref TeammatesController __instance)
        {
            Console.WriteLine("TeammatesController.OnClose()");
            teammates = null;
            teammatesController = null;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(TeammatesController), "Model_ItemAdded", new Type[] { typeof(string) })]
        public static bool TeammateCountPatchAdd(ref TeammatesController __instance, string entityId)
        {
            CharacterData component = Game.EntityManager.GetComponent<CharacterData>(entityId);
            if (component == null)
            {
                return false;
            }
            orderedTeammates.Add(component.Character);
            teammates.SetValue(orderedTeammates.GetRange(0, orderedTeammates.Count));
            var teammatesValue = teammates.GetValue<List<ICharacter>>();
            if (teammatesValue.Count > 6)
            {
                teammatesValue.RemoveRange(6, teammatesValue.Count - 6);
            }
            __instance.UpdateView();
            return false;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(TeammatesController), "Model_ItemRemoved", new Type[] { typeof(string) })]
        public static bool TeammateCountPatchRemove(ref TeammatesController __instance, string entityId)
        {
            CharacterData component = Game.EntityManager.GetComponent<CharacterData>(entityId);
            if (component == null)
            {
                return false;
            }
            if (orderedTeammates.Contains(component.Character))
            {
                orderedTeammates.Remove(component.Character);
                teammates.SetValue(orderedTeammates.GetRange(0, orderedTeammates.Count));
                var teammatesValue = teammates.GetValue<List<ICharacter>>();
                if (teammatesValue.Count > 6)
                {
                    teammatesValue.RemoveRange(6, teammatesValue.Count - 6);
                }
                __instance.UpdateView();
            }
            return false;
        }
        [HarmonyPostfix, HarmonyPatch(typeof(TitleSave), "Teammates", MethodType.Getter)]
        public static void TeammateCountPatchSave(ref List<string> __result)
        {
            if (__result != null && __result.Count > 6)
                __result = __result.GetRange(0, 6);
        }
        [HarmonyPrefix, HarmonyPatch(typeof(BattleFactory), "GenerateBattleCreateInfo", new Type[] { typeof(BattleConfig), typeof(IEnumerable<CullingComponent>) })]
        public static bool TeammateCountBattle(ref BattleFactory __instance, BattleConfig config, IEnumerable<BattleCreateInfo> __result)
        {
            bBattle = true;
            return true;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(Game), "Save", new Type[] { typeof(int), typeof(SaveType), typeof(bool) })]
        public static bool TeammateCountSave()
        {
            bBattle = false;
            return true;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(Teammate), "GetAllEntityId")]
        public static bool TeammateCountBattle2(ref Teammate __instance, ref IEnumerable<string> __result)
        {
            var newList = (from t in __instance.GetAllData<CharacterData>() select t.Character as ICharacter).ToList();
            if (orderedTeammates.Count == 0)
            {
                orderedTeammates = newList;
            }
            else
            {
                orderedTeammates = (from i in orderedTeammates where newList.Contains(i) select i).ToList(); // remove
                orderedTeammates.AddRange(from i in newList where !orderedTeammates.Contains(i) select i);  // add new
            }
            var dict = Traverse.Create(__instance).Field("characterIdByEntityId").GetValue<Dictionary<string, string>>();
            var list = new List<string>();
            alwaysBattleCount.Value = Math.Max(alwaysBattleCount.Value, 1);
            alwaysBattleCount.Value = Math.Min(alwaysBattleCount.Value, 6);
            int count = bBattle ? alwaysBattleCount.Value : orderedTeammates.Count;
            for ( int i = 0; i < count; ++i)
            {
                var character = orderedTeammates[i];
                foreach (var item in dict)
                {
                    if (item.Value == character.Id)
                        list.Add(item.Key);
                }
            }
            __result = list.AsEnumerable();
            return false;
        }

        // 这些有各种各样问题和坑 想试试可以打开XD
        //[HarmonyPostfix, HarmonyPatch(typeof(BattleFactory), "GenerateBattleCreateInfo", new Type[] { typeof(BattleConfig), typeof(IEnumerable<CullingComponent>) })]
        //public static void TeammateCountBattleFactory(ref BattleFactory __instance, BattleConfig config, IEnumerable<BattleCreateInfo> __result)
        //{
        //    Console.WriteLine("config.PlayerUnitSettingType = " + config.PlayerUnitSettingType);
        //    foreach (var info in __result)
        //    {
        //        Console.WriteLine("加入战斗人物 = " + info.Name);
        //        Console.WriteLine("加入战斗阵营 = " + info.UnitParty);
        //    }
        //}
        //public static IEnumerable<CodeInstruction> TeammateCountBattle1(IEnumerable<CodeInstruction> instructions)
        //{
        //    if (alwaysBattleCount.Value > 6)
        //    {
        //        var codes = instructions.ToList();
        //        for (int i = 0; i < codes.Count - 1; ++i)
        //        {
        //            var code = codes[i];
        //            if (code.opcode == OpCodes.Ldc_I4_6 && codes[i + 1].opcode == OpCodes.Newarr)  // 原本为Ldc_I4_6, 所以只能上6人
        //            {
        //                code.opcode = OpCodes.Ldc_I4_S;
        //                code.operand = 127; // 差不多得了
        //                Console.WriteLine("修改指令code index = " + i); // 当前为264
        //                break;
        //            }
        //        }
        //        return codes.AsEnumerable();
        //    }
        //    return instructions;
        //}

        // 全难度切磋
        //[HarmonyTranspiler, HarmonyPatch(typeof(AmbushEventHandler), "OnEvent", new Type[] { typeof(AmbushEventArgs) })]
        public static IEnumerable<CodeInstruction> AmbushPatch(IEnumerable<CodeInstruction> instructions)
        {
            if (alwaysAmbush.Value)
            {
                var codes = instructions.ToList();
                codes[8].opcode = OpCodes.Ldc_I4_M1;   // 原本为Ldc_I4_2
                return codes.AsEnumerable();
            }
            return instructions;
        }

        // 全队共享感悟
        [HarmonyPrefix, HarmonyPatch(typeof(AddTalentPoint), "GetValue")]
        public static bool SharePointsPatch(AddTalentPoint __instance, bool __result)
        {
            if (sharePoints.Value)
            {
                List<String> characterIdList = new List<String>();
                foreach (CharacterData characterData in Game.EntityManager.GetComponents<CharacterData>())
                {
                    if (characterData != null && !characterIdList.Contains(characterData.Id))
                    {
                        characterIdList.Add(characterData.Id);
                        characterData.TalentData.NewPoint += __instance.Point;
                    }
                }
                __result = false;
                return false;
            }
            return true;
        }

        // 跳点天赋
        [HarmonyPrefix, HarmonyPatch(typeof(WgTalentTreeController), "NodeClick", new Type[] { typeof(TalentNode)})]
        public static bool JumpTalentPatch(WgTalentTreeController __instance, TalentNode talentNode)
        {
            var t = Traverse.Create(__instance);
            bool flag = t.Method("CheckCondition", talentNode).GetValue<bool>();//__instance.CheckCondition(talentNode);
            int a = 1;
            if (jumpTalent.Value)
            {
                a = (flag ? 1 : 5);
                flag = (flag || __instance.talentPoint >= talentNode.NeedPoints * a);
            }
            if (flag)
            {
                StringTableItem stringTableItem = Game.Data.Get<StringTableItem>("UITalentNodeClickDialog");
                string text = (stringTableItem != null) ? stringTableItem.StringValue : null;
                text = string.Format(text, talentNode.Item.Name, talentNode.NeedPoints * a);
                Action<bool> onResult = delegate (bool confirm)
                {
                    if (confirm)
                    {
                        talentNode.CurrentState = eSkillNodeType.Clicked;
                        __instance.Model.TalentData.ClickedNodes.Add(talentNode.Item.Id);
                        TalentNode talentNode2 = talentNode;
                        if (talentNode2 == null)
                        {
                            flag = false;
                        }
                        else
                        {
                            TalentItem item = talentNode2.Item;
                            flag = (((item != null) ? item.Values : null) != null);
                        }
                        if (flag)
                        {
                            __instance.Model.TalentData.AddValues(talentNode.Item.Values);
                        }
                        if (talentNode.Item != null && talentNode.Item.PropertyValues != null)
                        {
                            __instance.Model.PropertiesChange(talentNode.Item.PropertyValues, true, false);
                        }
                        Action<string> talentLearned = __instance.Model.TalentData.TalentLearned;
                        if (talentLearned != null)
                        {
                            talentLearned(talentNode.Item.Id);
                        }
                        __instance.talentPoint -= talentNode.NeedPoints * a;
                        Action<int> updateCurrentExp = __instance.UpdateCurrentExp;
                        if (updateCurrentExp != null)
                        {
                            updateCurrentExp(__instance.talentPoint);
                        }
                        __instance.Model.TalentData.NewPoint -= talentNode.NeedPoints * a;
                        t.Method("AdjacentUpdata", talentNode).GetValue();//__instance.AdjacentUpdata(talentNode);
                        Action<string> learnNode = __instance.LearnNode;
                        if (learnNode != null)
                        {
                            learnNode(talentNode.Id);
                        }
                        Action<int> onTalentPointChanged = __instance.OnTalentPointChanged;
                        if (onTalentPointChanged != null)
                        {
                            onTalentPointChanged(__instance.Model.TalentData.NewPoint);
                        }
                        if (talentNode.IsStart)
                        {
                            Action closeOtherStartNodes = __instance.CloseOtherStartNodes;
                            if (closeOtherStartNodes != null)
                            {
                                closeOtherStartNodes();
                            }
                        }
                        if (__instance.talentPoint == 0)
                        {
                            t.Method("UpdateCanClicked").GetValue();//__instance.UpdateCanClicked();
                        }
                        if (!talentNode.Item.OpenNextId.IsNullOrEmpty())
                        {
                            Action<string> openNextNode = __instance.OpenNextNode;
                            if (openNextNode != null)
                            {
                                openNextNode(talentNode.Item.OpenNextId);
                            }
                        }
                        new SoundEffect("LearnTalent").PlayOnce();
                        Heluo.Logger.Log("學會天賦" + talentNode.Item.Id + ". " + talentNode.Item.Name, Heluo.Logger.LogLevel.MESSAGE, "white", "NodeClick", "D:\\JingYong\\Assets\\Script\\UI\\View\\TalentTree\\WgSkillTreeController.cs", 874);
                        return;
                    }
                };
                Game.UI.ShowDialog(string.Empty, text, onResult, false).buttonConfirm.ConfrimSoundEffect = new SoundEffect(GameConfig.MuteSoundID);
            }
            return false;
        }
    }
}
