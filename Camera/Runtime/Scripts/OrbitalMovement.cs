using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

namespace Oxipital
{
	public class OrbitalMovement : CameraMovement
	{
		public float rotateYSpeed;
		public float rotateXSpeed;
		public float rotateZSpeed;
		[Space()]
		public bool controlRotateWithAngle = false;
		public float rotateYAngle;
		public float rotateXAngle;
		public float rotateZAngle;
		[Space()]
		public Vector3 positionTarget;
		public float moveToDuration;

		private CinemachineFollow _transposer;
		private readonly CameraTween _moveTween = new CameraTween();
		private readonly CameraTween _resetMoveTween = new CameraTween();
		private readonly CameraTween _rotateTween = new CameraTween();

		public override void Init()
		{
			base.Init();
			_transposer = virtualCamera.GetComponentInChildren<CinemachineFollow>();
			type = CameraController.CameraMovementType.Orbital;
			_isActive = false;
		}

		void Update()
		{
			_moveTween.Update(Time.deltaTime);
			_resetMoveTween.Update(Time.deltaTime);
			_rotateTween.Update(Time.deltaTime);
		}

		public override bool UpdateMovement()
		{
			// our camera is not active so leave now.
			if (!base.UpdateMovement())
				return false;

			// Rotate target transform according to speed
			if (controlRotateWithAngle == false)
			{
				transform.Rotate(rotateXSpeed * Time.deltaTime, rotateYSpeed * Time.deltaTime, rotateZSpeed * Time.deltaTime);
				rotateXAngle = transform.eulerAngles.x;
				rotateYAngle = transform.eulerAngles.y;
				rotateZAngle = transform.eulerAngles.z;
			}
			else // We want to control rotation with angle directly
			{
				transform.eulerAngles = new Vector3(rotateXAngle, rotateYAngle, rotateZAngle);
			}

			if(!_moveTween.active)
				transform.position = positionTarget;

			return true;
		}

		public override void Reset(float duration)
		{
			rotateYSpeed = 0;
			rotateXSpeed = 0;
			rotateZSpeed = 0;
			_resetMoveTween.Move(transform, Vector3.zero, duration);
			_rotateTween.Rotate(transform, Vector3.zero, duration);
		}

		public override void UpdateZOffset(float offset)
		{
			// Control distance between target and camera
			if (_transposer != null)
			{
				_transposer.FollowOffset = new Vector3(0, 0, -offset);
			}
		}

		public void TopView()
		{
			rotateYSpeed = 0;
			rotateXSpeed = 0;
			_rotateTween.Rotate(transform, new Vector3(90, 0, 0), moveToDuration);
		}

		public void DownView()
		{
			rotateYSpeed = 0;
			rotateXSpeed = 0;
			_rotateTween.Rotate(transform, new Vector3(-90, 0, 0), moveToDuration);
		}

		public void LeftView()
		{
			rotateYSpeed = 0;
			rotateXSpeed = 0;
			_rotateTween.Rotate(transform, new Vector3(0, 90, 0), moveToDuration);
		}

		public void RightView()
		{
			rotateYSpeed = 0;
			rotateXSpeed = 0;
			_rotateTween.Rotate(transform, new Vector3(0, -90, 0), moveToDuration);
		}

		public void FrontView()
		{
			rotateYSpeed = 0;
			rotateXSpeed = 0;
			_rotateTween.Rotate(transform, new Vector3(0, 0, 0), moveToDuration);
		}

		public void RandomView()
		{
			Random.InitState(Time.frameCount);
			float xRandom = Random.Range(-180, 180);
			float yRandom = Random.Range(-180, 180);
			float zRandom = Random.Range(-180, 180);
			Debug.Log(xRandom + " , " + yRandom + " , " + zRandom);

			_rotateTween.Rotate(transform, new Vector3(xRandom, yRandom, zRandom), moveToDuration);
		}

		public override void SetActive(bool activate, float duration = 0)
		{
			base.SetActive(activate);
			//_rigidbody.isKinematic = activate;

			if(activate)
				_moveTween.Move(transform, positionTarget, duration);
		}

		public override void SetCameraTransform(Vector3 position, Quaternion rotation)
		{
			// The virtual camera always looks at this rig's own position (Follow offset
			// is applied behind the rig, Aim just copies the rig's rotation), so the rig
			// must stay pinned to positionTarget - that's what keeps it centered on the
			// pivot. Dragging the rig to the incoming camera's position would break that
			// and make it look at empty space until the move-in tween finished.
			// Only the rotation is handed off, so the orbit starts facing roughly where
			// the previous camera was looking; Cinemachine's blend smooths out the
			// resulting positional discontinuity.
			transform.rotation = rotation;

			Vector3 euler = rotation.eulerAngles;
			rotateXAngle = euler.x;
			rotateYAngle = euler.y;
			rotateZAngle = euler.z;
		}
	}
}
