using UnityEditor;
using PlayWay.Water;
using UnityEngine;

namespace PlayWay.WaterEditor
{
	[CustomEditor(typeof(WaterCamera), true)]
	public class WaterCameraEditor : WaterEditorBase
	{
		public override void OnInspectorGUI()
		{
			var waterCamera = (WaterCamera)target;
			var camera = waterCamera.GetComponent<Camera>();
			
			var renderModeProp = PropertyField("renderMode", "Render Mode");
			var renderMode = (WaterRenderMode)renderModeProp.enumValueIndex;

			if(waterCamera.RenderMode == WaterRenderMode.DefaultQueue)
				EditorGUILayout.HelpBox("This render mode doesn't support opaque image effects like SSAO, SSR, global fog and atmospheric scattering, but it is lightweight and fast.\n\nIf you use Unity's deferred render mode, don't disable Blend Edges and/or Refraction on Water objects.", MessageType.Info);
			
			PropertyField("geometryType", "Water Geometry");

			if(renderMode != WaterRenderMode.ImageEffectDeferred)
				PropertyField("renderWaterDepth", "Render Water Depth");

			PropertyField("renderVolumes", "Render Volumes");
			PropertyField("singlePassStereoRendering", "Single Pass Stereo Rendering");
			PropertyField("effectsLight", "Effects Light");

			if(renderMode != WaterRenderMode.ImageEffectDeferred)
				PropertyField("baseEffectsQuality", "Base Effects Quality");
			else
				PropertyField("mainWater", "Main Water");

			PropertyField("submersionStateChanged", "Submersion State Changed");

			if(camera.farClipPlane < 100.0f)
				EditorGUILayout.HelpBox("Your camera farClipPlane is set below 100 units. It may be too low for the underwater effects to \"see\" the max depth and they may produce some artifacts.", MessageType.Warning, true);

			if(Application.isPlaying && StaticWaterInteraction.staticWaterInteractions.Count != 0)
			{
				Ray ray = waterCamera.GetComponent<Camera>().ScreenPointToRay(Input.mousePosition);

				if(ray.direction.y < 0.0f)
				{
					float distance = -ray.origin.y / ray.direction.y;
					Vector3 position = ray.origin + ray.direction * distance;

					EditorGUILayout.LabelField("Shore Depth at Cursor", StaticWaterInteraction.GetTotalDepthAt(position.x, position.z).ToString());
				}
			}

			serializedObject.ApplyModifiedProperties();
		}

		/*private void DisplayTexturesInspector()
		{
			var waterCamera = (WaterCamera)target;
		}

		private List<WaterMap> GetWaterMaps()
		{
			var camera = (WaterCamera)target;
			var textures = new List<WaterMap>();

			textures.Add(new WaterMap("WaterCamera - SubtractiveMask", () => camera.SubtractiveMask));

			return textures;
		}*/
	}

	public enum WaterRenderModeExtended
	{
		Default,
		Forward,
		DeferredPre,
		DeferredPost
	}
}