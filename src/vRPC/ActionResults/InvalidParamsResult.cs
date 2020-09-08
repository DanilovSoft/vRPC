﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal sealed class InvalidParamsResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.InvalidParams;
        private readonly string _message;

        public InvalidParamsResult()
        {
            _message = "Invalid params";
        }

        public InvalidParamsResult(string message)
        {
            _message = message;
        }

        public void WriteJsonRpcResult(int id, IBufferWriter<byte> buffer)
        {
            JsonRpcSerializer.SerializeErrorResponse(buffer, DefaultStatusCode, _message, id);
        }

        public void WriteResult(ref ActionContext context)
        {
            context.StatusCode = DefaultStatusCode;
            context.ResponseBuffer.WriteStringBinary(_message);
        }
    }
}
