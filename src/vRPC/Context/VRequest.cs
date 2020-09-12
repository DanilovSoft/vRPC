using DanilovSoft.vRPC.Context;
using DanilovSoft.vRPC.Source;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace DanilovSoft.vRPC
{
    internal interface IVRequest
    {
        RequestMethodMeta Method { get; }
        object[]? Args { get; }
        bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer, out int headerSize);
    }

    [DebuggerDisplay(@"\{Request = {Method.FullName}\}")]
    internal sealed class VRequest<TResult> : IMessageToSend, IVRequest, IRequest
    {
        private TaskCompletionSource<TResult> _tcs;
        public RequestMethodMeta Method { get; }
        public object[]? Args { get; private set; }
        public Task<TResult> Task => _tcs.Task;

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
        public int Id { get; set; }

        // ctor
        internal VRequest(RequestMethodMeta method, object[] args)
        {
            Debug.Assert(!method.IsNotificationRequest);

            Method = method;
            Args = args;
            _tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
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
            Debug.Assert(_tcs != null);

            _tcs.TrySetException(exception);
        }

        private void SetResult([AllowNull] TResult result)
        {
            Debug.Assert(_tcs != null);

            _tcs.TrySetResult(result!);
        }

        private void DeserializeResponse(ReadOnlyMemory<byte> payload, string? contentEncoding)
        {
            TResult result = contentEncoding switch
            {
                KnownEncoding.ProtobufEncoding => ExtensionMethods.DeserializeProtoBuf<TResult>(payload),
                _ => ExtensionMethods.DeserializeJson<TResult>(payload.Span), // Сериализатор по умолчанию.
            };
            SetResult(result);
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
                                $"Ошибка десериализации ответа на запрос \"{Method.FullName}\".", deserializationException));
                        }
                    }
                    else
                    // У ответа отсутствует контент — это равнозначно Null.
                    {
                        if (typeof(TResult).CanBeNull())
                        // Результат запроса поддерживает Null.
                        {
                            SetResult(default);
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
                    SetResult(default);
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
            Debug.Assert(false, "Сюда не должны попадать");
            throw new InvalidOperationException();
        }

        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer, out int headerSize)
        {
            Debug.Assert(Args != null);

            buffer = new ArrayBufferWriter<byte>();
            var toDispose = buffer;
            try
            {
                Method.SerializeRequest(Args, buffer);

                var header = new HeaderDto(Id, buffer.WrittenCount, contentEncoding: null, Method.FullName);

                // Записать заголовок в конец стрима. Не бросает исключения.
                headerSize = header.SerializeJson(buffer);

                toDispose = null;
                return true;
            }
            catch (Exception ex)
            {
                var vex = new VRpcSerializationException($"Не удалось сериализовать запрос в json.", ex);
                SetException(vex);
                headerSize = -1;
                return false;
            }
            finally
            {
                Args = null; // Освободить память.
                toDispose?.Dispose();
            }
        }

        public TResult GetResult(short token)
        {
            throw new NotImplementedException();
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            throw new NotImplementedException();
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            throw new NotImplementedException();
        }
    }
}
