using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Потокобезопасная очередь запросов к удалённой стороне ожидающих ответы.
    /// Имеет лимит в 65'535 запросов.
    /// </summary>
    [DebuggerDisplay(@"\{Count = {_dict.Count}\}")]
    internal sealed class PendingRequestDictionary
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly Dictionary<int, ResponseAwaiter> _dict = new Dictionary<int, ResponseAwaiter>();
        /// <summary>
        /// Не является потокобезопасным.
        /// </summary>
        private readonly SpinWait _spinWait = new SpinWait();
        private Exception? _disconnectException;
        private int _reqIdSeq;

        /// <summary>
        /// Потокобезопасная очередь запросов к удалённой стороне ожидающих ответы.
        /// </summary>
        public PendingRequestDictionary()
        {
            
        }

        /// <summary>
        /// Потокобезопасно добавляет запрос в словарь запросов и возвращает уникальный идентификатор.
        /// </summary>
        /// <exception cref="Exception">Происходит если уже происходил обрыв соединения.</exception>
        public ResponseAwaiter AddRequest(RequestMethodMeta requestToSend, out int uid)
        {
            var responseAwaiter = new ResponseAwaiter(requestToSend);
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
                            } while (!_dict.TryAdd(uid, responseAwaiter));
                            return responseAwaiter;
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



#if NETSTANDARD2_0 || NET472
        public bool TryRemove(int uid, out ResponseAwaiter tcs)
        {
            lock (_dict)
            {
                return _dict.Remove(uid, out tcs);
            }
        }
#else
        /// <summary>
        /// Потокобезопасно удаляет запрос из словаря.
        /// </summary>
        public bool TryRemove(int uid, [MaybeNullWhen(false)] out ResponseAwaiter tcs)
        {
            lock (_dict)
            {
                return _dict.Remove(uid, out tcs);
            }
        }
#endif

        /// <summary>
        /// Потокобезопасно распространяет исключение всем ожидающим потокам. Дальнейшее создание запросов будет провоцировать это исключение.
        /// </summary>
        internal void TryPropagateExceptionAndLockup(Exception exception)
        {
            lock (_dict)
            {
                if (_disconnectException == null)
                {
                    _disconnectException = exception;
                    if (_dict.Count > 0)
                    {
                        foreach (ResponseAwaiter tcs in _dict.Values)
                        {
                            tcs.TrySetException(exception);
                        }
                        _dict.Clear();
                    }
                }
            }
        }
    }
}
