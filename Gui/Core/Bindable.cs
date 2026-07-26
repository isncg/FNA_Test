using System;

namespace FNA.Gui
{
    /// <summary>
    /// A lightweight observable value for MVVM-lite data binding.
    /// Fires <see cref="Changed"/> when the value is modified.
    /// Zero-reflection, AOT-safe.
    /// </summary>
    public class Bindable<T>
    {
        private T _value;

        public T Value
        {
            get => _value;
            set
            {
                if (!Equals(_value, value))
                {
                    _value = value;
                    Changed?.Invoke(_value);
                }
            }
        }

        /// <summary>Fired when <see cref="Value"/> changes. Passes the new value.</summary>
        public event Action<T>? Changed;

        public Bindable(T initialValue = default!)
        {
            _value = initialValue;
        }

        public static implicit operator T(Bindable<T> b) => b.Value;

        public override string ToString() => _value?.ToString() ?? "null";
    }

    /// <summary>
    /// Static helper for creating bindings between Bindable values and widget properties.
    /// </summary>
    public static class Binding
    {
        /// <summary>
        /// Bind a Bindable source to a widget property setter (one-way: source → UI).
        /// Returns an IDisposable that unsubscribes when disposed.
        /// The binding is evaluated immediately.
        /// </summary>
        public static IDisposable OneWay<TSource>(
            Bindable<TSource> source,
            Action<TSource> setter)
        {
            setter(source.Value);
            source.Changed += setter;
            return new BindingDisposable(() => source.Changed -= setter);
        }

        /// <summary>
        /// Bind a widget event to a Bindable (one-way: UI → source).
        /// Returns an IDisposable that unsubscribes when disposed.
        /// </summary>
        public static IDisposable FromWidget<TValue>(
            Action<Action<TValue>> subscribe,
            Action<Action<TValue>> unsubscribe,
            Bindable<TValue> target)
        {
            Action<TValue> handler = v => target.Value = v;
            subscribe(handler);
            return new BindingDisposable(() => unsubscribe(handler));
        }

        private class BindingDisposable : IDisposable
        {
            private Action? _unsubscribe;
            public BindingDisposable(Action unsubscribe) => _unsubscribe = unsubscribe;
            public void Dispose() { _unsubscribe?.Invoke(); _unsubscribe = null; }
        }
    }
}
