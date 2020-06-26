using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC
{
    internal interface IResponseAwaiter
    {
        void TrySetException(Exception exception);
        void SetResponse(in HeaderDto header, ReadOnlyMemory<byte> payload);
    }

    /// <summary>
    /// Атомарный <see langword="await"/>'ер. Связывает результат с запросом.
    /// </summary>
    [DebuggerDisplay(@"\{Request = {Request.ActionFullName}\}")]
    internal sealed class ResponseAwaiter<TResult> : IResponseAwaiter
    {
        private readonly TaskCompletionSource<TResult> _tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        public RequestMethodMeta Request { get; }
        public Task<TResult> Task => _tcs.Task;

#if DEBUG
        private object? ValueForDebugDisplay
        {
            get
            {
                if (_tcs.Task.IsCompleted)
                {
                    return _tcs.Task.Exception ?? _tcs.Task.Result as object;
                }
                else
                {
                    return "Pending...";
                }
            }
        }
#endif

        // ctor.
        public ResponseAwaiter(RequestMethodMeta requestToSend)
        {
            Request = requestToSend;
        }

        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        public void TrySetException(Exception exception)
        {
            _tcs.TrySetException(exception);
        }

        /// <summary>
        /// Передает результат ожидающему потоку.
        /// </summary>
        private void TrySetResult([AllowNull] TResult result)
        {
            _tcs.TrySetResult(result!);
        }

        public void DeserializeResponse(ReadOnlyMemory<byte> payload, string? contentEncoding)
        {
            TResult result = contentEncoding switch
            {
                KnownEncoding.ProtobufEncoding => ExtensionMethods.DeserializeProtoBuf<TResult>(payload),
                _ => ExtensionMethods.DeserializeJson<TResult>(payload.Span), // Сериализатор по умолчанию.
            };
            TrySetResult(result);
        }

        public void TrySetDefaultResult()
        {
            TrySetResult(default);
        }

//        private void WakeContinuation()
//        {
//            // Результат уже установлен. Можно разрешить fast-path.
//            _isCompleted = true;

//            // Атомарно записать заглушку или вызвать оригинальный continuation.
//            Action? continuation = Interlocked.CompareExchange(ref _continuationAtomic, GlobalVars.SentinelAction, null);
//            if (continuation != null)
//            {
//                // Нельзя делать продолжение текущим потоком т.к. это затормозит/остановит диспетчер 
//                // или произойдет побег специального потока диспетчера.
//#if NETSTANDARD2_0 || NET472
//                ThreadPool.UnsafeQueueUserWorkItem(CallContinuation, continuation);
//#else
//                ThreadPool.UnsafeQueueUserWorkItem(CallContinuation, continuation, preferLocal: false); // Через глобальную очередь.
//#endif
//            }
//        }

//#if NETSTANDARD2_0 || NET472
//        //[DebuggerStepThrough]
//        private static void CallContinuation(object? state)
//        {
//            var action = state as Action;
//            Debug.Assert(action != null);
//            CallContinuation(argState: action);
//        }
//#endif

//        private static void CallContinuation(Action argState)
//        {
//            argState.Invoke();
//        }

//        public void OnCompleted(Action continuation)
//        {
//            // Атомарно передаем continuation другому потоку.
//            if (Interlocked.CompareExchange(ref _continuationAtomic, continuation, null) == null)
//            {
//                return;
//            }
//            else
//            {
//                // Шанс попасть в этот блок очень маленький.
//                // В переменной _continuationAtomic была другая ссылка, 
//                // это значит что другой поток уже установил результат и его можно забрать.
//                // Но нужно предотвратить углубление стека (stack dive) поэтому продолжение вызывается
//                // другим потоком.

//#if NETSTANDARD2_0 || NET472
//                ThreadPool.UnsafeQueueUserWorkItem(CallContinuation, continuation);
//#else
//                ThreadPool.UnsafeQueueUserWorkItem(CallContinuation, continuation, preferLocal: true); // Через локальную очередь.
//#endif
//            }
//        }

        //private static void QueueUserWorkItem(Action action)
        //{
        //    Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        //}

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        public void SetResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(header.IsRequest == false);

            if (header.StatusCode == StatusCode.Ok)
            // Запрос на удалённой стороне был выполнен успешно.
            {
                #region Передать успешный результат

                if (typeof(TResult) != typeof(VoidStruct))
                // Поток ожидает некий объект как результат.
                {
                    if (!payload.IsEmpty)
                    {
                        // Десериализатор в соответствии с ContentEncoding.
                        //Func<ReadOnlyMemory<byte>, Type, object> deserializer = header.GetDeserializer();
                        try
                        {
                            DeserializeResponse(payload, header.PayloadEncoding);
                        }
                        catch (Exception deserializationException)
                        {
                            // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удалённой стороны.
                            TrySetException(new VRpcProtocolErrorException(
                                $"Ошибка десериализации ответа на запрос \"{Request.ActionFullName}\".", deserializationException));
                        }
                    }
                    else
                    // У ответа отсутствует контент — это равнозначно Null.
                    {
                        if (typeof(TResult).CanBeNull())
                        // Результат запроса поддерживает Null.
                        {
                            TrySetDefaultResult();
                        }
                        else
                        // Результатом этого запроса не может быть Null.
                        {
                            // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удаленной стороны.
                            TrySetException(new VRpcProtocolErrorException(
                                $"Ожидался не пустой результат запроса но был получен ответ без результата."));
                        }
                    }
                }
                else
                // void.
                {
                    TrySetDefaultResult();
                }
                #endregion
            }
            else
            // Сервер прислал код ошибки.
            {
                // Телом ответа в этом случае будет строка.
                string errorMessage = payload.ReadAsString();

                // Сообщить ожидающему потоку что удаленная сторона вернула ошибку в результате выполнения запроса.
                TrySetException(new VRpcBadRequestException(errorMessage, header.StatusCode));
            }
        }
    }
}
