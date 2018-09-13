using UnityEngine;

namespace PlayWay.Water
{
	public static class WaterUtilities
	{
		public static Vector3 RaycastPlane(Camera camera, float planeHeight, Vector3 pos)
		{
			Ray ray = camera.ViewportPointToRay(pos);
			Vector3 direction = ray.direction;

			if(camera.transform.position.y > planeHeight)
			{
				if(direction.y > -0.01f)
					direction.y = -direction.y - 0.02f;
			}
			else if(direction.y < 0.01f)
				direction.y = -direction.y + 0.02f;

			float t = -(ray.origin.y - planeHeight) / direction.y;
			float angle = -camera.transform.eulerAngles.y * Mathf.Deg2Rad;
			float s = Mathf.Sin(angle);
			float c = Mathf.Cos(angle);

			return new Vector3(
				t * (direction.x * c + direction.z * s),
				t * direction.y,
				t * (direction.x * s + direction.z * c)	
			);
		}

		public static Vector3 ViewportWaterPerpendicular(Camera camera)
		{
			Vector3 down = camera.worldToCameraMatrix.MultiplyVector(new Vector3(0.0f, -1.0f, 0.0f));
			down.z = 0.0f;

			// normalize, mul 0.5
			float lenInv = 0.5f / Mathf.Sqrt(down.x * down.x + down.y * down.y);
			down.x = down.x * lenInv + 0.5f;
			down.y = down.y * lenInv + 0.5f;

			return down;
		}

		public static Vector3 ViewportWaterRight(Camera camera)
		{
			Vector3 right = camera.worldToCameraMatrix.MultiplyVector(Vector3.Cross(camera.transform.forward, new Vector3(0.0f, -1.0f, 0.0f)));
			right.z = 0.0f;

			// normalize, mul 0.5
			float lenInv = 0.5f / Mathf.Sqrt(right.x * right.x + right.y * right.y);
			right.x *= lenInv;
			right.y *= lenInv;

			return right;
		}

		public static void Destroy(this Object obj)
		{
#if !UNITY_EDITOR
			Object.Destroy(obj);
#else
			if(Application.isPlaying)
				Object.Destroy(obj);
			else
				Object.DestroyImmediate(obj);
#endif
		}
	}
}
