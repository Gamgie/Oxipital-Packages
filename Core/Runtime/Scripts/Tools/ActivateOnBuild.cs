using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Oxipital
{
	public class ActivateOnBuild : MonoBehaviour
	{
		public bool activateOnBuild = false;
		public bool activateInEditor = false;

		private void OnEnable()
		{
			if(Application.isEditor)
			{
				this.gameObject.SetActive(activateInEditor);
			}
			else
			{
				this.gameObject.SetActive(activateOnBuild);
			}
		}
	}
}
