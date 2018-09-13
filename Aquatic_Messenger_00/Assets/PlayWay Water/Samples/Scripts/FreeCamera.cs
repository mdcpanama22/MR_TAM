using UnityEngine;

namespace PlayWay.WaterSamples
{
	public sealed class FreeCamera : MonoBehaviour
	{
		[SerializeField]
		private float speed = 20.0f;

		[SerializeField]
		private float mouseSensitivity = 2.0f;

		private Camera localCamera;

		private void Awake()
		{
			localCamera = GetComponentInChildren<Camera>();
		}

		private void Update()
		{
			float speed = this.speed;

			if(Input.GetKey(KeyCode.LeftShift))
				speed *= 4.0f;

			if(Input.GetKey(KeyCode.W))
				transform.position += transform.forward * speed * Time.unscaledDeltaTime;
			
			if(Input.GetKey(KeyCode.S))
				transform.position -= transform.forward * speed * Time.unscaledDeltaTime;

			if(Input.GetKey(KeyCode.A))
				transform.position -= transform.right * speed * Time.unscaledDeltaTime;

			if(Input.GetKey(KeyCode.D))
				transform.position += transform.right * speed * Time.unscaledDeltaTime;

			if(Input.GetMouseButton(1))
			{
				transform.localEulerAngles += new Vector3(-Input.GetAxisRaw("Mouse Y") * mouseSensitivity, 0.0f, 0.0f);
				transform.localEulerAngles += new Vector3(0.0f, Input.GetAxisRaw("Mouse X") * mouseSensitivity, 0.0f);
			}

			localCamera.farClipPlane = Mathf.Max(4000.0f, 2000.0f + transform.position.y * 40);
			localCamera.nearClipPlane = localCamera.farClipPlane * (1.0f / 4000.0f);
        }
	}
}
