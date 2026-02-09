using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Reflection;

namespace YCode.CLI
{
    internal static class ServiceRegistration
    {
        public static IServiceProvider Register(this IServiceCollection services)
        {
            var key = Environment.GetEnvironmentVariable("YCODE_AUTH_TOKEN")!;
            var uri = Environment.GetEnvironmentVariable("YCODE_API_BASE_URI")!;
            var model = Environment.GetEnvironmentVariable("YCODE_MODEL")!;
            var workDir = Directory.GetCurrentDirectory();

            services.AddSingleton(new AppConfig(key, uri, model, workDir));

            services.RegisterAttributedServices();

            return services.BuildServiceProvider();
        }

        private static void RegisterAttributedServices(this IServiceCollection services)
        {
            var assembly = Assembly.GetExecutingAssembly();

            var types = assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsClass: true });

            foreach (var type in types)
            {
                var attr = type.GetCustomAttributes()
                    .OfType<InjectAttribute>()
                    .FirstOrDefault();

                if (attr == null)
                {
                    continue;
                }

                var serviceType = attr.ServiceType ?? type;

                switch (attr.Lifetime)
                {
                    case ServiceLifetime.Singleton:
                        services.AddSingleton(serviceType, type);
                        break;
                    case ServiceLifetime.Scoped:
                        services.AddScoped(serviceType, type);
                        break;
                    case ServiceLifetime.Transient:
                        services.AddTransient(serviceType, type);
                        break;
                }
            }
        }
    }

    internal sealed record AppConfig(string Key, string Uri, string Model, string WorkDir);

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    internal class InjectAttribute : Attribute
    {
        public InjectAttribute(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            this.Lifetime = lifetime;
        }


        public InjectAttribute(Type serviceType, ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            this.ServiceType = serviceType;
            this.Lifetime = lifetime;
        }

        public ServiceLifetime Lifetime { get; }

        public Type? ServiceType { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class InjectAttribute<TService> : InjectAttribute where TService : class
    {
        public InjectAttribute(ServiceLifetime lifetime = ServiceLifetime.Singleton) : base(typeof(TService), lifetime)
        { }
    }
}

