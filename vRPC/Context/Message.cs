using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace vRPC
{
    /// <summary>
    /// Сообщение для передачи удаленной стороне. Не подлежит сериализации.
    /// </summary>
    internal abstract class Message
    {
        /// <summary>
        /// Параметры для удаленного метода <see cref="ActionName"/>.
        /// </summary>
        public JToken[] Args { get; protected set; }

        protected Message()
        {

        }

        public static Arg[] PrepareArgs(object[] args)
        {
            var jArgs = new Arg[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                jArgs[i] = new Arg(args[i]);
            }
            return jArgs;
        }
    }
}
