using System.Diagnostics;
using System.Threading.Tasks;

namespace vRPC
{
    internal static class DynamicAwaiter
    {
        /// <summary>
        /// Асинхронно ожидает завершение задачи если <paramref name="controllerResult"/> является <see cref="Task"/>'ом.
        /// </summary>
        /// <param name="controllerResult"><see cref="Task"/> или любой объект.</param>
        /// <returns></returns>
        //[DebuggerStepThrough]
        public static ValueTask<object> WaitAsync(object controllerResult)
        {
            // Все методы InnerConvert должны возвращать одинаковый тип.
            return InnerConvert((dynamic)controllerResult);
        }

        private static ValueTask<object> InnerConvert(ValueTask task)
        {
            if(task.IsCompleted)
            {
                task.GetAwaiter().GetResult();
                return new ValueTask<object>(null);
            }
            else
            {
                return WaitAsync(task);
            }

            static async ValueTask<object> WaitAsync(ValueTask task)
            {
                await task.ConfigureAwait(false);
                return null;
            }
        }

        private static ValueTask<object> InnerConvert(object rawResult)
        {
            return new ValueTask<object>(rawResult);
        }

        private static ValueTask<object> InnerConvert(Task task)
        {
            if (task.IsCompleted)
            {
                task.GetAwaiter().GetResult();
                return new ValueTask<object>(null);
            }
            else
            {
                return WaitAsync(task);
            }

            static async ValueTask<object> WaitAsync(Task task)
            {
                await task.ConfigureAwait(false);
                return null;
            }
        }

        private static ValueTask<object> InnerConvert<T>(Task<T> task)
        {
            if(task.IsCompleted)
            {
                T result = task.GetAwaiter().GetResult();
                return new ValueTask<object>(result);
            }
            else
            {
                return WaitAsync(task);
            }

            static async ValueTask<object> WaitAsync(Task<T> task)
            {
                return await task.ConfigureAwait(false);
            }
        }

        private static ValueTask<object> InnerConvert<T>(ValueTask<T> task)
        {
            if(task.IsCompleted)
            {
                T result = task.GetAwaiter().GetResult();
                return new ValueTask<object>(result);
            }
            else
            {
                return WaitAsync(task);
            }

            static async ValueTask<object> WaitAsync(ValueTask<T> task)
            {
                return await task.ConfigureAwait(false);
            }
        }
    }
}
