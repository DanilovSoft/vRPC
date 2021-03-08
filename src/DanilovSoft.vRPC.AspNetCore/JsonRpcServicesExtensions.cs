namespace Microsoft.Extensions.DependencyInjection
{
    using DanilovSoft.vRPC;
    using DanilovSoft.vRPC.AspNetCore;
    using DanilovSoft.vRPC.AspNetCore.Internal;
    using DanilovSoft.vRPC.AspNetCore.Model.Internal;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text;

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

            var jrpcServices = new ServiceCollection();
            var controllers = new InvokeActionsDictionary(controllerTypes);
            // Добавим скрытый контроллер для авторизации.
            jrpcServices.AddScoped(typeof(DanilovSoft.vRPC.Controllers.AccountController));

            // Добавить контроллеры в IoC.
            foreach (Type controllerType in controllerTypes.Values)
            {
                jrpcServices.AddScoped(controllerType);
            }
            var rpcServices = BuildServiceCollection(jrpcServices);

            services.TryAddSingleton(controllers);

            // Model
            services.TryAddSingleton(typeof(ServiceRouteBuilder));

            return new JrpcServerBuilder(services);
        }

        private static ServiceProvider BuildServiceCollection(ServiceCollection serviceCollection)
        {
            serviceCollection.AddScoped<RequestContextScope>();
            serviceCollection.AddScoped(typeof(IProxy<>), typeof(ProxyFactory<>));
            serviceCollection.AddSingleton<IHostApplicationLifetime>(this);

            return serviceCollection.BuildServiceProvider();
        }
    }
}
