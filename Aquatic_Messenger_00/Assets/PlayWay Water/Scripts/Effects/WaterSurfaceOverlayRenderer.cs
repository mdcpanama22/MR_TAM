using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class WaterSurfaceOverlayRenderer : MonoBehaviour
{
	[SerializeField] private Material displacementNormalMaterial;
	[SerializeField] private Material foamMaterial;

	private Renderer rendererComponent;

	public static List<WaterSurfaceOverlayRenderer> list = new List<WaterSurfaceOverlayRenderer>();

	private void Awake()
	{
		rendererComponent = GetComponent<Renderer>();
	}

	private void OnEnable()
	{
		list.Add(this);
	}

	private void OnDisable()
	{
		list.Remove(this);
	}

	public Material DisplacementNormalMaterial
	{
		get { return displacementNormalMaterial; }
	}

	public Material FoamMaterial
	{
		get { return foamMaterial; }
	}

	public Renderer Renderer
	{
		get { return rendererComponent; }
	}
}
