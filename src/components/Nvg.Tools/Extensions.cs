using System.Drawing;
using System.Numerics;

namespace NvgNET
{
	public static class Extensions
	{
		public static Vector2 ToVector2(this PointF pointF) => new Vector2(pointF.X, pointF.Y);
    
		public static Vector2 ToVector2(this SizeF pointF) => new Vector2(pointF.Width, pointF.Height);
	}
}