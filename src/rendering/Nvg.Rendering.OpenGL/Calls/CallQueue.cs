using System.Collections.Generic;

namespace NvgNET.Rendering.OpenGL.Calls
{
    internal class CallQueue
    {

        private readonly Queue<Call> _calls = new Queue<Call>();

        public bool HasCalls => _calls.Count > 0;

        public void Add(Call call)
        {
            _calls.Enqueue(call);
        }

        public void Run()
        {
            foreach (Call call in _calls)
            {
                call.Blend();
                call.Run();
            }
        }

        public void Clear()
        {
            _calls.Clear();
        }

    }
}
