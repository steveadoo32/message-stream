using System.Runtime.CompilerServices;

namespace System
{
    public static class SpanExtensions
    {

        /// <summary>
        /// Reads an unsigned int from the buffer. You need to be sure that you have enough in the buffer, this won't check bounds.
        /// Increments index by 4
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            int b1 = buffer[index + 0];
            int b2 = buffer[index + 1];
            int b3 = buffer[index + 2];
            int b4 = buffer[index + 3];

            index += 4;

            return ((uint)(byte)b1 << 24) | ((uint)(byte)b2 << 16) | ((uint)(byte)b3 << 8) | (byte)b4;
        }

        /// <summary>
        /// Reads an int from the buffer. You need to be sure that you have enough in the buffer, this won't check bounds.
        /// Increments index by 4
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            int b1 = buffer[index + 0];
            int b2 = buffer[index + 1];
            int b3 = buffer[index + 2];
            int b4 = buffer[index + 3];

            index += 4;

            return ((byte)b1 << 24) | ((byte)b2 << 16) | ((byte)b3 << 8) | (byte)b4;
        }

        /// <summary>
        /// Reads a a short from the buffer. You need to be sure that you have enough in the buffer, this won't check bounds.
        /// Increments index by 2
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe short ReadShort(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            int b1 = buffer[index + 0];
            int b2 = buffer[index + 1];

            index += 2;

            return (short)(((byte)b1 << 8) | (byte)b2);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt(this in Span<byte> buffer, ref int index, uint value)
        {
            buffer[index + 0] = (byte)(value >> 24 & 0xFF);
            buffer[index + 1] = (byte)(value >> 16 & 0xFF);
            buffer[index + 2] = (byte)(value >> 8 & 0xFF);
            buffer[index + 3] = (byte)(value & 0xFF);

            index += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt(this in Span<byte> buffer, ref int index, int value)
        {
            buffer[index + 0] = (byte)(value >> 24 & 0xFF);
            buffer[index + 1] = (byte)(value >> 16 & 0xFF);
            buffer[index + 2] = (byte)(value >> 8 & 0xFF);
            buffer[index + 3] = (byte)(value & 0xFF);

            index += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteShort(this in Span<byte> buffer, ref int index, short value)
        {
            buffer[index + 0] = (byte)(value >> 8 & 0xFF);
            buffer[index + 1] = (byte)(value & 0xFF);

            index += 2;
        }

    }
}
