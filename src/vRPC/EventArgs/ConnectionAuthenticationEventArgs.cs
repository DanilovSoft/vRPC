using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    public sealed class ConnectionAuthenticationEventArgs : EventArgs
    {
        ///// <summary>
        ///// Соединение которое запрашивает токен авторизации.
        ///// </summary>
        //public ClientSideConnection Connection { get; }
        /// <summary>
        /// Токен авторизации который будет передан серверу. Может быть Null.
        /// </summary>
        public AccessToken AuthenticateConnectionToken { get; set; }

        [DebuggerStepThrough]
        public ConnectionAuthenticationEventArgs()
        {
            //Connection = connection;
        }
    }
}
