using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Потокобезопасная очередь запросов к удалённой стороне ожидающих ответы.
    /// Имеет лимит в 65'535 запросов.
    /// </summary>
    [DebuggerDisplay(@"\{Count = {_dict.Count}\}")]
    internal sealed class RequestQueue
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly Dictionary<int, RequestAwaiter> _dict = new Dictionary<int, RequestAwaiter>();
        /// <summary>
        /// Не является потокобезопасным.
        /// </summary>
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
        public RequestAwaiter AddRequest(RequestToSend requestToSend, out int uid)
        {
            var tcs = new RequestAwaiter(requestToSend);

            do
            {
                lock (_dict)
                {
                    if (_disconnectException == null)
                    {
                        if (_dict.Count < int.MaxValue)
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

                    // Словарь переполнен — подождать и повторить попытку.
                    _spinWait.SpinOnce();
                }

            } while (true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int IncrementSeq()
        {
            int uid = Interlocked.Increment(ref _reqIdSeq);
            return uid;
        }

        /// <summary>
        /// Потокобезопасно удаляет запрос из словаря.
        /// </summary>
        public bool TryRemove(int uid, out RequestAwaiter tcs)
        {
            lock (_dict)
            {
                if (_dict.Remove(uid, out tcs))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Потокобезопасно распространяет исключение всем ожидающим потокам. Дальнейшее создание запросов будет провоцировать это исключение.
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
