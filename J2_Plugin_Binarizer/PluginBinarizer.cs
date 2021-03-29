using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx;

namespace J2
{
    [BepInPlugin("binarizer.plugin.j2.function_sets", "功能合集 by Binarizer", "1.0")]
    public class PluginBinarizer : BaseUnityPlugin
    {
        void RegisterHook(IHook hook)
        {
            hook.OnRegister(this);
            Harmony.CreateAndPatchAll(hook.GetType());
            hooks.Add(hook);
        }

        private List<IHook> hooks = new List<IHook>();

        void Awake()
        {
            Console.WriteLine("美好的初始化开始");

            RegisterHook(new HookModExtensions());
            RegisterHook(new HookGenerals());
            RegisterHook(new HookFeaturesAndFixes());
        }

        void Start()
        {
            Console.WriteLine("美好的第一帧开始");
        }

        void Update()
        {
            foreach (IHook hook in hooks)
            {
                hook.OnUpdate();
            }
        }
    }
}

