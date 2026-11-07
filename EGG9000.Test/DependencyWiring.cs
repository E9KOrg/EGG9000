using EGG9000.Common.Bus;
using EGG9000.Common.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EGG9000.Test {
    internal static class DependencyWiring {
        public static List<Type> ConcreteTypes(Assembly assembly, Func<Type, bool> predicate) =>
            [.. assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && predicate(t)).OrderBy(t => t.FullName)];

        public static bool DerivesFromGeneric(Type type, Type openGeneric) {
            for(var current = type.BaseType; current is not null; current = current.BaseType) {
                if(current.IsGenericType && current.GetGenericTypeDefinition() == openGeneric)
                    return true;
            }
            return false;
        }

        public static bool IsBusHandler(Type? type) =>
            type is not null && type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBusHandler<>));

        public static List<ServiceDescriptor> HostedServices(IServiceCollection services) =>
            [.. services.Where(d => d.ServiceType == typeof(IHostedService) && !d.IsKeyedService && (d.ImplementationType is null || IsFirstParty(d.ImplementationType)))];

        public static bool IsFirstParty(Type type) =>
            type.Assembly.GetName().Name?.StartsWith("EGG9000.", StringComparison.Ordinal) == true;

        public static List<Type> BusHandlers(IServiceCollection services) =>
            [.. services.Where(d => !d.IsKeyedService && IsBusHandler(d.ImplementationType)).Select(d => d.ImplementationType!).Distinct()];

        public static void UseInMemoryDatabase(IServiceCollection services, string database) {
            foreach(var descriptor in services.Where(d => IsContextRegistration(d.ServiceType)).ToList())
                services.Remove(descriptor);
            services.AddDbContextFactory<ApplicationDbContext>(options => options.UseInMemoryDatabase(database));
        }

        private static bool IsContextRegistration(Type type) =>
            type == typeof(ApplicationDbContext) || type == typeof(DbContextOptions) || (type.IsGenericType && type.GenericTypeArguments.Contains(typeof(ApplicationDbContext)));

        public static object? Construct(IServiceProvider provider, ServiceDescriptor descriptor) {
            if(descriptor.ImplementationInstance is not null)
                return descriptor.ImplementationInstance;
            if(descriptor.ImplementationFactory is not null)
                return descriptor.ImplementationFactory(provider);
            return ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
        }

        public static string Describe(ServiceDescriptor descriptor) =>
            descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName ?? "factory registration";

        public static IEnumerable<string> UnresolvedInteractionDependencies(IServiceProvider provider, Type type) {
            var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
            if(constructors.Length != 1) {
                yield return $"has {constructors.Length} public constructors";
                yield break;
            }
            var members = constructors[0].GetParameters().Select(p => (Name: p.Name!, Type: p.ParameterType))
                .Concat(type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.SetMethod is { IsPublic: true, IsStatic: false } && !p.GetCustomAttributes(true).Any(a => a.GetType().Name == "DontInjectAttribute")).Select(p => (Name: p.Name, Type: p.PropertyType)));
            foreach(var (name, memberType) in members) {
                if(memberType == typeof(Discord.Interactions.InteractionService) || memberType == typeof(IServiceProvider))
                    continue;
                if(provider.GetService(memberType) is null)
                    yield return $"{name} ({memberType.Name}) is not registered";
            }
        }

        public static void AssertInteractionTypesResolve(IServiceProvider provider, IReadOnlyCollection<Type> types) {
            Assert.IsNotEmpty(types, "Discovery found nothing to construct.");
            var failures = types.SelectMany(t => UnresolvedInteractionDependencies(provider, t).Select(m => $"{t.FullName}: {m}")).ToList();
            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
            AssertAllConstruct(provider, types);
        }

        public static void AssertAllConstruct(IServiceProvider provider, IReadOnlyCollection<Type> types) =>
            AssertAllConstruct(types, t => t.FullName!, t => ActivatorUtilities.CreateInstance(provider, t));

        public static void AssertAllConstruct<T>(IReadOnlyCollection<T> items, Func<T, string> name, Func<T, object?> construct) {
            Assert.IsNotEmpty(items, "Discovery found nothing to construct.");
            var failures = new List<string>();
            foreach(var item in items) {
                try {
                    if(construct(item) is null)
                        failures.Add($"{name(item)}: resolved to null");
                } catch(Exception e) {
                    failures.Add($"{name(item)}: {e.GetBaseException().Message}");
                }
            }
            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        }
    }
}
