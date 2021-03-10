﻿using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableJRequest : IJRequest, IResponseAwaiter
    {
        private readonly ArrayBufferWriter<byte> _reusableBuffer = new(initialize: false);
        private readonly RpcManagedConnection _context;
        public RequestMethodMeta? Method { get; private set; }
        public object?[]? Args { get; private set; }
        public int Id { get; set; }
        public bool IsNotification => false;
        private object? _tcs;
        private Action<object?>? _trySetResponse;
        private Func<Exception, bool>? _trySetErrorResponse;
        // Решает какой поток будет выполнять Reset.
        private ReusableRequestState _state = new(ReusableRequestStateEnum.Reset);

        public ReusableJRequest(RpcManagedConnection context)
        {
            _context = context;
        }

        private void Reset()
        {
            Debug.Assert(!_reusableBuffer.IsInitialized);
            Debug.Assert(_state.State
                is ReusableRequestStateEnum.GotResponse
                or ReusableRequestStateEnum.GotErrorResponse);

            Id = 0;
            Method = null;
            Args = null;
            _tcs = null;
            _trySetResponse = null;
            _trySetErrorResponse = null;

            _state.Reset();
            _context.AtomicReleaseReusableJ(this);
        }

        internal Task<TResult> Initialize<TResult>(RequestMethodMeta method, object?[] args)
        {
            Debug.Assert(Method == null);
            Debug.Assert(Args == null);
            Debug.Assert(_tcs == null);
            Debug.Assert(_trySetResponse == null);
            Debug.Assert(_trySetErrorResponse == null);
            Debug.Assert(_reusableBuffer != null);
            Debug.Assert(!_reusableBuffer.IsInitialized);

            _reusableBuffer.Initialize();
            Method = method;
            Args = args;

            var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            _tcs = tcs;
            _trySetErrorResponse = tcs.TrySetException;
            _trySetResponse = TrySetResponse<TResult>;
            _state.SetReady();

            return tcs.Task;
        }

        public void TrySetErrorResponse(Exception vException)
        {
            var trySetErrorResponse = _trySetErrorResponse;
            Debug.Assert(trySetErrorResponse != null);

            // Атомарно узнаём в каком состоянии было сообщение.
            var prevState = _state.SetErrorResponse();

            Debug.Assert(prevState
                is ReusableRequestStateEnum.ReadyToSend
                or ReusableRequestStateEnum.Sending
                or ReusableRequestStateEnum.Sended);

            // upd: Может быть случай когда читающий поток завершается
            // ошибкой ещё до отправки сообщения отправляющим потоком.
            if (prevState != ReusableRequestStateEnum.Sending)
            {
                // Нужно сделать сброс перед установкой результата
                // иначе другой поток может начать переиспользование это-го класса.
                Reset();
            }

            trySetErrorResponse(vException);
        }

        public void TrySetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(Method != null);

            if (Method.ReturnType != typeof(VoidStruct))
            // Поток ожидает некий объект как результат.
            {
                object? result;
                try
                {
                    // Шаблонный сериализатор экономит на упаковке.
                    result = JsonSerializer.Deserialize(ref reader, Method.ReturnType);
                }
                catch (JsonException deserializationException)
                {
                    // Сообщить ожидающему потоку что произошла ошибка при разборе ответа для него.
                    TrySetErrorResponse(new VRpcProtocolErrorException(
                        $"Ошибка десериализации ответа на запрос \"{Method.FullName}\".", deserializationException));

                    return;
                }
                TrySetResponse(result);
            }
            else
            // void.
            {
                TrySetResponse(default(VoidStruct));
            }
        }

        // TODO не диспозить с наружи - race condition!
        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? reusableBuffer)
        {
            Debug.Assert(Args != null);
            Debug.Assert(Method != null);

            var args = Args;
            Args = null;

            if (JsonRpcSerializer.TrySerializeRequest(Method.FullName, args, Id, _reusableBuffer, out var exception))
            {
                reusableBuffer = _reusableBuffer;
                return true;
            }
            else
            {
                _reusableBuffer.Reset();
                TrySetErrorResponse(exception);
                reusableBuffer = null;
                return false;
            }
        }

        public void CompleteSend()
        {
            var prevState = _state.TrySetSended();

            // Во время отправки уже мог прийти ответ и поменять статус или могла произойти ошибка.
            if (prevState is ReusableRequestStateEnum.GotResponse or ReusableRequestStateEnum.GotErrorResponse)
            // Ответственность на сбросе на нас.
            {
                Reset();
            }
        }

        public void CompleteSend(VRpcException exception)
        {
            _state.SetErrorResponse();
            Reset();
        }

        public bool TryBeginSend()
        {
            // Отправляющий поток пытается атомарно забрать объект.
            var prevState = _state.TrySetSending();
            return prevState == ReusableRequestStateEnum.ReadyToSend;
        }

        void IResponseAwaiter.TrySetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new NotSupportedException();
        }

        private void TrySetResponse<TResult>(object? result)
        {
            var tcs = _tcs as TaskCompletionSource<TResult>;
            Debug.Assert(tcs != null);

            // Атомарно узнаём в каком состоянии было сообщение.
            var prevState = _state.SetGotResponse();

            // В редких случаях мы могли обогнать отправляющий поток,
            // в этом случае сброс сделает отправляющий поток.
            if (prevState != ReusableRequestStateEnum.Sending)
            {
                // Нужно сделать сброс перед установкой результата.
                Reset();
            }
            tcs.TrySetResult((TResult)result!);
        }

        private void TrySetResponse(object? result)
        {
            Debug.Assert(_trySetResponse != null);

            _trySetResponse(result);
        }
    }
}
