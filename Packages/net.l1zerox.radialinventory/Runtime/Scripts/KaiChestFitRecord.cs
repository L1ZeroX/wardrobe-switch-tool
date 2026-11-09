using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    // Recovery metadata only; the SDK strips this component from the uploaded avatar.
    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class KaiChestFitRecord : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        public SkinnedMeshRenderer Clothing, Body;
        public Mesh OriginalMesh, CorrectedMesh;
        public Bounds OriginalBounds, CorrectedBounds;
        public string BodyShape;
        public float OriginalBodyValue, AppliedBodyValue;
        public bool Applied;
    }
}
