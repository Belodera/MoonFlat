namespace MoonSharp.Interpreter
{
    public partial class Table
    {
        // A derived allocation avoids both a side-table lookup and a field-size
        // tax on every ordinary Lua Table. It remains a real Table everywhere.
        private sealed class PristineVectorTable : Table
        {
            internal readonly double X,Y,Z;
            internal bool Active;
            internal PristineVectorTable(Script owner,double x,double y,double z):base(owner){X=x;Y=y;Z=z;}
        }
        // Explicit factory, never used by ordinary Lua table constructors.
        public static Table CreatePristineVector3(Script owner, Table metatable, double x,double y,double z)
        {
            var table=new PristineVectorTable(owner,x,y,z);table.MetaTable=metatable;table.Active=true;return table;
        }
        public bool TryReadPristineVector3(out double x,out double y,out double z)
        {
            if(this is PristineVectorTable vector && vector.Active)
            {x=vector.X;y=vector.Y;z=vector.Z;return true;}
            x=y=z=0;return false;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void MaterializePristineVector3()
        {
            if(this is PristineVectorTable vector && vector.Active) MaterializePristineVector3Slow(vector);
        }
        private void MaterializePristineVector3Slow(PristineVectorTable vector)
        {
            vector.Active=false;
            // Reading a frozen table may expose its ordinary representation but
            // must not make it writable. Internal materialization runs no user code.
            bool frozen=m_IsFrozen;m_IsFrozen=false;
            try {Set("__vector3",DynValue.True);Set("__x",DynValue.NewNumber(vector.X));Set("__y",DynValue.NewNumber(vector.Y));Set("__z",DynValue.NewNumber(vector.Z));}
            finally {m_IsFrozen=frozen;}
        }
    }
}
