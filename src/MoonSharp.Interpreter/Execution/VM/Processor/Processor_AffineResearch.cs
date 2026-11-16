using System;
namespace MoonSharp.Interpreter.Execution.VM {
 sealed partial class Processor {
  private struct AffineAction { internal int Kind,Slot,To;internal OpCode Op;internal double Value; }
  private bool TryBuildAffineActions(int pc,int end,Span<AffineAction> actions,out int count) {
   count=0;var code=m_RootChunk.Code;int p=pc+1;
   if(code[p].OpCode!=OpCode.Clean)return false;
   actions[count++]=new AffineAction{Kind=1,Slot=code[p].NumVal,To=code[p].NumVal2};p++;
   if(code[p].OpCode!=OpCode.StoreLcl||code[p].NumVal!=0||code[p].NumVal2!=0)return false;
   int index=code[p].Symbol.i_Index;actions[count++]=new AffineAction{Kind=2,Slot=index};p++;
   while(p<end-2) {
    var i=code[p];if(i.OpCode==OpCode.Debug||i.OpCode==OpCode.Nop){p++;continue;}
    if(i.OpCode==OpCode.Clean){if(p!=end-3)return false;actions[count++]=new AffineAction{Kind=1,Slot=i.NumVal,To=i.NumVal2};p++;continue;}
    if(i.OpCode!=OpCode.Local||i.Symbol.i_Index==index)return false;int target=i.Symbol.i_Index;actions[count++]=new AffineAction{Kind=3,Slot=target};p++;
    while(p<end-2) {
     i=code[p];if(i.OpCode==OpCode.Scalar){p++;continue;}
     if(i.OpCode==OpCode.Neg){actions[count++]=new AffineAction{Kind=4,Op=OpCode.Neg};p++;continue;}
     if(i.OpCode!=OpCode.Literal)break;
     if(i.Value==null||i.Value.Type!=DataType.Number||double.IsNaN(i.Value.Number)||p+1>=end-2)return false;
     var op=code[p+1].OpCode;if(op!=OpCode.Add&&op!=OpCode.Sub&&op!=OpCode.Mul&&op!=OpCode.Div&&op!=OpCode.Mod&&op!=OpCode.Power)return false;
     actions[count++]=new AffineAction{Kind=4,Op=op,Value=i.Value.Number};p+=2;
    }
    if(p+1>=end-2||code[p].OpCode!=OpCode.StoreLcl||code[p].Symbol.i_Index!=target||code[p].NumVal!=0||code[p].NumVal2!=0||code[p+1].OpCode!=OpCode.Pop||code[p+1].NumVal!=1)return false;
    actions[count++]=new AffineAction{Kind=5,Slot=target};p+=2;
   }
   return count>2;
  }
  private bool RunAffineActions(Span<AffineAction> actions,Span<double> regs,Span<byte> states,double counter) {
   double value=0;for(int i=0;i<actions.Length;i++){var a=actions[i];switch(a.Kind){
    case 1:if(a.Slot>=0&&a.To>=a.Slot)for(int k=a.Slot;k<=a.To;k++)states[k]=0;break;
    case 2:regs[a.Slot]=counter;states[a.Slot]=1;break;
    case 3:if(states[a.Slot]!=1)return false;value=regs[a.Slot];break;
    case 4:switch(a.Op){case OpCode.Neg:value=-value;break;case OpCode.Add:value+=a.Value;break;case OpCode.Sub:value-=a.Value;break;case OpCode.Mul:value*=a.Value;break;case OpCode.Div:value/=a.Value;break;case OpCode.Mod:value=NumericModulo(value,a.Value);break;default:value=Math.Pow(value,a.Value);break;}if(double.IsNaN(value))return false;break;
    case 5:regs[a.Slot]=value;states[a.Slot]=1;break;
   }}return true;
  }
 }
}
