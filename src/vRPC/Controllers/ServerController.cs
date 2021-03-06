﻿using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace DanilovSoft.vRPC
{
    public abstract class ServerController : Controller
    {
        /// <summary>
        /// Контекст подключения.
        /// </summary>
        public ServerSideConnection? Context { get; private set; }
        /// <summary>
        /// Пользователь ассоциированный с текущим запросом.
        /// </summary>
        public ClaimsPrincipal? User { get; private set; }

        // Должен быть пустой конструктор для наследников.
        public ServerController()
        {
            
        }

        internal sealed override void BeforeInvokeController(ManagedConnection connection, ClaimsPrincipal? user)
        {
            Context = connection as ServerSideConnection;
            Debug.Assert(Context != null, "Возможно перепутаны серверный и клиентский тип контроллера.");

            User = user;
        }
        
        /// <exception cref="VRpcException"/>
        public BearerToken CreateAccessToken(ClaimsPrincipal claimsPrincipal, TimeSpan validTime)
        {
            if (claimsPrincipal == null)
                ThrowHelper.ThrowArgumentNullException(nameof(claimsPrincipal));

            if (validTime < TimeSpan.Zero)
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(validTime));

            if (Context == null)
                ThrowHelper.ThrowVRpcException("Current server context is Null.");

            return Context.CreateAccessToken(claimsPrincipal, validTime);
        }

        /// <summary>
        /// Сбрасывает аутентификацию соединения в изначальное состояние.
        /// </summary>
        public void SignOut()
        {
            if (Context == null)
                ThrowHelper.ThrowVRpcException("Current server context is Null.");

            Context.SignOut();
        }
    }
}
