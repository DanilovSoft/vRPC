using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace vRPC
{
    /// <summary>
    /// Потокобезопасная очередь запросов к удалённой стороне ожидающих ответы.
    /// </summary>
    [DebuggerDisplay("{DebugDisplay,nq}")]
    internal sealed class RequestQueue
    {
        #region Debug
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay => "{" + $"Count = {_dict.Count}" + "}";
        #endregion
        private readonly Random _rnd;
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly Dictionary<short, TaskCompletionSource> _dict = new Dictionary<short, TaskCompletionSource>();
        private Exception _disconnectException;

        /// <summary>
        /// Потокобезопасная очередь запросов к удалённой стороне ожидающих ответы.
        /// </summary>
        public RequestQueue()
        {
            _rnd = new Random();
        }

        /// <summary>
        /// Потокобезопасно добавляет запрос в словарь запросов и возвращает уникальный идентификатор.
        /// </summary>
        /// <exception cref="Exception">Происходит если уже происходил обрыв соединения.</exception>
        public TaskCompletionSource AddRequest(Type resultType, string requestAction, out short uid)
        {
            lock (_dict)
            {
                if (_disconnectException != null)
                    throw _disconnectException;

                do
                {
                    uid = (short)_rnd.Next();
                } while (_dict.ContainsKey(uid)); // Предотвратить дубли уникальных ключей.

                var tcs = new TaskCompletionSource(resultType, requestAction);
                _dict.Add(uid, tcs);
                return tcs;
            }
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

        public bool TryTake(short uid, out TaskCompletionSource tcs)
        {
            return TryRemove(uid, out tcs);
        }

        /// <summary>
        /// Потокобезопасно передает исключение как результат запроса ожидающему потоку.
        /// </summary>
        public void OnErrorResponse(short uid, Exception exception)
        {
            // Потокобезопасно удалить запрос из словаря.
            if (TryRemove(uid, out TaskCompletionSource tcs))
            {
                tcs.TrySetException(exception);
            }
        }

        /// <summary>
        /// Потокобезопасно удаляет запрос из словаря.
        /// </summary>
        private bool TryRemove(short uid, out TaskCompletionSource tcs)
        {
            lock (_dict)
            {
                if (_dict.TryGetValue(uid, out tcs))
                {
                    // Обязательно удалить из словаря,
                    // что-бы дубль результата не мог сломать рабочий процесс.
                    _dict.Remove(uid);
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
