using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly Dictionary<ushort, TaskCompletionSource> _dict = new Dictionary<ushort, TaskCompletionSource>();
        private readonly SpinWait _spinWait = new SpinWait();
        private Exception _disconnectException;
        private int _reqIdSeq;
        //private volatile ushort _freeUid;

        /// <summary>
        /// Потокобезопасная очередь запросов к удалённой стороне ожидающих ответы.
        /// </summary>
        public RequestQueue()
        {
            //_rnd = new Random();
        }

        /// <summary>
        /// Потокобезопасно добавляет запрос в словарь запросов и возвращает уникальный идентификатор.
        /// </summary>
        /// <exception cref="Exception">Происходит если уже происходил обрыв соединения.</exception>
        public TaskCompletionSource AddRequest(Type resultType, string requestAction, out ushort uid)
        {
            var tcs = new TaskCompletionSource(resultType, requestAction);

            do
            {
                lock (_dict)
                {
                    if (_disconnectException != null)
                        throw _disconnectException;

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

                _spinWait.SpinOnce();

            } while (true);
        }

        private ushort IncrementSeq()
        {
            ushort uid = unchecked((ushort)Interlocked.Increment(ref _reqIdSeq));
            return uid;
        }

        ///// <summary>
        ///// Потокобезопасно передает результат запроса ожидающему потоку.
        ///// </summary>
        //public void OnResponse(Message message)
        //{
        //    if(TryRemove(message.Uid, out TaskCompletionSource tcs))
        //    {
        //        tcs.OnResponse(message);
        //    }
        //}

        public bool TryTake(ushort uid, out TaskCompletionSource tcs)
        {
            return TryRemove(uid, out tcs);
        }

        ///// <summary>
        ///// Потокобезопасно передает исключение как результат запроса ожидающему потоку.
        ///// </summary>
        //public void OnErrorResponse(ushort uid, Exception exception)
        //{
        //    // Потокобезопасно удалить запрос из словаря.
        //    if (TryRemove(uid, out TaskCompletionSource tcs))
        //    {
        //        tcs.TrySetException(exception);
        //    }
        //}

        /// <summary>
        /// Потокобезопасно удаляет запрос из словаря.
        /// </summary>
        private bool TryRemove(ushort uid, out TaskCompletionSource tcs)
        {
            lock (_dict)
            {
                // Обязательно удалить из словаря,
                // что-бы дубль результата не мог сломать рабочий процесс.
                if (_dict.Remove(uid, out tcs))
                {
                    //_dict.Remove(uid);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Потокобезопасно распространяет исключение всем ожидающим потокам. Дальнейшее создание запросов будет генерировать это исключение.
        /// </summary>
        internal void OnDisconnect(Exception exception)
        {
            lock (_dict)
            {
                if (_disconnectException == null)
                {
                    _disconnectException = exception;
                    if (_dict.Count > 0)
                    {
                        foreach (TaskCompletionSource tcs in _dict.Values)
                            tcs.TrySetException(exception);
                        
                        _dict.Clear();
                    }
                }
            }
        }
    }
}
