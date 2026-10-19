using System;
using UnityEngine;

namespace Oxipital
{
	// Replaces the few DOTween calls of the camera rigs, so the package needs no plugin.
	// Same feel as DOTween's defaults : ease OutQuad, advanced in Update with Time.deltaTime,
	// and Rotate interpolates each Euler axis the shortest way like DORotate (RotateMode.Fast).
	// The owner calls Update every frame; starting a tween replaces the one running.
	internal class CameraTween
	{
		Action<float> apply;
		float duration;
		float elapsed;

		public bool active { get; private set; }

		public void Start(float duration, Action<float> apply)
		{
			this.apply = apply;
			this.duration = duration;
			elapsed = 0;
			active = true;

			if (duration <= 0)
				Update(0);
		}

		public void Kill()
		{
			active = false;
		}

		public void Update(float deltaTime)
		{
			if (!active)
				return;

			elapsed += deltaTime;
			float t = duration > 0 ? Mathf.Clamp01(elapsed / duration) : 1;
			if (t >= 1)
				active = false;

			apply(1 - (1 - t) * (1 - t));
		}

		public void Move(Transform target, Vector3 to, float duration)
		{
			Vector3 from = target.position;
			Start(duration, e => target.position = Vector3.LerpUnclamped(from, to, e));
		}

		public void Rotate(Transform target, Vector3 toEuler, float duration)
		{
			Vector3 from = target.eulerAngles;
			Vector3 change = new Vector3(
				Mathf.DeltaAngle(from.x, toEuler.x),
				Mathf.DeltaAngle(from.y, toEuler.y),
				Mathf.DeltaAngle(from.z, toEuler.z));
			Start(duration, e => target.rotation = Quaternion.Euler(from + change * e));
		}
	}
}
