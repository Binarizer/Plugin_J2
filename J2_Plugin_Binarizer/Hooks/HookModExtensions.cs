using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using BepInEx;
using BepInEx.Configuration;
using Heluo;
using Heluo.UI;
using Heluo.Mod;
using Heluo.Data;
using Heluo.Flow;
using Heluo.Battle;
using Heluo.Components;
using Heluo.Controller;

namespace J2
{
    // Mod相关改动
    public class HookModExtensions : IHook
    {
        static ConfigEntry<bool> modIgnoreDifferent;
        static ConfigEntry<bool> modTextSave;

        static string cacheModId;
        static string cacheSaveVersion;

        public void OnRegister(BaseUnityPlugin plugin)
        {
            // configrations
            modIgnoreDifferent = plugin.Config.Bind("mod扩展", "去除读档限制", false, "去除不同mod间读档限制");
            modTextSave = plugin.Config.Bind("mod扩展", "文本格式存档", false, "用json格式存档，方便修改");
            modTextSave.SettingChanged += (object o, EventArgs e) =>
            {
                if (cacheSaveVersion == null)
                    cacheSaveVersion = GameConfig.SaveDataVersion;
                GameConfig.SaveDataVersion = modTextSave.Value ? "TitleSave" : cacheSaveVersion;
            };

            // patch internal classes
            Harmony harmony = new Harmony("HookModExtensions");
            var source = AccessTools.TypeByName("Heluo.UI.UILoad").GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
            Console.WriteLine("source=" + source);
            var prefix = typeof(HookModExtensions).GetMethod("ModPatch_LimitLoadPre");
            var postfix = typeof(HookModExtensions).GetMethod("ModPatch_LimitLoadPost");
            harmony.Patch(source, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
        }
        public void OnUpdate()
        {
        }

        // 去除读档Mod限制 UILoad.OnClick
        public static bool ModPatch_LimitLoadPre(object __instance, WgSaveLoadFile wgFile)
        {
            if (modIgnoreDifferent.Value)
            {
                // cache modId
                ModInfo currentModInfo = Traverse.Create(__instance).Property("Mod").GetValue<IModManager>().GetCurrentModInfo();
                var fileModId = Traverse.Create(wgFile).Field("gameData").Property("ModId");
                cacheModId = fileModId.GetValue<string>();
                fileModId.SetValue(currentModInfo.Id);
            }
            return true;
        }
        public static void ModPatch_LimitLoadPost(object __instance, WgSaveLoadFile wgFile)
        {
            if (modIgnoreDifferent.Value)
            {
                // restore modId
                var fileModId = Traverse.Create(wgFile).Field("gameData").Property("ModId");
                fileModId.SetValue(cacheModId);
            }
        }
    }
}
