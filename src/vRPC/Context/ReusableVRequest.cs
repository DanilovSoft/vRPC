using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableVRequest : IVRequest, IResponseAwaiter
    {
        private readonly RpcManagedConnection _context;
        public RequestMethodMeta? Method { get; private set; }
        public object[]? Args { get; private set; }
        public int Id { get; set; }
        public bool IsNotification => false;
        private object? _tcs;
        private Action<ReusableVRequest, object?>? _setResponse;
        private Action<ReusableVRequest, Exception>? _setErrorResponse;

        // 0 - сброшен. 1 - готов к отправке. 2 - в процессе отправки. 3 - отправлен, 4 - завершен с ошибкой, 5 - получен ответ.
        /// <summary>
        /// Решает какой поток будет выполнять Reset.
        /// </summary>
        private ReusableRequestState _state;
        //private int _state = 0;

        public ReusableVRequest(RpcManagedConnection context)
        {
            _context = context;
        }

        /// <summary>Переводит в состояние 0.</summary>
        private void Reset(bool allowReuse)
        {
            Debug.Assert(_state.State
                is ReusableRequestStateEnum.GotResponse
                or ReusableRequestStateEnum.GotErrorResponse);

            Id = 0;
            Method = null;
            Args = null;
            _tcs = null;
            _setResponse = null;
            _setErrorResponse = null;

            if (allowReuse)
            {
                _state.Reset();
                _context.ReleaseReusable(this);
            }
        }

        /// <summary>Переводит в состояние 1.</summary>
        internal Task<TResult> Initialize<TResult>(RequestMethodMeta method, object[] args)
        {
            Debug.Assert(Method == null);
            Debug.Assert(Args == null);
            Debug.Assert(_tcs == null);
            Debug.Assert(_setResponse == null);
            Debug.Assert(_setErrorResponse == null);

            Method = method;
            Args = args;

            var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            _tcs = tcs;
            _setResponse = SetResponse<TResult>;
            _setErrorResponse = SetErrorResponse<TResult>;
            _state.Ready();

            return tcs.Task;
        }

        /// <summary>Переводит в состояние 2.</summary>
        public bool TryBeginSend()
        {
            // Отправляющий поток пытается атомарно забрать объект.
            var prevState = _state.TrySetSending();
            return prevState == ReusableRequestStateEnum.ReadyToSend;
        }

        /// <summary>Переводит в состояние 3.</summary>
        public void CompleteSend()
        {
            var prevState = _state.TrySetSended();

            // Во время отправки уже мог прийти ответ и поменять статус или могла произойти ошибка.
            if (prevState is ReusableRequestStateEnum.GotResponse or ReusableRequestStateEnum.GotErrorResponse)
            // Ответственность на сбросе на нас.
            {
                bool resetSatate = prevState == ReusableRequestStateEnum.GotResponse;
                Reset(resetSatate);
            }
        }

        /// <summary>Переводит в состояние 4.</summary>
        private void SetResponse(object result)
        {
            Debug.Assert(_setResponse != null);

            _setResponse(this, result);
        }

        /// <summary>Переводит в состояние 4.</summary>
        private static void SetResponse<TResult>(ReusableVRequest self, object? result)
        {
            var tcs = self._tcs as TaskCompletionSource<TResult>;
            Debug.Assert(tcs != null);

            var prevState = self._state.SetGotResponse();

            // В редких случаях мы могли обогнать отправляющий поток,
            // в этом случае сброс сделает отправляющий поток.
            if (prevState != ReusableRequestStateEnum.Sending)
            {
                // Нужно сделать сброс перед установкой результата.
                self.Reset(allowReuse: true);
            }

            tcs.TrySetResult((TResult)result!);
        }

        /// <summary>Переводит в состояние 5.</summary>
        public void CompleteSend(VRpcException exception)
        {
            _state.SetErrorResponse();

            // На этот раз просто что-бы освободить память.
            Reset(allowReuse: false);
        }

        /// <summary>
        /// При получении ответа с ошибкой или при обрыве соединения что технически считается результатом на запрос.
        /// </summary>
        /// <remarks>Переводит в состояние 5.</remarks>
        private static void SetErrorResponse<TResult>(ReusableVRequest self, Exception exception)
        {
            var tcs = self._tcs as TaskCompletionSource<TResult>;
            Debug.Assert(tcs != null);

            // Атомарно узнаём в каком состоянии было сообщение.
            var prevState = self._state.SetErrorResponse();

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
                self.Reset(allowReuse: true);
            }

            tcs.TrySetException(exception);
        }

        public void SetErrorResponse(VRpcException vException)
        {
            SetErrorResponse(exception: vException);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetErrorResponse(Exception exception)
        {
            Debug.Assert(_setErrorResponse != null);

            _setErrorResponse(this, exception);
        }

        void IResponseAwaiter.SetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new NotSupportedException();
        }

        public void SetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(Method != null);

            var result = Method.DeserializeVResponse(in header, payload, out VRpcException? vException);

            if (vException == null)
            {
                SetResponse(result);
            }
            else
            {
                SetErrorResponse(vException);
            }
        }

        public bool TrySerialize(out ArrayBufferWriter<byte> buffer, out int headerSize)
        {
            Debug.Assert(Args != null);
            Debug.Assert(Method != null);

            var args = Args;
            Args = null;

            if (Method.TrySerializeVRequest(args, Id, out headerSize, out buffer, out var vException))
            {
                return true;
            }
            else
            {
                SetErrorResponse(vException);
                return false;
            }
        }
    }
}
