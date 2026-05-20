using System;
using System.Buffers;
using System.IO;

namespace McpServer.AgentRouter.Tools;

internal static class TextWriterSpanExtensions
{
    private const int StackBufferSize = 256;

    public static void WriteRepeated(this TextWriter writer, char value, int count)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (count <= 0)
        {
            return;
        }

        if (count <= StackBufferSize)
        {
            Span<char> buffer = stackalloc char[count];
            buffer.Fill(value);
            writer.Write(buffer);
            return;
        }

        var rented = ArrayPool<char>.Shared.Rent(count);
        try
        {
            rented.AsSpan(0, count).Fill(value);
            writer.Write(rented, 0, count);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }
}
