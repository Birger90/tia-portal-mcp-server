using System.Linq.Expressions;
using System.Reflection;

namespace TiaMcpServer.Contracts.Shared;

/// <summary>
/// Subscribes to a public instance event of a live object whose delegate type is not documented (bound by exact event name; the
/// delegate must take (sender, args) and return void). The handler receives the event arguments; the returned object removes it.
/// </summary>
public static class RuntimeEvents
{
    public static IDisposable Subscribe(object target, string eventName, Action<object?> handler, string documented)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        var info = target.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
        var invoke = info?.EventHandlerType?.GetMethod("Invoke");
        var parameters = invoke?.GetParameters();
        if (info is null || invoke is null || parameters is null || parameters.Length != 2 || invoke.ReturnType != typeof(void))
            throw new RuntimeBindingException(Worker.WorkerFailureCategories.ApiMemberMissing,
                $"'{target.GetType().FullName}' exposes no event {eventName}(sender, args) on this TIA Portal installation ({documented}). Nothing was changed.");
        var sender = Expression.Parameter(parameters[0].ParameterType, "sender");
        var args = Expression.Parameter(parameters[1].ParameterType, "args");
        var body = Expression.Invoke(Expression.Constant(handler), Expression.Convert(args, typeof(object)));
        var compiled = Expression.Lambda(info.EventHandlerType!, body, sender, args).Compile();
        info.AddEventHandler(target, compiled);
        return new Subscription(() => info.RemoveEventHandler(target, compiled));
    }

    private sealed class Subscription(Action remove) : IDisposable
    {
        private Action? _remove = remove;

        public void Dispose()
        {
            var remove = Interlocked.Exchange(ref _remove, null);
            try
            {
                remove?.Invoke();
            }
            catch (TargetInvocationException)
            {
                // The source is gone.
            }
        }
    }
}
