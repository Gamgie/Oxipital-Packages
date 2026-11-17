using UnityEngine;
using Unity.Cinemachine;

namespace Oxipital
{
    /// <summary>
    /// Puts the PFC Dome Creator rig on the main camera. It follows right after Cinemachine has moved the camera
    /// (CinemachineCore.CameraUpdatedEvent), so the dome renders this frame's pose. Following in Update showed the
    /// previous frame's pose: each frame moved by the last frame's step, which jittered with the frame time.
    /// LateUpdate (run late) covers a camera that Cinemachine does not drive.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class DomeCameraRigFollow : MonoBehaviour
    {
        public GameObject cameraTarget;

    	// PFC Dome creator is offset from center. We need another target for position.
    	public GameObject offsetTarget;
        [Range(-2f,-0.01f)]
        public float offset = -0.2f;

        // Tilts the physical dome rig itself (its own local frame, after the axis
        // remap below), independent of the active camera movement mode.
        [Range(-60f, 60f)]
        public float rigTiltOffset = 0f;

        void OnEnable()
        {
            CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated);
        }

        void OnDisable()
        {
            CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);
        }

        void OnCameraUpdated(CinemachineBrain brain)
        {
            Follow();
        }

        void LateUpdate()
        {
            Follow();
        }

        void Follow()
        {
            if (cameraTarget == null || offsetTarget == null)
                return;

            offsetTarget.transform.localPosition = new Vector3(0, 0, -offset*20);

            transform.position = offsetTarget.transform.position;
            transform.rotation = cameraTarget.transform.rotation;
            transform.Rotate(90,0,0);
            transform.Rotate(0,180,0);
            transform.Rotate(rigTiltOffset, 0, 0);
    	}
    }
}
