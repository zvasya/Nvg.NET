using System.Numerics;

using NvgNET.Common;
using NvgNET.Core.Paths;

namespace NvgNET.Core.Instructions
{
    internal struct LineToInstruction
    {
        private readonly Vector2 _position;
        private readonly PathCache _pathCache;

        public LineToInstruction(Vector2 position, PathCache pathCache)
        {
            _position = position;
            _pathCache = pathCache;
        }

        public static void BuildPaths(LineToInstruction lineToInstruction)
        {
	        lineToInstruction._pathCache.LastPath.AddPoint(lineToInstruction._position, PointFlags.Corner);
        }
    }
}
