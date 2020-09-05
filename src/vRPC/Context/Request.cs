using DanilovSoft.vRPC.Context;
using DanilovSoft.vRPC.Source;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal interface IRequest : IResponseAwaiter
    {
        RequestMethodMeta Method { get; }
        object[] Args { get; }
        int Id { get; }
    }

    [DebuggerDisplay(@"\{Request = {Method.FullName}\}")]
    internal sealed class Request<TResult> : IMessageToSend, IRequest
    {
        private readonly TaskCompletionSource<TResult> _tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        public RequestMethodMeta Method { get; }
        public object[] Args { get; }
        internal Task<TResult> Task => _tcs.Task;

#if DEBUG
        [SuppressMessage("CodeQuality", "IDE0051:Удалите неиспользуемые закрытые члены", Justification = "Для отладчика")]
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
        public ManagedConnection Context { get; }
        public int Id { get; set; }

        // ctor
        internal Request(ManagedConnection context, RequestMethodMeta method, object[] args)
        {
            Context = context;
            Method = method;
            Args = args;
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
                        try
                        {
                            DeserializeResponse(payload, header.PayloadEncoding);
                        }
                        catch (Exception deserializationException)
                        {
                            // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удалённой стороны.
                            TrySetException(new VRpcProtocolErrorException(
                                $"Ошибка десериализации ответа на запрос \"{Method.FullName}\".", deserializationException));
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
                            TrySetException(new VRpcProtocolErrorException(
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

                // Сообщить ожидающему потоку что удаленная сторона вернула ошибку в результате выполнения запроса.
                TrySetException(new VRpcBadRequestException(errorMessage, header.StatusCode));
            }
        }

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        public void TrySetResponse(ref Utf8JsonReader reader)
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
                    TrySetException(new VRpcProtocolErrorException(
                        $"Ошибка десериализации ответа на запрос \"{Method.FullName}\".", deserializationException));

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
