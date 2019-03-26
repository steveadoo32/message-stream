using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace System
{
    public static class SpanExtensions
    {
        
        /// <summary>
        /// Reads an unsigned int from the buffer. You need to be sure that you have enough in the buffer, this won't check bounds.
        /// Increments index by 4
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint ReadUInt(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            uint* shiftBytes = stackalloc uint[4];
            shiftBytes[0] = 24;
            shiftBytes[1] = 16;
            shiftBytes[2] = 8;
            shiftBytes[3] = 0;

            var arr = stackalloc byte[4];
            var span = new Span<byte>(arr, 4);
            buffer.Slice(index, 4).CopyTo(span);
            var vector = Avx.ConvertToVector128Int32(Avx.LoadVector128(arr));
            var shiftVector = Avx.LoadVector128(shiftBytes);
            var result = Avx2.ShiftLeftLogicalVariable(vector, shiftVector).AsUInt32();

            index += 4;

            return (uint) result.GetElement(0) | result.GetElement(1) | result.GetElement(2) | result.GetElement(3); // ((uint)b1 << 24) | ((uint)b2 << 16) | ((uint)b3 << 8) | b4;
        }

        /// <summary>
        /// Reads an int from the buffer. You need to be sure that you have enough in the buffer, this won't check bounds.
        /// Increments index by 4
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int ReadInt(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            uint* shiftBytes = stackalloc uint[4];
            shiftBytes[0] = 24;
            shiftBytes[1] = 16;
            shiftBytes[2] = 8;
            shiftBytes[3] = 0;

            var arr = stackalloc byte[4];
            var span = new Span<byte>(arr, 4);
            buffer.Slice(index, 4).CopyTo(span);
            var vector = Avx.ConvertToVector128Int32(Avx.LoadVector128(arr));
            var shiftVector = Avx.LoadVector128(shiftBytes);
            var result = Avx2.ShiftLeftLogicalVariable(vector, shiftVector);

            index += 4;

            return (int)result.GetElement(0) | result.GetElement(1) | result.GetElement(2) | result.GetElement(3);
        }

        /// <summary>
        /// Reads a a short from the buffer. You need to be sure that you have enough in the buffer, this won't check bounds.
        /// Increments index by 2
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe short ReadShort(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            byte b1 = buffer[index + 0];
            byte b2 = buffer[index + 1];

            index += 2;

            return (short)(((byte)b1 << 8) | (byte)b2);
        }


        /// <summary>
        /// Reads a a short from the buffer. You need to be sure that you have enough in the buffer, this won't check bounds.
        /// Increments index by 2
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe byte ReadByte(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            byte b1 = buffer[index + 0];

            index += 1;

            return b1;
        }

        /// <summary>
        /// Reads a character from the buffer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char ReadChar(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            return (char)buffer[index++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteUInt(this in Span<byte> buffer, ref int index, uint value)
        {
            uint* bytes = stackalloc uint[4];
            bytes[0] = value;
            bytes[1] = value;
            bytes[2] = value;
            bytes[3] = value;
            uint* shiftBytes = stackalloc uint[4];
            shiftBytes[0] = 24;
            shiftBytes[1] = 16;
            shiftBytes[2] = 8;
            shiftBytes[3] = 0;
            int* andBytes = stackalloc int[4];
            andBytes[0] = 0xFF;
            andBytes[1] = 0xFF;
            andBytes[2] = 0xFF;
            andBytes[3] = 0xFF;

            var vector = Avx.LoadVector128(bytes);
            var int32Var = vector.AsInt32();
            var shiftVector = Avx.LoadVector128(shiftBytes);
            var result = Avx2.ShiftRightLogicalVariable(int32Var, shiftVector);
            var andResult = Avx.And(result, Avx.LoadVector128(andBytes));

            buffer[index + 0] = (byte) andResult.GetElement(0);
            buffer[index + 1] = (byte)andResult.GetElement(1);
            buffer[index + 2] = (byte)andResult.GetElement(2);
            buffer[index + 3] = (byte)andResult.GetElement(3);

            index += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteInt(this in Span<byte> buffer, ref int index, int value)
        {
            int* bytes = stackalloc int[4];
            bytes[0] = value;
            bytes[1] = value;
            bytes[2] = value;
            bytes[3] = value;
            uint* shiftBytes = stackalloc uint[4];
            shiftBytes[0] = 24;
            shiftBytes[1] = 16;
            shiftBytes[2] = 8;
            shiftBytes[3] = 0;
            int* andBytes = stackalloc int[4];
            andBytes[0] = 0xFF;
            andBytes[1] = 0xFF;
            andBytes[2] = 0xFF;
            andBytes[3] = 0xFF;

            var vector = Avx.LoadVector128(bytes);
            var int32Var = vector.AsInt32();
            var shiftVector = Avx.LoadVector128(shiftBytes);
            var result = Avx2.ShiftRightLogicalVariable(int32Var, shiftVector);
            var andResult = Avx.And(result, Avx.LoadVector128(andBytes));

            buffer[index + 0] = (byte)andResult.GetElement(0);
            buffer[index + 1] = (byte)andResult.GetElement(1);
            buffer[index + 2] = (byte)andResult.GetElement(2);
            buffer[index + 3] = (byte)andResult.GetElement(3);

            index += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteShort(this in Span<byte> buffer, ref int index, short value)
        {
            buffer[index + 0] = (byte)(value >> 8 & 0xFF);
            buffer[index + 1] = (byte)(value & 0xFF);

            index += 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteByte(this in Span<byte> buffer, ref int index, byte value)
        {
            buffer[index + 0] = (byte)value;

            index += 1;
        }

    }
}
