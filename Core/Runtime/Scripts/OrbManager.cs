using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Oxipital
{
    public class OrbManager : BaseManager<OrbGroup>
    {
        
        public float firstItemIntensity = .5f;

        // Mesh drawn by the LitMesh render type in every orb group (none : the sphere of the VFX)
        [OSCQuery.DoNotExpose]
        public Mesh particleMesh;

        OrbManager() : base("Orb Group")
        {
        }

        override protected void Update()
        {
            base.Update();
            foreach (OrbGroup orbGroup in items) if (orbGroup != null) orbGroup.particleMesh = particleMesh;
        }

        protected override OrbGroup addItem()
        {
            OrbGroup g = base.addItem();
            if (items.Count == 1) items[0].dancerIntensity = firstItemIntensity;
            return g;
        }

        override protected void killLastItem() { 
            (items[items.Count - 1] as OrbGroup).kill(getKillTime()); 
        }

        protected override float getKillTime()
        {
            float result = 0;
            foreach (OrbGroup orbGroup in items) result = Mathf.Max(orbGroup.life);
            return result;
        }

        public void KillAllParticles()
        {
            foreach (OrbGroup orbGroup in items) orbGroup.killAllParticle();
        }
    }
}