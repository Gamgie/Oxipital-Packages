using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Oxipital
{
    public class Ballet : MonoBehaviour
    {
        OrbManager orbManager;
        StandardForceManager standardForceManager;
        OpticalFlowManager opticalFlow;

        public float totalParticles;

        // Reused every frame. Force group names are cached (reading gameObject.name allocates a new string
        // each time): a group renamed while playing keeps its first name until the group count changes.
        readonly Dictionary<string, GraphicsBuffer> forceBuffers = new Dictionary<string, GraphicsBuffer>();
        readonly Dictionary<StandardForceGroup, string> forceGroupNames = new Dictionary<StandardForceGroup, string>();
        int namedForceGroupCount = -1;

        void OnEnable()
        {
            orbManager = GetComponentInChildren<OrbManager>();
            standardForceManager = GetComponentInChildren<StandardForceManager>();
            opticalFlow = GetComponentInChildren<OpticalFlowManager>();
            if (opticalFlow == null) opticalFlow = FindFirstObjectByType<OpticalFlowManager>();
        }

        // Update is called once per frame
        void Update()
        {
            // Gather all force buffer
			forceBuffers.Clear();
			if (standardForceManager.items.Count != namedForceGroupCount)
			{
				forceGroupNames.Clear();
				namedForceGroupCount = standardForceManager.items.Count;
			}
			foreach (StandardForceGroup group in standardForceManager.items)
			{
				if (!forceGroupNames.TryGetValue(group, out string name))
				{
					name = group.gameObject.name;
					forceGroupNames[group] = name;
				}
				forceBuffers.Add(name, group.buffer);
			}

            // Send it to orbs and compute total particle count
            totalParticles = 0;
            foreach (OrbGroup group in orbManager.items)
            {
                group.setForceBuffers(forceBuffers);
                if (opticalFlow != null) group.setOpticalFlow(opticalFlow);
                totalParticles += group.vfx.aliveParticleCount;
            }
        }
    }
}
