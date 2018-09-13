using System.Collections.Generic;

namespace PlayWay.Water
{
	public class WaterGlobals
	{
		private static WaterGlobals instance;

		private readonly List<Water> waters;
		private readonly List<Water> boundlessWaters;
		private readonly List<Water> dynamicWaters;
		
		private WaterGlobals()
		{
			waters = new List<Water>();
			boundlessWaters = new List<Water>();
			dynamicWaters = new List<Water>();
		}

		/// <summary>
		/// Retrieves instance of WaterGlobals.
		/// </summary>
		public static WaterGlobals Instance
		{
			get { return instance ?? (instance = new WaterGlobals()); }
		}

		/// <summary>
		/// Enabled waters in the current scene.
		/// </summary>
		public List<Water> Waters
		{
			get { return waters; }
		}

		/// <summary>
		/// Enabled boundless waters in the current scene.
		/// </summary>
		public List<Water> BoundlessWaters
		{
			get { return boundlessWaters; }
		}

		/// <summary>
		/// Waters that have DynamicWater component.
		/// </summary>
		public List<Water> DynamicWaters
		{
			get { return dynamicWaters; }
		}

		public void AddWater(Water water)
		{
			if(!waters.Contains(water))
				waters.Add(water);

			if((water.Volume == null || water.Volume.Boundless) && !boundlessWaters.Contains(water))
				boundlessWaters.Add(water);
		}

		public void RemoveWater(Water water)
		{
			waters.Remove(water);
			boundlessWaters.Remove(water);
		}

		public void AddDynamicWater(DynamicWater dynamicWater)
		{
			dynamicWaters.Add(dynamicWater.Water);
		}

		public void RemoveDynamicWater(DynamicWater dynamicWater)
		{
			dynamicWaters.Remove(dynamicWater.Water);
		}
	}
}
