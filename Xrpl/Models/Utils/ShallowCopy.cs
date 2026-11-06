using System;
using System.Reflection;

namespace Xrpl.Models.Utils;

/// <summary>
/// A member-wise copy of a model. Not a JSON round trip: that drops null members.
/// </summary>
internal static class ShallowCopy
{
    private static readonly Func<object, object> MemberwiseClone = (Func<object, object>)Delegate.CreateDelegate(
        typeof(Func<object, object>),
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!);

    internal static T Of<T>(T value) where T : class => (T)MemberwiseClone(value);
}
