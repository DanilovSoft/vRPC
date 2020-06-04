using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Атомарный <see langword="await"/>'ер. Связывает запрос с его результатом.
    /// </summary>
    [DebuggerDisplay(@"\{Request = {Request.ActionName}\}")]
    internal sealed class ResponseAwaiter : INotifyCompletion
    {
        public RequestMethodMeta Request { get; }
        /// <summary>
        /// Флаг используется как fast-path.
        /// </summary>
        private volatile bool _isCompleted;
        [DebuggerNonUserCode]
        public bool IsCompleted => _isCompleted;
        private volatile object? _responseValue;
        private volatile Exception? _exception;
        private Action? _continuationAtomic;

#if DEBUG
        private object? ValueForDebugDisplay
        {
            get
            {
                if (_isCompleted)
                {
                    return _exception ?? _responseValue;
                }
                return default;
            }
        }
#endif

        // ctor.
        public ResponseAwaiter(RequestMethodMeta requestToSend)
        {
            Request = requestToSend;
        }

        [DebuggerStepThrough]
        public ResponseAwaiter GetAwaiter() => this;

        //[DebuggerNonUserCode]
        public object? GetResult()
        {
            if (_exception == null)
            {
                return _responseValue;
            }
            else
            {
                // Исключение является полноценным результатом.
                throw _exception;
            }
        }

        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        public void TrySetException(Exception exception)
        {
            _exception = exception;
            WakeContinuation();
        }

        /// <summary>
        /// Передает результат ожидающему потоку.
        /// </summary>
        public void TrySetResult(object? rawResult)
        {
            _responseValue = rawResult;
            WakeContinuation();
        }

        private void WakeContinuation()
        {
            // Результат уже установлен. Можно установить fast-path.
            _isCompleted = true;

            // Атомарно записать заглушку или вызвать оригинальный continuation.
            Action? continuation = Interlocked.CompareExchange(ref _continuationAtomic, GlobalVars.SentinelAction, null);
            if (continuation != null)
            {
                // Нельзя делать продолжение текущим потоком т.к. это затормозит/остановит диспетчер 
                // или произойдет побег специального потока диспетчера.
#if NETSTANDARD2_0 || NET472
                ThreadPool.UnsafeQueueUserWorkItem(CallContinuation, continuation);
#else
                // Через глобальную очередь.
                ThreadPool.UnsafeQueueUserWorkItem(CallContinuation, continuation, preferLocal: false); // Через глобальную очередь.
#endif
            }
        }

#if NETSTANDARD2_0 || NET472
        //[DebuggerStepThrough]
        private static void CallContinuation(object? state)
        {
            var action = state as Action;
            Debug.Assert(action != null);
            CallContinuation(argState: action);
        }
#endif

        private static void CallContinuation(Action argState)
        {
            argState.Invoke();
        }

        public void OnCompleted(Action continuation)
        {
            // Атомарно передаем continuation другому потоку.
            if (Interlocked.CompareExchange(ref _continuationAtomic, continuation, null) == null)
            {
                return;
            }
            else
            {
                // Шанс попасть в этот блок очень маленький.
                // В переменной _continuationAtomic была другая ссылка, 
                // это значит что другой поток уже установил результат и его можно забрать.
                // Но нужно предотвратить углубление стека (stack dive) поэтому продолжение вызывается
                // другим потоком.

#if NETSTANDARD2_0 || NET472
                ThreadPool.UnsafeQueueUserWorkItem(CallContinuation, continuation);
#else
                ThreadPool.UnsafeQueueUserWorkItem(CallContinuation, continuation, preferLocal: true); // Через локальную очередь.
#endif
            }
        }

        private static void QueueUserWorkItem(Action action)
        {
            Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }
    }
}
