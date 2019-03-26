using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace System
{
    public static class SpanExtensions
    {

        private static readonly uint[] shiftBytes = new uint[4]
        {
            24, 16, 8, 0
        };

        private static readonly int[] andBytes = new int[4]
        {
            0xFF, 0xFF, 0xFF, 0xFF
        };

        /// <summary>
        /// Reads an unsigned int from the buffer. You need to be sure that you have enough in the buffer, this won't check bounds.
        /// Increments index by 4
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint ReadUInt(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            fixed(byte* bufferPtr = buffer.Slice(index, 4))
            fixed(uint* shiftBytesPtr = new Span<uint>(shiftBytes))
            {
                var vector = Avx.ConvertToVector128Int32(Avx.LoadVector128(bufferPtr));
                var shiftVector = Avx.LoadVector128(shiftBytesPtr);
                var result = Avx2.ShiftLeftLogicalVariable(vector, shiftVector).AsUInt32();

                index += 4;
                
                return result.GetElement(0) | result.GetElement(1) | result.GetElement(2) | result.GetElement(3);
            }
        }

        /// <summary>
        /// Reads an int from the buffer. You need to be sure that you have enough in the buffer, this won't check bounds.
        /// Increments index by 4
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int ReadInt(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            fixed (byte* bufferPtr = buffer.Slice(index, 4))
            fixed (uint* shiftBytesPtr = new Span<uint>(shiftBytes))
            {
                var vector = Avx.ConvertToVector128Int32(Avx.LoadVector128(bufferPtr));
                var shiftVector = Avx.LoadVector128(shiftBytesPtr);
                var result = Avx2.ShiftLeftLogicalVariable(vector, shiftVector);

                index += 4;

                return result.GetElement(0) | result.GetElement(1) | result.GetElement(2) | result.GetElement(3);
            }
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
            fixed(byte* ptr = buffer.Slice(index, 4))
            fixed (uint* shiftBytesPtr = new Span<uint>(shiftBytes))
            fixed (int* andBytesPtr = new Span<int>(andBytes))
            {
                var vector = Avx.LoadVector128(ptr).AsUInt32()
                    .WithElement(0, value)
                    .WithElement(1, value)
                    .WithElement(2, value)
                    .WithElement(3, value);
                var shiftVector = Avx.LoadVector128(shiftBytesPtr);
                var result = Avx2.ShiftRightLogicalVariable(vector.AsInt32(), shiftVector);
                var andResult = Avx.And(result, Avx.LoadVector128(andBytesPtr));

                ptr[index + 0] = (byte)andResult.GetElement(0);
                ptr[index + 1] = (byte)andResult.GetElement(1);
                ptr[index + 2] = (byte)andResult.GetElement(2);
                ptr[index + 3] = (byte)andResult.GetElement(3);

                index += 4;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteInt(this in Span<byte> buffer, ref int index, int value)
        {
            fixed (byte* ptr = buffer.Slice(index, 4))
            fixed (uint* shiftBytesPtr = new Span<uint>(shiftBytes))
            fixed (int* andBytesPtr = new Span<int>(andBytes))
            {
                var vector = Avx.LoadVector128(ptr).AsInt32()
                    .WithElement(0, value)
                    .WithElement(1, value)
                    .WithElement(2, value)
                    .WithElement(3, value);
                var shiftVector = Avx.LoadVector128(shiftBytesPtr);
                var result = Avx2.ShiftRightLogicalVariable(vector.AsInt32(), shiftVector);
                var andResult = Avx.And(result, Avx.LoadVector128(andBytesPtr));

                buffer[index + 0] = (byte)andResult.GetElement(0);
                buffer[index + 1] = (byte)andResult.GetElement(1);
                buffer[index + 2] = (byte)andResult.GetElement(2);
                buffer[index + 3] = (byte)andResult.GetElement(3);

                index += 4;
            }
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
