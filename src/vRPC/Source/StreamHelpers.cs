using System;
using System.Collections.Generic;
using System.Text;
using DanilovSoft.vRPC;

namespace System.IO
{
    /// <summary>Provides methods to help in the implementation of Stream-derived types.</summary>
    internal static partial class StreamHelpers
    {
        /// <summary>Validate the arguments to CopyTo, as would Stream.CopyTo.</summary>
        public static void ValidateCopyToArgs(Stream source, Stream destination, int bufferSize)
        {
            if (destination == null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(destination));
            }

            if (bufferSize <= 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(bufferSize));
            }

            bool sourceCanRead = source.CanRead;
            if (!sourceCanRead && !source.CanWrite)
            {
                ThrowHelper.ThrowObjectDisposedException(null, "StreamClosed");
            }

            bool destinationCanWrite = destination.CanWrite;
            if (!destinationCanWrite && !destination.CanRead)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(destination), "StreamClosed");
            }

            if (!sourceCanRead)
            {
                ThrowHelper.ThrowNotSupportedException("UnreadableStream");
            }

            if (!destinationCanWrite)
            {
                ThrowHelper.ThrowNotSupportedException("NotSupported_UnwritableStream");
            }
        }
    }
}