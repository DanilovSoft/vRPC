using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace vRPC
{
    internal static class TaskConverter
    {
        private static readonly MethodInfo _InnerConvertTaskMethod;
        private static readonly MethodInfo _InnerConvertValueTaskMethod;
        private static readonly SyncDictionary<Type, Func<Task<object>, object>> _dict = new SyncDictionary<Type, Func<Task<object>, object>>();

        // ctor.
        static TaskConverter()
        {
            _InnerConvertTaskMethod = typeof(TaskConverter).GetMethod(nameof(InnerConvertTask), BindingFlags.NonPublic | BindingFlags.Static);
            _InnerConvertValueTaskMethod = typeof(TaskConverter).GetMethod(nameof(InnerConvertValueTask), BindingFlags.NonPublic | BindingFlags.Static);

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

        [DebuggerStepThrough]
        private static async ValueTask<T> ConvertValueTaskAsync<T>(Task<object> task)
        {
            object result = await task.ConfigureAwait(false);
            return (T)result;
        }

        [DebuggerStepThrough]
        private static async Task<T> ConvertTaskAsync<T>(Task<object> task)
        {
            //return task.ContinueWith(t => (T)t.Result, CancellationToken.None, 
            //    TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            object result = await task.ConfigureAwait(false);
            return (T)result;
        }

        //[DebuggerStepThrough]
        //private static T Convert<T>(Task<object> task)
        //{
        //    var result = (T)task.GetAwaiter().GetResult();
        //    return result;
        //}

        private static Func<Task<object>, object> Factory(Type key, Type returnType)
        {
            MethodInfo converterGenericMethod;
            if (returnType.GetGenericTypeDefinition() != typeof(ValueTask<>))
            {
                // Создать шаблонный метод InnerConvertTask<T>.
                converterGenericMethod = _InnerConvertTaskMethod.MakeGenericMethod(key);
            }
            else
            {
                // Создать шаблонный метод InnerConvertValueTask<T>.
                converterGenericMethod = _InnerConvertValueTaskMethod.MakeGenericMethod(key);
            }

            // Создать типизированный делегат.
            var deleg = (Func<Task<object>, object>)converterGenericMethod.CreateDelegate(typeof(Func<Task<object>, object>));
            return deleg;
        }
    }
}
