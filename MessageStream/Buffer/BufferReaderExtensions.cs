using System.Runtime.CompilerServices;

namespace System.Buffers
{
    public static class BufferReaderExtensions
    {
        
        /// <summary>
        /// Reads from the BufferReader into the buffer. You need to be sure that you have enough in the buffer, this won't check bounds.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void ReadBytes(ref this BufferReader bufferReader, ref SequencePosition position, Span<byte> buffer)
        {
            position = bufferReader.Position;
            
            // Check if we need to read across different spans
            if ((bufferReader.CurrentSegment.Length - bufferReader.CurrentSegmentIndex) < buffer.Length)
            {
                int bufferIndex = 0;
                while(bufferIndex < buffer.Length)
                {
                    // Pull either the rest of the segment, or up to the remaning space in the buffer.
                    int segmentSize = Math.Min(buffer.Length - bufferIndex, bufferReader.CurrentSegment.Length - bufferReader.CurrentSegmentIndex);
                    var segment = bufferReader.CurrentSegment.Slice(bufferReader.CurrentSegmentIndex, segmentSize);
                    segment.CopyTo(buffer.Slice(bufferIndex, segmentSize));
                    bufferIndex += segmentSize;
                    bufferReader.Advance(segmentSize);
                }
            }
            else
            {
                // We save a small amount of performance reading directly from the array.
                bufferReader.CurrentSegment.Slice(bufferReader.CurrentSegmentIndex, buffer.Length).CopyTo(buffer);

                bufferReader.Advance(buffer.Length);
            }

            position = bufferReader.Position;
        }

    }
}
