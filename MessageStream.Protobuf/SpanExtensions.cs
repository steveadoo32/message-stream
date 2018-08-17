using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace MessageStream.ProtoBuf
{
    public static class SpanExtensions
    {

        /// <summary>
        /// Reads a string from the buffer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ReadString(this in ReadOnlySpan<byte> buffer, ref int index)
        {
            var sb = new StringBuilder();
            char c = '0';
            while((c = buffer.ReadChar(ref index)) != 0)
            {
                sb.Append(c);
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// Reads a string from the buffer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteString(this in Span<byte> buffer, ref int index, string str)
        {
            for(int i = 0; i < str.Length; i++)
            {
                buffer[index++] = (byte) str[i];
            }
            buffer[index++] = 0;
        }

    }
}
