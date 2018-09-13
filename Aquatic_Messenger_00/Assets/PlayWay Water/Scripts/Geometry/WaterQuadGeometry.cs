using PlayWay.Water.Internal;
using UnityEngine;

namespace PlayWay.Water
{
	public class WaterQuadGeometry : WaterPrimitiveBase
	{
		private Mesh[] meshes;

		public override Mesh[] GetTransformedMeshes(Camera camera, out Matrix4x4 matrix, int vertexCount, bool volume)
		{
			matrix = GetMatrix(camera);
			return meshes ?? (meshes = new[] { Quads.BipolarXZ });
		}

		protected override Matrix4x4 GetMatrix(Camera camera)
		{
			Vector3 position = camera.transform.position;
			float farClipPlane = camera.farClipPlane;

			Matrix4x4 matrix = new Matrix4x4
			{
				m03 = position.x,
				m13 = position.y,
				m23 = position.z,
				m00 = farClipPlane,
				m11 = farClipPlane,
				m22 = farClipPlane
			};

			return matrix;
		}

		protected override Mesh[] CreateMeshes(int vertexCount, bool volume)
		{
			return new[] { Quads.BipolarXZ };
		}
	}
}
