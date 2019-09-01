using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace vRPC
{
    /// <summary>
    /// Атомарный <see langword="await"/>'ер. Связывает запрос с его результатом.
    /// </summary>
    [DebuggerDisplay(@"\{Request: {Request.ActionName,nq}\}")]
    internal sealed class RequestAwaiter : INotifyCompletion
    {
        //[DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public RequestMessage Request { get; }
        /// <summary>
        /// Флаг используется как fast-path
        /// </summary>
        private volatile bool _isCompleted;
        [DebuggerNonUserCode]
        public bool IsCompleted => _isCompleted;
        private volatile object _response;
        private volatile Exception _exception;
        private Action _continuationAtomic;

        // ctor.
        public RequestAwaiter(RequestMessage requestToSend)
        {
            Request = requestToSend;
        }

        [DebuggerStepThrough]
        public RequestAwaiter GetAwaiter() => this;

        //[DebuggerStepThrough]
        [DebuggerNonUserCode]
        public object GetResult()
        {
            if (_exception == null)
                return _response;
            else
                throw _exception;
        }

        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        public void TrySetException(Exception exception)
        {
            _exception = exception;
            OnResultAtomic();
        }

        /// <summary>
        /// Передает результат ожидающему потоку.
        /// </summary>
        public void TrySetResult(object rawResult)
        {
            _response = rawResult;
            OnResultAtomic();
        }

        private void OnResultAtomic()
        {
            // Результат уже установлен. Можно установить fast-path.
            _isCompleted = true;

            // Атомарно записать заглушку или вызвать оригинальный continuation.
            Action continuation = Interlocked.CompareExchange(ref _continuationAtomic, GlobalVars.DummyAction, null);
            if (continuation != null)
            {
                // Нельзя делать продолжение текущим потоком т.к. это затормозит/остановит диспетчер 
                // или произойдет побег специального потока диспетчера.
                //ThreadPool.UnsafeQueueUserWorkItem(CallContinuation, continuation);

                // TODO сравнить.
                QueueUserWorkItem(continuation);
            }
        }

        [DebuggerStepThrough]
        private static void CallContinuation(object state)
        {
            ((Action)state).Invoke();
        }

        public void OnCompleted(Action continuation)
        {
            // Атомарно передаем continuation другому потоку.
            if (Interlocked.CompareExchange(ref _continuationAtomic, continuation, null) == null)
            {
                return;
            }

            // P.S. шанс попасть в этот блок очень маленький.
            // В переменной _continuationAtomic была другая ссылка, 
            // это значит что другой поток уже установил результат и его можно забрать.
            continuation();
        }

        private static void QueueUserWorkItem(Action action) =>
            Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
    }
}
