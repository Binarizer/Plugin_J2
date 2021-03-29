using System;
using System.Linq;
using System.Collections.Generic;
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
using Heluo.Controller;

namespace J2
{
    // 一般游戏设定功能
    public class HookGenerals : IHook
    {
        static ConfigEntry<float> speedValue;
        static ConfigEntry<KeyCode> speedKey;
        static ConfigEntry<bool> alwaysFullMember;
        static ConfigEntry<bool> alwaysAmbush;
        static ConfigEntry<bool> sharePoints;
        static ConfigEntry<bool> jumpTalent;

        public void OnRegister(BaseUnityPlugin plugin)
        {
            speedValue = plugin.Config.Bind("游戏设定", "速度值", 1.5f, "调整速度值");
            speedKey = plugin.Config.Bind("游戏设定", "速度热键", KeyCode.F2, "开关速度调节");
            alwaysFullMember = plugin.Config.Bind("游戏设定", "全难度6队友", false, "全难度6队友");
            alwaysAmbush = plugin.Config.Bind("游戏设定", "全难度开启切磋", false, "全难度开启切磋(需重启游戏生效)");
            sharePoints = plugin.Config.Bind("游戏设定", "共享成就感悟点", false, "全队成员可享受成就感悟点");
            jumpTalent = plugin.Config.Bind("游戏设定", "跳点天赋", false, "花费5点跳点不连接的天赋");
        }
        public void OnUpdate()
        {
            if (Input.GetKeyDown(speedKey.Value))
            {
                Time.timeScale = Time.timeScale == 1.0f ? Math.Max(0.1f, speedValue.Value) : 1.0f;
            }
        }

        // 全难度6队友
        [HarmonyPostfix, HarmonyPatch(typeof(Teammate), "TeammateMaxCount", MethodType.Getter)]
        public static void TeammateCountPatch(ref int __result)
        {
            if (alwaysFullMember.Value)
                __result = GameConfig.TeammatesMaxCount + 2;
        }

        // 全难度切磋
        [HarmonyTranspiler, HarmonyPatch(typeof(AmbushEventHandler), "OnEvent", new Type[] { typeof(AmbushEventArgs) })]
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
