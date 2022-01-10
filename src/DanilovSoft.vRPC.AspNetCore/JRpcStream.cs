using DanilovSoft.WebSockets;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC.AspNetCore
{
    internal sealed class JRpcStream : IJrpcStream
    {
        private readonly Stream _stream;

        public JRpcStream(Stream stream, EndPoint localEndPoint, EndPoint remoteEndPoint)
        {
            _stream = stream;
            LocalEndPoint = localEndPoint;
            RemoteEndPoint = remoteEndPoint;
        }

        public Socket? Socket { get; }
        public bool CanRead => true;
        public bool CanWrite => true;
        public EndPoint RemoteEndPoint { get; }
        public EndPoint LocalEndPoint { get; }

        public IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new NotImplementedException();
        }

        public void Close()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _stream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public ValueTask<int> ReadAsync(Memory<byte> memory, CancellationToken cancellationToken)
        {
            return _stream.ReadAsync(memory, cancellationToken);
        }

        public ValueTask<SocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public ValueTask<SocketError> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public void Shutdown(SocketShutdown how)
        {
            throw new NotImplementedException();
        }

        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _stream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            return _stream.WriteAsync(buffer);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            return _stream.WriteAsync(buffer, cancellationToken);
        }
    }
}
