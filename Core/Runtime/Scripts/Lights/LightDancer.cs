using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using OSCQuery;

namespace Oxipital
{
    /// <summary>
    /// A dancer of a <see cref="LightGroup"/>: an HDRP light (child "Light") and the mesh that shows it (child "Mesh",
    /// a glowing sphere by default). The group sets everything each frame; this only holds the references.
    /// </summary>
    public class LightDancer : Dancer
    {
        [DoNotExpose] public Light lightSource;
        [DoNotExpose] public MeshFilter meshFilter;
        [DoNotExpose] public MeshRenderer meshRenderer;

        internal HDAdditionalLightData lightData;
        internal Mesh defaultMesh;
        internal Vector3 lastPosition;
        internal Vector3 moveDirection = Vector3.forward;
        internal bool hasLastPosition;
        internal MaterialPropertyBlock block;

        void Awake()
        {
            if (lightSource != null) lightData = lightSource.GetComponent<HDAdditionalLightData>();
            if (meshFilter != null) defaultMesh = meshFilter.sharedMesh;
            block = new MaterialPropertyBlock();
        }
    }
}
