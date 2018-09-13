using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayWay.Water
{
	[Serializable]
	public class WaterRadialGrid : WaterPrimitiveBase
	{
		private static readonly int[][] horizontalVerticesToVertexCount;

		static WaterRadialGrid()
		{
			const int maxHorizontalSegments = 600;
			horizontalVerticesToVertexCount = new int[17][];
			
			for (int fovIndex = 0; fovIndex < horizontalVerticesToVertexCount.Length; ++fovIndex)
			{
				horizontalVerticesToVertexCount[fovIndex] = new int[maxHorizontalSegments];

				float fov = ((fovIndex + 1)*5.0f - 1.0f) * Mathf.Deg2Rad;

				Vector2 vector1 = new Vector2(Mathf.Sin(-fov), Mathf.Cos(-fov)).normalized;

				for (int i = 0; i < maxHorizontalSegments; ++i)
				{
					int horizontalVerticesCount = i + 2;

					float fx = fov * (2.0f/(horizontalVerticesCount - 1) - 1.0f);
					Vector2 vector2 = new Vector2(Mathf.Sin(fx), Mathf.Cos(fx)).normalized;

					float fy = 1.0f;
					int verticalVerticesCount = 0;
					float maxDistance = Vector2.Distance(vector1, vector2);

					while (fy >= 0.005f)
					{
						++verticalVerticesCount;
						fy -= maxDistance * fy;
					}

					verticalVerticesCount += 2;

					horizontalVerticesToVertexCount[fovIndex][i] = horizontalVerticesCount*verticalVerticesCount;
				}
			}
		}

		public override Mesh[] GetTransformedMeshes(Camera camera, out Matrix4x4 matrix, int vertexCount, bool volume)
		{
			int fovIndex;

			if (camera != null)
			{
				float horizontalFov = 2.0f * Mathf.Atan(Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * camera.aspect) * Mathf.Rad2Deg;
				fovIndex = camera.orthographic ? 14 : Mathf.CeilToInt(horizontalFov * 0.5f * 0.2f - 0.8f);

				if (fovIndex >= horizontalVerticesToVertexCount.Length)
					fovIndex = horizontalVerticesToVertexCount.Length - 1;

				matrix = GetMatrix(camera, ((fovIndex + 1) * 5.0f - 1.0f) * Mathf.Deg2Rad);
			}
			else
			{
				matrix = Matrix4x4.identity;
				fovIndex = 14;
			}

			CachedMeshSet cachedMeshSet;
			int hash = vertexCount | (fovIndex << 26);

			if(volume) hash = -hash;

			if(!cache.TryGetValue(hash, out cachedMeshSet))
				cache[hash] = cachedMeshSet = new CachedMeshSet(CreateMeshes(vertexCount, volume, fovIndex));
			else
				cachedMeshSet.Update();

			return cachedMeshSet.meshes;
		}

		protected override Mesh[] CreateMeshes(int vertexCount, bool volume)
		{
			throw new NotImplementedException();
		}

		protected Mesh[] CreateMeshes(int vertexCount, bool volume, int fovIndex)
		{
			int verticesX = 0;

			var horizontalVerticesToVertexCount = WaterRadialGrid.horizontalVerticesToVertexCount[fovIndex];

			for (int i = 0; i < horizontalVerticesToVertexCount.Length; ++i)
			{
				if (horizontalVerticesToVertexCount[i] > vertexCount)
				{
					verticesX = i + 1;
					break;
				}
			}
			
			int totalVerticesY = Mathf.FloorToInt((float)vertexCount / verticesX);
			int verticesY = totalVerticesY;
			
			var meshes = new List<Mesh>();
			var vertices = new List<Vector3>();
			var indices = new List<int>();
			int vertexIndex = 0;
			int meshIndex = 0;
			float fov = ((fovIndex + 1) * 5.0f - 1.0f) * Mathf.Deg2Rad;

			var vectors = new Vector2[verticesX];

			for(int x = 0; x < verticesX; ++x)
			{
				float fx = (float)x / (verticesX - 1) * 2.0f - 1.0f;
				
				// put more vertices at the center
				fx = fx >= 0.0f ? 1.0f + Mathf.Sin((fx - 1.0f) * Mathf.PI * 0.5f) : -1.0f - Mathf.Sin((fx - 1.0f) * Mathf.PI * 0.5f);

				fx *= fov;

				vectors[x] = new Vector2(
						Mathf.Sin(fx),
						Mathf.Cos(fx)
					).normalized;
			}

			float fy = 1.0f, previousFy = 1.0f;
			float maxDistance = Vector2.Distance(vectors[0], vectors[1]);

			if (volume)
			{
				// no need to draw distant geometry for this purpose
				while(fy > 0.4f)
				{
					previousFy = fy;
					fy -= maxDistance * fy;
					--verticesY;
				}
			}

			for(int y = 0; y < verticesY; ++y)
			{
				for(int x = 0; x < verticesX; ++x)
				{
					Vector2 vector = vectors[x] * fy;

					if (y == verticesY - 1)
						vertices.Add(new Vector3(0.0f, 0.0f, 0.0f));
					else if (y > 1 || !volume)
						vertices.Add(new Vector3(vector.x, 0.0f, vector.y));
					else if (y == 1)
					{
						vector = vectors[x] * (1.0f - maxDistance);
						vertices.Add(new Vector3(vector.x*10.0f, -0.9f, vector.y)*0.5f);
					}
					else
					{
						vector = vectors[x];
						vertices.Add(new Vector3(vector.x*10.0f, -0.9f, vector.y*-10.0f)*0.5f);
					}

					if(x != 0 && y != 0 && vertexIndex > verticesX)
					{
						indices.Add(vertexIndex);
						indices.Add(vertexIndex - 1);
						indices.Add(vertexIndex - verticesX - 1);
						indices.Add(vertexIndex - verticesX);
					}

					++vertexIndex;

					if(vertexIndex == 65000)
					{
						var mesh = CreateMesh(vertices.ToArray(), indices.ToArray(), string.Format("Radial Grid {0}x{1} - {2}", verticesX, totalVerticesY, meshIndex.ToString("00")));
						meshes.Add(mesh);

						--x; --y;

						fy = previousFy;

						vertexIndex = 0;
						vertices.Clear();
						indices.Clear();

						++meshIndex;
					}
				}

				previousFy = fy;
				fy -= maxDistance * fy;
			}

			if(vertexIndex != 0)
			{
				var mesh = CreateMesh(vertices.ToArray(), indices.ToArray(), string.Format("Radial Grid {0}x{1} - {2}", verticesX, totalVerticesY, meshIndex.ToString("00")));
				meshes.Add(mesh);
			}

			return meshes.ToArray();
		}

		protected Matrix4x4 GetMatrix(Camera camera, float fov)
		{
			Vector3 down = WaterUtilities.ViewportWaterPerpendicular(camera);
			Vector3 right = WaterUtilities.ViewportWaterRight(camera);

			float waterPositionY = water.transform.position.y;
			Vector3 ld = WaterUtilities.RaycastPlane(camera, waterPositionY, (down - right));
			Vector3 rd = WaterUtilities.RaycastPlane(camera, waterPositionY, (down + right));

			float farClipPlane = camera.farClipPlane;
			Vector3 position = camera.transform.position;
			float width = rd.x - ld.x;

			if(width < 0.0f)
				width = -width;

			float tan = Mathf.Tan(fov*0.5f);
			float offset = -(width + Mathf.Max(0.0f, farClipPlane * 0.005f - camera.nearClipPlane) + (water.MaxHorizontalDisplacement + water.MaxVerticalDisplacement) / tan);

			if(camera.orthographic)
				offset -= camera.orthographicSize * 3.2f;

			float dp = camera.transform.forward.y;             // Vector3.Dot(Vector3.down, camera.transform.forward)
			Vector3 forward = dp < -0.98f || dp > 0.98f ? -camera.transform.up : camera.transform.forward;
			float len = Mathf.Sqrt(forward.x * forward.x + forward.z * forward.z);
			forward.x /= len;
			forward.z /= len;

			float scale = farClipPlane - offset;

			return Matrix4x4.TRS(
				new Vector3(position.x + forward.x * offset, waterPositionY, position.z + forward.z * offset),
				Quaternion.AngleAxis(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, Vector3.up),
				new Vector3(scale, scale, scale)
			);
		}

		protected override Matrix4x4 GetMatrix(Camera camera)
		{
			throw new System.InvalidOperationException();
		}
	}
}
