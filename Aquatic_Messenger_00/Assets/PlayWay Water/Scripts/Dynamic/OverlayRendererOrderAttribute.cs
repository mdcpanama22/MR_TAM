using System;

namespace PlayWay.Water
{
	public class OverlayRendererOrderAttribute : Attribute
	{
		private readonly int priority;

		public OverlayRendererOrderAttribute(int priority)
		{
			this.priority = priority;
		}

		public int Priority
		{
			get { return priority; }
		}
	}
}
