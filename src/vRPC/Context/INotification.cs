﻿using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal interface INotification : IMessageToSend
    {
        ValueTask WaitNotificationAsync();
    }

    //internal interface IVNotification : IVRequest, INotification
    //{
        
    //}

    //internal interface IJNotification : IJRequest, INotification
    //{
        
    //}
}
