using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC.Context
{
    internal interface IJRequest
    {
        RequestMethodMeta Method { get; }
        object[] Args { get; }
        int Id { get; set; }
        bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer);
    }

    internal sealed class JRequest<TResult> : IJRequest, IRequest<TResult>
    {
        private readonly TaskCompletionSource<TResult> _tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManagedConnection Context { get; }
        public RequestMethodMeta Method { get; }
        public int Id { get; set; }
        public Task<TResult> Task => _tcs.Task;
        public object[] Args { get; }

        public JRequest(ManagedConnection context, RequestMethodMeta method, object[] args)
        {
            Context = context;
            Method = method;
            Args = args;
        }

        /// <summary>
        /// Сериализация пользовательских данных может спровоцировать исключение 
        /// <exception cref="VRpcSerializationException"/> которое будет перенаправлено ожидающему потоку.
        /// </summary>
        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer)
        {
            buffer = new ArrayBufferWriter<byte>();
            var toDispose = buffer;
            try
            {
                JsonRpcSerializer.SerializeRequest(buffer, Method.FullName, Args, Id);
                toDispose = null; // Предотвратить Dispose.
                return true;
            }
            catch (Exception ex)
            {
                var vex = new VRpcSerializationException("Ошибка при сериализации пользовательских данных.", ex);
                TrySetException(vex);
                return false;
            }
            finally
            {
                toDispose?.Dispose();
            }
        }

        public void TrySetException(Exception exception)
        {
            _tcs.TrySetException(exception);
        }

        public void TrySetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }

        public void TrySetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }
    }
}
