using BepInEx;

namespace J2
{
    interface IHook
    {
        void OnRegister(BaseUnityPlugin plugin);

        void OnUpdate();
    }
}

