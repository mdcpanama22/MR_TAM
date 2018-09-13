using UnityEngine;

namespace PlayWay.WaterSamples
{
	[RequireComponent(typeof(CharacterController))]
	public class SampleCharacterController : MonoBehaviour
	{
		[SerializeField]
		private Camera eyesCamera;

		[SerializeField]
		private float walkSpeed = 8.0f;

		[SerializeField]
		private float runSpeed = 14.0f;

		private CharacterController characterController;
		private bool freeCamera;
		private Vector3 velocity;

		private void Awake()
		{
			characterController = GetComponent<CharacterController>();
		}

		private void OnEnable()
		{
			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;
		}

		private void OnDisable()
		{
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
		}

		private void Update()
		{
			if(Input.GetKeyDown(KeyCode.F))
			{
				freeCamera = !freeCamera;
				Cursor.lockState = freeCamera ? CursorLockMode.None : CursorLockMode.Locked;
				Cursor.visible = freeCamera;

				if (!freeCamera)
				{
					// reset position
					eyesCamera.transform.localPosition = new Vector3(0.0f, 0.644f, 0.0f);
					eyesCamera.transform.localRotation = new Quaternion();
				}

				eyesCamera.GetComponent<FreeCamera>().enabled = freeCamera;
			}

			if(freeCamera)
				return;

			float horizontal = Input.GetAxis("Mouse X");
			float vertical = Input.GetAxis("Mouse Y");

			eyesCamera.transform.localEulerAngles = new Vector3(eyesCamera.transform.localEulerAngles.x - vertical, 0.0f, 0.0f);
			transform.localEulerAngles = new Vector3(0.0f, transform.localEulerAngles.y + horizontal, 0.0f);

			if (characterController.isGrounded)
			{
				velocity = new Vector3();

				if (Input.GetKey(KeyCode.W))
					velocity += transform.forward;

				if (Input.GetKey(KeyCode.S))
					velocity -= transform.forward;

				if (Input.GetKey(KeyCode.A))
					velocity -= transform.right;

				if (Input.GetKey(KeyCode.D))
					velocity += transform.right;

				if (Input.GetKey(KeyCode.Space))
					velocity += velocity + new Vector3(0.0f, 2.2f, 0.0f);
			}
			
			velocity += Physics.gravity * Time.deltaTime;

			float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
			characterController.Move(velocity * (speed * Time.deltaTime));
		}
	}
}
