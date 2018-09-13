using UnityEditor;
using UnityEngine;

namespace PlayWay.Water
{
	[CanEditMultipleObjects]
	[CustomEditor(typeof(WaterProfile))]
	public class WaterProfileEditor : WaterEditorBase
	{
		private Texture2D illustrationTex;

		private GUIStyle warningLabel;
		private GUIStyle normalMapLabel;
		private bool initialized;

		private static GradientContainer gradientContainer;
		private static SerializedObject serializedGradientContainer;

		protected override void UpdateStyles()
		{
			base.UpdateStyles();
			
			if(!initialized)
			{
				Undo.undoRedoPerformed -= OnUndoRedoPerformed;
				Undo.undoRedoPerformed += OnUndoRedoPerformed;
				initialized = true;
			}

			if(warningLabel == null)
			{
				warningLabel = new GUIStyle(GUI.skin.label)
				{
					wordWrap = true,
					normal = {textColor = new Color32(255, 201, 2, 255)}
				};
			}

			if(illustrationTex == null)
			{
				string texPath = WaterPackageUtilities.WaterPackagePath + "/Textures/Editor/Illustration.png";
				illustrationTex = (Texture2D)AssetDatabase.LoadMainAssetAtPath(texPath);
			}

			if(normalMapLabel == null)
			{
				normalMapLabel = new GUIStyle(GUI.skin.label)
				{
					stretchHeight = true,
					fontStyle = FontStyle.Bold,
					alignment = TextAnchor.MiddleLeft
				};
			}
		}

		public override bool RequiresConstantRepaint()
		{
			return true;
		}

		public override void OnInspectorGUI()
		{
			UpdateGUI();

			var profile = (WaterProfile)target;

			GUI.enabled = !Application.isPlaying;
			PropertyField("spectrumType");

			DrawWindSpeedGUI();

			PropertyField("tileSize");
			PropertyField("tileScale");
			PropertyField("wavesAmplitude");
			PropertyField("wavesFrequencyScale");
			GUI.enabled = true;

			PropertyField("horizontalDisplacementScale");

			if(profile.SpectrumType == WaterProfile.WaterSpectrumType.Phillips)
				PropertyField("phillipsCutoffFactor", "Cutoff Factor");

			PropertyField("directionality");
			PropertyField("fetch");

			GUILayout.Space(12.0f);

			GUILayout.Label("Colors", EditorStyles.boldLabel);

			PropertyField("diffuseColor", "Diffuse");
			PropertyField("reflectionColor", "Reflection");

			var serializedSettings = new SerializedObject(WaterProjectSettings.Instance);

			var absorptionEditModeProp = serializedSettings.FindProperty("absorptionEditMode");
			EditorGUILayout.PropertyField(absorptionEditModeProp);
			var absorptionEditMode = (WaterProjectSettings.AbsorptionEditMode)absorptionEditModeProp.enumValueIndex;

			EditorGUILayout.BeginHorizontal();
			{
				GUILayout.Space(20.0f);

				EditorGUILayout.BeginVertical();
				{
					if(absorptionEditMode == WaterProjectSettings.AbsorptionEditMode.Absorption)
					{
						DrawAbsorptionColorField("absorptionColor", "Absorption", absorptionEditMode);
						var customUnderwaterAbsorptionField = PropertyField("customUnderwaterAbsorptionColor", "Custom Underwater Absorption");
						if(customUnderwaterAbsorptionField.boolValue)
							DrawAbsorptionGradientField("absorptionColorByDepth", "Absorption (Underwater IME)", absorptionEditMode);
					}
					else
					{
						DrawAbsorptionColorField("absorptionColor", "Transmission", absorptionEditMode);
						var customUnderwaterAbsorptionField = PropertyField("customUnderwaterAbsorptionColor", "Custom Underwater Transmission");
						if(customUnderwaterAbsorptionField.boolValue)
							DrawAbsorptionGradientField("absorptionColorByDepth", "Transmission (Underwater IME)", absorptionEditMode);
					}

					if(GUI.changed)
						UpdateFlatAbsorptionGradient();

					EditorGUILayout.EndVertical();
				}

				EditorGUILayout.EndHorizontal();
			}

			var specularEditModeProp = serializedSettings.FindProperty("specularEditMode");
			EditorGUILayout.PropertyField(specularEditModeProp);
			var specularEditMode = (WaterProjectSettings.SpecularEditMode)specularEditModeProp.enumValueIndex;

			serializedSettings.ApplyModifiedProperties();

			EditorGUILayout.BeginHorizontal();
			{
				GUILayout.Space(20.0f);

				EditorGUILayout.BeginVertical();
				{
					if(specularEditMode == WaterProjectSettings.SpecularEditMode.IndexOfRefraction)
					{
						float ior = BiasToIOR((profile.SpecularColor.r + profile.SpecularColor.g + profile.SpecularColor.b) * 0.333333f);
						float newIOR = EditorGUILayout.Slider(new GUIContent("Specular (Index of refraction)", "Water index of refraction is 1.330."), ior, 1.0f, 4.05f);

						if(newIOR != ior)
						{
							float bias = IORToBias(newIOR);
							serializedObject.FindProperty("specularColor").colorValue = new Color(bias, bias, bias);
						}
					}
					else
						PropertyField("specularColor", "Specular (Custom color)");

					EditorGUILayout.EndVertical();
				}

				EditorGUILayout.EndHorizontal();
			}
			
			GUILayout.Space(8.0f);

			GUILayout.Label("Subsurface Scattering", EditorStyles.boldLabel);
			PropertyField("isotropicScatteringIntensity", "Isotropic");
			PropertyField("forwardScatteringIntensity", "Forward");
			PropertyField("subsurfaceScatteringContrast", "Contrast");
			PropertyField("subsurfaceScatteringShoreColor", "Shore Color");
			PropertyField("directionalWrapSSS", "Directional Wrap SSS");
			PropertyField("pointWrapSSS", "Point Wrap SSS");

			GUILayout.Label("Basic Properties", EditorStyles.boldLabel);

			PropertyField("smoothness");
			var customAmbientSmoothnessProp = PropertyField("customAmbientSmoothness");

			if(!customAmbientSmoothnessProp.hasMultipleDifferentValues)
			{
				if(customAmbientSmoothnessProp.boolValue)
					PropertyField("ambientSmoothness");
			}

			PropertyField("dynamicSmoothnessIntensity");
			PropertyField("refractionDistortion", "Refraction Distortion");
			PropertyField("edgeBlendFactor", "Edge Blend Factor");
			PropertyField("density");

			GUILayout.Space(8.0f);

			GUILayout.Label("Normals", EditorStyles.boldLabel);
			//PropertyField("normalsFadeDistance", "Fade Distance");
			//PropertyField("normalsFadeBias", "Fade Bias");
			PropertyField("detailFadeDistance", "Detail Fade Distance");
			PropertyField("displacementNormalsIntensity", "Normal Intensity");
			DrawNormalAnimationEditor();

			GUILayout.Space(8.0f);

			GUILayout.Label("Foam", EditorStyles.boldLabel);
			PropertyField("foamIntensity", "Intensity");
			PropertyField("foamThreshold", "Threshold");
			PropertyField("foamFadingFactor", "Fade Factor");
			PropertyField("foamShoreIntensity", "Foam Shore Intensity");
			PropertyField("foamShoreExtent", "Foam Shore Extent");
			PropertyField("foamNormalScale", "Foam Normal Scale");
			PropertyField("foamDiffuseColor", "Foam Diffuse Color");
			PropertyField("foamSpecularColor", "Foam Specular Color");

			GUILayout.Space(8.0f);

			GUILayout.Label("Planar Reflections", EditorStyles.boldLabel);
			PropertyField("planarReflectionIntensity", "Intensity");
			PropertyField("planarReflectionFlatten", "Flatten");
			PropertyField("planarReflectionVerticalOffset", "Offset");

			GUILayout.Space(8.0f);

			GUILayout.Label("Underwater", EditorStyles.boldLabel);
			PropertyField("underwaterBlurSize", "Blur Size");
			PropertyField("underwaterLightFadeScale", "Underwater Light Fade Scale");
			PropertyField("underwaterDistortionsIntensity", "Distortion Intensity");
			PropertyField("underwaterDistortionAnimationSpeed", "Distortion Animation Speed");

			GUILayout.Space(8.0f);

			GUILayout.Label("Spray", EditorStyles.boldLabel);
			PropertyField("sprayThreshold", "Threshold");
			PropertyField("spraySkipRatio", "Skip Ratio");
			PropertyField("spraySize", "Size");

			GUILayout.Space(8.0f);

			GUILayout.Label("Textures", EditorStyles.boldLabel);
			PropertyField("normalMap", "Normal Map");
			//PropertyField("heightMap", "Height Map");
			PropertyField("foamDiffuseMap", "Foam Diffuse Map");
			PropertyField("foamNormalMap", "Foam Normal Map");
			PropertyField("foamTiling", "Foam Tiling");

			serializedObject.ApplyModifiedProperties();

			if(GUI.changed)
				ValidateWaterObjects();
		}

		private static void ValidateWaterObjects()
		{
			var waters = FindObjectsOfType<Water>();

			for(int i=waters.Length-1; i>=0; --i)
			{
				var profilesManager = waters[i].ProfilesManager;
				profilesManager.SetProfiles(profilesManager.Profiles);
				profilesManager.ValidateProfiles();
			}
		}

		private void DrawAbsorptionColorField(string propertyName, string label, WaterProjectSettings.AbsorptionEditMode editMode)
		{
			var property = serializedObject.FindProperty(propertyName);
			
			switch(editMode)
			{
				case WaterProjectSettings.AbsorptionEditMode.Absorption:
				{
					PropertyField(propertyName, label);
					break;
				}

				case WaterProjectSettings.AbsorptionEditMode.Transmission:
				{
					Color transmissionColor = property.colorValue;
					transmissionColor.r = Mathf.Exp(-transmissionColor.r);
					transmissionColor.g = Mathf.Exp(-transmissionColor.g);
					transmissionColor.b = Mathf.Exp(-transmissionColor.b);

					if(property.hasMultipleDifferentValues)
						EditorGUI.showMixedValue = true;

					Color newTransmissionColor = EditorGUILayout.ColorField(new GUIContent(label), transmissionColor, false, false, true, new ColorPickerHDRConfig(0.0f, 1.0f, 0.0f, 1.0f));

					EditorGUI.showMixedValue = false;

					if(transmissionColor != newTransmissionColor)
					{
						var newAbsorptionColor = newTransmissionColor;
						newAbsorptionColor.r = -Mathf.Log(newAbsorptionColor.r);
						newAbsorptionColor.g = -Mathf.Log(newAbsorptionColor.g);
						newAbsorptionColor.b = -Mathf.Log(newAbsorptionColor.b);
						property.colorValue = newAbsorptionColor;
					}

					break;
				}
			}
		}

		private void DrawAbsorptionGradientField(string propertyName, string label, WaterProjectSettings.AbsorptionEditMode editMode)
		{
			var property = serializedObject.FindProperty(propertyName);

			switch(editMode)
			{
				case WaterProjectSettings.AbsorptionEditMode.Absorption:
				{
					PropertyField(propertyName, label);
					break;
				}

				case WaterProjectSettings.AbsorptionEditMode.Transmission:
				{
					if(property.hasMultipleDifferentValues)
						return;                 // multiple gradients editing is not supported for now

					var profile = (WaterProfile) target;
					Gradient absorptionGradient = profile.AbsorptionColorByDepth;

					if (absorptionGradient == null)
					{
						absorptionGradient = new Gradient();
						var field = profile.GetType()
							.GetField(propertyName,
								System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
						field.SetValue(profile, absorptionGradient);
					}
					
					if (gradientContainer == null)
					{
						gradientContainer = CreateInstance<GradientContainer>();
						gradientContainer.hideFlags = HideFlags.DontSave;
						gradientContainer.gradient = new Gradient();

						serializedGradientContainer = new SerializedObject(gradientContainer);
					}

					Gradient transmissionGradient = gradientContainer.gradient;
					var absorptionKeys = absorptionGradient.colorKeys;
					
					for (int i = 0; i < absorptionKeys.Length; ++i)
					{
						Color absorptionColor = absorptionKeys[i].color;
						absorptionKeys[i].color = new Color(
							Mathf.Exp(-absorptionColor.r),
							Mathf.Exp(-absorptionColor.g),
							Mathf.Exp(-absorptionColor.b),
							absorptionColor.a
						);
					}

					transmissionGradient.colorKeys = absorptionKeys;
					transmissionGradient.alphaKeys = absorptionGradient.alphaKeys;
					serializedGradientContainer.Update();
					EditorGUILayout.PropertyField(serializedGradientContainer.FindProperty("gradient"), new GUIContent(label));
					
					if (serializedGradientContainer.ApplyModifiedPropertiesWithoutUndo())
					{
						transmissionGradient = gradientContainer.gradient;
						absorptionKeys = transmissionGradient.colorKeys;

						for(int i = 0; i < absorptionKeys.Length; ++i)
						{
							Color transmissionColor = absorptionKeys[i].color;
							absorptionKeys[i].color = new Color(
								-Mathf.Log(transmissionColor.r),
								-Mathf.Log(transmissionColor.g),
								-Mathf.Log(transmissionColor.b),
								transmissionColor.a
							);
						}

						absorptionGradient.colorKeys = absorptionKeys;
						absorptionGradient.alphaKeys = transmissionGradient.alphaKeys;
						serializedObject.Update();
						EditorUtility.SetDirty(target);
					}
					
					break;
				}
			}
		}

		private void DrawWindSpeedGUI()
		{
			//var profile = (WaterProfile)target;

			var windSpeedProp = serializedObject.FindProperty("windSpeed");

			float mps = windSpeedProp.floatValue;
			float knots = MpsToKnots(mps);

			if(windSpeedProp.hasMultipleDifferentValues)
				EditorGUI.showMixedValue = true;

			float newKnots = EditorGUILayout.Slider(new GUIContent(string.Format("Wind Speed ({0})", GetWindSpeedClassification(knots)), "Wind speed in knots."), knots, 0.0f, 70.0f);

			EditorGUI.showMixedValue = false;

			if(knots != newKnots)
				windSpeedProp.floatValue = KnotsToMps(newKnots);
		}
		
		private void DrawNormalAnimationEditor()
		{
			EditorGUILayout.BeginHorizontal(GUILayout.Height(60.0f));
			{
				GUILayout.Space(10);
				GUILayout.Label("Tiles 1", normalMapLabel);

				EditorGUILayout.BeginVertical();
				{
					SubPropertyField("normalMapAnimation1", "speed", "Speed");
					SubPropertyField("normalMapAnimation1", "deviation", "Deviation");
					SubPropertyField("normalMapAnimation1", "intensity", "Intensity");
					SubPropertyField("normalMapAnimation1", "tiling", "Tiling");

					EditorGUILayout.EndVertical();
				}

				EditorGUILayout.EndHorizontal();
			}

			GUILayout.Space(6);

			EditorGUILayout.BeginHorizontal(GUILayout.Height(60.0f));
			{
				GUILayout.Space(10);
				GUILayout.Label("Tiles 2", normalMapLabel);

				EditorGUILayout.BeginVertical();
				{
					SubPropertyField("normalMapAnimation2", "speed", "Speed");
					SubPropertyField("normalMapAnimation2", "deviation", "Deviation");
					SubPropertyField("normalMapAnimation2", "intensity", "Intensity");
					SubPropertyField("normalMapAnimation2", "tiling", "Tiling");

					EditorGUILayout.EndVertical();
				}

				EditorGUILayout.EndHorizontal();
			}
		}

		private void UpdateFlatAbsorptionGradient()
		{
			var absorptionColorProp = serializedObject.FindProperty("absorptionColor");
			
			var gradientProp = serializedObject.FindProperty("absorptionColorByDepthFlatGradient");
			gradientProp.FindPropertyRelative("m_NumColorKeys").intValue = 1;
			gradientProp.FindPropertyRelative("m_NumAlphaKeys").intValue = 1;
			gradientProp.FindPropertyRelative("key0").colorValue = absorptionColorProp.colorValue;
		}

		private float MpsToKnots(float f)
		{
			return f / 0.5144f;
		}

		private float KnotsToMps(float f)
		{
			return 0.5144f * f;
		}

		private string GetWindSpeedClassification(float f)
		{
			if(f < 1.0f)
				return "Calm";
			else if(f < 3.0f)
				return "Light Air";
			else if(f < 6.0f)
				return "Light Breeze";
			else if(f < 10.0f)
				return "Gentle Breeze";
			else if(f < 16.0f)
				return "Moderate Breeze";
			else if(f < 21.0f)
				return "Fresh Breeze";
			else if(f < 27.0f)
				return "Strong Breeze";
			else if(f < 33.0f)
				return "Near Gale";
			else if(f < 40.0f)
				return "Gale";
			else if(f < 47.0f)
				return "Strong Gale";
			else if(f < 55.0f)
				return "Storm";
			else if(f < 63.0f)
				return "Violent Storm";
			else
				return "Hurricane";
		}

		private void OnUndoRedoPerformed()
		{
			serializedObject.Update();
			ValidateWaterObjects();
			Repaint();
		}

		private float IORToBias(float ior)
		{
			float a = (1.0f - ior);
			float b = (1.0f + ior);
			return (a * a) / (b * b);
		}

		private float BiasToIOR(float bias)
		{
			return (Mathf.Sqrt(bias) + 1) / (1 - Mathf.Sqrt(bias));
        }
	}
}
