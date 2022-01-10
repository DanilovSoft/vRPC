using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal static class DynamicAwaiter
    {
        /// <summary>
        /// Асинхронно ожидает завершение задачи если <paramref name="actionResult"/> является <see cref="Task"/>'ом.
        /// </summary>
        /// <exception cref="Exception">Инкапсулированное в Task.</exception>
        /// <param name="actionResult"><see cref="Task"/> или любой объект.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ValueTask<object?> ConvertToTask(object actionResult)
        {
            // Все методы InnerConvert должны возвращать одинаковый тип.
            return InnerConvert((dynamic)actionResult);
        }

        private static ValueTask<object?> InnerConvert(ValueTask task)
        {
            if (task.IsCompleted)
            {
                // Может быть исключение — аналогично await task.
                task.GetAwaiter().GetResult();

                return new ValueTask<object?>();
            }
            else
            {
                return WaitAsync(task);
            }

            static async ValueTask<object?> WaitAsync(ValueTask task)
            {
                await task.ConfigureAwait(false);
                return null;
            }
        }

        private static ValueTask<object> InnerConvert(object rawResult)
        {
            return new ValueTask<object>(result: rawResult);
        }

        private static ValueTask<object?> InnerConvert(Task task)
        {
            if (task.IsCompleted)
            {
                // Может быть исключение — аналогично await task.
                task.GetAwaiter().GetResult();

                return new ValueTask<object?>();
            }
            else
            {
                return WaitAsync(task);
            }

            static async ValueTask<object?> WaitAsync(Task task)
            {
                await task.ConfigureAwait(false);
                return null;
            }
        }

        private static ValueTask<object?> InnerConvert<T>(Task<T> task)
        {
            if (task.IsCompleted)
            {
                // Может быть исключение — GetAwaiter().GetResult() аналогично await task и предпочтительнее чем Result.
                T result = task.GetAwaiter().GetResult();

                return new ValueTask<object?>(result);
            }
            else
            {
                return WaitAsync(task);
            }

            static async ValueTask<object?> WaitAsync(Task<T> task)
            {
                return await task.ConfigureAwait(false);
            }
        }

        private static ValueTask<object?> InnerConvert<T>(ValueTask<T> task)
        {
            if (task.IsCompleted)
            {
                // Может быть исключение — GetAwaiter().GetResult() аналогично await task и предпочтительнее чем Result.
                T result = task.GetAwaiter().GetResult();

                return new ValueTask<object?>(result: result);
            }
            else
            {
                return WaitAsync(task);
            }

            static async ValueTask<object?> WaitAsync(ValueTask<T> task)
            {
                return await task.ConfigureAwait(false);
            }
        }
    }
}
