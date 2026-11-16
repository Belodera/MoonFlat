using System;

namespace MoonSharp.Interpreter.Execution.VM
{
	// This part is practically written procedural style - it looks more like C than C#.
	// This is intentional so to avoid this-calls and virtual-calls as much as possible.
	// Same reason for the "sealed" declaration.
	sealed partial class Processor
	{
		public DynValue Coroutine_Create(Closure closure)
		{
			// create a processor instance
			Processor P = new Processor(this);

			// Put the closure as first value on the stack, for future reference
			P.m_ValueStack.Push(DynValue.NewClosure(closure));

			// Return the coroutine handle
			return DynValue.NewCoroutine(new Coroutine(P));
		}

		public CoroutineState State { get { return m_State; } }
		public Coroutine AssociatedCoroutine { get; set; }

		internal void Coroutine_Close()
		{
			if (m_State == CoroutineState.Running || m_State == CoroutineState.Main)
				throw new ScriptRuntimeException("cannot close a running coroutine");
			m_IdleClrFrame = null;
			m_ValueStack.ReleaseStorage();
			m_ExecutionStack.ReleaseStorage();
			m_SavedInstructionPtr = -1;
			m_State = CoroutineState.Dead;
            m_CoroutineBoundary = null;
		}

		public DynValue Coroutine_Resume(DynValue[] args)
		{
			// Reject invalid resumes before entering or owning the coroutine boundary.
            // A self/ancestor resume must not mark the live coroutine dead or clear its stacks.
            if (m_State != CoroutineState.NotStarted && m_State != CoroutineState.Suspended && m_State != CoroutineState.ForceSuspended)
					throw ScriptRuntimeException.CannotResumeNotSuspended(m_State);
            if (m_State == CoroutineState.ForceSuspended && args != null && args.Length > 0)
                throw new ArgumentException("When resuming a force-suspended coroutine, args must be empty.");
            EnterProcessor();
            var previousBoundary = m_NativeBoundary;
            NativeBoundary activeBoundary = null;

			try
			{
				int entrypoint = 0;
                if (m_CoroutineBoundary != null)
                    activeBoundary = m_NativeBoundary = m_CoroutineBoundary;

				

				if (m_State == CoroutineState.NotStarted)
				{
					m_CoroutineBoundary = new NativeBoundary(m_ExecutionStack.Count, m_ValueStack.Count - 1);
                    activeBoundary = m_NativeBoundary = m_CoroutineBoundary;
                    entrypoint = PushClrToScriptStackFrame(CallStackItemFlags.ResumeEntryPoint, null, args);
				}
				else if (m_State == CoroutineState.Suspended)
				{
					m_ValueStack.Push(DynValue.NewTuple(args));
					entrypoint = m_SavedInstructionPtr;
				}
				else if (m_State == CoroutineState.ForceSuspended)
				{
					if (args != null && args.Length > 0)
						throw new ArgumentException("When resuming a force-suspended coroutine, args must be empty.");

					entrypoint = m_SavedInstructionPtr;
				}

				m_State = CoroutineState.Running;
				DynValue retVal = Processing_Loop(entrypoint);

				if (retVal.Type == DataType.YieldRequest)
				{
					if (retVal.YieldRequest.Forced)
					{
						m_State = CoroutineState.ForceSuspended;
						return retVal;
					}
					else
					{
						m_State = CoroutineState.Suspended;
						return DynValue.NewTuple(retVal.YieldRequest.ReturnValues);
					}
				}
				else
				{
					m_State = CoroutineState.Dead;
					return retVal;
				}
			}
			catch (Exception)
			{
				// Unhandled exception - move to dead
				m_State = CoroutineState.Dead;
				throw;
			}
			finally
			{
				if (m_State == CoroutineState.Dead) {
                    CleanNativeBoundary(activeBoundary);
                    m_CoroutineBoundary = null;
                }
                m_NativeBoundary = previousBoundary;
                LeaveProcessor();
			}
		}



	}

}
