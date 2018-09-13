using UnityEngine;

namespace PlayWay.Water
{
	public class GlobalWaterInteraction : MonoBehaviour, IWaterInteraction
	{
		[HideInInspector] [SerializeField] private Shader maskDisplayShader;

		[SerializeField]
		private Texture2D intensityMask;

		[SerializeField]
		private Vector2 worldUnitsOffset;

		[SerializeField]
		private Vector2 worldUnitsSize;

		private MeshRenderer interactionMaskRenderer;
		private Material interactionMaskMaterial;
		
		private void Awake()
		{
			OnValidate();
			CreateMaskRenderer();

			gameObject.layer = WaterProjectSettings.Instance.WaterTempLayer;
		}

		private void OnEnable()
		{
			DynamicWater.RegisterInteraction(this);
		}

		private void OnDisable()
		{
			DynamicWater.UnregisterInteraction(this);
		}

		public Vector2 WorldUnitsOffset
		{
			get { return worldUnitsOffset; }
			set { worldUnitsOffset = value; }
		}

		public Vector2 WorldUnitsSize
		{
			get { return worldUnitsSize; }
			set { worldUnitsSize = value; }
		}

		private void OnValidate()
		{
			if(maskDisplayShader == null)
				maskDisplayShader = Shader.Find("PlayWay Water/Utility/ShorelineMaskRenderSimple");
		}

		private void CreateMaskRenderer()
		{
			var mf = gameObject.AddComponent<MeshFilter>();
			mf.sharedMesh = PlayWay.Water.Internal.Quads.BipolarXZ;

			interactionMaskMaterial = new Material(maskDisplayShader) {hideFlags = HideFlags.DontSave};
			interactionMaskMaterial.SetTexture("_MainTex", intensityMask);

			interactionMaskRenderer = gameObject.AddComponent<MeshRenderer>();
			interactionMaskRenderer.sharedMaterial = interactionMaskMaterial;
			interactionMaskRenderer.enabled = false;
			
			transform.localRotation = Quaternion.identity;
		}
		
		public void OnInteractionPreRender(Camera camera, float waterVerticalOffset, int layerMask)
		{
			if(((1 << gameObject.layer) & layerMask) != 0)
			{
				Vector3 pos = camera.transform.position;
				pos.y = waterVerticalOffset;
				interactionMaskRenderer.transform.position = pos;
				interactionMaskRenderer.transform.localScale = new Vector3(camera.farClipPlane, camera.farClipPlane, camera.farClipPlane);
				interactionMaskRenderer.enabled = true;

				interactionMaskMaterial.SetVector("_OffsetScale", new Vector4(0.5f + worldUnitsOffset.x / worldUnitsSize.x, 0.5f + worldUnitsOffset.y / worldUnitsSize.y, 1.0f / worldUnitsSize.x, 1.0f / worldUnitsSize.y));
			}
		}

		public void OnInteractionPostRender(DynamicWaterCameraData overlays)
		{
			interactionMaskRenderer.enabled = false;
		}

		public void RenderInteractionDirect()
		{
			interactionMaskRenderer.sharedMaterial.SetPass(0);
			Graphics.DrawMeshNow(interactionMaskRenderer.GetComponent<MeshFilter>().sharedMesh, interactionMaskRenderer.transform.localToWorldMatrix, 0);
		}
	}
}
