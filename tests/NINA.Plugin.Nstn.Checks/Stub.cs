using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace NINA.Plugin.Nstn.Checks {

    /// <summary>
    /// Any N.I.N.A. interface as an inert object: getters return a stable default
    /// (nested interfaces as further stubs, classes via their parameterless
    /// constructor), setters store, events and void methods do nothing. Enough for
    /// devices that are "not connected".
    /// </summary>
    public class Stub : DispatchProxy {

        private readonly ConcurrentDictionary<MethodInfo, object> values = new ConcurrentDictionary<MethodInfo, object>();

        private static readonly MethodInfo CreateGeneric = typeof(DispatchProxy).GetMethods()
            .Single(m => m.Name == nameof(DispatchProxy.Create) && m.IsGenericMethodDefinition);

        /// <summary>Simulated behaviour for single methods; return <see cref="NotHandled"/> for the default.</summary>
        public Func<MethodInfo, object[], object> Handler;

        public static readonly object NotHandled = new object();

        public static T Create<T>() where T : class => (T)Create(typeof(T));

        public static T Create<T>(Func<MethodInfo, object[], object> handler) where T : class {
            var stub = Create<T>();
            ((Stub)(object)stub).Handler = handler;
            return stub;
        }

        public static object Create(Type interfaceType) =>
            CreateGeneric.MakeGenericMethod(interfaceType, typeof(Stub)).Invoke(null, null);

        protected override object Invoke(MethodInfo method, object[] args) {
            if (method.Name.StartsWith("set_", StringComparison.Ordinal) && args.Length == 1) {
                var getter = method.DeclaringType.GetProperty(method.Name.Substring(4))?.GetMethod;
                if (getter != null) {
                    values[getter] = args[0];
                }
                return null;
            }
            // out/ref arguments are copied back by the proxy: a null for a value type would throw.
            var parameters = method.GetParameters();
            for (var i = 0; i < parameters.Length; i++) {
                var p = parameters[i].ParameterType;
                if (p.IsByRef && args[i] == null && p.GetElementType().IsValueType) {
                    args[i] = Activator.CreateInstance(p.GetElementType());
                }
            }
            if (Handler != null) {
                var handled = Handler(method, args);
                if (!ReferenceEquals(handled, NotHandled)) {
                    return handled;
                }
            }
            var type = method.ReturnType;
            if (type == typeof(void)) {
                return null;
            }
            return values.GetOrAdd(method, _ => Make(type));
        }

        private static object Make(Type type) {
            if (type == typeof(Task)) {
                return Task.CompletedTask;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)) {
                var inner = type.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult)).MakeGenericMethod(inner).Invoke(null, new[] { Make(inner) });
            }
            if (type == typeof(string)) {
                return string.Empty;
            }
            if (type == typeof(CultureInfo)) {
                return new CultureInfo("en-US");
            }
            if (type.IsValueType) {
                return Activator.CreateInstance(type);
            }
            if (type.IsInterface) {
                return Create(type);
            }
            if (!type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null) {
                try {
                    return Activator.CreateInstance(type);
                } catch {
                    return null;
                }
            }
            return null;
        }
    }
}
