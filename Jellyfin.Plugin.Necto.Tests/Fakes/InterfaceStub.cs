using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// A stand-in for a large Jellyfin interface where a test needs only a member or two. Every call is
/// recorded, members with an answer return it, and the rest return their type's default. A stub made with
/// <see cref="Of{T}"/> answers the rest with nothing instead: an empty list, a finished task, a folder of its own
/// for a path, so a service that reads them runs on.
/// </summary>
public class InterfaceStub : DispatchProxy
{
    private static readonly string PathsFolder = Path.Join(Path.GetTempPath(), "necto-tests-paths");

    private readonly Dictionary<string, Func<object?[], object?>> _answers = new(StringComparer.Ordinal);

    public List<(string Name, object?[] Args)> Calls { get; } = new();

    /// <summary>Gets or sets a value indicating whether members without an answer answer nothing rather than null.</summary>
    public bool AnswersEmpty { get; set; }

    public static (T Proxy, InterfaceStub Stub) Create<T>()
        where T : class
    {
        var proxy = Create<T, InterfaceStub>();
        return (proxy, (InterfaceStub)(object)proxy);
    }

    /// <summary>
    /// Creates a stand-in that answers the members named as given and every other member with nothing.
    /// </summary>
    /// <typeparam name="T">The interface.</typeparam>
    /// <param name="answers">The members to answer, by name.</param>
    /// <returns>The stand-in.</returns>
    public static T Of<T>(params (string Member, Func<object?[], object?> Answer)[] answers)
        where T : class
    {
        var (proxy, stub) = Create<T>();
        stub.AnswersEmpty = true;
        foreach (var (member, answer) in answers)
        {
            stub.Answer(member, answer);
        }

        return proxy;
    }

    public InterfaceStub Answer(string memberName, Func<object?[], object?> answer)
    {
        _answers[memberName] = answer;
        return this;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        args ??= Array.Empty<object?>();
        lock (Calls)
        {
            Calls.Add((targetMethod.Name, args));
        }

        if (_answers.TryGetValue(targetMethod.Name, out var answer))
        {
            return answer(args);
        }

        var type = targetMethod.ReturnType;
        if (!AnswersEmpty)
        {
            return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
        }

        if (type == typeof(string) && targetMethod.Name.StartsWith("get_", StringComparison.Ordinal) && targetMethod.Name.EndsWith("Path", StringComparison.Ordinal))
        {
            return Directory.CreateDirectory(Path.Join(PathsFolder, targetMethod.Name[4..])).FullName;
        }

        return Empty(type);
    }

    // Nothing of a type: no text, a finished task, an empty list, or a new instance where one can be made.
    private static object? Empty(Type type)
    {
        if (type == typeof(void) || type == typeof(string))
        {
            return null;
        }

        if (type == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = type.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { Empty(inner) });
        }

        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, 0);
        }

        if (type.IsGenericType && type.IsInterface && typeof(IEnumerable).IsAssignableFrom(type))
        {
            var element = type.GetGenericArguments()[0];
            return Array.CreateInstance(element, 0) is var empty && type.IsInstanceOfType(empty)
                ? empty
                : Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        return !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(type) : null;
    }
}
