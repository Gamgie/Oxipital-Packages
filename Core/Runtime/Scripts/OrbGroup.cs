using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using UnityEngine.VFX.SDF;

namespace Oxipital
{
    public class MeshLoader
    {
        public static bool isLoaded = false;
        
        MeshToSDFBaker m_Baker;

        public struct MeshTextured
        {
            public Texture2D texture;
            public Texture collisionSDF;
            public Mesh mesh;
        }
            

        static Dictionary<string, MeshTextured> shapeMeshMap;
        public MeshLoader()
        {
        }

        async public static void loadMeshes()
        {
            if (shapeMeshMap != null) return;
            shapeMeshMap = new Dictionary<string, MeshTextured>();

            // Get files from StreamingAssets/emitters
            string emittersPath = Path.Combine(Application.streamingAssetsPath, "emitters");
            string[] files = Directory.GetFiles(emittersPath, "*.glb");

            foreach (string file in files)
            {
                var fileWithoutExtension = Path.GetFileNameWithoutExtension(file);

                try
                {
                    var gltfI = new GLTFast.GltfImport();
                    var success = await gltfI.Load(file);

                    if (success)
                    {
                        var meshes = gltfI.Meshes;
                        if (meshes.Count > 0)
                        {
                            Mesh m = System.Linq.Enumerable.First(meshes);
                            if (m == null)
                            {
                                Debug.LogWarning(file + " loaded but no mesh found inside");
                                continue;
                            }

                            var text = gltfI.GetTexture(0);

                            MeshTextured mt = new MeshTextured();
                            mt.mesh = m;
                            mt.texture = text;

                            string meshNameLower = fileWithoutExtension.ToLower();

                            // Generate SDF for the mesh
                            //RenderTexture sdfTexture = GenerateSDFForMesh(m, fileWithoutExtension);
                            //if (sdfTexture != null)
                            //{
                            //    mt.collisionSDF = sdfTexture;
                            //    Debug.Log($"Generated SDF for {fileWithoutExtension}");
                            //}
                            //else
                            //{
                            //    Debug.LogWarning($"Failed to generate SDF for {fileWithoutExtension}");
                            //}

                            shapeMeshMap.Add(meshNameLower, mt);

                            Debug.Log("Loaded mesh " + fileWithoutExtension);
                        }
                        else
                        {
                            Debug.LogWarning("No mesh found in glTF file: " + fileWithoutExtension);
                        }
                    }
                    else
                    {
                        Debug.LogError("Loading glTF failed: " + fileWithoutExtension);
                    }
                }
                catch (Exception e)
                {
                    // Un mesh en échec (ex: NullReferenceException dans la génération de matériau
                    // HDRP/glTFast côté build, shader manquant, etc.) ne doit pas bloquer le
                    // chargement des autres meshes ni empêcher isLoaded de passer à true.
                    Debug.LogError($"Failed to load glTF mesh '{fileWithoutExtension}': {e.Message}\n{e.StackTrace}");
                }
            }

            Debug.Log("Loaded all meshes");
            isLoaded = true;
        }

        public static MeshTextured getMesh(string name)
        {
            if(name == "")
            {
                return shapeMeshMap["sphere"];
            }
            else 
            {
                return shapeMeshMap[name];
            }
        }

        private static RenderTexture GenerateSDFForMesh(Mesh mesh, string meshName)
        {
            try
            {
                // Create a temporary GameObject with the mesh
                GameObject tempObject = new GameObject("TempSDFBaker_" + meshName);
                MeshFilter meshFilter = tempObject.AddComponent<MeshFilter>();
                meshFilter.mesh = mesh;

                int maxResolution = 64;
                Vector3 center = Vector3.zero;
                Vector3 sizeBox = Vector3.one * 1.8F;

                MeshToSDFBaker baker = new MeshToSDFBaker(sizeBox, center, maxResolution, mesh);

                // Bake the SDF
                baker.BakeSDF();

                // Get the resulting texture
                RenderTexture sdfTexture = baker.SdfTexture;

                // Clean up
                UnityEngine.Object.DestroyImmediate(tempObject);

                //baker.Dispose();

                return sdfTexture;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error generating SDF for mesh {meshName}: {e.Message}");
                return null;
            }
        }

    }

    [RequireComponent(typeof(VisualEffect))]
    public class OrbGroup : DancerGroup<Dancer>
    {
        [Header("Emission")]
        [InBuffer(0)]
        [Range(0, 120)]
        public float life = 20;

        // Particles never die : Emitter Intensity sets the particle count (intensity * maxInfiniteParticles)
        [InBuffer(1)]
        public bool infiniteLife = false;

        [InBuffer(2)]
        [HideInInspector]
        [OSCQuery.DoNotExpose]
        public float infiniteCount = 0; // infinite particles spawned and kept (particles with infiniteRank > infiniteCount are killed)

        public enum EmitterShape { Sphere, Plane, Torus, Cube, Pipe, Egg, Line, Circle, Merkaba, Pyramid, Custom, Augmenta }
        // Lit types are drawn by the single Lit Mesh output of OrbVFX: meshIndex = renderType - LitCube (mesh0 cube, mesh1 sphere, mesh2 "Particle Mesh")
        public enum RenderType { UnlitOpaque, UnlitAdditive, LitCube, LitSphere, LitMesh }

        [InBuffer(3)]
        public EmitterShape emitterShape;
        EmitterShape lastEmitterShape;

        

        [InBuffer(4)]
        [Range(0, 1)]
        public float emitterSurfaceFactor = 0;

        [InBuffer(5)]
        [Range(0, 1)]
        public float emitterVolumeFactor = 0;

        [InBuffer(6)]
        [Range(0, 1)]
        public float emitterPositionNoise = 0;

        [InBuffer(7)]
        [Range(0, 5)]
        public float emitterPositionNoiseFrequency = 1;

        [InBuffer(8)]
        [Range(0, 1)]
        public float emitterPositionNoiseRadius = 1;

        // Grid emission (next feature, not used by the VFX yet)
        [InBuffer(9)]
        public Vector3 gridDimension = Vector3.one;

        [InBuffer(12)]
        [Range(0, 500)]
        public float gridDensity = 0; // particles per dimension unit

        [Header("Appearance")]
        [InBuffer(13)]
        [ColorUsage(true, true)]
        public Color color = Color.white;

        public Color colorLife = Color.black;
        [Range(0, 1)]
        public float colorLifeRange = 1;
        public Gradient colorOverLife;
		[InBuffer(16)]
		[Range(0, 1)]
		public float colorLifeBlend = 0;

		public Color colorSpeed = Color.black;
		[Range(0, 1)]
		public float colorSpeedRange = 1;
		public Gradient colorOverSpeed;
		[InBuffer(17)]
		[Range(0, 1)]
		public float colorSpeedBlend = 0;

		[InBuffer(18)]
		[Range(0, 1)]
		public float colorMaxSpeed = 1;

		[InBuffer(19)]
        [Range(0, 1)]
        public float alpha = .5f;

        [InBuffer(20)]
        [Range(0, 1)]
        public float hdrMultiplier = 1;

        [InBuffer(21)]
        [Range(0, 1)]
        public float alphaSpeedThreshold = 0;

        [InBuffer(22)]
        [Range(0, 1)]
        public float textureOpacity = 0;

        [InBuffer(23)]
        [Range(0, 1)]
        public float particleSize = 0;

        [InBuffer(24)]
        public RenderType renderType = RenderType.UnlitAdditive;

        [InBuffer(25)]
        [Range(0, 1)]
        public float meshOpacity = 0;
        [Range(0, 1)]
        public float meshColorIntensity = 0;
        [Range(0, 1)]
        public float lightIntensity = 0;
        public string textureFolderPath = "";
        public string textureName = "";
        public bool useSpoutTexture = false;
        public RenderTexture spoutTexture;

        public Material transparentMaterial;
        public Material opaqueMaterial;


		[Header("Physics")]
        [InBuffer(26)]
        [Range(0, 1)]
        public float forceWeight = 1;

        [InBuffer(27)]
        [Range(0, 1)]
        public float drag = .5f;

        [InBuffer(28)]
        [Range(0, 1)]
        public float velocityDrag = 0;

        [InBuffer(29)]
        [Range(0, 1)]
        public float noisyDrag = 0;

        [InBuffer(30)]
        [Range(0, 5)]
        public float noisyDragFrequency = 0;

        [InBuffer(31)]
        public bool activateCollision = false;
        
        [InBuffer(32)]
        public Vector3 emitterScale = Vector3.one;

        // Infinite life bookkeeping
        public int maxInfiniteParticles = 100000;

        [InBuffer(35)]
        [HideInInspector]
        [OSCQuery.DoNotExpose]
        public float infiniteRankBase = 0; // rank of the first infinite particle spawned this frame - 1

        VFXEventAttribute infiniteSpawnAttribute;
        static readonly int spawnInfiniteEventID = Shader.PropertyToID("SpawnInfinite");
        static readonly int spawnCountID = Shader.PropertyToID("spawnCount");
        static readonly int orbSystemID = Shader.PropertyToID("Orb VFX");
        static readonly int spawnSystemID = Shader.PropertyToID("Spawn System");
        VFXSpawnerState spawnerState;
        // Particles spawned (rate spawner + infinite events) during the last seconds : the alive count readback
        // is refreshed about once per second so it may not include them yet.
        const float SPAWN_READBACK_DELAY = 2;
        Queue<(float time, int count)> recentSpawns = new Queue<(float, int)>();
        int recentSpawnCount = 0;

        [Range(0, 1)]
        public float opticalFlowWeight = 1;


        public string customMeshName = string.Empty;

        [OSCQuery.DoNotExpose]
        public string meshName = "";
        string lastMeshName = "";

        Mesh currentMesh;
        internal VisualEffect vfx;

        internal Mesh particleMesh; // set by OrbManager, drawn by the LitMesh render type
        Mesh sentParticleMesh;
        static readonly int particleMeshID = Shader.PropertyToID("Particle Mesh");

        //PCLToGraphicsBuffer pclGraphics;

        private bool textureLoaded = false;
        private string lastTexturePath = "";
        private Texture2D spoutTexture2D;

        // Per-frame work without allocation : property IDs instead of names, reused lists, and the
        // gradients / "Emitter Color" texture are only sent to the VFX when they change.
        static readonly int emitterIntensityID = Shader.PropertyToID("Emitter Intensity");
        static readonly int orbBufferID = Shader.PropertyToID("Orb Buffer");
        static readonly int colorOverLifeID = Shader.PropertyToID("Color Over Life");
        static readonly int colorOverSpeedID = Shader.PropertyToID("Color Over Speed");
        static readonly int sourcePositionID = Shader.PropertyToID("Source Position");
        // Force buffer property IDs by force group name ("Force Group 1" and "Force Group 1B")
        static readonly Dictionary<string, (int buffer, int bufferB)> forceBufferIDs = new Dictionary<string, (int, int)>();
        readonly HashSet<string> missingForceBuffers = new HashSet<string>();

        readonly List<MeshRenderer> meshRenderers = new List<MeshRenderer>();
        readonly List<Light> lights = new List<Light>();

        readonly GradientColorKey[] gradientColorKeys = new GradientColorKey[2];
        readonly GradientAlphaKey[] gradientAlphaKeys = new GradientAlphaKey[2];
        bool gradientsSent;
        Color sentColor, sentColorLife, sentColorSpeed;
        float sentColorLifeRange, sentColorSpeedRange, sentHdrMultiplier;

        Texture emitterColorTexture; // last texture sent as "Emitter Color"
        string textureFolderPathUsed, textureNameUsed, textureFullPath;

        protected override void OnEnable()
        {
            base.OnEnable();
            MeshLoader.loadMeshes();
            vfx = GetComponent<VisualEffect>();
            infiniteSpawnAttribute = vfx.CreateVFXEventAttribute();
            spawnerState = new VFXSpawnerState();
            infiniteCount = 0;
            // pclGraphics = GetComponent<PCLToGraphicsBuffer>();
            textureLoaded = false;
            gradientsSent = false;
            emitterColorTexture = null;
			spoutTexture2D = new Texture2D(spoutTexture.width, spoutTexture.height, TextureFormat.RGBAFloat, false);
		}

        protected override void OnDisable()
        {
            base.OnDisable();
            spawnerState.Dispose();
            infiniteSpawnAttribute.Dispose();
        }

        protected override void Update()
        {
            // Before base.Update() so the buffer sent this frame carries the new count / rank base
            updateInfiniteLife();

            base.Update();

			bool isDying = killProgress > 0;
            // In infinite life mode, particles are only spawned by the SpawnInfinite event
            vfx.SetFloat(emitterIntensityID, isDying || infiniteLife ? 0 : dancerIntensity);
            vfx.SetGraphicsBuffer(orbBufferID, buffer);
            updateGradients();
            updateParticleMesh();
            vfx.SetVector3(sourcePositionID, transform.position);

			foreach (var item in items) item.debugColor = color;

            if (emitterShape != lastEmitterShape)
            {
                if (emitterShape != EmitterShape.Line && emitterShape != EmitterShape.Circle && emitterShape != EmitterShape.Custom && emitterShape != EmitterShape.Augmenta)
                {
                    string shape = emitterShape.ToString().ToLower();
                    meshName = shape;
                }

                lastEmitterShape = emitterShape;
            }

            // Load Augmenta Point Clouds
            if (emitterShape == EmitterShape.Augmenta)
            {
                // if (augmentaObject != null)
                // {
                //     pclGraphics.pObject = augmentaObject;
                // }
            }

            // Load custom mesh
            if (emitterShape == EmitterShape.Custom)
            {
                meshName = customMeshName;
            }

            if (MeshLoader.isLoaded)
            {
                // Update mesh in case we change it
                if (meshName != lastMeshName)
                {
                    currentMesh = MeshLoader.getMesh(meshName.ToLower()).mesh;
                    Texture2D texture = MeshLoader.getMesh(meshName.ToLower()).texture;
                    Texture sdfTexture = MeshLoader.getMesh(meshName.ToLower()).collisionSDF;

                    if (currentMesh == null) Debug.LogWarning("Could not find mesh for " + meshName);
                    else
                    {
                        setMesh(currentMesh);
                        SetSDFtexture(sdfTexture);
                    }

                    if (texture == null)
                    {
                        Debug.LogWarning("Could not find texture for " + meshName);
                        setColor(Texture2D.whiteTexture, "Emitter Mesh Color");
                    }
                    else
                    {
                        setColor(texture, "Emitter Mesh Color");
                    }

                    lastMeshName = meshName;
                }
            }

			// Load 2D Texture
			if (textureFullPath == null || textureFolderPath != textureFolderPathUsed || textureName != textureNameUsed)
			{
				textureFullPath = Path.Combine(textureFolderPath, textureName);
				textureFolderPathUsed = textureFolderPath;
				textureNameUsed = textureName;
			}
			string fullPath = textureFullPath;
            if (fullPath != lastTexturePath) textureLoaded = false;

			if (textureFolderPath != "" && textureName != "" && textureLoaded == false)
            {
                Texture2D tex = LoadTextureFromPath(fullPath);
                if (tex != null)
                {
                    setEmitterColor(tex);
                    textureLoaded = true;
                    lastTexturePath = fullPath;

				}
			}

            if(useSpoutTexture)
            {
				Graphics.ConvertTexture(spoutTexture, spoutTexture2D);
				//Texture2D newTexture = toTexture2D(spoutTexture);
				setEmitterColor(spoutTexture2D);
			}
            else
            {
                setEmitterColor(Texture2D.whiteTexture);
            }


            Color meshColor = Color.Lerp(Color.black, color, meshColorIntensity);
            meshColor.a = meshOpacity;
            GetComponentsInChildren(meshRenderers);
            foreach (MeshRenderer r in meshRenderers)
            {
                Material material = r.material;
                if (meshOpacity == 1)
                {
                    if (material.renderQueue > (int)RenderQueue.GeometryLast)
                    {
                        r.material = opaqueMaterial;
                        material = r.material;
                    }
                }
                else
                {
                    if (material.renderQueue < (int)RenderQueue.GeometryLast)
                    {
                        r.material = transparentMaterial;
                        material = r.material;
                    }
                }

                material.color = meshColor;
                material.mainTexture = useSpoutTexture ? spoutTexture : null;
            }

            float lightValue = Unity.Mathematics.math.remap(0, 1, 0, 1000, lightIntensity);
            GetComponentsInChildren(lights);
            foreach (Light l in lights)
            {
                l.intensity = lightValue;
                l.shadowStrength = 1;
                l.color = color;
            }

        }

        // The gradients only depend on these parameters : rebuilt and sent to the VFX when one of them changes
        void updateGradients()
        {
            if (gradientsSent && colorOverLife != null && colorOverSpeed != null
                && color == sentColor && colorLife == sentColorLife && colorSpeed == sentColorSpeed
                && colorLifeRange == sentColorLifeRange && colorSpeedRange == sentColorSpeedRange && hdrMultiplier == sentHdrMultiplier)
                return;

            if (colorOverLife == null) colorOverLife = new Gradient();
            if (colorOverSpeed == null) colorOverSpeed = new Gradient();
            setGradientKeys(colorOverLife, color, colorLife, 0, 1, 0, colorLifeRange);
            setGradientKeys(colorOverSpeed, Color.black, colorSpeed, 1, 1, (1 - colorSpeedRange), 1);
            vfx.SetGradient(colorOverLifeID, colorOverLife);
            vfx.SetGradient(colorOverSpeedID, colorOverSpeed);

            gradientsSent = true;
            sentColor = color;
            sentColorLife = colorLife;
            sentColorSpeed = colorSpeed;
            sentColorLifeRange = colorLifeRange;
            sentColorSpeedRange = colorSpeedRange;
            sentHdrMultiplier = hdrMultiplier;
        }

        // Sent to the VFX only when it changes ; without mesh the VFX keeps its default one (the sphere)
        void updateParticleMesh()
        {
            if (particleMesh == sentParticleMesh) return;
            if (particleMesh != null) vfx.SetMesh(particleMeshID, particleMesh);
            else vfx.ResetOverride(particleMeshID);
            sentParticleMesh = particleMesh;
        }

        void setEmitterColor(Texture2D texture)
        {
            if (texture == emitterColorTexture) return;
            setColor(texture, "Emitter Color");
            emitterColorTexture = texture;
        }

        // Infinite particles are ranked 1..infiniteCount in the VFX (InfiniteLifeInit/Update.hlsl).
        // Growing the count spawns the missing ranks, shrinking it kills the ranks above the count.
        void updateInfiniteLife()
        {
            int target = infiniteLife ? Mathf.RoundToInt(dancerIntensity * maxInfiniteParticles) : 0;

            infiniteCount = Mathf.Min(infiniteCount, target);
            infiniteRankBase = infiniteCount;

            // Only spawn what fits in the free capacity : the VFX silently drops particles spawned when it is full
            // (e.g. finite particles still fading out after switching to infinite life). The rest comes on the next frames.
            while (recentSpawns.Count > 0 && Time.time - recentSpawns.Peek().time > SPAWN_READBACK_DELAY) recentSpawnCount -= recentSpawns.Dequeue().count;
            vfx.GetSpawnSystemInfo(spawnSystemID, spawnerState);
            addRecentSpawn((int)spawnerState.spawnCount);

            VFXParticleSystemInfo info = vfx.GetParticleSystemInfo(orbSystemID);
            int free = (int)info.capacity - (int)info.aliveCount - recentSpawnCount;
            int toSpawn = Mathf.Min(target - (int)infiniteCount, free);

            if (toSpawn > 0)
            {
                infiniteSpawnAttribute.SetFloat(spawnCountID, toSpawn);
                vfx.SendEvent(spawnInfiniteEventID, infiniteSpawnAttribute);
                infiniteCount += toSpawn;
                addRecentSpawn(toSpawn);
            }
        }

        void addRecentSpawn(int count)
        {
            if (count <= 0) return;
            recentSpawns.Enqueue((Time.time, count));
            recentSpawnCount += count;
        }

        internal void setForceBuffers(Dictionary<string, GraphicsBuffer> forceBuffers)
        {
            if (vfx == null) return;

            foreach (var b in forceBuffers)
            {
                if (!forceBufferIDs.TryGetValue(b.Key, out var ids))
                {
                    ids = (Shader.PropertyToID(b.Key), Shader.PropertyToID(b.Key + "B"));
                    forceBufferIDs[b.Key] = ids;
                }

                // Missing slots are reported once per orb group instead of every frame
                if (!vfx.HasGraphicsBuffer(ids.buffer))
                {
                    if (missingForceBuffers.Add(b.Key)) Debug.LogWarning(b.Key + " not found in VFX");
                    continue;
                }

                vfx.SetGraphicsBuffer(ids.buffer, b.Value);

                if (!vfx.HasGraphicsBuffer(ids.bufferB))
                {
                    if (missingForceBuffers.Add(b.Key + "B")) Debug.LogWarning("B Buffer " + b.Key + " not found in VFX");
                    continue;
                }

                vfx.SetGraphicsBuffer(ids.bufferB, b.Value);
            }

            //Update intensity here as we need to pass it outside the GraphicsBuffer
            //Discussion : https://discussions.unity.com/t/spawn-a-variable-amount-of-particles-from-graphics-buffer/899049/2
        }

        internal void setOpticalFlow(OpticalFlowManager opticalFlow)
        {
            if (vfx == null) return;
            opticalFlow.ApplyTo(vfx, opticalFlowWeight);
        }

        override protected Dancer addItem()
        {
            Dancer d = base.addItem();
            d.GetComponent<MeshFilter>().sharedMesh = currentMesh;
            return d;
        }

        internal void setMesh(Mesh m)
        {
            if (vfx == null) return;
            if (m == null) return;

            if (!vfx.HasMesh("Emitter Mesh"))
            {
                Debug.LogWarning("Emitter Mesh not found in VFX");
                return;
            }

            vfx.SetMesh("Emitter Mesh", m);
            foreach (var item in items) item.GetComponent<MeshFilter>().sharedMesh = m;
        }

        internal void SetSDFtexture(Texture texture)
        {
            if (vfx == null) return;
            if (texture == null) return;
            if (!vfx.HasTexture("Collision SDF"))
            {
                Debug.LogWarning("Collision SDF not found in VFX");
                return;
            }
            vfx.SetTexture("Collision SDF", texture);
        }

        internal void setColor(Texture2D texture, string name)
        {
            if (vfx == null) return;
            if (texture == null) return;

            if (!vfx.HasTexture(name))
            {
                Debug.LogWarning(name + " not found in VFX");
                return;
            }
            vfx.SetTexture(name, texture);
        }


        public override void kill(float time)
        {
            base.kill(time);
            infiniteLife = false; // infinite particles go back to a normal life and fade out
            if (vfx == null) return;
            vfx.SetFloat(emitterIntensityID, 0);
            count = 0;
        }

        override protected Type getGroupType()
        {
            return GetType();
        }

        protected override float getKillTime()
        {
            return life;
        }

        public void killAllParticle()
        {
            vfx.Reinit();
            infiniteCount = 0;
        }

        public new void ResetPattern()
        {
            base.ResetPattern();
        }

        public Gradient CreateGradient(Color colorA, Color colorB, float alphaKeyA, float alphaKeyB, float timeA, float timeB)
        {
			Gradient result = new Gradient();
            setGradientKeys(result, colorA, colorB, alphaKeyA, alphaKeyB, timeA, timeB);
            return result;
        }

        // Fills an existing gradient (the key arrays are reused, SetKeys copies them)
        void setGradientKeys(Gradient gradient, Color colorA, Color colorB, float alphaKeyA, float alphaKeyB, float timeA, float timeB)
        {
            if (timeA == 0)
                timeA = 0.001f;

            float hdr = (float)Math.Pow(2, hdrMultiplier * 5);
            gradientColorKeys[0].color = colorA * hdr;
            gradientColorKeys[0].time = timeA;
            gradientColorKeys[1].color = colorB * hdr;
            gradientColorKeys[1].time = timeB;
            gradientAlphaKeys[0].alpha = alphaKeyA;
            gradientAlphaKeys[0].time = timeA;
            gradientAlphaKeys[1].alpha = alphaKeyB;
            gradientAlphaKeys[1].time = timeB;
            gradient.SetKeys(gradientColorKeys, gradientAlphaKeys);
        }

        public Texture2D LoadTextureFromPath(string path)
        {
            if (File.Exists(path))
            {
                byte[] fileData = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(fileData))
                {
                    return tex;
                }
            }
            return null;
		}
	}

}


