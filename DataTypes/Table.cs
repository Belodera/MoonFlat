using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter.DataStructs;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// A class representing a Lua table.
	/// </summary>
	public partial class Table : RefIdObject, IScriptPrivateResource
	{
		LinkedList<TablePair> m_Values;
		LinkedListIndex<DynValue, TablePair> m_ValueMap;
		LinkedListIndex<string, TablePair> m_StringMap;
		LinkedListIndex<int, TablePair> m_ArrayMap;
		readonly Script m_Owner;

		int m_InitArray = 0;
		int m_CachedLength = 0;
		bool m_ContainsNilEntries = false;
		bool m_IsFrozen;

		/// <summary>Whether Luau table.freeze has made this table read-only.</summary>
		public bool IsFrozen { get { return m_IsFrozen; } }

		public void Freeze()
		{
			if (m_IsFrozen)
				throw new ScriptRuntimeException("table is already frozen");
			if (m_MetaTable != null && m_MetaTable.Get("__metatable").IsNotNil())
				throw new ScriptRuntimeException("cannot freeze a table with a protected metatable");
			m_IsFrozen = true;
		}

		private void CheckWritable()
		{
            MaterializePristineVector3();
			if (m_IsFrozen)
				throw new ScriptRuntimeException("attempt to modify a readonly table");
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Table"/> class.
		/// </summary>
		/// <param name="owner">The owner script.</param>
		public Table(Script owner)
		{
			m_Owner = owner;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Table"/> class.
		/// </summary>
		/// <param name="owner">The owner.</param>
		/// <param name="arrayValues">The values for the "array-like" part of the table.</param>
		public Table(Script owner, params DynValue[] arrayValues)
			: this(owner)
		{
			for (int i = 0; i < arrayValues.Length; i++)
			{
				this.Set(i + 1, arrayValues[i]);
			}
		}

		/// <summary>
		/// Gets the script owning this resource.
		/// </summary>
		public Script OwnerScript
		{
			get { return m_Owner; }
		}

		/// <summary>
		/// Removes all items from the Table.
		/// </summary>
		public void Clear()
		{
			CheckWritable();
			if (m_Values == null)
			{
				m_Small = null;
				m_Dense = null;
				m_CompactCount = 0;
				m_ContainsNilEntries = false;
				m_CachedLength = 0;
				return;
			}
			m_Values.Clear();
			m_StringMap.Clear();
			m_ArrayMap.Clear();
			m_ValueMap.Clear();
            m_CachedLength = -1;
		}

		/// <summary>
		/// Gets the integral key from a double.
		/// </summary>
		private int GetIntegralKey(double d)
		{
			int v = ((int)d);

			if (d >= 1.0 && d == v)
				return v;

			return -1;
		}

		/// <summary>
		/// Gets or sets the 
		/// <see cref="System.Object" /> with the specified key(s).
		/// This will marshall CLR and MoonSharp objects in the best possible way.
		/// Multiple keys can be used to access subtables.
		/// </summary>
		/// <value>
		/// The <see cref="System.Object" />.
		/// </value>
		/// <param name="keys">The keys to access the table and subtables</param>
		public object this[params object[] keys]
		{
			get
			{
				return Get(keys).ToObject();
			}
			set
			{
				Set(keys, DynValue.FromObject(OwnerScript, value));
			}
		}

		/// <summary>
		/// Gets or sets the <see cref="System.Object"/> with the specified key(s).
		/// This will marshall CLR and MoonSharp objects in the best possible way.
		/// </summary>
		/// <value>
		/// The <see cref="System.Object"/>.
		/// </value>
		/// <param name="key">The key.</param>
		/// <returns></returns>
		public object this[object key]
		{
			get
			{
				return Get(key).ToObject();
			}
			set
			{
				Set(key, DynValue.FromObject(OwnerScript, value));
			}
		}

		private Table ResolveMultipleKeys(object[] keys, out object key)
		{
			//Contract.Ensures(Contract.Result<Table>() != null);
			//Contract.Requires(keys != null);

			Table t = this;
			key = (keys.Length > 0) ? keys[0] : null;

			for (int i = 1; i < keys.Length; ++i)
			{
				DynValue vt = t.RawGet(key);

				if (vt == null)
					throw new ScriptRuntimeException("Key '{0}' did not point to anything");

				if (vt.Type != DataType.Table)
					throw new ScriptRuntimeException("Key '{0}' did not point to a table");

				t = vt.Table;
				key = keys[i];
			}

			return t;
		}

		/// <summary>
		/// Append the value to the table using the next available integer index.
		/// </summary>
		/// <param name="value">The value.</param>
		public void Append(DynValue value)
		{
			int key = Length + 1;
			if (m_Values == null) Set(key, value);
			else
			{
				this.CheckScriptOwnership(value);
				PerformTableSet(m_ArrayMap, key, DynValue.NewNumber(key), value, true, key);
			}
		}

		#region Set

		private void PerformTableSet<T>(LinkedListIndex<T, TablePair> listIndex, T key, DynValue keyDynValue, DynValue value, bool isNumber, int appendKey)
		{
			CheckWritable();
			// Missing keys already read as nil. Do not retain a tombstone for a
			// key which never existed; existing tombstones support next(t, key).
			if (value.IsNil() && listIndex.Find(key) == null)
				return;
			TablePair prev = listIndex.Set(key, new TablePair(keyDynValue, value));

			// If this is an insert, we can invalidate all iterators and collect dead keys
			if (m_ContainsNilEntries && value.IsNotNil() && (prev.Value == null || prev.Value.IsNil()))
			{
				CollectDeadKeys();
			}
			// If this value is nil (and we didn't collect), set that there are nil entries, and invalidate array len cache
			else if (value.IsNil())
			{
				m_ContainsNilEntries = true;

				if (isNumber)
					m_CachedLength = -1;
			}
			else if (isNumber)
			{
				// If this is an array insert, we might have to invalidate the array length
				if (prev.Value == null || prev.Value.IsNilOrNan())
				{
					// If this is an array append, let's check the next element before blindly invalidating
					if (appendKey >= 0)
					{
						LinkedListNode<TablePair> next = m_ArrayMap.Find(appendKey + 1);
						if (next == null || next.Value.Value == null || next.Value.Value.IsNil())
						{
							m_CachedLength += 1;
						}
						else
						{
							m_CachedLength = -1;
						}
					}
					else
					{
						m_CachedLength = -1;
					}
				}
			}
		}

		/// <summary>
		/// Sets the value associated to the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void Set(string key, DynValue value)
		{
			if (key == null)
				throw ScriptRuntimeException.TableIndexIsNil();

			this.CheckScriptOwnership(value);
			CheckWritable();
            if (m_Values != null && value != null && value.IsNil())
            {
                var node = m_StringMap.Find(key);
                if (node == null) { DynValue.ConsumeElidedReferenceID(); return; }
                node.Value = new TablePair(DynValue.NewString(key), value);
                m_ContainsNilEntries = true;
                return;
            }
			if (m_Values == null && SetCompact(DynValue.NewString(key), value))
				return;
			PerformTableSet(m_StringMap, key, DynValue.NewString(key), value, false, -1);
		}

		/// <summary>
		/// Sets the value associated to the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void Set(int key, DynValue value)
		{
			this.CheckScriptOwnership(value);
			CheckWritable();
            if (m_Values != null && value != null && value.IsNil())
            {
                var node = m_ArrayMap.Find(key);
                if (node == null) { DynValue.ConsumeElidedReferenceID(); return; }
                node.Value = new TablePair(DynValue.NewNumber(key), value);
                m_ContainsNilEntries = true;
                m_CachedLength = -1;
                return;
            }
			if (m_Values == null && SetCompactInteger(key, value))
				return;
			PerformTableSet(m_ArrayMap, key, DynValue.NewNumber(key), value, true, -1);
		}

		/// <summary>
		/// Sets the value associated to the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void Set(DynValue key, DynValue value)
		{
			if (key.IsNilOrNan())
			{
				if (key.IsNil())
					throw ScriptRuntimeException.TableIndexIsNil();
				else
					throw ScriptRuntimeException.TableIndexIsNaN();
			}

			if (key.Type == DataType.String)
			{
				Set(key.String, value);
				return;
			}

			if (key.Type == DataType.Number)
			{
				int idx = GetIntegralKey(key.Number);

				if (idx > 0)
				{
					Set(idx, value);
					return;
				}
			}

			this.CheckScriptOwnership(key);
			this.CheckScriptOwnership(value);

			CheckWritable();
			if (m_Values == null && SetCompact(key, value))
				return;
			PerformTableSet(m_ValueMap, key, key, value, false, -1);
		}

		/// <summary>
		/// Sets the value associated with the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void Set(object key, DynValue value)
		{
			if (key == null)
				throw ScriptRuntimeException.TableIndexIsNil();

			if (key is string)
				Set((string)key, value);
			else if (key is int)
				Set((int)key, value);
			else
				Set(DynValue.FromObject(OwnerScript, key), value);
		}

		/// <summary>
		/// Sets the value associated with the specified keys.
		/// Multiple keys can be used to access subtables.
		/// </summary>
		/// <param name="key">The keys.</param>
		/// <param name="value">The value.</param>
		public void Set(object[] keys, DynValue value)
		{
			if (keys == null || keys.Length <= 0)
				throw ScriptRuntimeException.TableIndexIsNil();

			object key;
			ResolveMultipleKeys(keys, out key).Set(key, value);
		}

		#endregion

		#region Get

		/// <summary>
		/// Gets the value associated with the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(string key)
		{
			//Contract.Ensures(Contract.Result<DynValue>() != null);
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(int key)
		{
			//Contract.Ensures(Contract.Result<DynValue>() != null);
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(DynValue key)
		{
			//Contract.Ensures(Contract.Result<DynValue>() != null);
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// (expressed as a <see cref="System.Object"/>).
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(object key)
		{
			//Contract.Ensures(Contract.Result<DynValue>() != null);
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified keys (expressed as an 
		/// array of <see cref="System.Object"/>).
		/// This will marshall CLR and MoonSharp objects in the best possible way.
		/// Multiple keys can be used to access subtables.
		/// </summary>
		/// <param name="keys">The keys to access the table and subtables</param>
		public DynValue Get(params object[] keys)
		{
			//Contract.Ensures(Contract.Result<DynValue>() != null);
			return RawGet(keys) ?? DynValue.Nil;
		}

		#endregion

		#region RawGet

		private static DynValue RawGetValue(LinkedListNode<TablePair> linkedListNode)
		{
			return (linkedListNode != null) ? linkedListNode.Value.Value : null;
		}

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(string key)
		{
            MaterializePristineVector3();
			return m_Values == null ? SmallValue(FindSmall(key)) : RawGetValue(m_StringMap.Find(key));
		}

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(int key)
		{
            MaterializePristineVector3();
			if (m_Values != null) return RawGetValue(m_ArrayMap.Find(key));
			if (m_Dense != null) return key > 0 && key <= m_CompactCount ? m_Dense[key - 1] : null;
			return SmallValue(FindSmall(key));
		}

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(DynValue key)
		{
            MaterializePristineVector3();
			if (key.Type == DataType.String)
				return RawGet(key.String);

			if (key.Type == DataType.Number)
			{
				int idx = GetIntegralKey(key.Number);
				if (idx > 0)
					return RawGet(idx);
			}

			return m_Values == null ? SmallValue(FindSmall(key)) : RawGetValue(m_ValueMap.Find(key));
		}

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(object key)
		{
			if (key == null)
				return null;

			if (key is string)
				return RawGet((string)key);

			if (key is int)
				return RawGet((int)key);

			return RawGet(DynValue.FromObject(OwnerScript, key));
		}

		/// <summary>
		/// Gets the value associated with the specified keys (expressed as an
		/// array of <see cref="System.Object"/>).
		/// This will marshall CLR and MoonSharp objects in the best possible way.
		/// Multiple keys can be used to access subtables.
		/// </summary>
		/// <param name="keys">The keys to access the table and subtables</param>
		public DynValue RawGet(params object[] keys)
		{
			if (keys == null || keys.Length <= 0)
				return null;

			object key;
			return ResolveMultipleKeys(keys, out key).RawGet(key);
		}

		#endregion

		#region Remove

		private bool PerformTableRemove<T>(LinkedListIndex<T, TablePair> listIndex, T key, bool isNumber)
		{
			CheckWritable();
			var removed = listIndex.Remove(key);

			if (removed && isNumber)
			{
				m_CachedLength = -1;
			}

			return removed;
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(string key)
		{
			CheckWritable();
			if (m_Values == null) return RemoveCompact(FindSmall(key));
			return PerformTableRemove(m_StringMap, key, false);
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(int key)
		{
			CheckWritable();
			if (m_Values == null)
			{
				if (m_Dense != null)
				{
					if (key < 1 || key > m_CompactCount || m_Dense[key - 1] == null) return false;
					m_Dense[key - 1] = null;
					m_CachedLength = -1;
					return true;
				}
				return RemoveCompact(FindSmall(key));
			}
			return PerformTableRemove(m_ArrayMap, key, true);
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(DynValue key)
		{
			if (key.Type == DataType.String)
				return Remove(key.String);

			if (key.Type == DataType.Number)
			{
				int idx = GetIntegralKey(key.Number);
				if (idx > 0)
					return Remove(idx);
			}

			CheckWritable();
			if (m_Values == null) return RemoveCompact(FindSmall(key));
			return PerformTableRemove(m_ValueMap, key, false);
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(object key)
		{
			if (key is string)
				return Remove((string)key);

			if (key is int)
				return Remove((int)key);

			return Remove(DynValue.FromObject(OwnerScript, key));
		}

		/// <summary>
		/// Remove the value associated with the specified keys from the table.
		/// Multiple keys can be used to access subtables.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(params object[] keys)
		{
			if (keys == null || keys.Length <= 0)
				return false;

			object key;
			return ResolveMultipleKeys(keys, out key).Remove(key);
		}

		#endregion

		/// <summary>
		/// Collects the dead keys. This frees up memory but invalidates pending iterators.
		/// It's called automatically internally when the semantics of Lua tables allow, but can be forced
		/// externally if it's known that no iterators are pending.
		/// </summary>
		public void CollectDeadKeys()
		{
			// Tombstones do not change observable reads or iteration. A frozen
			// table cannot acquire new ones, so retaining them is safe.
			if (m_IsFrozen) return;
			if (m_Values == null)
			{
				CollectCompactDeadKeys();
				return;
			}
			for (LinkedListNode<TablePair> node = m_Values.First; node != null;)
			{
				var next = node.Next;
				if (node.Value.Value.IsNil())
				{
					Remove(node.Value.Key);
				}
				node = next;
			}

			m_ContainsNilEntries = false;
			m_CachedLength = -1;
		}


		/// <summary>
		/// Returns the next pair from a value
		/// </summary>
		public TablePair? NextKey(DynValue v)
		{
            MaterializePristineVector3();
			if (m_Values == null) return NextCompactKey(v);
			if (v.IsNil())
			{
				LinkedListNode<TablePair> node = m_Values.First;

				if (node == null)
					return TablePair.Nil;
				else
				{
					if (node.Value.Value.IsNil())
						return NextKey(node.Value.Key);
					else
						return node.Value;
				}
			}

			if (v.Type == DataType.String)
			{
				return GetNextOf(m_StringMap.Find(v.String));
			}

			if (v.Type == DataType.Number)
			{
				int idx = GetIntegralKey(v.Number);

				if (idx > 0)
				{
					return GetNextOf(m_ArrayMap.Find(idx));
				}
			}

			return GetNextOf(m_ValueMap.Find(v));
		}

		private TablePair? GetNextOf(LinkedListNode<TablePair> linkedListNode)
		{
			while (true)
			{
				if (linkedListNode == null)
					return null;

				if (linkedListNode.Next == null)
					return TablePair.Nil;

				linkedListNode = linkedListNode.Next;

				if (!linkedListNode.Value.Value.IsNil())
					return linkedListNode.Value;
			}
		}


		/// <summary>
		/// Gets the length of the "array part".
		/// </summary>
		public int Length
		{
			get
			{
				if (m_CachedLength < 0)
				{
					m_CachedLength = 0;

					for (int i = 1; RawGet(i) is DynValue value && !value.IsNil(); i++)
						m_CachedLength = i;
				}

				return m_CachedLength;
			}
		}

		internal void InitNextArrayKeys(DynValue val, bool lastpos)
		{
			if (val.Type == DataType.Tuple && lastpos)
			{
				foreach (DynValue v in val.Tuple)
					InitNextArrayKeys(v, true);
			}
			else
			{
				Set(++m_InitArray, val.ToScalar());
			}
		}

		/// <summary>
		/// Gets the meta-table associated with this instance.
		/// </summary>
		public Table MetaTable
		{
			get { return m_MetaTable; }
			set { CheckWritable(); this.CheckScriptOwnership(m_MetaTable); m_MetaTable = value; }
		}
		private Table m_MetaTable;



		/// <summary>
		/// Enumerates the key/value pairs.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<TablePair> Pairs
		{
			get
			{
                MaterializePristineVector3();
				return m_Values == null ? CompactPairs() : m_Values;
			}
		}

		/// <summary>
		/// Enumerates the keys.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<DynValue> Keys
		{
			get
			{
				return Pairs.Select(n => n.Key);
			}
		}

		/// <summary>
		/// Enumerates the values
		/// </summary>
		/// <returns></returns>
		public IEnumerable<DynValue> Values
		{
			get
			{
				return Pairs.Select(n => n.Value);
			}
		}
	}
}
