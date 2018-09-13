using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	///     Attach this to objects supposed to mask water in screen-space. It will mask both water surface and camera's
	///     underwater image effect. Great for sections etc.
	/// </summary>
	[RequireComponent(typeof(Renderer))]
	public sealed class WaterSimpleMask : MonoBehaviour
	{
		[SerializeField]
		private Water water;
		
		private void OnEnable()
		{
			var renderer = GetComponent<Renderer>();
			renderer.enabled = false;
			renderer.material.SetFloat("_WaterId", 1 << water.WaterId);

			gameObject.layer = WaterProjectSettings.Instance.WaterTempLayer;
			
			if(renderer == null)
				throw new System.InvalidOperationException("WaterSimpleMask is attached to an object without any renderer.");
			
			water.Renderer.AddMask(renderer);
			water.WaterIdChanged += OnWaterIdChanged;
		}

		private void OnDisable()
		{
			water.WaterIdChanged -= OnWaterIdChanged;

			var renderer = GetComponent<Renderer>();
			water.Renderer.RemoveMask(renderer);
		}

		public Water Water
		{
			get { return water; }
			set
			{
				if(water == value)
					return;

				enabled = false;
				water = value;
				enabled = true;
			}
		}

		private void OnValidate()
		{
			gameObject.layer = WaterProjectSettings.Instance.WaterTempLayer;
		}

		private void OnWaterIdChanged()
		{
			var renderer = GetComponent<Renderer>();
			renderer.material.SetFloat("_WaterId", 1 << water.WaterId);
		}
	}
}
