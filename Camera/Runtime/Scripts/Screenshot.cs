using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;

namespace Oxipital
{
    public class Screenshot : MonoBehaviour
    {
        public string filepath;
        public string filename;
        public int supersize;
        public KeyCode takeScreenshotKeyCode;
        public List<GameObject> objectToHide;

        private int fileIndex = 0;

        private void OnEnable()
        {
            FileIndexSetup();
        }

        private void Update()
        {
            // The key shortcut needs the legacy Input Manager : in a project set to the Input System only,
            // reading UnityEngine.Input throws every frame. TakeScreenshot() stays callable (Chataigne).
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyUp(takeScreenshotKeyCode))
            {
                TakeScreenshot();
            }
#endif
        }

        public void TakeScreenshot()
        {
            if (string.IsNullOrEmpty(filepath))
            {
                Debug.LogWarning("Screenshot : set a folder in filepath to capture screenshots");
                return;
            }

            string newScreenCaptureName = filepath + "\\" + filename + "." + fileIndex + ".png";

            while (File.Exists(newScreenCaptureName))
            {
                fileIndex++;
                newScreenCaptureName = filepath + "\\" + filename + "." + fileIndex + ".png";
            }

            List<GameObject> objectToActivate = new List<GameObject>();
            foreach(GameObject go in objectToHide)
    		{
                if(go.activeSelf)
    			{
                    objectToActivate.Add(go);
                    go.SetActive(false);
    			}
    		}

            ScreenCapture.CaptureScreenshot(newScreenCaptureName, supersize);
            fileIndex++;

            StartCoroutine(ActivateObjects(objectToActivate));
        }

        void FileIndexSetup()
        {
            fileIndex = 0;

            // No folder set (the prefab ships without one) : nothing to index
            if (string.IsNullOrEmpty(filepath))
                return;

            string screenCaptureFileName = filepath + "\\" + filename + ".";

            if(File.Exists(filepath))
    		{
                foreach (string file in System.IO.Directory.GetFiles(filepath))
                {
                    string actualFileNumber = file.Substring(screenCaptureFileName.Length);
                    actualFileNumber = actualFileNumber.Remove(actualFileNumber.Length - 4);
                    try
                    {
                        int actualFileIndex = Convert.ToInt32(actualFileNumber);
                        if (actualFileIndex > fileIndex)
                            fileIndex = actualFileIndex + 1;
                    }
                    catch (OverflowException)
                    {
                        Debug.LogError(" \" " + actualFileNumber + " \" is outside the range of the Int32 type.");
                    }
                    catch (FormatException)
                    {
                        Debug.LogError(" \" " + actualFileNumber + " \" is not in a recognizable format.");
                    }
                }
            }
            else
    		{
                Debug.LogWarning(filepath + "does not exist. Screenshot cannot be captured");
    		}
        }

        IEnumerator ActivateObjects(List<GameObject> goList)
    	{
            yield return new WaitForSeconds(2);

            foreach (GameObject go in goList)
            {
                go.SetActive(true);
            }
        }
    }
}
