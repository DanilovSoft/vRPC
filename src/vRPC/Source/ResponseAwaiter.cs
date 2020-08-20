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
        void TrySetException(Exception exception);
        void DeserializeAndSetResponse(in HeaderDto header, ReadOnlyMemory<byte> payload);
        void DeserializeJsonRpcResponse(ref Utf8JsonReader reader);
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

        public void TrySetDefaultResult()
        {
            TrySetResult(default);
        }

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        public void DeserializeAndSetResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
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
                            TrySetException(new VRpcProtocolErrorException(
                                $"Ошибка десериализации ответа на запрос \"{Request.MethodFullName}\".", deserializationException));
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
                            // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удалённой стороны.
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

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        public void DeserializeJsonRpcResponse(ref Utf8JsonReader reader)
        {
            #region Передать успешный результат

            if (typeof(TResult) != typeof(VoidStruct))
            // Поток ожидает некий объект как результат.
            {
                TResult result;
                try
                {
                    result = JsonSerializer.Deserialize<TResult>(ref reader);
                }
                catch (Exception deserializationException)
                {
                    // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удалённой стороны.
                    TrySetException(new VRpcProtocolErrorException(
                        $"Ошибка десериализации ответа на запрос \"{Request.MethodFullName}\".", deserializationException));

                    return;
                }
                TrySetResult(result);
            }
            else
            // void.
            {
                TrySetDefaultResult();
            }
            #endregion
        }
    }
}
