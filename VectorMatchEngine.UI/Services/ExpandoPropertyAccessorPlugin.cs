using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Data.Core.Plugins;

namespace VectorMatchEngine.UI.Services;

/// <summary>
/// Teaches Avalonia's binding system to read keys out of an ExpandoObject.
///
/// Avalonia's default fallback accessor (InpcPropertyAccessorPlugin) resolves paths by CLR
/// reflection. An ExpandoObject exposes its members through IDictionary&lt;string, object&gt;
/// rather than as real properties, so a Binding("A0") finds nothing and the cell silently renders
/// empty. Registering this plugin ahead of the defaults makes the dynamic MatchResults grid work.
/// </summary>
public class ExpandoPropertyAccessorPlugin : IPropertyAccessorPlugin
{
    public bool Match(object obj, string propertyName) => obj is IDictionary<string, object?>;

    public IPropertyAccessor? Start(WeakReference<object?> reference, string propertyName)
    {
        if (!reference.TryGetTarget(out var target) || target is not IDictionary<string, object?> dictionary)
            return null;

        return new ExpandoAccessor(dictionary, propertyName);
    }

    private sealed class ExpandoAccessor : PropertyAccessorBase
    {
        private readonly IDictionary<string, object?> _source;
        private readonly string _key;

        public ExpandoAccessor(IDictionary<string, object?> source, string key)
        {
            _source = source;
            _key = key;
        }

        public override Type? PropertyType => _source.TryGetValue(_key, out var value) && value is not null
            ? value.GetType()
            : typeof(object);

        public override object? Value => _source.TryGetValue(_key, out var value) ? value : null;

        public override bool SetValue(object? value, BindingPriority priority)
        {
            _source[_key] = value;
            PublishValue(value);
            return true;
        }

        protected override void SubscribeCore()
        {
            if (_source is INotifyPropertyChanged notifier)
                notifier.PropertyChanged += OnSourcePropertyChanged;

            PublishValue(Value);
        }

        protected override void UnsubscribeCore()
        {
            if (_source is INotifyPropertyChanged notifier)
                notifier.PropertyChanged -= OnSourcePropertyChanged;
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == _key || string.IsNullOrEmpty(e.PropertyName))
                PublishValue(Value);
        }
    }
}
