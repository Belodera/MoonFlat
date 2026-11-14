// Disable warnings about XML documentation
#pragma warning disable 1591


namespace MoonSharp.Interpreter.CoreLib
{
	/// <summary>
	/// Class implementing table Lua iterators (pairs, ipairs, next)
	/// </summary>
	[MoonSharpModule]
	public class TableIteratorsModule
	{
		// Luau guarantees the contiguous array prefix first, followed by all
		// remaining keys. Walk in place without sorting/copying the table.
		internal static DynValue CreateDefaultTableIterator(Table table)
		{
			int prefix = 0;
			bool inArray = true;
			return DynValue.NewCallback((execution, args) =>
			{
				DynValue control = args[1];
				if (inArray)
				{
					DynValue value = table.RawGet(prefix + 1);
					if (value != null && !value.IsNil())
						return DynValue.NewTuple(DynValue.NewNumber(++prefix), value);
					inArray = false;
					control = DynValue.Nil;
				}
				TablePair? pair = table.NextKey(control);
				while (pair.HasValue && !pair.Value.Key.IsNil())
				{
					DynValue key = pair.Value.Key;
					if (key.Type != DataType.Number || key.Number < 1 || key.Number > prefix || key.Number != System.Math.Floor(key.Number))
						return DynValue.NewTuple(key, pair.Value.Value);
					pair = table.NextKey(key);
				}
				return DynValue.Nil;
			});
		}

		// ipairs (t)
		// -------------------------------------------------------------------------------------------------------------------
		// If t has a metamethod __ipairs, calls it with t as argument and returns the first three results from the call.
		// Otherwise, returns three values: an iterator function, the table t, and 0, so that the construction
		//	  for i,v in ipairs(t) do body end
		// will iterate over the pairs (1,t[1]), (2,t[2]), ..., up to the first integer key absent from the table. 
		[MoonSharpModuleMethod]
		public static DynValue ipairs(ScriptExecutionContext executionContext, CallbackArguments args) 
		{
			DynValue table = args[0];

			DynValue meta = executionContext.GetMetamethodTailCall(table, "__ipairs", args.GetArray());

			return meta ?? DynValue.NewTuple(DynValue.NewCallback(__next_i), table, DynValue.NewNumber(0));
		}

		// pairs (t)
		// -------------------------------------------------------------------------------------------------------------------
		// If t has a metamethod __pairs, calls it with t as argument and returns the first three results from the call.
		// Otherwise, returns three values: the next function, the table t, and nil, so that the construction
		//     for k,v in pairs(t) do body end
		// will iterate over all key–value pairs of table t.
		// See function next for the caveats of modifying the table during its traversal. 
		[MoonSharpModuleMethod]
		public static DynValue pairs(ScriptExecutionContext executionContext, CallbackArguments args) 
		{
			DynValue table = args[0];

			DynValue meta = executionContext.GetMetamethodTailCall(table, "__pairs", args.GetArray());

			return meta ?? DynValue.NewTuple(DynValue.NewCallback(next), table);
		}

		// next (table [, index])
		// -------------------------------------------------------------------------------------------------------------------
		// Allows a program to traverse all fields of a table. Its first argument is a table and its second argument is an 
		// index in this table. next returns the next index of the table and its associated value. 
		// When called with nil as its second argument, next returns an initial index and its associated value. 
		// When called with the last index, or with nil in an empty table, next returns nil. If the second argument is absent, 
		// then it is interpreted as nil. In particular, you can use next(t) to check whether a table is empty.
		// The order in which the indices are enumerated is not specified, even for numeric indices. 
		// (To traverse a table in numeric order, use a numerical for.)
		// The behavior of next is undefined if, during the traversal, you assign any value to a non-existent field in the table. 
		// You may however modify existing fields. In particular, you may clear existing fields. 
		[MoonSharpModuleMethod]
		public static DynValue next(ScriptExecutionContext executionContext, CallbackArguments args) 
		{
			DynValue table = args.AsType(0, "next", DataType.Table);
			DynValue index = args[1];

			TablePair? pair = table.Table.NextKey(index);

			if (pair.HasValue)
				return DynValue.NewTuple(pair.Value.Key, pair.Value.Value);
			else
				throw new ScriptRuntimeException("invalid key to 'next'");
		}

		// __next_i (table [, index])
		// -------------------------------------------------------------------------------------------------------------------
		// Allows a program to traverse all fields of an array. index is an integer number
		public static DynValue __next_i(ScriptExecutionContext executionContext, CallbackArguments args) 
		{
			DynValue table = args.AsType(0, "!!next_i!!", DataType.Table);
			DynValue index = args.AsType(1, "!!next_i!!", DataType.Number);

			int idx = ((int)index.Number) + 1;
			DynValue val = table.Table.Get(idx);
			
			if (val.Type != DataType.Nil)
			{
				return DynValue.NewTuple(DynValue.NewNumber(idx), val);
			}
			else
			{
				return DynValue.Nil;
			}
		}
	}
}
