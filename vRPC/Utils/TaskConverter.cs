using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal static class TaskConverter
    {
        private static readonly SyncDictionary<Type, Func<Task<object>, object>> _dict = new SyncDictionary<Type, Func<Task<object>, object>>();
        private static readonly MethodInfo _InnerConvertTaskMethod = typeof(TaskConverter).GetMethod(nameof(InnerConvertTask), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo _InnerConvertValueTaskMethod = typeof(TaskConverter).GetMethod(nameof(InnerConvertValueTask), BindingFlags.NonPublic | BindingFlags.Static);

        // ctor.
        static TaskConverter()
        {
            Debug.Assert(_InnerConvertTaskMethod != null);
            Debug.Assert(_InnerConvertValueTaskMethod != null);
        }

        /// <summary>
        /// Преобразует <see cref="Task"/><see langword="&lt;object&gt;"/> в <see cref="Task{T}"/>.
        /// </summary>
        public static object ConvertTask(Task<object> task, Type desireType, Type returnType)
        {
            // Получить делегат шаблонного конвертера.
            Func<Task<object>, object> genericConverter = _dict.GetOrAdd(desireType, Factory, returnType);

            return genericConverter(task);
        }

        // Возвращает Task<T>
        private static object InnerConvertTask<T>(Task<object> task)
        {
            return ConvertTaskAsync<T>(task);
        }

        // Возвращает ValueTask<T>
        private static object InnerConvertValueTask<T>(Task<object> task)
        {
            return ConvertValueTaskAsync<T>(task);
        }

        //[DebuggerStepThrough]
        private static ValueTask<T> ConvertValueTaskAsync<T>(Task<object> task)
        {
            if (!task.IsCompleted)
            {
                return WaitForValueTaskAsync<T>(task);
            }
            else
            {
                object taskResult = task.GetAwaiter().GetResult();
                return new ValueTask<T>((T)taskResult);
            }
        }

        private static async ValueTask<T> WaitForValueTaskAsync<T>(Task<object> t)
        {
            object result = await t.ConfigureAwait(false);
            return (T)result;
        }

        //[DebuggerStepThrough]
        private static Task<T> ConvertTaskAsync<T>(Task<object> task)
        {
            if (!task.IsCompleted)
            {
                return WaitForTaskAsync<T>(task);
            }
            else
            {
                object taskResult = task.GetAwaiter().GetResult();
                return Task.FromResult((T)taskResult);
            }
        }

        private static async Task<T> WaitForTaskAsync<T>(Task<object> t)
        {
            object taskResult = await t.ConfigureAwait(false);
            return (T)taskResult;
        }

        private static Func<Task<object>, object> Factory(Type key, Type returnType)
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
            var deleg = (Func<Task<object>, object>)converterGenericMethod.CreateDelegate(typeof(Func<Task<object>, object>));
            return deleg;
        }
    }
}
