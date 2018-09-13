using UnityEngine;
using UnityEditor;
using UnityEditor.AnimatedValues;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayWay.Water
{
	[CustomEditor(typeof(Water))]
	public class WaterEditor : WaterEditorBase
	{
		private readonly AnimBool environmentFoldout = new AnimBool(true);
		private readonly AnimBool surfaceFoldout = new AnimBool(false);
		private readonly AnimBool planarReflectionsFoldout = new AnimBool(false);
		private readonly AnimBool geometryFoldout = new AnimBool(false);
		private readonly AnimBool subsurfaceScatteringFoldout = new AnimBool(false);
		private readonly AnimBool inspectFoldout = new AnimBool(false);
		
		private GUIContent[] waterShaderSets;
		private string[] waterShaderSetsPaths;
		private int currentShaderSetIndex;

		private GUIContent[] waterProfiles;
		private string[] waterProfilesPaths;
		private int currentWaterProfileIndex;

		private int selectedMapIndex = -1;
		private bool askedForWaterCamera;
		
		public override void OnInspectorGUI()
		{
			var water = (Water)target;

			if(!askedForWaterCamera && WaterProjectSettings.Instance.AskForWaterCameras && Event.current.type == EventType.Layout && !PlayerSettings.virtualRealitySupported)
				LookForWaterCamera();

			UpdateGUI();

			GUILayout.Space(4);

			DrawShaderSetField();
			DrawProfileField();
			DrawNotifications();

			if (water.ShaderSet == null)
			{
				EditorGUILayout.HelpBox("Please select a shader set for this water instance.", MessageType.Info);
				return;
			}

			if(BeginGroup("Environment", environmentFoldout))
			{
				//PropertyField("receiveShadows");
				//PropertyField("shadowCastingMode");

				//var reflectionProbesProp = PropertyField("useCubemapReflections", "Reflection Probes");
				if(water.ShaderSet.ReflectionProbeUsage != ReflectionProbeUsage.Off)
					SubPropertyField("waterRenderer", "reflectionProbeAnchor", "Reflection Probes Anchor");

				PropertyField("seed");
				SubPropertyField("volume", "boundless", "Boundless");
			}

			EndGroup();

			if(BeginGroup("Geometry", geometryFoldout))
			{
				SubPropertyField("geometry", "type", "Type");

				SubPropertyField("geometry", "baseVertexCount", "Vertices");
				SubPropertyField("geometry", "tesselatedBaseVertexCount", "Vertices (Tesselation)");
				SubSubPropertyField("geometry", "customSurfaceMeshes", "customMeshes", "Custom Meshes");

				if(water.Geometry.GeometryType == WaterGeometry.Type.ProjectionGrid && !water.ShaderSet.ProjectionGrid)
					EditorGUILayout.HelpBox("You have chosen a projection grid geometry, byt selected water shader doesn't support it. Change the shader, or enable projection grid support on it.", MessageType.Error);

				if (water.Geometry.GeometryType == WaterGeometry.Type.CustomMeshes && water.Geometry.CustomSurfaceMeshes.Meshes.Length != 0 && water.Geometry.CustomSurfaceMeshes.Triangular &&
				    !water.ShaderSet.CustomTriangularGeometry)
					EditorGUILayout.HelpBox("You have chosen a custom triangular geometry, but selected water shader doesn't support it. Change the shader, or enable custom triangular geometry support on it.", MessageType.Error);
			}

			EndGroup();

			if(BeginGroup("Shading", surfaceFoldout))
			{
				//PropertyField("autoDepthColor", "Auto Depth Color");

				//PropertyField("blendEdges");
				//PropertyField("refraction", "Refraction");
				SubPropertyField("materials", "tesselationFactor", "Tesselation Factor");
			}

			EndGroup();

			GUI.enabled = water.ShaderSet.PlanarReflections != PlanarReflectionsMode.Disabled;

			if (BeginGroup(GUI.enabled ? "Planar Reflections" : "Planar Reflections (Disabled)", planarReflectionsFoldout))
			{
				SubPropertyField("planarReflectionData", "ReflectionMask", "Reflection Mask");
				SubPropertyField("planarReflectionData", "ReflectSkybox", "Reflect Skybox");
				SubPropertyField("planarReflectionData", "RenderShadows", "Render Shadows");
				SubPropertyField("planarReflectionData", "Resolution", "Resolution");
				SubPropertyField("planarReflectionData", "RetinaResolution", "Retina Resolution");
			}

			EndGroup();

			GUI.enabled = true;

			if(BeginGroup("Subsurface Scattering", subsurfaceScatteringFoldout))
			{
				var mode = SubPropertyField("subsurfaceScattering", "mode", "Mode");

				if((WaterSubsurfaceScattering.SubsurfaceScatteringMode)mode.enumValueIndex == WaterSubsurfaceScattering.SubsurfaceScatteringMode.TextureSpace)
				{
					SubPropertyField("subsurfaceScattering", "ambientResolution", "Resolution");
					//SubPropertyField("subsurfaceScattering", "ignoredLightFraction", "Ignored Light Fraction");
					DrawLightLayerField();
					SubPropertyField("subsurfaceScattering", "lightCount", "Light Count");
				}
			}

			EndGroup();

			GUI.enabled = water.ShaderSet.LocalEffectsSupported;

			if(BeginGroup("Dynamic Effects", subsurfaceScatteringFoldout))
			{
				var renderCustomEffectsProp = SubPropertyField("dynamicWaterData", "RenderCustomEffects", "Render Custom Effects");

				if(renderCustomEffectsProp.boolValue)
				{
					SubPropertyField("dynamicWaterData", "CustomEffectsLayerMask", "Custom Effects Layers");
				}
			}

			EndGroup();

			GUI.enabled = water.ShaderSet.WindWavesMode != WindWavesRenderMode.Disabled;

			if (BeginGroup(GUI.enabled ? "Wind Waves" : "Wind Waves (Disabled)", inspectFoldout))
			{
				DrawWindWavesInspector(serializedObject.FindProperty("windWavesData"));
			}

			EndGroup();

			if(BeginGroup("Inspect", inspectFoldout))
			{
				var maps = GetWaterMaps();
				selectedMapIndex = EditorGUILayout.Popup("Texture", selectedMapIndex, maps.Select(m => m.Name).ToArray());

				if(selectedMapIndex >= 0 && selectedMapIndex < maps.Count)
				{
					var texture = maps[selectedMapIndex].Getter();
					DisplayTextureInspector(texture);
				}
			}

			EndGroup();

			GUILayout.Space(10);
			DrawFeatureSelector();
			GUILayout.Space(10);

			serializedObject.ApplyModifiedProperties();
		}

		private void LookForWaterCamera()
		{
			askedForWaterCamera = true;

			foreach(var camera in Camera.allCameras)
			{
				if(WaterCamera.GetWaterCamera(camera) != null)
					return;
			}

			if(Camera.main == null)
				return;

			switch(EditorUtility.DisplayDialogComplex("PlayWay Water - Missing water camera", "Your scene doesn't contain any cameras with WaterCamera component, but only such cameras may actually see the water. Would you like to add this component to camera named \"" + Camera.main.name + "\"? ", "Ok", "Cancel", "Don't ask again"))
			{
				case 0:
				{
					Camera.main.gameObject.AddComponent<WaterCamera>();
					break;
				}
				
				case 2:
				{
					WaterProjectSettings.Instance.AskForWaterCameras = false;
					break;
				}
			}
		}

		private void DrawShaderSetField()
		{
			var shaderSetProp = serializedObject.FindProperty("shaderSet");

			if (shaderSetProp.objectReferenceValue == null)
			{
				shaderSetProp.objectReferenceValue = WaterPackageUtilities.FindDefaultAsset<ShaderSet>("\"Ocean\" t:ShaderSet", "t:ShaderSet");
				serializedObject.ApplyModifiedProperties();
			}

			if (waterShaderSets == null && Event.current.type == EventType.Layout)
			{
				FindAssets(
					"ShaderSet", shaderSetProp.objectReferenceValue,
					out currentShaderSetIndex, out waterShaderSets, out waterShaderSetsPaths);
			}

			EditorGUILayout.BeginHorizontal();
			int newShaderSetIndex = EditorGUILayout.Popup(new GUIContent("Shader Set"), currentShaderSetIndex, waterShaderSets, EditorStyles.popup);
			
			if (currentShaderSetIndex != newShaderSetIndex)
			{
				currentShaderSetIndex = newShaderSetIndex;
				shaderSetProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<ShaderSet>(waterShaderSetsPaths[currentShaderSetIndex]);
			}

			if(GUILayout.Button("Edit", EditorStyles.miniButton, GUILayout.Width(50)))
				Selection.activeObject = shaderSetProp.objectReferenceValue;

			EditorGUILayout.EndHorizontal();
		}

		private void DrawProfileField()
		{
			var profileField = serializedObject.FindProperty("profilesManager").FindPropertyRelative("initialProfile");

			if (profileField.objectReferenceValue == null)
				profileField.objectReferenceValue = WaterPackageUtilities.FindDefaultAsset<WaterProfile>("\"Sea - 6. Strong Breeze\" t:WaterProfile", "t:WaterProfile");

			if(waterProfiles == null)
			{
				FindAssets(
					"WaterProfile", profileField.objectReferenceValue,
					out currentWaterProfileIndex, out waterProfiles, out waterProfilesPaths);
			}

			EditorGUILayout.BeginHorizontal();
			int newShaderSetIndex = EditorGUILayout.Popup(new GUIContent("Profile"), currentWaterProfileIndex, waterProfiles, EditorStyles.popup);
			
			if(currentWaterProfileIndex != newShaderSetIndex)
			{
				currentWaterProfileIndex = newShaderSetIndex;
				profileField.objectReferenceValue = AssetDatabase.LoadAssetAtPath<WaterProfile>(waterProfilesPaths[currentWaterProfileIndex]);
			}

			if(GUILayout.Button("Edit", EditorStyles.miniButton, GUILayout.Width(50)))
				Selection.activeObject = profileField.objectReferenceValue;

			EditorGUILayout.EndHorizontal();
		}

		private void DrawNotifications()
		{
			if(!Application.isPlaying)
			{
				var versionProp = serializedObject.FindProperty("version");

				if(versionProp.floatValue != WaterProjectSettings.CurrentVersion)
				{
					GUILayout.BeginVertical();
					{
						GUILayout.Space(10);

						EditorGUILayout.HelpBox("This water object was created on version " + versionProp.floatValue.ToString("0.0") + ". Would you like to perform common update tasks? If everything looks as expected, you may dismiss this message.", MessageType.Error);

						GUILayout.BeginHorizontal();
						{
							GUILayout.FlexibleSpace();

							if(GUILayout.Button("Dismiss", EditorStyles.miniButtonRight, GUILayout.Width(100)))
							{
								versionProp.floatValue = WaterProjectSettings.CurrentVersion;
							}

							if(GUILayout.Button("Update to " + WaterProjectSettings.CurrentVersionString, EditorStyles.miniButtonRight, GUILayout.Width(120)))
							{
								EditorApplication.update += UpdateWater;
							}

							GUILayout.EndHorizontal();
						}

						GUILayout.Space(10);

						GUILayout.EndVertical();
					}
				}
			}
		}
		
		private void DrawFeatureSelector()
		{
			EditorGUILayout.BeginHorizontal();
			{
				GUILayout.FlexibleSpace();

				if(GUILayout.Button("Add feature...", GUILayout.Width(120)))
				{
					var menu = new GenericMenu();
					
					AddMenuItem(menu, "Network Water", typeof(NetworkWater));
					AddMenuItem(menu, "Waves Particle System", typeof(WaveParticleSystem));

					menu.ShowAsContext();
				}

				GUILayout.FlexibleSpace();

				EditorGUILayout.EndHorizontal();
			}
		}

		private void DrawMissingAssetFilesGUI()
		{
			var water = (Water)target;

			if(WaterProjectSettings.Instance.AssetFilesCreation == WaterProjectSettings.WaterAssetFilesCreation.Automatic)
			{
				if(Event.current.type != EventType.Layout)
					return;
				
				#if UNITY_5_2 || UNITY_5_1 || UNITY_5_0
					string scenePath = EditorApplication.currentScene;
				#else
					string scenePath = water.gameObject.scene.path;
				#endif

				int dotIndex = scenePath.LastIndexOf('.');

				if(!scenePath.EndsWith(".unity") || dotIndex == -1)
					return;

				string assetFilePath = scenePath.Substring(0, dotIndex) + " Water.asset";
                SaveWaterAssetFileTo(assetFilePath);

				PropertyField("shaderCollection");

				return;
            }

			EditorGUILayout.HelpBox("Each scene with water needs one unique asset file somewhere in your project. This file will contain shaders and baked data.", MessageType.Warning, true);

			EditorGUILayout.BeginHorizontal();
			{
				GUILayout.FlexibleSpace();

				if(GUILayout.Button("Save Asset..."))
				{
					string path = EditorUtility.SaveFilePanelInProject("Save Water Assets...", water.name, "asset", "");

					if(!string.IsNullOrEmpty(path))
						SaveWaterAssetFileTo(path);
				}

				EditorGUILayout.EndHorizontal();
			}
		}

		private void DrawLightLayerField()
		{
			var lightLayerProp = serializedObject.FindProperty("subsurfaceScattering").FindPropertyRelative("lightingLayer");
			lightLayerProp.intValue = EditorGUILayout.LayerField("Light Layer", lightLayerProp.intValue);
        }

		private void SaveWaterAssetFileTo(string path)
		{
			var shaderCollection = CreateInstance<ShaderSet>();
			AssetDatabase.CreateAsset(shaderCollection, path);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			serializedObject.FindProperty("shaderCollection").objectReferenceValue = shaderCollection;
			serializedObject.FindProperty("sceneHash").intValue = GetSceneHash();
		}

		private void AddMenuItem(GenericMenu menu, string label, System.Type type)
		{
			var water = (Water)target;

			if(water.GetComponent(type) == null)
			{
				menu.AddItem(new GUIContent(label), false, OnAddComponent, type);
			}
		}

		private void OnAddComponent(object componentTypeObj)
		{
			var water = (Water)target;
			water.gameObject.AddComponent((System.Type)componentTypeObj);
		}
		
		private static void FindAssets(string typeName, Object currentAsset, out int currentIndex, out GUIContent[] labels, out string[] assetPaths)
		{
			var guids = AssetDatabase.FindAssets("t:" + typeName);
			assetPaths = guids.Select<string, string>(AssetDatabase.GUIDToAssetPath).ToArray();
			labels =
				assetPaths.Select(path => new GUIContent(Path.GetFileNameWithoutExtension(path)))
					.ToArray();

			currentIndex = currentAsset != null
				? System.Array.IndexOf(assetPaths, AssetDatabase.GetAssetPath(currentAsset))
				: -1;
		}

		private List<WaterMap> GetWaterMaps()
		{
			var water = (Water)target;
			var textures = new List<WaterMap>();

			var windWaves = water.WindWaves;

			if(windWaves != null)
			{
				textures.Add(new WaterMap("WindWaves - Raw Omnidirectional Spectrum", () => windWaves.SpectrumResolver.GetSpectrum(SpectrumResolver.SpectrumType.RawOmnidirectional)));
				textures.Add(new WaterMap("WindWaves - Raw Directional Spectrum", () => windWaves.SpectrumResolver.GetSpectrum(SpectrumResolver.SpectrumType.RawDirectional)));
				textures.Add(new WaterMap("WindWaves - Height Spectrum", () => windWaves.SpectrumResolver.GetSpectrum(SpectrumResolver.SpectrumType.Height)));
				textures.Add(new WaterMap("WindWaves - Normal Spectrum", () => windWaves.SpectrumResolver.GetSpectrum(SpectrumResolver.SpectrumType.Normal)));
				textures.Add(new WaterMap("WindWaves - Horizontal Displacement Spectrum", () => windWaves.SpectrumResolver.GetSpectrum(SpectrumResolver.SpectrumType.Displacement)));

				var wavesFFT = windWaves.WaterWavesFFT;
				textures.Add(new WaterMap("WindWaves - Displacement Map 0", () => wavesFFT != null ? wavesFFT.GetDisplacementMap(0) : null));
				textures.Add(new WaterMap("WindWaves - Displacement Map 1", () => wavesFFT != null ? wavesFFT.GetDisplacementMap(1) : null));
				textures.Add(new WaterMap("WindWaves - Displacement Map 2", () => wavesFFT != null ? wavesFFT.GetDisplacementMap(2) : null));
				textures.Add(new WaterMap("WindWaves - Displacement Map 3", () => wavesFFT != null ? wavesFFT.GetDisplacementMap(3) : null));
				textures.Add(new WaterMap("WindWaves - Normal Map 0", () => wavesFFT != null ? wavesFFT.GetNormalMap(0) : null));
				textures.Add(new WaterMap("WindWaves - Normal Map 1", () => wavesFFT != null ? wavesFFT.GetNormalMap(1) : null));

				var foam = water.Foam;
				textures.Add(new WaterMap("WaterFoam - Foam Map", () => foam != null ? foam.FoamMap : null));
			}

			return textures;
		}

		private void SearchShaderVariantCollection()
		{
			var editedWater = (Water)target;
			var transforms = FindObjectsOfType<Transform>();

			foreach(var root in transforms)
			{
				if(root.parent == null)     // if that's really a root
				{
					var waters = root.GetComponentsInChildren<Water>(true);

					foreach(var water in waters)
					{
						if(water != editedWater && water.ShaderSet != null)
						{
							serializedObject.FindProperty("shaderCollection").objectReferenceValue = water.ShaderSet;
							serializedObject.FindProperty("sceneHash").intValue = GetSceneHash();
							serializedObject.ApplyModifiedProperties();
							return;
						}
					}
				}
			}
		}

		private int GetSceneHash()
		{
			var md5 = System.Security.Cryptography.MD5.Create();

#if UNITY_5_2 || UNITY_5_1 || UNITY_5_0
			string sceneName = EditorApplication.currentScene + "#" + target.name;
#else
			string sceneName = ((Water)target).gameObject.scene.name;
#endif

			if(!string.IsNullOrEmpty(sceneName))
			{
				var hash = md5.ComputeHash(System.Text.Encoding.ASCII.GetBytes(sceneName));
				return System.BitConverter.ToInt32(hash, 0);
			}

			return -1;
		}

		/// <summary>
		/// Performs common update tasks.
		/// </summary>
		private void UpdateWater()
		{
			EditorApplication.update -= UpdateWater;
			
			var versionProp = serializedObject.FindProperty("version");
			
			if (versionProp.floatValue < 2.0f)
				UpdateTo20();

			versionProp.floatValue = WaterProjectSettings.CurrentVersion;
			serializedObject.ApplyModifiedProperties();

			Debug.Log("Update was successful.");
		}

		/// <summary>
		/// Updates water to 2.0
		/// </summary>
		private void UpdateTo20()
		{
			var water = (Water)target;
			var shaderSet = water.ShaderSet;

#pragma warning disable 618
			var planarReflections = water.GetComponent<WaterPlanarReflectionDeprecated>();
			var windWaves = water.GetComponent<WindWavesDeprecated>();

			var serializedWindWaves = windWaves != null ? new SerializedObject(windWaves) : null;
			var dynamicSmoothnessProp = serializedWindWaves != null ? serializedWindWaves.FindProperty("dynamicSmoothness") : null;

			var dynamicWater = water.GetComponent<DynamicWaterDeprecated>();
			var wavesParticleSystem = water.GetComponent<WaveParticleSystem>();

			var serializedWavesParticleSystem = wavesParticleSystem != null ? new SerializedObject(wavesParticleSystem) : null;

			var foam = water.GetComponent<WaterFoamDeprecated>();
			var spray = water.GetComponent<Spray>();

			if(shaderSet != null)
			{
				Undo.RecordObject(shaderSet, "Water update to " + WaterProjectSettings.CurrentVersionString);

				var serializedShaderCollection = new SerializedObject(shaderSet);
				serializedShaderCollection.FindProperty("transparencyMode").intValue = (int)(serializedObject.FindProperty("refraction").boolValue || serializedObject.FindProperty("blendEdges").boolValue ? WaterTransparencyMode.Refractive : WaterTransparencyMode.Solid);
				serializedShaderCollection.FindProperty("receiveShadows").boolValue = serializedObject.FindProperty("receiveShadows").boolValue;
				serializedShaderCollection.FindProperty("reflectionProbeUsage").intValue = (int)(serializedObject.FindProperty("useCubemapReflections").boolValue ? ReflectionProbeUsage.BlendProbesAndSkybox : ReflectionProbeUsage.Off);
				serializedShaderCollection.FindProperty("planarReflections").boolValue = planarReflections != null;
				serializedShaderCollection.FindProperty("dynamicSmoothnessMode").intValue = (int)(dynamicSmoothnessProp == null || dynamicSmoothnessProp.FindPropertyRelative("enabled").boolValue ? DynamicSmoothnessMode.Physical : DynamicSmoothnessMode.CheapApproximation);
				serializedShaderCollection.FindProperty("localEffectsSupported").boolValue = dynamicWater != null;
				serializedShaderCollection.FindProperty("localEffectsDebug").boolValue = serializedWavesParticleSystem != null && serializedWavesParticleSystem.FindProperty("debugMode").boolValue;
				serializedShaderCollection.FindProperty("foam").boolValue = foam != null;
				serializedShaderCollection.FindProperty("supportProjectionGridGeometry").boolValue = water.Geometry.GeometryType == WaterGeometry.Type.ProjectionGrid;
				serializedShaderCollection.FindProperty("supportTriangularGeometry").boolValue = water.Geometry.Triangular;
				serializedShaderCollection.FindProperty("windWavesMode").intValue = serializedWindWaves != null ? serializedWindWaves.FindProperty("renderMode").intValue : (int)WindWavesRenderMode.Disabled;
				serializedShaderCollection.ApplyModifiedProperties();
			}

			if (serializedWindWaves != null)
			{
				var windWavesData = serializedObject.FindProperty("windWavesData");
				windWavesData.FindPropertyRelative("HighPrecision").boolValue = serializedWindWaves.FindProperty("highPrecision").boolValue;
				windWavesData.FindPropertyRelative("Resolution").intValue = serializedWindWaves.FindProperty("resolution").intValue;
				windWavesData.FindPropertyRelative("CpuFFTPrecisionBoost").intValue = serializedWindWaves.FindProperty("cpuFFTPrecisionBoost").intValue;
				windWavesData.FindPropertyRelative("CopyFrom").objectReferenceValue = serializedWindWaves.FindProperty("copyFrom").objectReferenceValue;
			}
			
			serializedObject.FindProperty("materials").FindPropertyRelative("tesselationFactor").floatValue = serializedObject.FindProperty("tesselationFactor").floatValue;
			serializedObject.ApplyModifiedProperties();

			if (planarReflections != null)
				DestroyImmediate(planarReflections);

			if (windWaves != null)
				DestroyImmediate(windWaves);

			if (dynamicWater != null)
				DestroyImmediate(dynamicWater);

			if (foam != null)
				DestroyImmediate(foam);

			if (spray != null)
				DestroyImmediate(spray);
#pragma warning restore 618
		}
		
		private readonly AnimBool fftFoldout = new AnimBool(false);
		private readonly AnimBool gerstnerFoldout = new AnimBool(false);

		private static readonly GUIContent[] resolutionLabels = { new GUIContent("4x32x32 (runs on potatos)"), new GUIContent("4x64x64"), new GUIContent("4x128x128"), new GUIContent("4x256x256 (very high; most PCs)"), new GUIContent("4x512x512 (extreme; gaming PCs)"), new GUIContent("4x1024x1024 (as seen in Titanic® and Water World®; future PCs)") };
		private static readonly int[] resolutions = { 32, 64, 128, 256, 512, 1024, 2048, 4096 };

		public void DrawWindWavesInspector(SerializedProperty windWavesData)
		{
			bool looping = windWavesData.FindPropertyRelative("LoopDuration").floatValue > 0.0f;

			//if(BeginGroup("Rendering", null))
			{
				var copyFromProp = windWavesData.FindPropertyRelative("CopyFrom");

				GUI.enabled = copyFromProp.objectReferenceValue == null;

				DrawResolutionGUI(windWavesData);
				PropertyField(windWavesData, "CpuDesiredStandardError", "Desired Standard Error (CPU)");
				PropertyField(windWavesData, "HighPrecision");

				PropertyField(windWavesData, "WindDirectionPointer");
				PropertyField(windWavesData, "LoopDuration");
				GUI.enabled = true;

				//SubPropertyField("dynamicSmoothness", "enabled", "Dynamic Smoothness");
				PropertyField(windWavesData, "CopyFrom");
			}

			//EndGroup();

			UseFoldouts = true;

			if(BeginGroup("FFT", fftFoldout, 14))
			{
				SubPropertyField(windWavesData, "WavesRendererFFTData", "HighQualityNormalMaps", "High Quality Normal Maps");
				SubPropertyField(windWavesData, "WavesRendererFFTData", "ForcePixelShader", "Force Pixel Shader");
				SubPropertyField(windWavesData, "WavesRendererFFTData", "FlattenMode", "Flatten Mode");

				if(looping)
					SubPropertyField(windWavesData, "WavesRendererFFTData", "CachedFrameCount", "Cached Frame Count");
			}

			EndGroup();

			if(BeginGroup("Gerstner", gerstnerFoldout, 14))
			{
				SubPropertyField(windWavesData, "WavesRendererGerstnerData", "NumGerstners", "Waves Count");
			}

			EndGroup();

			UseFoldouts = false;

			if (GUI.changed)
			{
				serializedObject.ApplyModifiedProperties();
				((Water)target).OnValidate();
			}

			serializedObject.Update();
		}

		private static void DrawResolutionGUI(SerializedProperty windWaves)
		{
			var property = windWaves.FindPropertyRelative("Resolution");
			DrawResolutionGUI(property, null);
		}

		public static void DrawResolutionGUI(SerializedProperty property, string name)
		{
			const string tooltip = "Higher values increase quality, but also decrease performance. Directly controls quality of waves, foam and spray.";

			int newResolution = IndexToResolution(EditorGUILayout.Popup(new GUIContent(name ?? property.displayName, tooltip), ResolutionToIndex(property.intValue), resolutionLabels));

			if(newResolution != property.intValue)
				property.intValue = newResolution;
		}

		private static int ResolutionToIndex(int resolution)
		{
			switch(resolution)
			{
				case 32: return 0;
				case 64: return 1;
				case 128: return 2;
				case 256: return 3;
				case 512: return 4;
				case 1024: return 5;
				case 2048: return 6;
				case 4096: return 7;
			}

			return 0;
		}

		static int IndexToResolution(int index)
		{
			return resolutions[index];
		}
	}
}
