using System.Collections.Generic;
using UnityEngine;

namespace PlayWay.Water
{
	public class PlanarReflection
	{
		[System.Serializable]
		public class Data
		{
			public LayerMask ReflectionMask = int.MaxValue;
			public bool ReflectSkybox = true;
			public bool RenderShadows = true;

			[Range(0.0f, 1.0f)]
			public float Resolution = 0.5f;

			[Range(0.0f, 1.0f)]
			[Tooltip("Allows you to use more rational resolution of planar reflections on screens with very high dpi. Planar reflections should be blurred anyway.")]
			public float RetinaResolution = 0.333f;
		}

		private readonly Data data;
		private readonly Water water;
		private readonly bool systemSupportsHDR;
		private readonly Dictionary<Camera, TemporaryRenderTexture> temporaryTargets =
			new Dictionary<Camera, TemporaryRenderTexture>();

		private Camera reflectionCamera;
		private TemporaryRenderTexture currentTarget;
		private float finalResolutionMultiplier;
		private bool renderPlanarReflections;
		private Material utilitiesMaterial;
		private Shader utilitiesShader;

		private static int reflectionTexProperty;

		private const float ClipPlaneOffset = 0.07f;
		
		public PlanarReflection(Water water, Data data)
		{
			this.water = water;
			this.data = data;

			reflectionTexProperty = Shader.PropertyToID("_PlanarReflectionTex");
			systemSupportsHDR = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);

			Validate();

			water.ProfilesManager.Changed.AddListener(OnProfilesChanged);
			OnProfilesChanged(water);
		}

		internal void Validate()
		{
			if(utilitiesShader == null)
				utilitiesShader = Shader.Find("PlayWay Water/Utilities/PlanarReflection - Utilities");

			data.Resolution = Mathf.Clamp01(Mathf.RoundToInt(data.Resolution * 10.0f) * 0.1f);
			data.RetinaResolution = Mathf.Clamp01(Mathf.RoundToInt(data.RetinaResolution * 10.0f) * 0.1f);

			float finalResolutionMultiplier = Screen.dpi <= 220 ? data.Resolution : data.RetinaResolution;

			if(this.finalResolutionMultiplier != finalResolutionMultiplier)
			{
				this.finalResolutionMultiplier = finalResolutionMultiplier;
				ClearRenderTextures();
			}

			if(reflectionCamera != null)
				ValidateReflectionCamera();
		}

		internal void Destroy()
		{
			ClearRenderTextures();
		}

		internal void Update()
		{
			ClearRenderTextures();
		}

		public void OnWaterRender(Camera camera)
		{
			if(camera == reflectionCamera || !camera.enabled || !renderPlanarReflections)
				return;

			if(!temporaryTargets.TryGetValue(camera, out currentTarget))
			{
				RenderReflection(camera);
				UpdateRenderProperties();
			}
		}

		public void OnWaterPostRender(Camera camera)
		{
			TemporaryRenderTexture renderTexture;

			if(temporaryTargets.TryGetValue(camera, out renderTexture))
			{
				temporaryTargets.Remove(camera);
				renderTexture.Dispose();
			}
		}

		private void RenderReflection(Camera camera)
		{
			if(reflectionCamera == null && !FindReflectionCamera())
				CreateReflectionCamera();

			// ReSharper disable once PossibleNullReferenceException
			//reflectionCamera.fieldOfView = camera.fieldOfView;
#if UNITY_5_6
			reflectionCamera.allowHDR = systemSupportsHDR && camera.allowHDR;
#else
			reflectionCamera.hdr = systemSupportsHDR && camera.hdr;
#endif
			reflectionCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);

			currentTarget = GetRenderTexture(camera.pixelWidth, camera.pixelHeight);
			temporaryTargets[camera] = currentTarget;

			var target = RenderTexturesCache.GetTemporary(currentTarget.Texture.width, currentTarget.Texture.height, 16, currentTarget.Texture.format, true, false);
			reflectionCamera.targetTexture = target;
			reflectionCamera.aspect = camera.aspect;

			Vector3 cameraEuler = camera.transform.eulerAngles;
			reflectionCamera.transform.eulerAngles = new Vector3(-cameraEuler.x, cameraEuler.y, cameraEuler.z);
			reflectionCamera.transform.position = camera.transform.position;

			Vector3 cameraPosition = camera.transform.position;
			cameraPosition.y = water.transform.position.y - cameraPosition.y;
			reflectionCamera.transform.position = cameraPosition;

			float d = -water.transform.position.y - ClipPlaneOffset;
			Vector4 reflectionPlane = new Vector4(0, 1, 0, d);

			Matrix4x4 reflection = Matrix4x4.zero;
			reflection = CalculateReflectionMatrix(reflection, reflectionPlane);
			Vector3 newpos = reflection.MultiplyPoint(camera.transform.position);

			reflectionCamera.worldToCameraMatrix = camera.worldToCameraMatrix * reflection;

			Vector4 clipPlane = CameraSpacePlane(reflectionCamera, water.transform.position, new Vector3(0, 1, 0), 1.0f);

			var matrix = camera.projectionMatrix;
			matrix = CalculateObliqueMatrix(matrix, clipPlane);
			reflectionCamera.projectionMatrix = matrix;

			reflectionCamera.transform.position = newpos;
			Vector3 cameraEulerB = camera.transform.eulerAngles;
			reflectionCamera.transform.eulerAngles = new Vector3(-cameraEulerB.x, cameraEulerB.y, cameraEulerB.z);

			reflectionCamera.clearFlags = data.ReflectSkybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;

			if (data.RenderShadows)
			{
				GL.invertCulling = true;
				reflectionCamera.Render();
				GL.invertCulling = false;
			}
			else
			{
				var originalShadowQuality = QualitySettings.shadows;
				QualitySettings.shadows = ShadowQuality.Disable;

				GL.invertCulling = true;
				reflectionCamera.Render();
				GL.invertCulling = false;

				QualitySettings.shadows = originalShadowQuality;
			}

			reflectionCamera.targetTexture = null;

			if(utilitiesMaterial == null)
				utilitiesMaterial = new Material(utilitiesShader) { hideFlags = HideFlags.DontSave };

			Graphics.Blit(target, currentTarget, utilitiesMaterial, 0);
			target.Dispose();
		}

		private void UpdateRenderProperties()
		{
			var block = water.Renderer.PropertyBlock;
			block.SetTexture(reflectionTexProperty, currentTarget);
			block.SetMatrix("_PlanarReflectionProj",
				(Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0.0f), Quaternion.identity, new Vector3(0.5f, 0.5f, 1.0f)) *
				 reflectionCamera.projectionMatrix * reflectionCamera.worldToCameraMatrix));
			block.SetFloat("_PlanarReflectionMipBias", -Mathf.Log(1.0f / finalResolutionMultiplier, 2));
		}

		private void CreateReflectionCamera()
		{
			var reflectionCameraGo = new GameObject(water.name + " Reflection Camera");
			reflectionCameraGo.transform.parent = water.transform;
			reflectionCamera = reflectionCameraGo.AddComponent<Camera>();
			reflectionCamera.renderingPath = RenderingPath.Forward;

			ValidateReflectionCamera();
		}

		private bool FindReflectionCamera()
		{
			for(int i = water.transform.childCount - 1; i >= 0; --i)
			{
				var child = water.transform.GetChild(i);

				if(child.name.Contains(" Reflection Camera"))
				{
					reflectionCamera = child.GetComponent<Camera>();
					return true;
				}
			}

			return false;
		}

		private void ValidateReflectionCamera()
		{
			reflectionCamera.enabled = false;
			reflectionCamera.cullingMask = data.ReflectionMask;
			reflectionCamera.depthTextureMode = DepthTextureMode.None;
		}

		private static Matrix4x4 CalculateReflectionMatrix(Matrix4x4 reflectionMat, Vector4 plane)
		{
			reflectionMat.m00 = (1.0f - 2.0f * plane.x * plane.x);
			reflectionMat.m01 = (-2.0f * plane.x * plane.y);
			reflectionMat.m02 = (-2.0f * plane.x * plane.z);
			reflectionMat.m03 = (-2.0f * plane.w * plane.x);

			reflectionMat.m10 = (-2.0f * plane.y * plane.x);
			reflectionMat.m11 = (1.0f - 2.0f * plane.y * plane.y);
			reflectionMat.m12 = (-2.0f * plane.y * plane.z);
			reflectionMat.m13 = (-2.0f * plane.w * plane.y);

			reflectionMat.m20 = (-2.0f * plane.z * plane.x);
			reflectionMat.m21 = (-2.0f * plane.z * plane.y);
			reflectionMat.m22 = (1.0f - 2.0f * plane.z * plane.z);
			reflectionMat.m23 = (-2.0f * plane.w * plane.z);

			reflectionMat.m30 = 0.0f;
			reflectionMat.m31 = 0.0f;
			reflectionMat.m32 = 0.0f;
			reflectionMat.m33 = 1.0f;

			return reflectionMat;
		}

		private static Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
		{
			Vector3 offsetPos = pos + normal * ClipPlaneOffset;
			Matrix4x4 m = cam.worldToCameraMatrix;
			Vector3 cpos = m.MultiplyPoint(offsetPos);
			Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;

			return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
		}

		private static Matrix4x4 CalculateObliqueMatrix(Matrix4x4 projection, Vector4 clipPlane)
		{
			Vector4 q = projection.inverse * new Vector4(Mathf.Sign(clipPlane.x), Mathf.Sign(clipPlane.y), 1.0f, 1.0f);

			Vector4 c = clipPlane * (2.0f / (Vector4.Dot(clipPlane, q)));
			projection[2] = c.x - projection[3];
			projection[6] = c.y - projection[7];
			projection[10] = c.z - projection[11];
			projection[14] = c.w - projection[15];

#if UNITY_5_4_0
			if(UnityEngine.VR.VRSettings.enabled)
			{
				// it seems that there is some bug in Unity 5.4.0 code that does something weird with projection matrix when any of these components is set to non-zero
				projection[2] = 0.0f;
				projection[6] = 0.0f;
			}
#endif

			return projection;
		}

		private TemporaryRenderTexture GetRenderTexture(int width, int height)
		{
			int adaptedWidth = Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(width * finalResolutionMultiplier));
			int adaptedHeight = Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(height * finalResolutionMultiplier));
#if UNITY_5_6
			bool hdr = reflectionCamera.allowHDR;
#else
			bool hdr = reflectionCamera.hdr;
#endif

			var renderTexture = RenderTexturesCache.GetTemporary(adaptedWidth, adaptedHeight, 0, hdr && systemSupportsHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, true, false, true);
			renderTexture.Texture.filterMode = FilterMode.Trilinear;
			renderTexture.Texture.wrapMode = TextureWrapMode.Clamp;

			return renderTexture;
		}

		private void ClearRenderTextures()
		{
			var enumerator = temporaryTargets.GetEnumerator();
			while(enumerator.MoveNext())
				RenderTexture.ReleaseTemporary(enumerator.Current.Value);

			temporaryTargets.Clear();
		}

		private void OnProfilesChanged(Water water)
		{
			var profiles = water.ProfilesManager.Profiles;

			if(profiles == null)
				return;

			float intensity = 0.0f;

			for(int i=profiles.Length-1; i>=0; --i)
			{
				var weightedProfile = profiles[i];

				var profile = weightedProfile.Profile;
				float weight = weightedProfile.Weight;

				intensity += profile.PlanarReflectionIntensity * weight;
			}

			renderPlanarReflections = intensity > 0.0f;
		}
	}
}
