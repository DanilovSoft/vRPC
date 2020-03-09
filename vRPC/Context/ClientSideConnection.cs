using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.WebSockets;
using Microsoft.Extensions.DependencyInjection;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{IsConnected = {IsConnected}\}")]
    public sealed class ClientSideConnection : ManagedConnection
    {
        private static readonly RequestMeta SignInMeta = new RequestMeta("", "SignIn", typeof(void), false);
        private static readonly RequestMeta SignOutMeta = new RequestMeta("", "SignOut", typeof(void), false);
        private static readonly RequestMeta SignInAsyncMeta = new RequestMeta("", "SignIn", typeof(Task), false);
        private static readonly RequestMeta SignOutAsyncMeta = new RequestMeta("", "SignOut", typeof(Task), false);
        internal static readonly LockedDictionary<MethodInfo, RequestMeta> InterfaceMethodsInfo = new LockedDictionary<MethodInfo, RequestMeta>();
        /// <summary>
        /// Методы SignIn, SignOut (async) должны выполняться последовательно
        /// что-бы синхронизироваться со свойством IsAuthenticated.
        /// </summary>
        private readonly object _authLock = new object();
        public RpcClient Client { get; }
        /// <summary>
        /// Установка свойства только через блокировку <see cref="_authLock"/>.
        /// Перед чтением этого значения нужно дождаться завершения <see cref="_lastAuthTask"/> — этот таск может модифицировать значение минуя захват блокировки.
        /// </summary>
        private volatile bool _isAuthenticated;
        public bool IsAuthenticated => _isAuthenticated;
        /// <summary>
        /// Установка свойства только через блокировку <see cref="_authLock"/>.
        /// Этот таск настроен не провоцировать исключения.
        /// </summary>
        private Task _lastAuthTask = Task.CompletedTask;
        private protected override IConcurrentDictionary<MethodInfo, RequestMeta> InterfaceMethods => InterfaceMethodsInfo;

        // ctor.
        internal ClientSideConnection(RpcClient client, ClientWebSocket ws, ServiceProvider serviceProvider, InvokeActionsDictionary controllers)
            : base(ws.ManagedWebSocket, isServer: false, serviceProvider, controllers)
        {
            Client = client;
        }

        // Клиент всегда разрешает серверу вызывать свои методы.
        private protected override bool ActionPermissionCheck(ControllerActionMeta actionMeta, out IActionResult permissionError, out ClaimsPrincipal user)
        {
            user = null;
            permissionError = null;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected override T InnerGetProxy<T>()
        {
            return Client.GetProxy<T>();
        }

        /// <summary>
        /// Выполняет аутентификацию соединения.
        /// </summary>
        /// <param name="accessToken">Аутентификационный токен передаваемый серверу.</param>
        internal void SignIn(AccessToken accessToken)
        {
            SignInAsync(accessToken).GetAwaiter().GetResult();
            //lock (_authLock)
            //{
            //    if (!_lastAuthTask.IsCompleted)
            //    // Кто-то уже выполняет SignIn/Out — нужно дождаться завершения.
            //    {
            //        // ВНИМАНИЕ опасность дедлока — _lastAuthTask не должен делать lock.
            //        // Не бросает исключения.
            //        _lastAuthTask.GetAwaiter().GetResult();
            //    }
                
            //    // Создаём запрос для отправки.
            //    BinaryMessageToSend binaryRequest = SignInMeta.SerializeRequest(new object[] { accessToken });
            //    try
            //    {
            //        var requestResult = SendRequestAndGetResult(binaryRequest, SignInMeta);
            //        binaryRequest = null;
            //        Debug.Assert(requestResult == null);
            //    }
            //    finally
            //    {
            //        binaryRequest?.Dispose();
            //    }

            //    _isAuthenticated = true;
            //}
        }

        /// <summary>
        /// Выполняет аутентификацию соединения.
        /// </summary>
        /// <param name="accessToken">Аутентификационный токен передаваемый серверу.</param>
        internal async Task SignInAsync(AccessToken accessToken)
        {
            bool retryRequired;
            do
            {
                Task task;
                lock (_authLock)
                {
                    if (_lastAuthTask.IsCompleted)
                    // Теперь мы имеем эксклюзивную возможность выполнить SignIn/Out.
                    {
                        // Начали свой запрос.
                        task = PrivateSignIn(accessToken);

                        // Можем обновить свойство пока в блокировке.
                        _lastAuthTask = task.ContinueWith(t => { }, default, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                        // Повторять захват блокировки больше не нужно.
                        retryRequired = false;
                    }
                    else
                    // Кто-то уже выполняет SignIn/Out.
                    {
                        // Будем ожидать чужой таск.
                        task = _lastAuthTask;

                        // Нужно повторить захват блокировки после завершения таска.
                        retryRequired = true;
                    }
                }

                // Наш таск может бросить исключение — чужой не может бросить исключение.
                await task.ConfigureAwait(false);

            } while (retryRequired);
        }

        private async Task PrivateSignIn(AccessToken accessToken)
        {
            Task requestTask;

            // Создаём запрос для отправки.
            BinaryMessageToSend binaryRequest = SignInAsyncMeta.SerializeRequest(new object[] { accessToken });
            try
            {
                requestTask = SendRequestAndGetResult(binaryRequest, SignInAsyncMeta) as Task;
                binaryRequest = null;
            }
            finally
            {
                binaryRequest?.Dispose();
            }

            // Ждём завершения SignIn.
            await requestTask.ConfigureAwait(false);

            // Делать lock нельзя! Может случиться дедлок (а нам и не нужно).
            _isAuthenticated = true;
        }

        /// <summary>
        /// 
        /// </summary>
        internal void SignOut()
        {
            SignOutAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 
        /// </summary>
        internal async Task SignOutAsync()
        {
            bool retryRequired;
            do
            {
                Task task;
                lock (_authLock)
                {
                    if (_lastAuthTask.IsCompleted)
                    // Теперь мы имеем эксклюзивную возможность выполнить SignIn/Out.
                    {
                        if (_isAuthenticated)
                        {
                            // Начали свой запрос.
                            task = PrivateSignOutAsync();

                            // Можем обновить свойство пока в блокировке.
                            _lastAuthTask = task.ContinueWith(t => { }, default, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                            // Повторять захват блокировки больше не нужно.
                            retryRequired = false;
                        }
                        else
                        // Другой поток уже выполнил SignOut — отправлять очередной запрос бессмысленно.
                        {
                            return;
                        }
                    }
                    else
                    // Кто-то уже выполняет SignIn/Out.
                    {
                        // Будем ожидать чужой таск.
                        task = _lastAuthTask;

                        // Нужно повторить захват блокировки после завершения таска.
                        retryRequired = true;
                    }
                }

                // Наш таск может бросить исключение — чужой не может бросить исключение.
                await task.ConfigureAwait(false);

            } while (retryRequired);
        }

        private async Task PrivateSignOutAsync()
        {
            Task requestTask;

            // Создаём запрос для отправки.
            BinaryMessageToSend binaryRequest = SignOutAsyncMeta.SerializeRequest(Array.Empty<object>());
            try
            {
                requestTask = SendRequestAndGetResult(binaryRequest, SignOutAsyncMeta) as Task;
                binaryRequest = null;
            }
            finally
            {
                binaryRequest?.Dispose();
            }

            // Ждём завершения SignOut — исключений быть не может, только при обрыве связи.
            await requestTask.ConfigureAwait(false);

            // Делать lock нельзя! Может случиться дедлок (а нам и не нужно).
            _isAuthenticated = false;
        }
    }
}
