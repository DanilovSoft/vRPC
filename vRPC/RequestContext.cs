using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    /// <summary>
    /// Контекст запроса.
    /// </summary>
    internal sealed class RequestContext
    {
        /// <summary>
        /// Запрашиваемый экшен контроллера.
        /// </summary>
        public ControllerAction ActionToInvoke { get; internal set; }
    }
}
