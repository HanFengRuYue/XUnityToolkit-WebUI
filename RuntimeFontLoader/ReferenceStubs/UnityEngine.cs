// Compile-time-only facade for BepInEx 5's BaseUnityPlugin inheritance chain.
// The DLL is never distributed; games provide the real UnityEngine assembly.
namespace UnityEngine
{
    public class Object { }
    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }
}
