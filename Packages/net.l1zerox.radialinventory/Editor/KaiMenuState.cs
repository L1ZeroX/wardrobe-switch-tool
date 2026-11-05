#if RISV4_JSON
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
namespace YagihataItems.RadialInventorySystemV4
{
    public sealed class KaiMenuState : ScriptableObject
    {
        public VRCExpressionsMenu sourceRoot;
        public VRCExpressionsMenu outputRoot;
        public bool detached;
    }
}
#endif
