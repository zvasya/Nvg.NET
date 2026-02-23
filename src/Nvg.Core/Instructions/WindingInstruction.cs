using NvgNET.Core.Paths;
using NvgNET.Paths;

namespace NvgNET.Core.Instructions
{
    internal struct WindingInstruction
    {
        private readonly Winding _winding;
        private readonly PathCache _pathCache;

        public WindingInstruction(Winding winding, PathCache pathCache)
        {
            _winding = winding;
            _pathCache = pathCache;
        }

        public static void BuildPaths(WindingInstruction windingInstruction)
        {
	        windingInstruction._pathCache.LastPath.Winding = windingInstruction._winding;
        }
    }
}
