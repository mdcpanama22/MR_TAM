using UnityEngine;

namespace PlayWay.Water.Internal
{
	/// <summary>
	/// An utility class that creates and caches some quads used for water rendering.
	/// </summary>
	public class Quads
	{
		private static Mesh bipolarXY;
		private static Mesh bipolarXInversedY;
		private static Mesh bipolarXZ;
		private static bool initialized;

		public static Mesh BipolarXY
		{
			get
			{
				if (!initialized)
					CreateMeshes();

				return bipolarXY;
			}
		}

		public static Mesh BipolarXInversedY
		{
			get
			{
				if(!initialized)
					CreateMeshes();

				return bipolarXInversedY;
			}
		}

		public static Mesh BipolarXZ
		{
			get
			{
				if(!initialized)
					CreateMeshes();

				return bipolarXZ;
			}
		}

		private static void CreateMeshes()
		{
			bipolarXY = CreateBipolarXY(false);
			bipolarXInversedY = CreateBipolarXY(SystemInfo.graphicsDeviceVersion.Contains("Direct3D"));
			bipolarXZ = CreateBipolarXZ();
			initialized = true;
		}

		private static Mesh CreateBipolarXY(bool inversedY)
		{
			var mesh = new Mesh
			{
				hideFlags = HideFlags.DontSave,
				vertices = new[]
				{
					new Vector3(-1.0f, -1.0f, 0.0f),
					new Vector3(1.0f, -1.0f, 0.0f),
					new Vector3(1.0f, 1.0f, 0.0f),
					new Vector3(-1.0f, 1.0f, 0.0f)
				},
				uv = new[]
				{
					new Vector2(0.0f, inversedY ? 1.0f : 0.0f),
					new Vector2(1.0f, inversedY ? 1.0f : 0.0f),
					new Vector2(1.0f, inversedY ? 0.0f : 1.0f),
					new Vector2(0.0f, inversedY ? 0.0f : 1.0f)
				}
			};
			
			mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
			mesh.UploadMeshData(true);
			return mesh;
		}

		private static Mesh CreateBipolarXZ()
		{
			var quadMesh = new Mesh
			{
				name = "Shoreline Quad Mesh",
				hideFlags = HideFlags.DontSave,
				vertices = new[]
				{
					new Vector3(-1.0f, 0.0f, -1.0f),
					new Vector3(-1.0f, 0.0f, 1.0f),
					new Vector3(1.0f, 0.0f, 1.0f),
					new Vector3(1.0f, 0.0f, -1.0f)
				},
				uv = new[]
				{
					new Vector2(0.0f, 0.0f),
					new Vector2(0.0f, 1.0f),
					new Vector2(1.0f, 1.0f),
					new Vector2(1.0f, 0.0f)
				}
			};


			quadMesh.SetIndices(new[] { 0, 1, 2, 3 }, MeshTopology.Quads, 0);
			quadMesh.UploadMeshData(true);

			return quadMesh;
		}
	}
}
