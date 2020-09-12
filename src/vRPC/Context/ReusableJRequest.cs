using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableJRequest : IJRequest, IRequest
    {
        public RequestMethodMeta? Method { get; private set; }
        public object[]? Args { get; private set; }
        public int Id { get; set; }
        private object? _tcs;
        private Action<object?, object>? _setResult;
        private Action<Exception, object>? _setException;

        public ReusableJRequest()
        {

        }

        internal void Reset()
        {
            Method = null;
            Args = null;
            _tcs = null;
            _setResult = null;
            _setException = null;
        }

        internal Task<TResult> Initialize<TResult>(RequestMethodMeta method, object[] args)
        {
            Debug.Assert(Method == null);
            Debug.Assert(Args == null);
            Debug.Assert(_tcs == null);
            Debug.Assert(_setResult == null);
            Debug.Assert(_setException == null);

            Method = method;
            Args = args;

            var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            _tcs = tcs;
            _setResult = SetResult<TResult>;
            _setException = SetException<TResult>;

            return tcs.Task;
        }

        private static void SetResult<TResult>(object? result, object state)
        {
            var tcs = (TaskCompletionSource<TResult>)state;
            tcs.TrySetResult((TResult)result!);
        }

        private static void SetException<TResult>(Exception exception, object state)
        {
            var tcs = (TaskCompletionSource<TResult>)state;
            tcs.TrySetException(exception);
        }

        public void SetException(VRpcException vException)
        {
            SetException(exception: vException);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetException(Exception exception)
        {
            Debug.Assert(_tcs != null);
            Debug.Assert(_setException != null);

            _setException(exception, _tcs);
        }

        public void SetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(Method != null);
            Debug.Assert(_tcs != null);
            Debug.Assert(_setResult != null);

            if (Method.ReturnType != typeof(VoidStruct))
            // Поток ожидает некий объект как результат.
            {
                object result;
                try
                {
                    // Шаблонный сериализатор экономит на упаковке.
                    result = JsonSerializer.Deserialize(ref reader, Method.ReturnType);
                }
                catch (JsonException deserializationException)
                {
                    // Сообщить ожидающему потоку что произошла ошибка при разборе ответа для него.
                    SetException(new VRpcProtocolErrorException(
                        $"Ошибка десериализации ответа на запрос \"{Method.FullName}\".", deserializationException));

                    return;
                }
                _setResult(result, _tcs);
            }
            else
            // void.
            {
                _setResult(default(VoidStruct), _tcs);
            }
        }

        public void SetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new NotSupportedException();
        }

        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer)
        {
            Debug.Assert(Args != null);
            Debug.Assert(Method != null);

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
                SetException(vex);
                return false;
            }
            finally
            {
                Args = null; // Освободить память.
                toDispose?.Dispose();
            }
        }
    }
}
