using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace MoonSharp.Interpreter.Execution.VM {
 sealed partial class Processor {
  private sealed class SharedLoopMemoStore { internal readonly Dictionary<Instruction, SharedLoopMemo> Entries=new Dictionary<Instruction, SharedLoopMemo>(); }
  private sealed class SharedLoopMemo { internal long[] Signature,Inputs;internal byte[] States;internal double[] Outputs;internal byte[] OutputStates;internal double Counter;internal int Limit,Laps,Peak; }
  private static readonly ConditionalWeakTable<Script,SharedLoopMemoStore> s_LoopMemo=new ConditionalWeakTable<Script,SharedLoopMemoStore>();
  private bool TryNumericLoop(ref int pc,ref long executed,bool quota) {
   var code=m_RootChunk.Code;var start=code[pc];if(start.OpCode!=OpCode.JFor)return false;
   int end=start.NumVal,width=end-pc;
   if(width<5||width>256||end>=code.Count||code[end-1].OpCode!=OpCode.Jump||code[end-1].NumVal!=pc||code[end-2].OpCode!=OpCode.Incr||code[end-2].NumVal!=1)return false;
   var scope=m_ExecutionStack.Peek().LocalScope;if(scope.Length>128||m_ValueStack.Count<3)return false;
   var cv=m_ValueStack.Peek(0);var sv=m_ValueStack.Peek(1);var ev=m_ValueStack.Peek(2);
   if(cv.Type!=DataType.Number||sv.Type!=DataType.Number||ev.Type!=DataType.Number)return false;
   double counter=cv.Number,step=sv.Number,stop=ev.Number;
   if(double.IsNaN(counter)||double.IsNaN(step)||double.IsNaN(stop)||step==0)return false;
   int limit=4096;if(quota)limit=(int)Math.Min(limit,(AutoYieldCounter-executed+1)/width);if(limit<1)return false;
   Span<double> regs=stackalloc double[128];Span<double> previous=stackalloc double[128];Span<byte> states=stackalloc byte[128];Span<byte> previousStates=stackalloc byte[128];Span<byte> dirty=stackalloc byte[128];dirty.Clear();Span<byte> cleared=stackalloc byte[128];cleared.Clear();
   regs.Clear();states.Clear();
   for(int k=0;k<scope.Length;k++){var v=scope[k];states[k]=v==null?(byte)0:v.Type==DataType.Number&&!double.IsNaN(v.Number)?(byte)1:(byte)2;if(states[k]==1)regs[k]=v.Number;}
   Span<long> signature=stackalloc long[1280];int signatureCount=0;
   int indexLocal=-1;bool indexRead=false;
   for(int p=pc+1;p<end-2;p++) {
    var i=code[p];var op=i.OpCode;
    signature[signatureCount++]=(int)op;signature[signatureCount++]=i.NumVal;signature[signatureCount++]=i.NumVal2;signature[signatureCount++]=i.Symbol==null?-1:i.Symbol.i_Index;
    signature[signatureCount++]=i.Value!=null&&i.Value.Type==DataType.Number?BitConverter.DoubleToInt64Bits(i.Value.Number):0;
    if(op==OpCode.Literal&&(i.Value==null||i.Value.Type!=DataType.Number||double.IsNaN(i.Value.Number)))return false;
    if(op==OpCode.Local||op==OpCode.StoreLcl){if(i.Symbol==null||i.Symbol.i_Index<0||i.Symbol.i_Index>=scope.Length)return false;int k=i.Symbol.i_Index;
     if(op==OpCode.StoreLcl){if(i.NumVal!=0||i.NumVal2!=0)return false;dirty[k]=1;if(indexLocal<0)indexLocal=k;}
     else if(k==indexLocal)indexRead=true;
    }else if(op==OpCode.Clean){if(i.NumVal>=0&&i.NumVal2>=i.NumVal){if(i.NumVal2>=scope.Length)return false;for(int k=i.NumVal;k<=i.NumVal2;k++){dirty[k]=1;cleared[k]=1;}}}
    else if(op!=OpCode.Literal&&op!=OpCode.Scalar&&op!=OpCode.Neg&&op!=OpCode.Add&&op!=OpCode.Sub&&op!=OpCode.Mul&&op!=OpCode.Div&&op!=OpCode.Mod&&op!=OpCode.Power&&op!=OpCode.Pop&&op!=OpCode.Debug&&op!=OpCode.Nop)return false;
   }
   // Mutating a local shared with another local would make the shadow register model invalid.
   for(int k=0;k<scope.Length;k++)if(dirty[k]!=0&&scope[k]!=null){if(scope[k].ReadOnly||scope[k].Type!=DataType.Number)return false;for(int j=0;j<scope.Length;j++)if(j!=k&&ReferenceEquals(scope[k],scope[j]))return false;}
   SharedLoopMemoStore store=null;SharedLoopMemo entry=null;bool memoHit=false;
   if(m_Script.Options.ResearchSharedLoopMemo) {
    store=s_LoopMemo.GetValue(m_Script,_=>new SharedLoopMemoStore());lock(store.Entries)store.Entries.TryGetValue(start,out entry);
    memoHit=entry!=null&&entry.Limit==limit&&entry.States.Length==scope.Length&&entry.Signature.Length==signatureCount;
    if(memoHit)for(int k=0;k<signatureCount;k++)if(entry.Signature[k]!=signature[k]){memoHit=false;break;}
    if(memoHit)for(int k=0;k<scope.Length;k++)if(entry.States[k]!=states[k]||(states[k]==1&&entry.Inputs[k]!=BitConverter.DoubleToInt64Bits(regs[k]))){memoHit=false;break;}
    if(memoHit) {int n=scope.Length;memoHit=entry.Inputs[n]==BitConverter.DoubleToInt64Bits(counter)&&entry.Inputs[n+1]==BitConverter.DoubleToInt64Bits(step)&&entry.Inputs[n+2]==BitConverter.DoubleToInt64Bits(stop)&&m_ValueStack.Count<=m_ValueStack.MaximumCapacity-entry.Peak;}
   }
   // Save only numeric state, never local DynValue identities.
   Span<double> initial=stackalloc double[128];Span<byte> initialStates=stackalloc byte[128];regs.CopyTo(initial);states.CopyTo(initialStates);double initialCounter=counter;
   Span<AffineAction> affine=m_Script.Options.ResearchAffineKernel?stackalloc AffineAction[256]:Span<AffineAction>.Empty;int affineCount=0;bool useAffine=m_Script.Options.ResearchAffineKernel&&m_ValueStack.Count<=m_ValueStack.MaximumCapacity-2&&TryBuildAffineActions(pc,end,affine,out affineCount);
   Span<double> stack=stackalloc double[128];int laps=0,peak=0;
   if(memoHit){entry.Outputs.AsSpan().CopyTo(regs);entry.OutputStates.AsSpan().CopyTo(states);counter=entry.Counter;laps=entry.Laps;}

   while(!memoHit && laps<limit && (step>0?counter<=stop:counter>=stop)) {
    regs.CopyTo(previous);states.CopyTo(previousStates);stack[0]=counter;int depth=1;
    if(useAffine){if(!RunAffineActions(affine.Slice(0,affineCount),regs,states,counter))return false;peak=Math.Max(peak,2);}
    else for(int p=pc+1;p<end-2;p++) {
     var i=code[p];var op=i.OpCode;
     if(op==OpCode.Local){int k=i.Symbol.i_Index;if(states[k]!=1)return false;if(depth>=128)return false;stack[depth++]=regs[k];}
     else if(op==OpCode.Literal){if(i.Value==null||i.Value.Type!=DataType.Number||double.IsNaN(i.Value.Number)||depth>=128)return false;stack[depth++]=i.Value.Number;}
     else if(op==OpCode.Clean){if(i.NumVal>=0&&i.NumVal2>=i.NumVal)for(int k=i.NumVal;k<=i.NumVal2;k++)states[k]=0;}
     else if(op==OpCode.StoreLcl){if(depth<1)return false;regs[i.Symbol.i_Index]=stack[depth-1];states[i.Symbol.i_Index]=1;}
     else if(op==OpCode.Pop){if(i.NumVal<0||depth-i.NumVal<1)return false;depth-=i.NumVal;}
     else if(op==OpCode.Debug||op==OpCode.Nop){}
     else if(op==OpCode.Scalar){if(depth<1)return false;}
     else if(op==OpCode.Neg){if(depth<1)return false;stack[depth-1]=-stack[depth-1];}
     else {if(depth<2)return false;double y=stack[--depth],x=stack[depth-1];switch(op){case OpCode.Add:stack[depth-1]=x+y;break;case OpCode.Sub:stack[depth-1]=x-y;break;case OpCode.Mul:stack[depth-1]=x*y;break;case OpCode.Div:stack[depth-1]=x/y;break;case OpCode.Mod:stack[depth-1]=NumericModulo(x,y);break;case OpCode.Power:stack[depth-1]=Math.Pow(x,y);break;default:return false;}if(double.IsNaN(stack[depth-1]))return false;}
     peak=Math.Max(peak,depth-1);if(m_ValueStack.Count>m_ValueStack.MaximumCapacity-(depth-1))return false;
    }
    if(depth!=1)return false;counter+=step;laps++;
    // Only exact integer +/-1 counters, index-independent pure transitions.
    if(m_Script.Options.ResearchLoopFixedpoint&&!indexRead&&(step==1||step==-1)&&Math.Abs(counter)<4503599627370496d&&Math.Abs(stop)<4503599627370496d&&counter==Math.Truncate(counter)&&stop==Math.Truncate(stop)) {
     bool equal=true;for(int k=0;k<scope.Length;k++)if(states[k]!=previousStates[k]||(states[k]==1&&BitConverter.DoubleToInt64Bits(regs[k])!=BitConverter.DoubleToInt64Bits(previous[k]))){equal=false;break;}
     if(equal){double remaining=step>0?stop-counter+1:counter-stop+1;int skip=(int)Math.Max(0,Math.Min(limit-laps,remaining));counter+=step*skip;laps+=skip;}
    }
   }
   if(laps==0)return false;
   if(store!=null&&!memoHit) {
    var item=new SharedLoopMemo{Signature=signature.Slice(0,signatureCount).ToArray(),Inputs=new long[scope.Length+3],States=initialStates.Slice(0,scope.Length).ToArray(),Outputs=regs.Slice(0,scope.Length).ToArray(),OutputStates=states.Slice(0,scope.Length).ToArray(),Counter=counter,Limit=limit,Laps=laps,Peak=peak};
    for(int k=0;k<scope.Length;k++)if(initialStates[k]==1)item.Inputs[k]=BitConverter.DoubleToInt64Bits(initial[k]);int n=scope.Length;item.Inputs[n]=BitConverter.DoubleToInt64Bits(initialCounter);item.Inputs[n+1]=BitConverter.DoubleToInt64Bits(step);item.Inputs[n+2]=BitConverter.DoubleToInt64Bits(stop);
    lock(store.Entries){if(store.Entries.Count<64||store.Entries.ContainsKey(start))store.Entries[start]=item;}
   }
   for(int k=0;k<scope.Length;k++)if(dirty[k]!=0){if(states[k]==0)scope[k]=null;else if(states[k]==1){if(scope[k]==null||cleared[k]!=0)scope[k]=DynValue.NewNumber(regs[k]);else scope[k].AssignNumber(regs[k]);}else return false;}
   if(cv.ReadOnly){m_ValueStack.Pop();cv=cv.CloneAsWritable();m_ValueStack.Push(cv);}cv.AssignNumber(counter);
   executed+=(long)width*laps-1;return true;
  }
 }
}
