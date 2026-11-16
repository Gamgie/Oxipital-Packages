using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using OSCQuery;
using System.Reflection;
using System.Runtime.InteropServices;
using System;

namespace Oxipital
{


	[DoNotExposeChildren]
	public class DancerGroup<T> : BaseManager<T>, IDancerGroup where T : Dancer
	{
		const int MAX_DANCERS = 16;
		public const int DANCER_DATA_SIZE = 8;

		[Header("Patterns")]
		[Range(0, 10)]
		public float patternSize = 1; // Size of this pattern

		[Range(0, 1)]
		public float patternSizeSpread = 0;
		[Range(0, 1)]
		public float patternAxisSpread = 0;


		[Header("Animation")]
		[Range(-1, 1)]
		public float patternSpeed = 1f; // speed of the choreography
		[Range(0, 1)]
		public float patternSpeedRandom = 0;

		[HideInInspector]
		[DoNotExpose]
		private float patternTime = 0;

		[Range(0, 1)]
		public float patternTimeOffset = 0; // offset phase

		[Header("Dancer")]
		[Range(0, 10)]
		public float dancerSize = 1;
		[Range(0, 1)]
		public float dancerHyperSize = 0;
		[Range(0, 1)]
		public float dancerSizeSpread = 0;

		[Range(0, 1)]
		public float dancerWeightSizeFactor = 0;
		[Range(0, 1)]
		public float dancerIntensity = 1;

		public Vector3 dancerRotation = Vector3.zero;
		public Vector3 dancerLookAt = Vector3.up;
		[Range(0, 2)]
		public float dancerLookAtMode = 0; // 0 = local, 1 = group, 2 = global

		List<DancePattern> patterns;

		//Graphics buffer

		public GraphicsBuffer buffer;

		InBufferLayout layout;
		int groupFixedDataSize;
		// Filled each frame and uploaded to the buffer (reused, so nothing is allocated per frame)
		float[] bufferData;
		Func<int, object> customValue;


		public AnimationCurve countDeadZone;

		[Header("Master")]
		[Tooltip("The dancer group this one follows: its dancer i goes where the master's dancer i is (looping when the master has fewer). None = free, on its own patterns. The Inspector lists the groups of the open scenes.")]
		[BalletName]
		public string targetMasterBallet = "None";
		[Tooltip("Seconds to move onto the master, from one master to another, or back to the own patterns.")]
		[Range(0, 20)]
		public float masterBalletTransition = 2f;
		[Tooltip("While synced, the dancers also take the intensity of their master dancer (its fades show on this group). Switching it blends over the transition.")]
		public bool masterSyncIntensity = true;
		[Tooltip("While synced, the dancers also take the size of their master dancer. Switching it blends over the transition.")]
		public bool masterSyncSize = true;

		IDancerGroup master;
		string masterName;
		string masterResolved;
		float masterWeight;
		float masterIntensityWeight, masterSizeWeight;
		float nextMasterSearch;
		readonly Vector3[] masterLast = new Vector3[MAX_DANCERS];
		readonly Vector3[] masterSwitchOffset = new Vector3[MAX_DANCERS];
		float masterSwitch = 1;

		public DancerGroup(string itemName = "Dancer") : base(itemName)
		{

		}

		override protected void OnEnable()
		{
			init();
			if (!DancerGroups.All.Contains(this)) DancerGroups.All.Add(this);
		}

		virtual protected void OnDisable()
		{
			DancerGroups.All.Remove(this);
			clear();
		}

		void init()
		{
			items = GetComponentsInChildren<T>().ToList();
			patterns = GetComponents<DancePattern>().ToList();
			initBufferAndFieldInfoList();
		}

		void clear()
		{
			buffer.Release();
		}

		internal void initBufferAndFieldInfoList()
		{
			// The [InBuffer] fields of the group type, analyzed once per type (see InBufferLayout)
			layout = InBufferLayout.Get(getGroupType());
			if (customValue == null) customValue = getBufferValueAtIndex;
			int lastGroupFloatIndex = layout.GroupFloatCount;

			if (buffer == null || !buffer.IsValid())
			{
				if (buffer != null) buffer.Dispose();
				buffer = null;
			}

			// Instantiate buffers
			if (buffer == null)
			{
				groupFixedDataSize = lastGroupFloatIndex + 1;
				int maxBufferSize = 1 //dancer count
					+ 1 //items start index
					+ groupFixedDataSize
					+ MAX_DANCERS * DANCER_DATA_SIZE;

				buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxBufferSize, Marshal.SizeOf(typeof(float)));
				bufferData = new float[maxBufferSize];
			}
		}


		protected override void Update()
		{
			UpdateDancers();
		}

		int updatedFrame = -1;

		/// <summary>Places the dancers for this frame, once: a slave calls it on its master before reading it, so it follows
		/// the master's positions of this frame whatever the order of the Updates.</summary>
		public void UpdateDancers()
		{
			if (updatedFrame == Time.frameCount) return;
			updatedFrame = Time.frameCount;
			base.Update();
			if (buffer == null || !buffer.IsValid()) init();

			if (Math.Abs(patternSpeed) < 0.01f)
			{
				patternSpeed = 0;
			}

			patternTime += Time.deltaTime * patternSpeed;

			float curCount = getCount();

			for (int i = 0; i < items.Count; i++)
			{
				Dancer d = items[i];
				d.weight = i <= curCount - 1 ? 1 : getCountRelativeProgression();
				d.intensity = dancerIntensity * d.weight;

				float dancerSizeSpreadFactor = 0;
                if (items.Count > 1)
				{
                    dancerSizeSpreadFactor = Mathf.Lerp(1, i / Mathf.Max(curCount - 1, 1), dancerSizeSpread * 0.99f); // if size is 0, particle glitch, so we avoid to reach a full 0 size
                }
				else
				{
					dancerSizeSpreadFactor = 1;
                }
					
				d.size = dancerSize * dancerSizeSpreadFactor * Mathf.Lerp(1, d.weight, dancerWeightSizeFactor);
				d.size = d.size * (1 + 100 * dancerHyperSize);

				d.transform.localPosition = Vector3.zero;

				d.localPatternTime += Time.deltaTime * (patternSpeed * Mathf.Lerp(1, .5f + d.randomFactor, patternSpeedRandom));
			}


			// The patterns add up in pattern units, then the sum is scaled once by patternSize (spread over the dancers by
			// patternSizeSpread), then the patterns that work in local units (the noise) apply on top
			foreach (var p in patterns)
			{
				if (!p.AfterSize) p.updatePattern(this);
			}
			for (int i = 0; i < items.Count; i++)
			{
				Dancer d = items[i];
				if (d == null) continue;
				float rel = i * 1f / Mathf.Max(activeCount, 1e-3f);
				float pSize = patternSize * Mathf.Lerp(1, rel, patternSizeSpread);
				d.transform.localPosition *= pSize;
			}
			foreach (var p in patterns)
			{
				if (p.AfterSize) p.updatePattern(this);
			}

			followMaster(Time.deltaTime);


			foreach (var d in items)
			{
				float lookAtWeight = Mathf.Clamp01(dancerLookAtMode);
				float groupAbsoluteWeight = Mathf.Clamp01(dancerLookAtMode - 1);

				Vector3 groupLookAtTarget = transform.TransformPoint(dancerLookAt);
				Vector3 absoluteLookAtTarget = dancerLookAt;
				Vector3 lookAtTarget = Vector3.Lerp(groupLookAtTarget, absoluteLookAtTarget, groupAbsoluteWeight);

				Vector3 up = transform.rotation * Vector3.up;
				Quaternion lookRot = lookAtTarget == d.transform.position ? Quaternion.identity : Quaternion.LookRotation(lookAtTarget - d.transform.position, up);

				Quaternion targetPureRot = Quaternion.Lerp(Quaternion.identity, lookRot, lookAtWeight) * Quaternion.Euler(dancerRotation);
				Quaternion targetGroupRotatedRot = transform.rotation * targetPureRot;

				d.targetRotation = Quaternion.Lerp(targetGroupRotatedRot, targetPureRot, lookAtWeight);
				d.transform.rotation = d.targetRotation;
			}

			//Update buffer with new data
			int count = fillBufferData();
			buffer.SetData(bufferData, 0, 0, count);
		}

		// Master: once synced, dancer i sits exactly on the master's dancer i (no smoothing). masterBalletTransition
		// only shapes the changes: the weight ramps from the own patterns to the master (and back on release), and a
		// change of master fades out the gap between the old place and the new master over the same time.
		void followMaster(float dt)
		{
			bool wanted = !string.IsNullOrEmpty(targetMasterBallet) && targetMasterBallet != "None";
			if (wanted && ((master as UnityEngine.Object) == null || masterResolved != targetMasterBallet))
			{
				if (masterName != targetMasterBallet || Time.unscaledTime >= nextMasterSearch)
				{
					masterName = targetMasterBallet;
					var found = DancerGroups.Find(targetMasterBallet, this);
					nextMasterSearch = Time.unscaledTime + 1f;
					// Switching from one master to another while synced: start from where the dancers are
					if (found != null && (master as UnityEngine.Object) != null && found != master && masterWeight > 0)
					{
						for (int i = 0; i < Mathf.Min(items.Count, MAX_DANCERS); i++)
							if (items[i] != null && found.DancerCount > 0)
								masterSwitchOffset[i] = masterLast[i] - found.GetDancerPosition(i % found.DancerCount);
						masterSwitch = 0;
					}
					if (found != null) { master = found; masterResolved = targetMasterBallet; }
				}
			}
			if ((master as UnityEngine.Object) != null) master.UpdateDancers();
			bool masterAlive = (master as UnityEngine.Object) != null && master.DancerCount > 0;
			bool following = wanted && masterAlive && masterResolved == targetMasterBallet;

			float step = masterBalletTransition > 0 ? dt / masterBalletTransition : 1;
			masterWeight = Mathf.MoveTowards(masterWeight, following ? 1 : 0, step);
			masterSwitch = Mathf.MoveTowards(masterSwitch, 1, step);
			masterIntensityWeight = Mathf.MoveTowards(masterIntensityWeight, masterSyncIntensity ? 1 : 0, step);
			masterSizeWeight = Mathf.MoveTowards(masterSizeWeight, masterSyncSize ? 1 : 0, step);
			if (masterWeight <= 0) return;

			float w = masterWeight * masterWeight * (3 - 2 * masterWeight);
			float s = masterSwitch * masterSwitch * (3 - 2 * masterSwitch);
			int n = Mathf.Min(items.Count, MAX_DANCERS);
			for (int i = 0; i < n; i++)
			{
				Dancer d = items[i];
				if (d == null) continue;
				// The master's dancer (kept while fading out on release; frozen if the master is gone)
				if (masterAlive) masterLast[i] = master.GetDancerPosition(i % master.DancerCount) + masterSwitchOffset[i] * (1 - s);
				d.transform.position = w >= 1 ? masterLast[i] : Vector3.Lerp(d.transform.position, masterLast[i], w);
				// Intensity and size can follow the master's dancer too (options), each blended in and out over the transition
				if (masterAlive)
				{
					int m = i % master.DancerCount;
					float wi = w * masterIntensityWeight * masterIntensityWeight * (3 - 2 * masterIntensityWeight);
					float ws = w * masterSizeWeight * masterSizeWeight * (3 - 2 * masterSizeWeight);
					if (wi > 0) d.intensity = Mathf.Lerp(d.intensity, master.GetDancerIntensity(m), wi);
					if (ws > 0) d.size = Mathf.Lerp(d.size, master.GetDancerSize(m), ws);
				}
			}
		}

		// Read when a slave looks for its master (not every frame): a manager renames its groups after they are enabled
		public string GroupName => gameObject.name;
		public float Count => activeCount;

		// A synced slave takes its master's count, ramped over the transition (its own count field is kept for release)
		protected override float activeCount
		{
			get
			{
				if (masterWeight <= 0 || countAsked || (master as UnityEngine.Object) == null) return count;
				countAsked = true;      // two groups following each other: no endless loop
				// Ramped with the transition: dancers appear (or leave) one by one, fading as the count does
				float w = masterWeight * masterWeight * (3 - 2 * masterWeight);
				float c = Mathf.Lerp(count, master.Count, w);
				countAsked = false;
				return c;
			}
		}
		bool countAsked;
		public int DancerCount => items != null ? items.Count : 0;
		public Vector3 GetDancerPosition(int index)
		{
			Dancer d = items[index];
			return d != null ? d.transform.position : transform.position;
		}
		public float GetDancerIntensity(int index)
		{
			Dancer d = items[index];
			return d != null ? d.intensity : 0;
		}
		public float GetDancerSize(int index)
		{
			Dancer d = items[index];
			return d != null ? d.size : 0;
		}

		//Helpers
		protected virtual Type getGroupType() { return GetType(); }

		// Fills bufferData (same layout as the buffer) and returns the number of floats to upload
		int fillBufferData()
		{
			int indexOffset = 2; // first 2 are dancer count and group start index
			int count = indexOffset + groupFixedDataSize + items.Count * DANCER_DATA_SIZE;
			Array.Clear(bufferData, 0, count);

			int itemsStartIndex = indexOffset + groupFixedDataSize - 1;

			// Dancer Count
			bufferData[0] = items.Count;
			// Dancer Start Index
			bufferData[1] = itemsStartIndex;

			//fill fixed data
			layout.Write(this, bufferData, indexOffset, customValue);

			//fill dancer data
			for (int i = 0; i < items.Count; i++)
			{
				Dancer d = items[i];
				int start = itemsStartIndex + i * DANCER_DATA_SIZE;
				Vector3 position = d.transform.position;
				Vector3 rotation = d.targetRotation.eulerAngles;
				bufferData[start] = position.x;
				bufferData[start + 1] = position.y;
				bufferData[start + 2] = position.z;
				bufferData[start + 3] = rotation.x;
				bufferData[start + 4] = rotation.y;
				bufferData[start + 5] = rotation.z;
				bufferData[start + 6] = d.intensity;
				bufferData[start + 7] = d.size;
			}

			return count;
		}

		public override float getCount()
		{
			float c = activeCount;
			int floorCount = (int)Math.Floor(c);
			float rel = c - floorCount;
			float dzRel = countDeadZone.Evaluate(rel);
			return floorCount + dzRel;
		}

		public int getFullDancersCount() { return Mathf.FloorToInt(getCount()); }
		public float getCountRelativeProgression() { return getCount() - getFullDancersCount(); }

		protected virtual object getBufferValueAtIndex(int index) { 
			return 0; 
		}

		public void ResetPattern()
		{
			patternTime = 0;

            for (int i = 0; i < items.Count; i++)
            {
                Dancer d = items[i];
                d.localPatternTime = 0;
            }
        }

		public float GetPatternTime()
		{
			return patternTime;
        }
    }
}