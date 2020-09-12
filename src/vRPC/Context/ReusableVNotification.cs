using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableVNotification : INotification, IValueTaskSource
    {
        public RequestMethodMeta? Method { get; private set; }
        public object[]? Args { get; private set; }
        public bool IsNotification => true;
        private ManualResetValueTaskSourceCore<VoidStruct> _mrv;

        /// <summary>
        /// Для создания синглтона.
        /// </summary>
        internal ReusableVNotification() 
        {
            _mrv.RunContinuationsAsynchronously = true;   
        }

        internal void Initialize(RequestMethodMeta method, object[] args)
        {
            Debug.Assert(!method.IsJsonRpc);
            Debug.Assert(method.IsNotificationRequest);

            Method = method;
            Args = args;
        }

        public ValueTask WaitNotificationAsync()
        {
            return new ValueTask(this, _mrv.Version);
        }

        private void SetException(VRpcException exception)
        {
            _mrv.SetException(exception);
        }

        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer, out int headerSize)
        {
            Debug.Assert(Args != null);
            Debug.Assert(Method != null);

            buffer = new ArrayBufferWriter<byte>();
            var toDispose = buffer;
            try
            {
                Method.SerializeRequest(Args, buffer);

                var header = new HeaderDto(id: null, buffer.WrittenCount, contentEncoding: null, Method.FullName);

                // Записать заголовок в конец стрима. Не бросает исключения.
                headerSize = header.SerializeJson(buffer);

                toDispose = null;
                return true;
            }
            catch (Exception ex)
            {
                var vex = new VRpcSerializationException($"Не удалось сериализовать запрос в json.", ex);
                SetException(vex);
                headerSize = -1;
                return false;
            }
            finally
            {
                Args = null; // Освободить память.
                toDispose?.Dispose();
            }
        }

        public VoidStruct GetResult(short token)
        {
            return _mrv.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return _mrv.GetStatus(token);
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            _mrv.OnCompleted(continuation, state, token, flags);
        }

        void IValueTaskSource.GetResult(short token)
        {
            _mrv.GetResult(token);
            _mrv.Reset();
        }
    }
}
