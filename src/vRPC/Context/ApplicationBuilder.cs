using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    public sealed class ApplicationBuilder
    {
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromMinutes(2);
        public TimeSpan ReceiveTimeout { get; set; } = TimeSpan.FromMinutes(4);
        public IServiceProvider ServiceProvider { get; }

        internal ApplicationBuilder(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }
    }
}
