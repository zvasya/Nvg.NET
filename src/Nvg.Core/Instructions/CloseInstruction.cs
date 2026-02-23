using NvgNET.Core.Paths;

namespace NvgNET.Core.Instructions
{
    internal struct CloseInstruction
    {
        private readonly PathCache _pathCache;

        public CloseInstruction(PathCache pathCache)
        {
            _pathCache = pathCache;
        }

        public static void BuildPaths(CloseInstruction closeInstruction)
        {
	        closeInstruction._pathCache.LastPath.Close();
        }
    }
}
