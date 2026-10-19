using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oxipital
{
    public class SceneLoader : MonoBehaviour
    {

        public int sceneIndex;

        public void LoadScene()
        {
            SceneManager.LoadScene(sceneIndex);
        }
    }
}
