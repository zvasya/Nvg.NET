using System;
using System.Collections.Generic;
using System.Numerics;

using NvgNET.Paths;

namespace NvgNET.Core.Instructions
{
    internal sealed class InstructionQueue
    {
        private const uint INIT_INSTRUCTIONS_SIZE = 256;

        enum InstructionType : byte
        {
	        Winding,
	        Close,
	        LineTo,
	        MoveTo,
	        BezierTo,
        }
        private readonly Queue<InstructionType> _instructions = new Queue<InstructionType>((int)INIT_INSTRUCTIONS_SIZE);
        
        private readonly Queue<WindingInstruction> _windingInstructions = new Queue<WindingInstruction>((int)INIT_INSTRUCTIONS_SIZE / 4);
        private readonly Queue<CloseInstruction> _closeInstructions = new Queue<CloseInstruction>((int)INIT_INSTRUCTIONS_SIZE / 4);
        private readonly Queue<LineToInstruction> _lineToInstructions = new Queue<LineToInstruction>((int)INIT_INSTRUCTIONS_SIZE / 4);
        private readonly Queue<MoveToInstruction> _moveToInstructions = new Queue<MoveToInstruction>((int)INIT_INSTRUCTIONS_SIZE / 4);
        private readonly Queue<BezierToInstruction> _bezierToInstructions = new Queue<BezierToInstruction>((int)INIT_INSTRUCTIONS_SIZE / 4);
        
        private readonly Nvg _nvg;

        public Vector2 EndPosition { get; private set; }

        public uint Count => (uint)_instructions.Count;

        public InstructionQueue(Nvg nvg)
        {
            _nvg = nvg;
            EndPosition = default;
        }

        public void AddMoveTo(Vector2 pos)
        {
            EndPosition = pos;
            _instructions.Enqueue(InstructionType.MoveTo);
            _moveToInstructions.Enqueue(new MoveToInstruction(Vector2.Transform(pos, _nvg.stateStack.CurrentState.Transform), _nvg.pathCache));
        }

        public void AddLineTo(Vector2 pos)
        {
            EndPosition = pos;
            _instructions.Enqueue(InstructionType.LineTo);
            _lineToInstructions.Enqueue(new LineToInstruction(Vector2.Transform(pos, _nvg.stateStack.CurrentState.Transform), _nvg.pathCache));
        }

        public void AddBezierTo(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            EndPosition = p2;
            Matrix3x2 transform = _nvg.stateStack.CurrentState.Transform;
            _instructions.Enqueue(InstructionType.BezierTo);
            _bezierToInstructions.Enqueue(new BezierToInstruction(Vector2.Transform(p0, transform), Vector2.Transform(p1, transform), Vector2.Transform(p2, transform), _nvg.pixelRatio.TessTol, _nvg.pathCache));
        }

        public void AddClose()
        {
            _instructions.Enqueue(InstructionType.Close);
            _closeInstructions.Enqueue(new CloseInstruction(_nvg.pathCache));
        }

        public void AddWinding(Winding winding)
        {
            _instructions.Enqueue(InstructionType.Winding);
            _windingInstructions.Enqueue(new WindingInstruction(winding, _nvg.pathCache));
        }

        public void FlattenPaths()
        {
            if (_nvg.pathCache.Count > 0)
            {
                return;
            }

            while (_instructions.Count > 0)
            {
	            switch (_instructions.Dequeue())
	            {
		            case InstructionType.Winding:
			            WindingInstruction.BuildPaths(_windingInstructions.Dequeue());
			            break;
		            case InstructionType.Close:
						CloseInstruction.BuildPaths(_closeInstructions.Dequeue());
			            break;
		            case InstructionType.LineTo:
						LineToInstruction.BuildPaths(_lineToInstructions.Dequeue());
			            break;
		            case InstructionType.MoveTo:
						MoveToInstruction.BuildPaths(_moveToInstructions.Dequeue());
			            break;
		            case InstructionType.BezierTo:
						BezierToInstruction.BuildPaths(_bezierToInstructions.Dequeue());
			            break;
		            default:
			            throw new ArgumentOutOfRangeException();
	            }
            }
            
            _nvg.pathCache.FlattenPaths();
        }

        public void Clear()
        {
            _instructions.Clear();
            _windingInstructions.Clear();
	        _closeInstructions.Clear();
            _lineToInstructions.Clear();
	        _moveToInstructions.Clear();
            _bezierToInstructions.Clear();
        }
    }
}
