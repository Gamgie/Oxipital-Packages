using UnityEngine;
using Klak.Spout;
using UnityEngine.UI;

namespace Oxipital
{
    /// <summary>
    /// Shows the Spout / Syphon output on screen: while CameraSpoutManager is on, the main camera renders into its
    /// texture and the Game view would be empty, so this full-screen RawImage (on a Screen Space Overlay canvas,
    /// "Spout Preview", child of "Unity Spout Oxipital" in the Cameras rig) displays that texture. Hidden when the output is off (an empty
    /// RawImage would draw white) and in a full dome. Runs in edit mode too, like CameraSpoutManager.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RawImage))]
    public class SpoutFeedback : MonoBehaviour
    {
        [Tooltip("The output to show. Empty: the sender's texture (older scenes).")]
        public CameraSpoutManager spoutManager;
        public SpoutSender spout;

        CameraController cameraController;
        RawImage image;

        public void OnEnable()
        {
            image = GetComponent<RawImage>();
            if (!cameraController)
                cameraController = FindFirstObjectByType<CameraController>();
        }

        void LateUpdate()
        {
            if (image == null)
                return;

            Texture texture = null;
            if (cameraController == null || !cameraController.isFullDome)
            {
                if (spoutManager != null)
                    texture = spoutManager.OutputTexture;
                else if (spout != null && spout.isActiveAndEnabled)
                    texture = spout.sourceTexture;
            }

            if (image.texture != texture)
                image.texture = texture;
            bool show = texture != null;
            if (image.enabled != show)
                image.enabled = show;
        }

        void OnDisable()
        {
            if (image != null)
            {
                image.texture = null;
                image.enabled = false;
            }
        }
    }
}
