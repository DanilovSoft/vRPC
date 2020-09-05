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
    /// </summary>
    [DebuggerDisplay(@"\{Count = {_dict.Count}\}")]
    internal sealed class PendingRequestDictionary
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly Dictionary<int, IResponseAwaiter> _dict = new Dictionary<int, IResponseAwaiter>();
        /// <summary>
        /// Не является потокобезопасным.
        /// </summary>
        private readonly SpinWait _spinWait;
        /// <summary>
        /// Может быть производным типом <see cref="VRpcException"/> или <see cref="ObjectDisposedException"/>.
        /// </summary>
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
        /// <exception cref="VRpcException">Происходит если уже был обрыв соединения.</exception>
        /// <exception cref="ObjectDisposedException"/>
        public void AddRequest<TResult>(Request<TResult> request, out int uid)
        {
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
                            } while (!_dict.TryAdd(uid, request));

                            return;
                        }
                    }
                    else
                        ThrowHelper.ThrowException(_disconnectException);

                    // Словарь переполнен — подождать и повторить попытку.
                    _spinWait.SpinOnce();
                }
            } while (true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int IncrementSeq() => Interlocked.Increment(ref _reqIdSeq);

        /// <summary>
        /// Потокобезопасно удаляет запрос из словаря.
        /// </summary>
        public bool TryRemove(int id, [MaybeNullWhen(false)] out IResponseAwaiter tcs)
        {
            lock (_dict)
            {
                return _dict.Remove(id, out tcs);
            }
        }

        /// <summary>
        /// Распространяет исключение всем ожидающим запросам. Дальнейшее создание запросов будет провоцировать это исключение.
        /// </summary>
        /// <remarks>Не бросает исключения. Потокобезопасно.</remarks>
        internal void TryPropagateExceptionAndLockup(Exception exception)
        {
            lock (_dict)
            {
                if (_disconnectException == null)
                {
                    _disconnectException = exception;
                    if (_dict.Count > 0)
                    {
                        foreach (IResponseAwaiter tcs in _dict.Values)
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
