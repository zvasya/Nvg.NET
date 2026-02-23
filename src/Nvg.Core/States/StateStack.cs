using System.Collections.Generic;

namespace NvgNET.Core.States
{
    internal sealed class StateStack
    {

        private const uint MAX_STATES = 32;

        private readonly Stack<State> _states;

        private State _currentState;
        public ref State CurrentState
        {
            get => ref _currentState;
        }

        public StateStack()
        {
            _states = new Stack<State>((int)MAX_STATES);
            Reset();
        }

        public void Save()
        {
            if (_states.Count >= MAX_STATES)
            {
                return;
            }
            else
            {
	            _states.Push(_currentState);
            }
        }

        public void Reset()
        {
	        _currentState = State.Default;
        }

        public void Restore()
        {
            if (_states.Count == 0)
            {
                return;
            }
            _currentState = _states.Pop();
        }

        public void Clear()
        {
            _states.Clear();
            Reset();
        }

    }
}
