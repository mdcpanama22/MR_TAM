using UnityEngine;

namespace PlayWay.Water
{
	public class WindWavesSpectrumOverlay
	{
		private Vector2[][] spectrumData;
		private Texture2D texture;
		private bool textureDirty = true;
		
		public event System.Action Cleared;

		private readonly WindWaves windWaves;

		public WindWavesSpectrumOverlay(WindWaves windWaves)
		{
			this.windWaves = windWaves;

			spectrumData = new Vector2[4][];

			for (int tileIndex = 0; tileIndex < 4; ++tileIndex)
				spectrumData[tileIndex] = new Vector2[windWaves.FinalResolution * windWaves.FinalResolution];
		}

		public void Destroy()
		{
			spectrumData = null;
			Cleared = null;

			if(texture != null)
			{
				Object.Destroy(texture);
				texture = null;
			}
		}

		public Texture2D Texture
		{
			get
			{
				if (textureDirty)
					ValidateTexture();

				return texture;
			}
		}

		public Vector2[] GetSpectrumDataDirect(int tileIndex)
		{
			return spectrumData[tileIndex];
		}

		public void Refresh()
		{
			int finalResolution = windWaves.FinalResolution;
			int finalResolutionSqr = finalResolution*finalResolution;

			for (int tileIndex = 0; tileIndex < 4; ++tileIndex)
			{
				var data = spectrumData[tileIndex];

				if (data.Length == finalResolutionSqr)
				{
					for (int i = 0; i < data.Length; ++i)
						data[i] = new Vector2(0.0f, 0.0f);
				}
				else
					spectrumData[tileIndex] = new Vector2[finalResolutionSqr];
			}

			textureDirty = true;

			if (Cleared != null)
				Cleared();
		}

		private void ValidateTexture()
		{
			textureDirty = false;

			int finalResolution = windWaves.FinalResolution;
			int finalResolutionx2 = finalResolution << 1;

			if (texture != null && texture.width != finalResolutionx2)
			{
				Object.Destroy(texture);
				texture = null;
			}

			if(texture == null)
			{
				texture = new Texture2D(finalResolutionx2, finalResolutionx2, TextureFormat.RGHalf, false, true)
				{
					filterMode = FilterMode.Point
				};
			}

			for(int tileIndex = 0; tileIndex < 4; ++tileIndex)
			{
				var data = spectrumData[tileIndex];

				int xOffset = tileIndex == 1 || tileIndex == 3 ? finalResolution : 0;
				int yOffset = tileIndex == 2 || tileIndex == 3 ? finalResolution : 0;

				for(int x = finalResolution - 1; x >= 0; --x)
				{
					for(int y = finalResolution - 1; y >= 0; --y)
					{
						Vector2 value = data[x * finalResolution + y];
						texture.SetPixel(xOffset + x, yOffset + y, new Color(value.x, value.y, 0.0f, 0.0f));
					}
				}
			}

			texture.Apply(false, false);
		}
	}
}
