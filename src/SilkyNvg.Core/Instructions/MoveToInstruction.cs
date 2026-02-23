using SilkyNvg.Common;
using SilkyNvg.Core.Paths;
using System.Numerics;

namespace SilkyNvg.Core.Instructions
{
    internal struct MoveToInstruction
    {
        private readonly Vector2 _position;
        private readonly PathCache _pathCache;

        public MoveToInstruction(Vector2 position, PathCache pathCache)
        {
            _position = position;
            _pathCache = pathCache;
        }

        public static void BuildPaths(MoveToInstruction  moveToInstruction)
        {
	        moveToInstruction._pathCache.AddPath();
	        moveToInstruction._pathCache.LastPath.AddPoint(moveToInstruction._position, PointFlags.Corner);
        }
    }
}
