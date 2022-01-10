using DanilovSoft.vRPC;
using DanilovSoft.vRPC.AspNetCore;
using DanilovSoft.vRPC.AspNetCore.Internal;
using DanilovSoft.vRPC.AspNetCore.Model.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class JsonRpcServicesExtensions
    {
        public static IJsonRpcServerBuilder AddJsonRpc(this IServiceCollection services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            // Найти контроллеры в сборке.
            Dictionary<string, Type> controllerTypes = GlobalVars.FindAllControllers(Assembly.GetCallingAssembly());

            services.AddRouting();
            services.AddOptions();
            services.TryAddSingleton<JrpcMarkerService>();

            var controllers = new InvokeActionsDictionary(controllerTypes);
            // Добавим скрытый контроллер для авторизации.
            services.AddScoped(typeof(DanilovSoft.vRPC.Controllers.AccountController));

            // Добавить контроллеры в IoC.
            foreach (Type controllerType in controllerTypes.Values)
            {
                services.AddScoped(controllerType);
            }
            services.AddScoped<RequestContextScope>();
            services.AddScoped(typeof(IProxy<>), typeof(ProxyFactory<>));
            services.AddSingleton<IHostApplicationLifetime, HostApplicationLifetimeBridge>();

            services.TryAddSingleton(controllers);

            // Model
            services.TryAddSingleton(typeof(ServiceRouteBuilder));

            return new JrpcServerBuilder(services);
        }
    }
}
