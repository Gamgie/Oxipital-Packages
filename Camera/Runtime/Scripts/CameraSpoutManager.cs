using UnityEngine;
using Klak.Syphon;
using Klak.Spout;

namespace Oxipital
{
    /// <summary>
    /// Sends the main camera's image to other apps (TouchDesigner, Resolume...): Spout on Windows, Syphon on macOS.
    /// The camera renders into a RenderTexture of width x height (remembered in PlayerPrefs) that the sender shares;
    /// while it is on, the Game view shows nothing (the camera no longer renders to the screen). Off in a full dome
    /// setup (CameraController.isFullDome: the dome rig renders, not the main camera). Disabled, it gives the camera
    /// back to the screen and frees the texture. The texture is never saved in the scene.
    /// </summary>
    [ExecuteInEditMode]
    public class CameraSpoutManager : MonoBehaviour
    {

        #region Public members
        public Klak.Spout.SpoutSender spoutSender;
        public Klak.Syphon.SyphonServer syphonServer;
        public Camera mainCamera;
        public int width;
        public int height;
        public RenderTextureFormat rtFormat;
        #endregion

        #region Private members
        RenderTexture m_renderTexture;
        CameraController m_cameraController;
        bool m_warnedSize;
        #endregion

        /// <summary>The texture the camera renders into while the output is on (null when off).</summary>
        public RenderTexture OutputTexture => isActiveAndEnabled ? m_renderTexture : null;

        static bool IsMac => Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor;
        static bool IsWindows => Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        private void OnEnable()
        {
            width = PlayerPrefs.GetInt("SpoutWidth", 1920);
            height = PlayerPrefs.GetInt("SpoutHeight", 1080);

            if (m_cameraController == null)
            {
                m_cameraController = FindFirstObjectByType<CameraController>();
            }

            if (m_cameraController != null && m_cameraController.isFullDome)
            {
                this.enabled = false;
                return;
            }

            // Handle multi platform
            if (syphonServer != null) syphonServer.enabled = IsMac;
            if (spoutSender != null) spoutSender.enabled = IsWindows;
        }

        private void OnDisable()
        {
            if (spoutSender != null) spoutSender.enabled = false;
            if (syphonServer != null) syphonServer.enabled = false;

            // Give the camera back to the screen
            if (mainCamera != null && mainCamera.targetTexture == m_renderTexture)
                mainCamera.targetTexture = null;
            ReleaseTexture();
        }

    	// Update is called once per frame
    	void Update()
        {
            UpdateTexture();
        }

        void UpdateTexture()
        {
            if(width <= 0 || height <= 0)
    		{
                if (!m_warnedSize)
                {
                    Debug.LogError("CameraSpoutManager: can't create a render texture of " + width + " x " + height + ".", this);
                    m_warnedSize = true;
                }
                return;
    		}
            m_warnedSize = false;

            // No texture or not a valid one
            if(m_renderTexture == null || width != m_renderTexture.width || height != m_renderTexture.height)
    		{
                if (mainCamera != null && mainCamera.targetTexture == m_renderTexture)
                    mainCamera.targetTexture = null;
                ReleaseTexture();

                m_renderTexture = new RenderTexture(width, height, 16, rtFormat);
                m_renderTexture.name = "Oxipital Spout Output";
                m_renderTexture.hideFlags = HideFlags.DontSave;
                m_renderTexture.Create();

                if (IsMac && syphonServer != null)
                    syphonServer.SourceTexture = m_renderTexture;
                else if (IsWindows && spoutSender != null)
                    spoutSender.sourceTexture = m_renderTexture;

                if (mainCamera != null)
                    mainCamera.targetTexture = m_renderTexture;

                // Disable and enable again to re-init the plugin
                if (IsWindows && spoutSender != null)
                {
                    spoutSender.enabled = false;
                    spoutSender.enabled = true;
                }
            }
        }

        void ReleaseTexture()
        {
            if (m_renderTexture == null)
                return;
            m_renderTexture.Release();
            if (Application.isPlaying) Destroy(m_renderTexture);
            else DestroyImmediate(m_renderTexture);
            m_renderTexture = null;
        }

    	private void OnDestroy()
    	{
            PlayerPrefs.SetInt("SpoutWidth", width);
            PlayerPrefs.SetInt("SpoutHeight", height);
        }
    }
}
