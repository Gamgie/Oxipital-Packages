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
	public class DancerGroup<T> : BaseManager<T> where T : Dancer
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

		public DancerGroup(string itemName = "Dancer") : base(itemName)
		{

		}

		override protected void OnEnable()
		{
			init();
		}

		virtual protected void OnDisable()
		{
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


			foreach (var p in patterns)
			{
				p.updatePattern(this);
			}


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
			int floorCount = (int)Math.Floor(count);
			float rel = count - floorCount;
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