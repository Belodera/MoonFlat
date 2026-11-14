using System;
using System.Collections.Generic;
namespace MoonSharp.Interpreter.Execution.VM
{
 sealed partial class Processor
 {
  // Default-off experiment: at most one pure numeric assignment, 64 bytecodes.
  // No call, branch, upvalue, table, or observable suspension can occur inside it.
  private sealed class ExpressionMemo { internal long[] Inputs; internal int[] Ops; internal double Result; }
  private Dictionary<int, ExpressionMemo> m_ExpressionMemo;
  private bool TryRegisterExpression(ref int pc, ref long executed, bool quota)
  {
   var code=m_RootChunk.Code; if(code[pc].OpCode!=OpCode.Local && code[pc].OpCode!=OpCode.Literal)return false;
   var locals=m_ExecutionStack.Peek().LocalScope;
   Span<double> values=stackalloc double[64]; Span<long> inputs=stackalloc long[64];
   Span<int> ops=stackalloc int[64]; int nops=0;
   int depth=0,peak=0,ninputs=0,end=-1;DynValue target=null;
   // Preflight everything before mutation. Re-evaluate bytecodes on every attempt:
   // compiler append/load cannot leave stale cached bytecode plans.
   for(int j=pc;j<code.Count && j<pc+64;j++) {
    var i=code[j];var op=i.OpCode;
    if(op==OpCode.Local || op==OpCode.Literal) {
     var v=op==OpCode.Local?locals[i.Symbol.i_Index]:i.Value;
     if(v==null || v.Type!=DataType.Number || double.IsNaN(v.Number))return false;
     depth++;inputs[ninputs++]=BitConverter.DoubleToInt64Bits(v.Number);ops[nops++]=-1;peak=Math.Max(peak,depth);
    } else if(op==OpCode.Scalar) {if(depth<1)return false;} else if(op==OpCode.Neg) {if(depth<1)return false;ops[nops++]=(int)op;}
    else if(op==OpCode.Add||op==OpCode.Sub||op==OpCode.Mul||op==OpCode.Div||op==OpCode.Mod||op==OpCode.Power) {
     if(depth<2)return false;depth--;ops[nops++]=(int)op;
    } else if(op==OpCode.StoreLcl && depth==1 && i.NumVal==0 && i.NumVal2==0 && j+1<code.Count && code[j+1].OpCode==OpCode.Pop && code[j+1].NumVal==1) {
     target=locals[i.Symbol.i_Index];if(target==null||target.ReadOnly||target.Type!=DataType.Number)return false;end=j+2;break;
    }else return false;
   }
   if(end<0||end-pc<5||m_ValueStack.Count>m_ValueStack.MaximumCapacity-peak)return false;
   int width=end-pc;if(quota && executed+width-1>AutoYieldCounter)return false;
   double result=0; ExpressionMemo memo=null; bool hit=false;
   if(m_Script.Options.ResearchExpressionMemo) {
    hit=m_ExpressionMemo!=null && m_ExpressionMemo.TryGetValue(pc,out memo)&&memo.Inputs.Length==ninputs&&memo.Ops.Length==nops;
    if(hit)for(int k=0;k<ninputs;k++)if(memo.Inputs[k]!=inputs[k]){hit=false;break;}
    if(hit)for(int k=0;k<nops;k++)if(memo.Ops[k]!=ops[k]){hit=false;break;}
   }
   if(hit)result=memo.Result;
   else {
    depth=0;int ri=0;
    for(int k=0;k<nops;k++) {
     if(ops[k]==-1){values[depth++]=BitConverter.Int64BitsToDouble(inputs[ri++]);continue;}
     var op=(OpCode)ops[k];if(op==OpCode.Neg){values[depth-1]=-values[depth-1];continue;}
     double y=values[--depth],x=values[depth-1];
     switch(op){case OpCode.Add:values[depth-1]=x+y;break;case OpCode.Sub:values[depth-1]=x-y;break;case OpCode.Mul:values[depth-1]=x*y;break;case OpCode.Div:values[depth-1]=x/y;break;case OpCode.Mod:values[depth-1]=NumericModulo(x,y);break;default:values[depth-1]=Math.Pow(x,y);break;}
     if(double.IsNaN(values[depth-1]))return false;
    }
    result=values[0];
    if(double.IsNaN(result))return false;
    if(m_Script.Options.ResearchExpressionMemo && (memo!=null||m_ExpressionMemo==null||m_ExpressionMemo.Count<256)) {
     if(memo==null||memo.Inputs.Length!=ninputs||memo.Ops.Length!=nops)memo=new ExpressionMemo{Inputs=new long[ninputs],Ops=new int[nops]};
     if(m_ExpressionMemo==null)m_ExpressionMemo=new Dictionary<int, ExpressionMemo>();
     inputs.Slice(0,ninputs).CopyTo(memo.Inputs);ops.Slice(0,nops).CopyTo(memo.Ops);memo.Result=result;m_ExpressionMemo[pc]=memo;
    }
   }
   target.AssignNumber(result);pc=end;executed+=width-1;return true;
  }
 }
}
