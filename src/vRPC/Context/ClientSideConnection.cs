using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Source;
using DanilovSoft.WebSockets;
using Microsoft.Extensions.DependencyInjection;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{IsConnected = {IsConnected}\}")]
    public sealed class ClientSideConnection : ManagedConnection
    {
        /// <summary>
        /// Internal запрос для аутентификации.
        /// </summary>
        private static readonly RequestMethodMeta SignInAsyncMeta = new RequestMethodMeta("", "SignIn", typeof(VoidStruct), isNotification: false);
        private static readonly RequestMethodMeta SignOutAsyncMeta = new RequestMethodMeta("", "SignOut", typeof(VoidStruct), isNotification: false);
        internal static readonly LockedDictionary<MethodInfo, RequestMethodMeta> MethodDict = new LockedDictionary<MethodInfo, RequestMethodMeta>();
        /// <summary>
        /// Методы SignIn, SignOut (async) должны выполняться последовательно
        /// что-бы синхронизироваться со свойством IsAuthenticated.
        /// </summary>
        private readonly object _authLock = new object();
        public VRpcClient Client { get; }
        /// <summary>
        /// Установка свойства только через блокировку <see cref="_authLock"/>.
        /// Перед чтением этого значения нужно дождаться завершения <see cref="_lastAuthTask"/> — этот таск может модифицировать значение минуя захват блокировки.
        /// </summary>
        private volatile bool _isAuthenticated;
        public sealed override bool IsAuthenticated => _isAuthenticated;
        /// <summary>
        /// Установка свойства только через блокировку <see cref="_authLock"/>.
        /// Этот таск настроен не провоцировать исключения.
        /// </summary>
        private Task _lastAuthTask = Task.CompletedTask;
        
        // ctor.
        /// <summary>
        /// Принимает открытое соединение Web-Socket.
        /// </summary>
        internal ClientSideConnection(VRpcClient client, ClientWebSocket ws, ServiceProvider serviceProvider, InvokeActionsDictionary controllers)
            : base(ws.ManagedWebSocket, isServer: false, serviceProvider, controllers)
        {
            Client = client;
        }

        // Клиент всегда разрешает серверу вызывать свои методы.
        private protected sealed override bool ActionPermissionCheck(ControllerMethodMeta actionMeta, out IActionResult? permissionError, out ClaimsPrincipal? user)
        {
            user = null;
            permissionError = null;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected sealed override T InnerGetProxy<T>()
        {
            return Client.GetProxy<T>();
        }

        /// <summary>
        /// Выполняет аутентификацию соединения.
        /// </summary>
        /// <param name="accessToken">Аутентификационный токен передаваемый серверу.</param>
        /// <exception cref="SocketException"/>
        internal void SignIn(AccessToken accessToken)
        {
            SignInAsync(accessToken).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Выполняет аутентификацию соединения.
        /// </summary>
        /// <param name="accessToken">Аутентификационный токен передаваемый серверу.</param>
        /// <exception cref="SocketException"/>
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
                        task = PrivateSignInAsync(accessToken);

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

        /// <exception cref="SocketException"/>
        internal async Task PrivateSignInAsync(AccessToken accessToken)
        {
            var request = new VRequest<VoidStruct>(SignInAsyncMeta, new object[] { accessToken });

            if (TrySendRequest<VoidStruct>(request, out var error))
            {
                await request.Task.ConfigureAwait(false);
            }
            else
                await error.ConfigureAwait(false);

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
        /// <exception cref="SocketException"/>
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

        /// <exception cref="SocketException"/>
        private async Task PrivateSignOutAsync()
        {
            var request = new VRequest<VoidStruct>(SignOutAsyncMeta, Array.Empty<object>());

            // Ждём завершения SignOut — исключений быть не может, только при обрыве связи.
            if (TrySendRequest<VoidStruct>(request, out var error))
            {
                await request.Task.ConfigureAwait(false);
            }
            else
                await error.ConfigureAwait(false);

            // Делать lock нельзя! Может случиться дедлок (а нам и не нужно).
            _isAuthenticated = false;
        }
    }
}
