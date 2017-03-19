using System;
using CPUx86 = Cosmos.Assembler.x86;
using Cosmos.Assembler.x86;

using XSharp.Common;
using static XSharp.Common.XSRegisters;
using static Cosmos.Assembler.x86.SSE.ComparePseudoOpcodes;

namespace Cosmos.IL2CPU.X86.IL
{
    /// <summary>
    /// Converts the unsigned integer value on top of the evaluation stack to F (native float) it can be double or some FPU extended precision Floating Point
    /// type as the weird 80 bit float of x87). For now we assume it to be always equal to double.
    /// </summary>
    [Cosmos.IL2CPU.OpCode(ILOpCode.Code.Conv_R_Un)]

    public class Conv_R_Un : ILOp
    {

        public Conv_R_Un(Cosmos.Assembler.Assembler aAsmblr)
            : base(aAsmblr)
        {
        }

        public override void Execute(_MethodInfo aMethod, ILOpCode aOpCode)
        {
            var xValue = aOpCode.StackPopTypes[0];
            var xValueIsFloat = TypeIsFloat(xValue);
            var xValueSize = SizeOfType(xValue);

            if (xValueSize > 8)
            {
                //EmitNotImplementedException( Assembler, aServiceProvider, "Size '" + xSize.Size + "' not supported (add)", aCurrentLabel, aCurrentMethodInfo, aCurrentOffset, aNextLabel );
                throw new NotImplementedException();
            }
            //TODO if on stack a float it is first truncated, http://msdn.microsoft.com/en-us/library/system.reflection.emit.opcodes.conv_r_un.aspx
            if (!xValueIsFloat)
            {
                switch (xValueSize)
                {
                    case 1:
                    case 2:
                    case 4:
                        /*
                         * Code generated by C# / Visual Studio 2015 
                         * mov         eax,dword ptr [anInt]  
                         * mov         dword ptr [ebp-0E0h],eax  
                         * cvtsi2sd    xmm0,dword ptr [ebp-0E0h]  
                         * mov         ecx,dword ptr [ebp-0E0h]
                         * shr         ecx,1Fh  
                         * addsd       xmm0,mmword ptr __xmm@41f00000000000000000000000000000 (01176B40h)[ecx*8]  
                         * movsd       mmword ptr [aDouble],xmm0 # This for now means to copy our converted double to ESP
                         */
                        string BaseLabel = GetLabel(aMethod, aOpCode) + ".";
                        string LabelSign_Bit_Unset = BaseLabel + "LabelSign_Bit_Unset";

                        XS.Set(EAX, ESP, sourceIsIndirect: true);
                        XS.Set(EBP, EAX, destinationDisplacement: -0xE0, destinationIsIndirect: true);
                        XS.SSE2.ConvertSI2SD(XMM0, EBP, sourceDisplacement: -0xE0, sourceIsIndirect: true);
                        XS.Set(ECX, EBP, sourceDisplacement: -0xE0, sourceIsIndirect: true);
                        // OK now we put in ECX the last bit of our unsigned value,  we call it "SIGN_BIT" but is a little improper...
                        XS.ShiftRight(ECX, 31);
                        /*
                         * if the 'SIGN_BIT' is 0 it means that our uint could have been placed in a normal int so ConvertSI2SD did already
                         * the right thing: we have finished
                         * if the value is 1 we need to do that addition with that weird constant to obtain the real value as double
                         */
                        XS.Compare(ECX, 0x00);
                        XS.Jump(ConditionalTestEnum.Equal, LabelSign_Bit_Unset);
                        XS.LiteralCode(@"addsd xmm0, [__uint2double_const]");
                        XS.Label(LabelSign_Bit_Unset);
                        // We have converted our value to double put it on ESP
                        // expand stack, that moved data is valid stack
                        XS.Sub(ESP, 4);
                        XS.SSE2.MoveSD(ESP, XMM0, destinationIsIndirect: true);
                        break;

                    case 8:
                        BaseLabel = GetLabel(aMethod, aOpCode) + ".";
                        LabelSign_Bit_Unset = BaseLabel + "LabelSign_Bit_Unset";

                        /*
                         * mov EAX, ESP + 4
                         * fild  qword ptr [esp]
                         * shr EAX, 31
                         * cmp ESP, 0
                         * jpe LabelSign_Bit_Unset
                         * LabelSign_Bit_Unset:
                         * fadd  dword ptr __ulong2double_const2
                         * fstp ESP
                         */
                        // Save the high part of the ulong in EAX (we cannot move all of ESP as it has 64 bit size)
                        XS.Set(EAX, ESP, sourceIsIndirect: true, sourceDisplacement: 4);
                        XS.FPU.IntLoad(ESP, isIndirect: true, size: RegisterSize.Long64);
                        XS.Test(EAX, EAX);
                        XS.Jump(ConditionalTestEnum.NotSign, LabelSign_Bit_Unset);
                        // If the sign is set we remove it using the constant __ulong2double_const4
                        XS.LiteralCode(@"fadd dword [__ulong2double_const]");
                        XS.Label(LabelSign_Bit_Unset);
                        // Convert the value to double and move it into the stack
                        XS.FPU.FloatStoreAndPop(ESP, isIndirect: true, size: RegisterSize.Long64);
                        break;

                    default:
                       //EmitNotImplementedException( Assembler, GetServiceProvider(), "Conv_I: SourceSize " + xSource + " not supported!", mCurLabel, mMethodInformation, mCurOffset, mNextLabel );
                       throw new NotImplementedException("Conv_R_Un with type " + xValue + " not supported!");
                   }
            }
            else
            {
                throw new NotImplementedException("Conv_R_Un with type " + xValue + " not supported!");
            }
        }
    }
}
 