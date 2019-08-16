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

        private static async ValueTask<object> InnerConvert(ValueTask task)
        {
            await task.ConfigureAwait(false);
            return null;
        }

        private static ValueTask<object> InnerConvert(object rawResult)
        {
            return new ValueTask<object>(rawResult);
        }

        private static async ValueTask<object> InnerConvert(Task task)
        {
            await task.ConfigureAwait(false);
            return null;
        }

        private static async ValueTask<object> InnerConvert<T>(Task<T> task)
        {
            return await task.ConfigureAwait(false);
        }

        private static async ValueTask<object> InnerConvert<T>(ValueTask<T> task)
        {
            return await task.ConfigureAwait(false);
        }
    }
}
