using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace PlayWay.Water
{
	[CustomEditor(typeof(LightWaterEffects))]
	public class LightWaterEffectsEditor : WaterEditorBase
	{
		private static readonly string[] options = { "None", "Projected Texture (Recommended)", "Physical" };

		public override void OnInspectorGUI()
		{
			var light = ((LightWaterEffects)target).GetComponent<Light>();
			
			PropertyField("castShadows");

			var modeField = serializedObject.FindProperty("causticsMode");
			modeField.intValue = EditorGUILayout.Popup("Caustics Mode", modeField.intValue, options);
			
			if (modeField.intValue != 0)
			{
				PropertyField("intensity");

				if (modeField.intValue == 1) // projected texture
				{
					PropertyField("projectedTexture");
					PropertyField("scrollDirectionPointer");
					PropertyField("scrollSpeed");
					PropertyField("distortions1");
					PropertyField("distortions2");
					PropertyField("uvScale");
				}
				else // physical
				{
					PropertyField("causticReceiversMask");
					PropertyField("blur");
					PropertyField("skipTerrainTrees");
				}

				EditorGUILayout.HelpBox(
					"This component will set this light position at runtime to encode some information in it for the shader. In most cases it is nothing to worry about.",
					MessageType.Info);
				
				var deferredShader = Shader.Find("Hidden/PlayWay Water-Scene-DeferredShading");

				if (GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredShading) != BuiltinShaderMode.UseCustom ||
				    GraphicsSettings.GetCustomShader(BuiltinShaderType.DeferredShading) != deferredShader)
				{
					EditorGUILayout.HelpBox(
						"You have to use \"PlayWay Water-Scene-DeferredShading.shader\" shader for deferred rendering. You can set it manually in \"Edit/Project Settings/Graphics\" or click a button below.",
						MessageType.Error);

					GUILayout.BeginHorizontal();
					GUILayout.FlexibleSpace();

					if (GUILayout.Button("Set Shaders"))
					{
						GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredShading, deferredShader);
						GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, BuiltinShaderMode.UseCustom);
					}

					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
				}
			}

			if(light != null && light.type != LightType.Directional)
				EditorGUILayout.HelpBox("This component works only with directional lights for the time being.", MessageType.Error);

			serializedObject.ApplyModifiedProperties();
		}
	}
}
