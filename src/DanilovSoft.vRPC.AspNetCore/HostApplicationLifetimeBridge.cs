namespace DanilovSoft.vRPC.AspNetCore
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class HostApplicationLifetimeBridge : IHostApplicationLifetime
    {
        private readonly Microsoft.Extensions.Hosting.IHostApplicationLifetime _lifeTime;

        public CancellationToken ApplicationStarted => _lifeTime.ApplicationStarted;

        public CancellationToken ApplicationStopping => _lifeTime.ApplicationStopping;

        public CancellationToken ApplicationStopped => _lifeTime.ApplicationStopped;

        public HostApplicationLifetimeBridge(Microsoft.Extensions.Hosting.IHostApplicationLifetime lifeTime)
        {
            _lifeTime = lifeTime;
        }
    }
}
