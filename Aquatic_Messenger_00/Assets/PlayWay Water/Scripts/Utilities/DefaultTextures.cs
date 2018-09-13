using UnityEngine;

namespace PlayWay.Water.Internal
{
	/// <summary>
	/// An utility class that creates and caches some default textures used for water rendering.
	/// </summary>
	public static class DefaultTextures
	{
		private static Texture2D blackTexture;
		private static Texture2D whiteTexture;
		private static bool texturesCreated;

		public static Texture2D BlackTexture
		{
			get
			{
				if(!texturesCreated)
					CreateTextures();

				return blackTexture;
			}
		}

		public static Texture2D WhiteTexture
		{
			get
			{
				if(!texturesCreated)
					CreateTextures();

				return whiteTexture;
			}
		}

		private static void CreateTextures()
		{
			var color = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			blackTexture = new Texture2D(2, 2, TextureFormat.ARGB32, false) { name = "Black Texture", hideFlags = HideFlags.DontSave };
			blackTexture.SetPixel(0, 0, color);
			blackTexture.SetPixel(1, 0, color);
			blackTexture.SetPixel(0, 1, color);
			blackTexture.SetPixel(1, 1, color);
			blackTexture.Apply(false, true);

			color = new Color(1.0f, 1.0f, 1.0f, 1.0f);
			whiteTexture = new Texture2D(2, 2, TextureFormat.ARGB32, false) { name = "White Texture", hideFlags = HideFlags.DontSave };
			whiteTexture.SetPixel(0, 0, color);
			whiteTexture.SetPixel(1, 0, color);
			whiteTexture.SetPixel(0, 1, color);
			whiteTexture.SetPixel(1, 1, color);
			whiteTexture.Apply(false, true);

			texturesCreated = true;
		}
	}
}
