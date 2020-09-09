using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC
{
    internal interface IResponseAwaiter
    {
        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        void SetException(VRpcException exception);
        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        void SetException(Exception exception);
        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        void SetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload);
        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        void SetJResponse(ref Utf8JsonReader reader);
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
        public void SetException(VRpcException rpcException)
        {
            SetException(exception: rpcException);
        }

        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetException(Exception exception)
        {
            _tcs.TrySetException(exception);
        }

        /// <summary>
        /// Передает результат ожидающему потоку.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
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

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        public void SetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
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
                        try
                        {
                            DeserializeResponse(payload, header.PayloadEncoding);
                        }
                        catch (Exception deserializationException)
                        {
                            // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удалённой стороны.
                            SetException(new VRpcProtocolErrorException(
                                $"Ошибка десериализации ответа на запрос \"{Request.FullName}\".", deserializationException));
                        }
                    }
                    else
                    // У ответа отсутствует контент — это равнозначно Null.
                    {
                        if (typeof(TResult).CanBeNull())
                        // Результат запроса поддерживает Null.
                        {
                            TrySetResult(default);
                        }
                        else
                        // Результатом этого запроса не может быть Null.
                        {
                            // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удалённой стороны.
                            SetException(new VRpcProtocolErrorException(
                                $"Ожидался не пустой результат запроса но был получен ответ без результата."));
                        }
                    }
                }
                else
                // void.
                {
                    TrySetResult(default);
                }
                #endregion
            }
            else
            // Сервер прислал код ошибки.
            {
                // Телом ответа в этом случае будет строка.
                string errorMessage = payload.ReadAsString();

                var rpcException = ExceptionHelper.ToException(header.StatusCode, errorMessage);

                // Сообщить ожидающему потоку что удаленная сторона вернула ошибку в результате выполнения запроса.
                SetException(rpcException);
            }
        }

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        public void SetJResponse(ref Utf8JsonReader reader)
        {
            if (typeof(TResult) != typeof(VoidStruct))
            // Поток ожидает некий объект как результат.
            {
                TResult result;
                try
                {
                    // Шаблонный сериализатор экономит на упаковке.
                    result = JsonSerializer.Deserialize<TResult>(ref reader);
                }
                catch (JsonException deserializationException)
                {
                    // Сообщить ожидающему потоку что произошла ошибка при разборе ответа для него.
                    SetException(new VRpcProtocolErrorException(
                        $"Ошибка десериализации ответа на запрос \"{Request.FullName}\".", deserializationException));

                    return;
                }
                TrySetResult(result);
            }
            else
            // void.
            {
                TrySetResult(default);
            }
        }
    }
}
