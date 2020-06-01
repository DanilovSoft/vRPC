using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal static class TaskConverter
    {
        private static readonly SyncDictionary<Type, Func<Task<object?>, object>> _dict = new SyncDictionary<Type, Func<Task<object?>, object>>();
        private static readonly MethodInfo _InnerConvertTaskMethod = typeof(TaskConverter).GetMethod(nameof(InnerConvertTask), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly MethodInfo _InnerConvertValueTaskMethod = typeof(TaskConverter).GetMethod(nameof(InnerConvertValueTask), BindingFlags.NonPublic | BindingFlags.Static)!;

        // ctor.
        static TaskConverter()
        {
            Debug.Assert(_InnerConvertTaskMethod != null);
            Debug.Assert(_InnerConvertValueTaskMethod != null);
        }

        /// <summary>
        /// Преобразует <see cref="Task"/><see langword="&lt;object&gt;"/> в <see cref="Task{T}"/> или в <see cref="ValueTask{T}"/>.
        /// Не бросает исключения но агрегирует их в Task.
        /// </summary>
        /// <returns><see cref="Task{T}"/> или в <see cref="ValueTask{T}"/>.</returns>
        public static object ConvertTask(Task<object?> task, Type desireType, Type returnType)
        {
            // Получить делегат шаблонного конвертера.
            return _dict.GetOrAdd(desireType, DelegateFactory, returnType).Invoke(task);
        }

        // Возвращает Task<T>
        /// <returns><see cref="Task{TResult}"/> где TResult может быть Null.</returns>
        private static object InnerConvertTask<T>(Task<object?> task)
        {
            return ConvertTaskAsync<T>(task);
        }


        // Возвращает ValueTask<T>
        /// <returns><see cref="ValueTask{TResult}"/> упакованный в object где TResult может быть Null.</returns>
        [SuppressMessage("Reliability", "CA2012:Используйте ValueTasks правильно", Justification = "ValueTask будет использован правильно но мы должны упаковать его в object")]
        private static object InnerConvertValueTask<T>(Task<object?> task)
        {
            return ConvertValueTaskAsync<T>(task);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ValueTask<T> ConvertValueTaskAsync<T>(Task<object?> task)
        {
            // Получить результат синхронно можно только при успешном 
            // завершении таска, что-бы в случае исключения они агрегировались в Task.

            if (!task.IsCompletedSuccessfully())
            {
                return WaitForValueTaskAsync(task);
            }
            else
            {
                // Никогда не бросает исключения.
                object? taskResult = task.GetAwaiter().GetResult();
                return new ValueTask<T>(result: (T)taskResult!);
            }

            // Локальная функция.
            static async ValueTask<T> WaitForValueTaskAsync(Task<object?> task)
            {
                object? result = await task.ConfigureAwait(false);
                return (T)result!;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Task<T> ConvertTaskAsync<T>(Task<object?> task)
        {
            // Получить результат синхронно можно только при успешном 
            // завершении таска, что-бы в случае исключения они агрегировались в Task.

            if (!task.IsCompletedSuccessfully())
            {
                return WaitForTaskAsync(task);
            }
            else
            {
                // Никогда не бросает исключения.
                object? taskResult = task.GetAwaiter().GetResult();
                return Task.FromResult((T)taskResult!);
            }

            // Локальная функция.
            static async Task<T> WaitForTaskAsync(Task<object?> task)
            {
                object? taskResult = await task.ConfigureAwait(false);
                return (T)taskResult!;
            }
        }

        /// <summary>
        /// Создаёт делегат к нужному конвертеру. Не бросает исключения.
        /// </summary>
        private static Func<Task<object?>, object> DelegateFactory(Type key, Type returnType)
        {
            MethodInfo converterGenericMethod;
            if (returnType.GetGenericTypeDefinition() != typeof(ValueTask<>))
            // Является Task.
            {
                // Создать шаблонный метод InnerConvertTask<T>.
                // Возвращает Task<T>
                converterGenericMethod = _InnerConvertTaskMethod.MakeGenericMethod(key);
            }
            else
            // Является ValueTask.
            {
                // Создать шаблонный метод InnerConvertValueTask<T>.
                // Возвращает ValueTask<T>
                converterGenericMethod = _InnerConvertValueTaskMethod.MakeGenericMethod(key);
            }

            // Создать типизированный делегат.
            var deleg = converterGenericMethod.CreateDelegate(typeof(Func<Task<object?>, object>)) as Func<Task<object?>, object>;
            Debug.Assert(deleg != null);
            return deleg;
        }
    }
}
