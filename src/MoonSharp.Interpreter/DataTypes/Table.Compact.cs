using System;
using System.Collections.Generic;
using MoonSharp.Interpreter.DataStructs;

namespace MoonSharp.Interpreter
{
	public partial class Table
	{
		private const int SmallCapacity = 8;
        internal void ReserveSmallConstructor() { if(m_Values==null && m_Small==null && m_Dense==null && m_CompactCount==0) m_Small=new TablePair[8]; }
		private TablePair[] m_Small;
		private DynValue[] m_Dense;
		private int m_CompactCount;

		private int FindSmall(string key)
		{
			for (int i = 0; i < m_CompactCount && m_Small != null; i++)
				if (m_Small[i].Key.Type == DataType.String && m_Small[i].Key.String == key) return i;
			return -1;
		}

		private int FindSmall(int key)
		{
			if (key < 1) return -1;
			for (int i = 0; i < m_CompactCount && m_Small != null; i++)
				if (m_Small[i].Key.Type == DataType.Number && m_Small[i].Key.Number == key) return i;
			return -1;
		}

		private int FindSmall(DynValue key)
		{
			for (int i = 0; i < m_CompactCount && m_Small != null; i++)
				if (m_Small[i].Key.Equals(key)) return i;
			return -1;
		}

		private DynValue SmallValue(int index)
		{
			return index < 0 ? null : m_Small[index].Value;
		}

		private bool SetCompactInteger(int key, DynValue value)
		{
			// Preserve the host int overload's legacy index for nonpositive keys.
			if (key < 1)
			{
				if (value.IsNil()) return true;
				PromoteCompact();
				return false;
			}
			if (m_Dense == null)
				return SetCompact(DynValue.NewNumber(key), value);

			DynValue previous = key <= m_CompactCount ? m_Dense[key - 1] : null;
			if (value.IsNil() && previous == null) return true;
			if (previous == null && key != m_CompactCount + 1)
			{
				PromoteCompact();
				return false;
			}
			if (key > m_Dense.Length)
				Array.Resize(ref m_Dense, checked(m_Dense.Length * 2));
			m_Dense[key - 1] = value;
			if (key > m_CompactCount) m_CompactCount = key;
			CompactValueChanged(previous, value, key);
			return true;
		}

		private bool SetCompact(DynValue key, DynValue value)
		{
			if (m_Dense != null)
			{
				if (key.Type == DataType.Number && GetIntegralKey(key.Number) > 0)
					return SetCompactInteger((int)key.Number, value);
				if (value.IsNil()) return true;
				PromoteCompact();
				return false;
			}
			if (key.Type == DataType.Number && GetIntegralKey(key.Number) < 1)
			{
				if (value.IsNil()) return true;
				PromoteCompact();
				return false;
			}
			int index = FindSmall(key);
			if (index < 0 && value.IsNil()) return true;
			if (index < 0 && m_CompactCount == SmallCapacity)
			{
				// Sequential integer tables can grow without nodes, maps or boxed keys.
				if (key.Type == DataType.Number && key.Number == m_CompactCount + 1 && IsSmallSequential())
				{
					m_Dense = new DynValue[SmallCapacity * 2];
					for (int i = 0; i < m_CompactCount; i++) m_Dense[i] = m_Small[i].Value;
					m_Small = null;
					return SetCompactInteger((int)key.Number, value);
				}
				PromoteCompact();
				return false;
			}
			if (m_Small == null) m_Small = new TablePair[4];
			if (index < 0)
			{
				if (m_CompactCount == m_Small.Length) Array.Resize(ref m_Small, SmallCapacity);
				index = m_CompactCount++;
			}
			var previous = m_Small[index].Value;
			m_Small[index] = new TablePair(key, value);
			CompactValueChanged(previous, value, key.Type == DataType.Number ? (int)key.Number : -1);
			return true;
		}

		private bool IsSmallSequential()
		{
			for (int i = 0; i < m_CompactCount; i++)
				if (m_Small[i].Key.Type != DataType.Number || m_Small[i].Key.Number != i + 1) return false;
			return true;
		}

		private void CompactValueChanged(DynValue previous, DynValue value, int integerKey)
		{
			if (m_ContainsNilEntries && value.IsNotNil() && (previous == null || previous.IsNil()))
				CollectCompactDeadKeys();
			if (value.IsNil())
			{
				m_ContainsNilEntries = true;
				if (integerKey > 0) m_CachedLength = -1;
			}
			else if (integerKey == m_CachedLength + 1 && m_CachedLength >= 0)
			{
				DynValue next;
				while ((next = RawGet(m_CachedLength + 1)) != null && next.IsNotNil())
					m_CachedLength++;
			}
		}

		private bool RemoveCompact(int index)
		{
			if (index < 0) return false;
			Array.Copy(m_Small, index + 1, m_Small, index, m_CompactCount - index - 1);
			m_Small[--m_CompactCount] = default(TablePair);
			m_CachedLength = -1;
			return true;
		}

		private void CollectCompactDeadKeys()
		{
			if (m_Dense != null)
			{
				for (int i = 0; i < m_CompactCount; i++)
					if (m_Dense[i] != null && m_Dense[i].IsNil()) m_Dense[i] = null;
			}
			else
			{
				int count = 0;
				for (int i = 0; i < m_CompactCount; i++)
					if (m_Small[i].Value.IsNotNil()) m_Small[count++] = m_Small[i];
				if (m_Small != null) Array.Clear(m_Small, count, m_CompactCount - count);
				m_CompactCount = count;
			}
			m_ContainsNilEntries = false;
			m_CachedLength = -1;
		}

		private TablePair? NextCompactKey(DynValue key)
		{
			int index = -1;
			if (!key.IsNil())
			{
				if (m_Dense != null)
				{
					int integer = key.Type == DataType.Number ? GetIntegralKey(key.Number) : -1;
					if (integer < 1 || integer > m_CompactCount || m_Dense[integer - 1] == null) return null;
					index = integer - 1;
				}
				else if ((index = FindSmall(key)) < 0) return null;
			}
			for (int i = index + 1; i < m_CompactCount; i++)
			{
				var value = m_Dense != null ? m_Dense[i] : m_Small[i].Value;
				if (value != null && value.IsNotNil())
					return m_Dense != null ? new TablePair(DynValue.NewNumber(i + 1), value) : m_Small[i];
			}
			return TablePair.Nil;
		}

		private IEnumerable<TablePair> CompactPairs()
		{
			for (int i = 0; i < m_CompactCount; i++)
			{
				if (m_Dense == null) yield return m_Small[i];
				else if (m_Dense[i] != null) yield return new TablePair(DynValue.NewNumber(i + 1), m_Dense[i]);
			}
		}

		private void PromoteCompact()
		{
			int strings = 0, numbers = 0, others = 0;
            if (m_Dense != null) { for(int i=0;i<m_CompactCount;i++) if(m_Dense[i]!=null) numbers++; }
            else if (m_Small != null) { for(int i=0;i<m_CompactCount;i++) {
                var k=m_Small[i].Key;
                if(k.Type==DataType.String) strings++;
                else if(k.Type==DataType.Number && GetIntegralKey(k.Number)>0) numbers++;
                else others++;
            }}
            m_Values = new LinkedList<TablePair>();
			m_StringMap = new LinkedListIndex<string, TablePair>(m_Values);
			m_ArrayMap = new LinkedListIndex<int, TablePair>(m_Values);
			m_ValueMap = new LinkedListIndex<DynValue, TablePair>(m_Values);
			foreach (var pair in CompactPairs())
			{
				if (pair.Key.Type == DataType.String) m_StringMap.Set(pair.Key.String, pair, strings);
				else if (pair.Key.Type == DataType.Number && GetIntegralKey(pair.Key.Number) > 0)
					m_ArrayMap.Set((int)pair.Key.Number, pair, numbers);
				else m_ValueMap.Set(pair.Key, pair, others);
			}
			m_Small = null;
			m_Dense = null;
			m_CompactCount = 0;
		}
	}
}
