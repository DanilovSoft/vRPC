using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC.Context
{
    internal sealed class VErrorResponse : IMessageToSend
    {
        internal int Id { get; }
        private IActionResult _result { get; }

        public VErrorResponse(int id, IActionResult error)
        {
            Id = id;
            _result = error;
        }
    }
}
