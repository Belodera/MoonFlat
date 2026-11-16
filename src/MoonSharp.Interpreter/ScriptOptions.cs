using System;
using System.IO;
using MoonSharp.Interpreter.Loaders;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// This class contains options to customize behaviour of Script objects.
	/// </summary>
	public class ScriptOptions
	{
        public bool ResearchNumericFusion { get; set; }
        public bool ResearchRegisterExpressions { get; set; }
        public bool ResearchNumericLoops { get; set; }
        public bool ResearchAffineKernel { get; set; }
        public bool ResearchSharedLoopMemo { get; set; }
        public bool ResearchLoopFixedpoint { get; set; }
        public bool ResearchExpressionMemo { get; set; }
        public bool ResearchConstructorHints { get; set; }
		/// <summary>Optional host collection contract. Null preserves legacy behavior.</summary>
		public Func<string, DynValue> CollectionPolicy { get; set; }
		internal ScriptOptions()
		{
		}

		internal ScriptOptions(ScriptOptions defaults)
		{
			this.DebugInput = defaults.DebugInput;
			this.DebugPrint = defaults.DebugPrint;

			this.UseLuaErrorLocations = defaults.UseLuaErrorLocations;
			this.Stdin = defaults.Stdin;
			this.Stdout = defaults.Stdout;
			this.Stderr = defaults.Stderr;
			this.TailCallOptimizationThreshold = defaults.TailCallOptimizationThreshold;

			this.ScriptLoader = defaults.ScriptLoader;

			this.CheckThreadAccess = defaults.CheckThreadAccess;
			this.CollectionPolicy = defaults.CollectionPolicy;
            this.ResearchNumericFusion = defaults.ResearchNumericFusion;
            this.ResearchRegisterExpressions = defaults.ResearchRegisterExpressions;
            this.ResearchNumericLoops = defaults.ResearchNumericLoops;
            this.ResearchAffineKernel = defaults.ResearchAffineKernel;
            this.ResearchSharedLoopMemo = defaults.ResearchSharedLoopMemo;
            this.ResearchLoopFixedpoint = defaults.ResearchLoopFixedpoint;
            this.ResearchExpressionMemo = defaults.ResearchExpressionMemo;
            this.ResearchConstructorHints = defaults.ResearchConstructorHints;
		}

		/// <summary>
		/// Gets or sets the current script-loader.
		/// </summary>
		public IScriptLoader ScriptLoader { get; set; }

		/// <summary>
		/// Gets or sets the debug print handler
		/// </summary>
		public Action<string> DebugPrint { get; set; }

		/// <summary>
		/// Gets or sets the debug input handler (takes a prompt as an input, for interactive interpreters, like debug.debug).
		/// </summary>
		public Func<string, string> DebugInput { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether error messages will use Lua error locations instead of MoonSharp 
		/// improved ones. Use this for compatibility with legacy Lua code which parses error messages.
		/// </summary>
		public bool UseLuaErrorLocations { get; set; }

		/// <summary>
		/// Gets or sets a value which dictates the behaviour of the colon (':') operator in callbacks to CLR code.
		/// </summary>
		public ColonOperatorBehaviour ColonOperatorClrCallbackBehaviour { get; set; }

		/// <summary>
		/// Gets or sets the stream used as stdin. If null, a default stream is used.
		/// </summary>
		public Stream Stdin { get; set; }

		/// <summary>
		/// Gets or sets the stream used as stdout. If null, a default stream is used.
		/// </summary>
		public Stream Stdout { get; set; }

		/// <summary>
		/// Gets or sets the stream used as stderr. If null, a default stream is used.
		/// </summary>
		public Stream Stderr { get; set; }

		/// <summary>
		/// Gets or sets the stack depth threshold at which MoonSharp starts doing
		/// tail call optimizations.
		/// TCOs can provide the little benefit of avoiding stack overflows in corner case
		/// scenarios, at the expense of losing debug information and error stack traces 
		/// in all other, more common scenarios. MoonSharp choice is to start performing
		/// TCOs only after a threshold of stack usage is reached. This fork clamps
		/// the inherited default during construction to at most one eighth of the
		/// smaller configured stack capacity, leaving room to reach the tail call.
		/// Set this to int.MaxValue to disable TCOs entirely, or to 0 to always have
		/// TCOs enabled.
		/// </summary>
		public int TailCallOptimizationThreshold { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the thread check is enabled.
		/// A "lazy" thread check is performed everytime execution is entered to ensure that no two threads
		/// calls MoonSharp execution concurrently. However 1) the check is performed best effort (thus, it might
		/// not detect all issues) and 2) it might trigger in very odd legal situations (like, switching threads 
		/// inside a CLR-callback without actually having concurrency.
		/// 
		/// Disable this option if the thread check is giving problems in your scenario, but please check that
		/// you are not calling MoonSharp execution concurrently as it is not supported.
		/// </summary>
		public bool CheckThreadAccess { get; set; }

	}
}
