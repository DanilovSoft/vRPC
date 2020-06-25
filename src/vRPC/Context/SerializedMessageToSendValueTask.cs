using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace DanilovSoft.vRPC
{
    partial class SerializedMessageToSend : IValueTaskSource
    {
        /// <summary>Sentinel object used to indicate that the operation has completed prior to OnCompleted being called.</summary>
        private static readonly Action<object?> s_completedSentinel = new Action<object?>(state => 
        { 
            Debug.Assert(false, "Ошибка синхронизации потоков"); 
            throw new Exception(nameof(s_completedSentinel)); 
        });

        /// <summary>
        /// Текущее значение токена отданное ValueTask'у которое затем будет сравнено со значением переданным нам обратно.
        /// </summary>
        /// <remarks>
        /// Это не обеспечивает абсолютную синхронизацию, а даёт превентивную защиту от неправильного
        /// использования ValueTask, таких как многократное ожидание и обращение к уже использованому прежде.
        /// </remarks>
        private short _token;
        [SuppressMessage("Usage", "CA2213:Следует высвобождать высвобождаемые поля", Justification = "Мы не владеем этим объектом")]
        private ExecutionContext? _executionContext;
        private object? _scheduler;
        private Action<object?>? _continuation;
        private object? _state;

        /// <summary>
        /// Dispose завершает данный таск.
        /// </summary>
        public ValueTask WaitNotificationAsync()
        {
            Debug.Assert(MessageToSend.IsNotificationRequest);

            if (_continuation == s_completedSentinel)
            // Операция уже завершена.
            {
                ResetTaskState();
                return default;
            }
            else
            {
                return new ValueTask(this, _token);
            }
        }

        private void ResetTaskState()
        {
            _token++;
        }

        void IValueTaskSource.GetResult(short token)
        {
            if (token == _token)
            {
                ResetTaskState();
            }
            else
            {
                ThrowHelper.ThrowException(IncorrectTokenException());
            }
        }

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
        {
            if (token == _token)
            {
                return ReferenceEquals(_continuation, s_completedSentinel)
                    ? ValueTaskSourceStatus.Succeeded
                    : ValueTaskSourceStatus.Pending;
            }
            else
            {
                ThrowHelper.ThrowException(IncorrectTokenException());
                return default;
            }
        }

        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            if (token == _token)
            {
                if ((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0)
                {
                    _executionContext = ExecutionContext.Capture();
                }

                if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != 0)
                {
                    SynchronizationContext? sc = SynchronizationContext.Current;
                    if (sc != null && sc.GetType() != typeof(SynchronizationContext))
                    {
                        _scheduler = sc;
                    }
                    else
                    {
                        TaskScheduler ts = TaskScheduler.Current;
                        if (ts != TaskScheduler.Default)
                        {
                            _scheduler = ts;
                        }
                    }
                }

                _state = state; // Use UserToken to carry the continuation state around
                Action<object?>? prevContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);

                if (prevContinuation == null)
                // Успешно сохранили колбэк для другого потока.
                {
                    return;
                }
                else if (ReferenceEquals(prevContinuation, s_completedSentinel))
                {
                    // Другой поток нас опередил и операция уже завершилась.
                    // Мы должны просто вызвать колбек но нужно сбросить стек вызовов.

                    // Lost the race condition and the operation has now already completed.
                    // We need to invoke the continuation, but it must be asynchronously to
                    // avoid a stack dive.  However, since all of the queueing mechanisms flow
                    // ExecutionContext, and since we're still in the same context where we
                    // captured it, we can just ignore the one we captured.
                    bool requiresExecutionContextFlow = _executionContext != null;
                    _executionContext = null;
                    _state = null;

                    InvokeContinuation(continuation, state, forceAsync: true, requiresExecutionContextFlow);
                }
                else if (prevContinuation != null)
                {
                    // Flag errors with the continuation being hooked up multiple times.
                    // This is purely to help alert a developer to a bug they need to fix.
                    ThrowMultipleContinuationsException();
                }
            }
            else
            {
                ThrowHelper.ThrowException(IncorrectTokenException());
            }
        }

        /// <param name="requiresExecutionContextFlow">Служит для небольшой оптимизации.</param>
        private void InvokeContinuation(Action<object?> continuation, object? state, bool forceAsync, bool requiresExecutionContextFlow)
        {
            object? scheduler = _scheduler;
            _scheduler = null;

            if (scheduler != null)
            {
                if (scheduler is SynchronizationContext sc)
                {
                    sc.Post(s =>
                    {
                        var t = ((Action<object?> c, object? state))s!;
                        t.c(t.state);
                    }, (continuation, state));
                }
                else
                {
                    Debug.Assert(scheduler is TaskScheduler, $"Expected TaskScheduler, got {scheduler}");
                    Task.Factory.StartNew(continuation, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach, (scheduler as TaskScheduler)!);
                }
            }
            else if (forceAsync)
            {
                if (requiresExecutionContextFlow)
                {
#if NETSTANDARD2_0 || NET472
                    ThreadPool.QueueUserWorkItem(new WaitCallback(continuation), state);
#else
                    ThreadPool.QueueUserWorkItem(continuation, state, preferLocal: true);
#endif
                }
                else
                {
#if NETSTANDARD2_0 || NET472
                    ThreadPool.UnsafeQueueUserWorkItem(new WaitCallback(continuation), state);
#else
                    ThreadPool.UnsafeQueueUserWorkItem(continuation, state, preferLocal: true);
#endif
                }
            }
            else
            {
                continuation(state);
            }
        }

        /// <summary>
        /// Выполняется когда запрос-нотификация успешно отправлен.
        /// </summary>
        public void CompleteNotification()
        {
            Debug.Assert(MessageToSend.IsNotificationRequest);

            // В переменной может быть Null если await ещё не начался или колбэк для завершения await.
            Action<object?>? continuation = Interlocked.Exchange(ref _continuation, s_completedSentinel);

            if (continuation == null)
            // Операция завершена раньше чем начался await.
            {
                return;
            }
            else
            {
                if (continuation != s_completedSentinel)
                {
                    //Debug.Assert(continuation != s_completedSentinel, "The delegate should not have been the completed sentinel.");

                    object? continuationState = _state;
                    _state = null;

                    ExecutionContext? ec = _executionContext;
                    if (ec == null)
                    {
                        InvokeContinuation(continuation, continuationState, forceAsync: false, requiresExecutionContextFlow: false);
                    }
                    else
                    {
                        // This case should be relatively rare, as the async Task/ValueTask method builders
                        // use the awaiter's UnsafeOnCompleted, so this will only happen with code that
                        // explicitly uses the awaiter's OnCompleted instead.
                        _executionContext = null;
                        ExecutionContext.Run(ec, runState =>
                        {
                            var t = ((SerializedMessageToSend self, Action<object?> c, object? state))runState!;

                            t.self.InvokeContinuation(t.c, t.state, forceAsync: false, requiresExecutionContextFlow: false);

                        }, (this, continuation, continuationState));
                    }
                }
            }
        }

        private static Exception IncorrectTokenException() =>
            new InvalidOperationException("Произошла попытка многократного использования ValueTask.");

        [DoesNotReturn]
        private static void ThrowMultipleContinuationsException() =>
                throw new InvalidOperationException("Multiple continuations not allowed.");

        private static InvalidOperationException SimultaneouslyOperationException()
        {
            return new InvalidOperationException("Operation already in progress.");
        }
    }
}
