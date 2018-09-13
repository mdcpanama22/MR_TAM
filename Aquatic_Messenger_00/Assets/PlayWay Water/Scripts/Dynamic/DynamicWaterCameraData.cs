using UnityEngine;

namespace PlayWay.Water
{
	public class DynamicWaterCameraData
	{
		private RenderTexture dynamicDisplacementMap;			// RGB - displacement xyz, A - free
		private RenderTexture normalMap;
		private RenderTexture foamMapA;
		private RenderTexture foamMapB;
		private RenderTexture displacementsMask;
		private RenderTexture totalDisplacementMap;
		private RenderTexture debugMap;
		private bool totalDisplacementMapDirty;
		private readonly int antialiasing;
		private readonly WaterCamera camera;
		private readonly DynamicWater dynamicWater;

		internal int lastFrameUsed;

		public DynamicWaterCameraData(DynamicWater dynamicWater, WaterCamera camera, int antialiasing)
		{
			this.dynamicWater = dynamicWater;
			this.camera = camera;
			this.antialiasing = antialiasing;

			Initialization = true;

			camera.RenderTargetResized += Camera_RenderTargetResized;
			CreateRenderTargets();
		}

		public bool Initialization { get; set; }

		/// <summary>
		/// RGB = XYZ displacement, A = free
		/// </summary>
		public RenderTexture DynamicDisplacementMap
		{
			get { return dynamicDisplacementMap; }
		}

		/// <summary>
		/// RG = XY normal
		/// </summary>
		public RenderTexture NormalMap
		{
			get { return normalMap; }
		}

		public RenderTexture FoamMap
		{
			get { return foamMapA; }
		}

		public RenderTexture FoamMapPrevious
		{
			get { return foamMapB; }
		}

		public RenderTexture DisplacementsMask
		{
			get { return displacementsMask; }
		}
		
		public WaterCamera Camera
		{
			get { return camera; }
		}

		public RenderTexture GetDebugMap(bool createIfNotExists = false)
		{
			if(createIfNotExists && debugMap == null)
				debugMap = CreateDynamicMap("Dynamic Water Map: Debug", RenderTextureFormat.ARGBHalf, dynamicDisplacementMap.width, dynamicDisplacementMap.height, dynamicDisplacementMap.antiAliasing);

			return debugMap;
		}

		public RenderTexture GetTotalDisplacementMap()
		{
			if(totalDisplacementMapDirty)
			{
				dynamicWater.RenderTotalDisplacementMap(camera, totalDisplacementMap);
				totalDisplacementMapDirty = false;
			}

			return totalDisplacementMap;
		}

		public void Dispose()
		{
			camera.RenderTargetResized -= Camera_RenderTargetResized;
			DisposeTextures();
		}

		private void DisposeTextures()
		{
			if(dynamicDisplacementMap != null)
			{
				dynamicDisplacementMap.Destroy();
				dynamicDisplacementMap = null;
			}

			if(normalMap != null)
			{
				normalMap.Destroy();
				normalMap = null;
			}

			if(foamMapA != null)
			{
				foamMapA.Destroy();
				foamMapA = null;
			}

			if(foamMapB != null)
			{
				foamMapB.Destroy();
				foamMapB = null;
			}

			if(displacementsMask != null)
			{
				displacementsMask.Destroy();
				displacementsMask = null;
			}

			if(totalDisplacementMap != null)
			{
				totalDisplacementMap.Destroy();
				totalDisplacementMap = null;
			}

			if(debugMap != null)
			{
				debugMap.Destroy();
				debugMap = null;
			}
		}

		public void ClearOverlays()
		{
			SwapFoamMaps();

			Graphics.SetRenderTarget(dynamicDisplacementMap);
			GL.Clear(false, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));

			Graphics.SetRenderTarget(normalMap);
			GL.Clear(false, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));

			Graphics.SetRenderTarget(foamMapA);
			GL.Clear(false, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));

			Graphics.SetRenderTarget(displacementsMask);
			GL.Clear(false, true, new Color(1.0f, 1.0f, 1.0f, 1.0f));
			
			if(debugMap != null)
			{
				Graphics.SetRenderTarget(debugMap);
				GL.Clear(false, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));
			}

			totalDisplacementMapDirty = true;
		}

		private void Camera_RenderTargetResized(WaterCamera camera)
		{
			CreateRenderTargets();
		}

		private void CreateRenderTargets()
		{
			DisposeTextures();

			int width = Mathf.RoundToInt(camera.CameraComponent.pixelWidth);
			int height = Mathf.RoundToInt(camera.CameraComponent.pixelHeight);

			dynamicDisplacementMap = CreateDynamicMap("Dynamic Water Map: Displacement", RenderTextureFormat.ARGBHalf, width >> 1, height >> 1, antialiasing);
			normalMap = CreateDynamicMap("Dynamic Water Map: Normals", RenderTextureFormat.RGHalf, width >> 1, height >> 1, antialiasing);
			foamMapA = CreateDynamicMap("Dynamic Water Map: Foam A", RenderTextureFormat.RGHalf, width, height, antialiasing);
			foamMapB = CreateDynamicMap("Dynamic Water Map: Foam B", RenderTextureFormat.RGHalf, width, height, antialiasing);
			displacementsMask = CreateDynamicMap("Dynamic Water Map: Displacements Mask", RenderTextureFormat.ARGBHalf, width >> 1, height >> 1, antialiasing);
			totalDisplacementMap = CreateDynamicMap("Dynamic Water Map: Total Displacement", RenderTextureFormat.ARGBHalf, 256, 256, 1);

			Graphics.SetRenderTarget(foamMapA);
			GL.Clear(false, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));
			Graphics.SetRenderTarget(null);
		}

		private static RenderTexture CreateDynamicMap(string name, RenderTextureFormat format, int width, int height, int antialiasing)
		{
			var rt = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
			{
				hideFlags = HideFlags.DontSave,
				antiAliasing = antialiasing,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				name = name
			};

			Graphics.SetRenderTarget(rt);
			GL.Clear(false, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));

			return rt;
		}

		public void SwapFoamMaps()
		{
			var t = foamMapB;
			foamMapB = foamMapA;
			foamMapA = t;
		}
	}
}
