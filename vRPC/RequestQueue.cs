using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace vRPC
{
    /// <summary>
    /// Потокобезопасная очередь запросов к удалённой стороне ожидающих ответы.
    /// Имеет лимит в 65'535 запросов.
    /// </summary>
    [DebuggerDisplay(@"\{Count = {_dict.Count}\}")]
    internal sealed class RequestQueue
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly Dictionary<ushort, RequestAwaiter> _dict = new Dictionary<ushort, RequestAwaiter>();
        private readonly SpinWait _spinWait = new SpinWait();
        private Exception _disconnectException;
        private int _reqIdSeq;

        /// <summary>
        /// Потокобезопасная очередь запросов к удалённой стороне ожидающих ответы.
        /// </summary>
        public RequestQueue()
        {
            
        }

        /// <summary>
        /// Потокобезопасно добавляет запрос в словарь запросов и возвращает уникальный идентификатор.
        /// </summary>
        /// <exception cref="Exception">Происходит если уже происходил обрыв соединения.</exception>
        public RequestAwaiter AddRequest(Type resultType, Message requestToSend, out ushort uid)
        {
            var tcs = new RequestAwaiter(resultType, requestToSend);

            do
            {
                lock (_dict)
                {
                    if (_disconnectException == null)
                    {
                        if (_dict.Count < ushort.MaxValue)
                        // Словарь еще не переполнен — можно найти свободный ключ.
                        {
                            do
                            {
                                uid = IncrementSeq();
                            } while (!_dict.TryAdd(uid, tcs));
                            return tcs;
                        }
                    }
                    else
                        throw _disconnectException;
                }

                // Словарь переполнен — подождать и повторить попытку.
                _spinWait.SpinOnce();

            } while (true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort IncrementSeq()
        {
            ushort uid = unchecked((ushort)Interlocked.Increment(ref _reqIdSeq));
            return uid;
        }

        /// <summary>
        /// Потокобезопасно удаляет запрос из словаря.
        /// </summary>
        public bool TryRemove(ushort uid, out RequestAwaiter tcs)
        {
            lock (_dict)
            {
                if (_dict.Remove(uid, out tcs))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Потокобезопасно распространяет исключение всем ожидающим потокам. Дальнейшее создание запросов будет генерировать это исключение.
        /// </summary>
        internal void PropagateExceptionAndLockup(Exception exception)
        {
            lock (_dict)
            {
                if (_disconnectException == null)
                {
                    _disconnectException = exception;
                    if (_dict.Count > 0)
                    {
                        foreach (RequestAwaiter tcs in _dict.Values)
                            tcs.TrySetException(exception);
                        
                        _dict.Clear();
                    }
                }
            }
        }
    }
}
