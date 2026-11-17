using UnityEngine;
using UnityEngine.VFX;

namespace Oxipital
{
    /// <summary>
    /// Sends the main camera's position to the "Camera Position" property of this object's VisualEffect every frame.
    /// The camera is found by its MainCamera tag, not Camera.main: in a dome setup the main camera is disabled
    /// (the dome rig renders) and Camera.main is then null, while its transform still moves. Missing camera or effect:
    /// one warning, then it looks for the camera again about once a second (no log or search every frame).
    /// </summary>
    public class CameraVFXInfo : MonoBehaviour
    {
        static readonly int CameraPositionId = Shader.PropertyToID("Camera Position");

        VisualEffect visualEffect;
        Transform cameraTransform;
        float nextSearchTime;
        bool warnedCamera;

        void Awake()
        {
            visualEffect = GetComponent<VisualEffect>();
            if (visualEffect == null)
                Debug.LogWarning("CameraVFXInfo: no VisualEffect on " + name + ".", this);
        }

        void Update()
        {
            if (visualEffect == null)
                return;

            if (cameraTransform == null && Time.unscaledTime >= nextSearchTime)
            {
                GameObject mainCamera = GameObject.FindWithTag("MainCamera");
                if (mainCamera != null)
                {
                    cameraTransform = mainCamera.transform;
                }
                else
                {
                    nextSearchTime = Time.unscaledTime + 1f;
                    if (!warnedCamera)
                    {
                        Debug.LogWarning("CameraVFXInfo: no camera tagged MainCamera in the scene.", this);
                        warnedCamera = true;
                    }
                }
            }

            if (cameraTransform != null)
                visualEffect.SetVector3(CameraPositionId, cameraTransform.position);
        }
    }
}
