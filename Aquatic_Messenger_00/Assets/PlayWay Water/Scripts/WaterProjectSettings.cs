using System;
using UnityEngine;

namespace PlayWay.Water
{
	public class WaterProjectSettings : ScriptableObjectSingleton
	{
		public static readonly float CurrentVersion = 2.0f;
		public static readonly string CurrentVersionString = "2.0 Beta 4";

#pragma warning disable 0414
		[SerializeField]
		private float serializedVersion = 2.0f;
#pragma warning restore 0414

		[SerializeField]
		private int waterLayer = 4;

		[Tooltip("Used for some camera effects. Has to be unused. You don't need to mask it on your cameras.")]
		[SerializeField]
		private int waterTempLayer = 22;

		[Tooltip("PlayWay Water internally uses colliders to detect camera entering into subtractive volumes etc. You will have to ignore this layer in your scripting raycasts.")]
		[SerializeField]
		private int waterCollidersLayer = 1;

		[Tooltip("Each scene with water needs one unique asset file somewhere in your project. By default these files are generated automatically, but you may choose to create them manually.")]
		[SerializeField]
		private WaterAssetFilesCreation assetFilesCreation;

		[Tooltip("More threads increase physics precision under stress, but also decrease overall performance a bit.")]
		[SerializeField]
		private int physicsThreads = 1;

		[SerializeField]
		private System.Threading.ThreadPriority physicsThreadsPriority = System.Threading.ThreadPriority.BelowNormal;

		[SerializeField]
		private bool allowCpuFFT = true;

		[Tooltip("Some hardware doesn't support floating point mip maps correctly and they are forcefully disabled. You may simulate how the water would look like on such hardware by disabling this option. Most notably fp mip maps don't work correctly on most AMD graphic cards (for now).")]
		[SerializeField]
		private bool allowFloatingPointMipMaps = true;

		[SerializeField]
		private bool debugPhysics = false;

		[SerializeField]
		private bool askForWaterCameras = true;

		[SerializeField]
		private AbsorptionEditMode absorptionEditMode = AbsorptionEditMode.Transmission;

		[SerializeField]
		private SpecularEditMode specularEditMode = SpecularEditMode.IndexOfRefraction;
		
		private static WaterProjectSettings instance;
		private static bool noInstance = true;

		public static WaterProjectSettings Instance
		{
			get
			{
				if(noInstance)			// performance
				{
					instance = LoadSingleton<WaterProjectSettings>();
					noInstance = false;
				}

				return instance;
			}
		}

		public int PhysicsThreads
		{
			get { return physicsThreads; }
			set { physicsThreads = value; }
		}

		public int WaterLayer
		{
			get { return waterLayer; }
		}

		public int WaterTempLayer
		{
			get { return waterTempLayer; }
		}

		public int WaterCollidersLayer
		{
			get { return waterCollidersLayer; }
		}

		public WaterAssetFilesCreation AssetFilesCreation
		{
			get { return assetFilesCreation; }
		}

		public System.Threading.ThreadPriority PhysicsThreadsPriority
		{
			get { return physicsThreadsPriority; }
		}

		public bool AllowCpuFFT
		{
			get { return allowCpuFFT; }
		}

		public bool AllowFloatingPointMipMaps
		{
			get {
				string vendor = SystemInfo.graphicsDeviceVendor.ToLower();
				return !vendor.Contains("amd") && !vendor.Contains("ati") && !SystemInfo.graphicsDeviceName.ToLower().Contains("radeon") && allowFloatingPointMipMaps;
			}
		}

		public bool DebugPhysics
		{
			get { return debugPhysics; }
		}

		public bool AskForWaterCameras
		{
			get { return askForWaterCameras; }
			set { askForWaterCameras = value; }
		}

		public AbsorptionEditMode InspectorAbsorptionEditMode
		{
			get { return absorptionEditMode; }
		}

		public SpecularEditMode InspectorSpecularEditMode
		{
			get { return specularEditMode; }
		}

		public enum WaterAssetFilesCreation
		{
			Automatic,
			Manual
		}

		public enum AbsorptionEditMode
		{
			Absorption,
			Transmission
		}

		public enum SpecularEditMode
		{
			IndexOfRefraction,
			CustomColor
		}
	}
}
