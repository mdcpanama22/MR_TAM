using System.Collections.Generic;
using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	///     Alternative for RenderTexture.GetTemporary with UAV textures support and no allocations.
	/// </summary>
	public class RenderTexturesCache
	{
		private static readonly Dictionary<ulong, RenderTexturesCache> cache = new Dictionary<ulong, RenderTexturesCache>(UInt64EqualityComparer.Default);

		private readonly Queue<RenderTexture> renderTextures;
		private int lastFrameAllUsed;

		private readonly ulong hash;
		private readonly int width, height, depthBuffer;
		private readonly RenderTextureFormat format;
		private readonly bool linear, uav, mipMaps;

		public RenderTexturesCache(ulong hash, int width, int height, int depthBuffer, RenderTextureFormat format, bool linear, bool uav, bool mipMaps)
		{
			this.hash = hash;
			this.width = width;
			this.height = height;
			this.depthBuffer = depthBuffer;
			this.format = format;
			this.linear = linear;
			this.uav = uav;
			this.mipMaps = mipMaps;
			this.renderTextures = new Queue<RenderTexture>();
		}

		public static RenderTexturesCache GetCache(int width, int height, int depthBuffer, RenderTextureFormat format, bool linear, bool uav, bool mipMaps = false)
		{
			RenderTexturesUpdater.EnsureInstance();

			ulong hash = 0;

			hash |= (uint)width;
			hash |= ((uint)height << 16);
			hash |= ((ulong)depthBuffer << 29);        // >> 3 << 32
			hash |= ((linear ? 1UL : 0UL) << 34);
			hash |= ((uav ? 1UL : 0UL) << 35);
			hash |= ((mipMaps ? 1UL : 0UL) << 36);
			hash |= ((ulong)format << 37);
			
			RenderTexturesCache renderTexturesCache;

			if(!cache.TryGetValue(hash, out renderTexturesCache))
				cache[hash] = renderTexturesCache = new RenderTexturesCache(hash, width, height, depthBuffer, format, linear, uav, mipMaps);

			return renderTexturesCache;
		}

		public static TemporaryRenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format, bool linear, bool uav, bool mipMaps = false)
		{
			return GetCache(width, height, depthBuffer, format, linear, uav, mipMaps).GetTemporary();
		}

		public TemporaryRenderTexture GetTemporary()
		{
			return new TemporaryRenderTexture(this);
		}

		public RenderTexture GetTemporaryDirect()
		{
			RenderTexture renderTexture;

			if(renderTextures.Count == 0)
			{
				renderTexture = new RenderTexture(width, height, depthBuffer, format, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
				renderTexture.hideFlags = HideFlags.DontSave;
				renderTexture.name = "Temporary#" + hash;
				renderTexture.filterMode = FilterMode.Point;
				renderTexture.anisoLevel = 1;
				renderTexture.wrapMode = TextureWrapMode.Repeat;
				renderTexture.useMipMap = mipMaps;
#if UNITY_5_0 || UNITY_5_1 || UNITY_5_2 || UNITY_5_3 || UNITY_5_4
				renderTexture.generateMips = mipMaps;
#else
				renderTexture.autoGenerateMips = mipMaps;
#endif

				if(uav)
					renderTexture.enableRandomWrite = true;
			}
			else
				renderTexture = renderTextures.Dequeue();

			if(uav && !renderTexture.IsCreated())
				renderTexture.Create();

			if(renderTextures.Count == 0)
				lastFrameAllUsed = Time.frameCount;

			return renderTexture;
		}

		public void ReleaseTemporaryDirect(RenderTexture renderTexture)
		{
			renderTextures.Enqueue(renderTexture);
		}

		internal void Update(int frame)
		{
			if(frame - lastFrameAllUsed > 3 && renderTextures.Count != 0)
			{
				var renderTexture = renderTextures.Dequeue();
				renderTexture.Destroy();
			}
		}

		[ExecuteInEditMode]
		public class RenderTexturesUpdater : MonoBehaviour
		{
			private static RenderTexturesUpdater instance;

			public static void EnsureInstance()
			{
				if(instance == null)
				{
					var go = new GameObject("Water.RenderTexturesCache");
					go.hideFlags = HideFlags.HideAndDontSave;

					if(Application.isPlaying)
						DontDestroyOnLoad(go);

					instance = go.AddComponent<RenderTexturesUpdater>();
				}
			}

			void Update()
			{
				int frame = Time.frameCount;

				var enumerator = cache.GetEnumerator();
				while(enumerator.MoveNext())
					enumerator.Current.Value.Update(frame);
			}
		}
	}
}