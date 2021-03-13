using DanilovSoft.vRPC.Source;
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
    internal sealed class JRequest<TResult> : IJRequest, IResponseAwaiter
    {
        private readonly TaskCompletionSource<TResult?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RequestMethodMeta Method { get; }
        public int Id { get; set; }
        public Task<TResult?> Task => _tcs.Task;
        public object?[]? Args { get; private set; }
        public bool IsNotification => false;
        private ReusableRequestState _state = new(ReusableRequestStateEnum.ReadyToSend);

        public JRequest(RequestMethodMeta method, object?[] args)
        {
            Debug.Assert(!method.IsNotificationRequest);

            Method = method;
            Args = args;
        }

        /// <summary>
        /// Сериализация пользовательских данных может спровоцировать исключение 
        /// <exception cref="VRpcSerializationException"/> которое будет перенаправлено ожидающему потоку.
        /// </summary>
        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer)
        {
            Debug.Assert(Args != null);

            var args = Args;
            Args = null; // Освободить память.

            if (JsonRpcSerializer.TrySerializeRequest(Method.FullName, args, Id, out buffer, out var exception))
            {
                return true;
            }
            else
            {
                InnerTrySetErrorResponse(exception);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TrySetErrorResponse(Exception exception)
        {
            InnerTrySetErrorResponse(exception);
        }

        public void TrySetJResponse(ref Utf8JsonReader reader)
        {
            if (typeof(TResult) != typeof(VoidStruct))
            // Поток ожидает некий объект как результат.
            {
                TResult? result;
                try
                {
                    // Шаблонный сериализатор экономит на упаковке.
                    result = JsonSerializer.Deserialize<TResult>(ref reader);
                }
                catch (JsonException deserializationException)
                {
                    // Сообщить ожидающему потоку что произошла ошибка при разборе ответа для него.
                    InnerTrySetErrorResponse(new VRpcProtocolErrorException(
                        $"Ошибка десериализации ответа на запрос \"{Method.FullName}\".", deserializationException));

                    return;
                }
                _tcs.TrySetResult(result!);
            }
            else
            // void.
            {
                _tcs.TrySetResult(default!);
            }
        }

        public void CompleteSend(VRpcException exception)
        {
            // Игнорируем.
        }

        public void CompleteSend()
        {
            // Игнорируем.
        }

        public bool TryBeginSend()
        {
            // Отправляющий поток пытается атомарно забрать объект.
            var prevState = _state.TrySetSending();
            return prevState == ReusableRequestStateEnum.ReadyToSend;
        }

        private void InnerTrySetErrorResponse(Exception exception)
        {
            // Предотвратит бесмысленный TryBeginSend.
            _state.SetErrorResponse();

            _tcs.TrySetException(exception);
        }

        void IResponseAwaiter.TrySetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new InvalidOperationException();
        }
    }
}
