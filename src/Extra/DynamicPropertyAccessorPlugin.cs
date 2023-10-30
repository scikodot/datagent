// Dynamic property accessor for binding dynamic objects to views;
// see https://github.com/AvaloniaUI/Avalonia/blob/11.0.5/src/Avalonia.Base/Data/Core/Plugins/InpcPropertyAccessorPlugin.cs

using Avalonia.Data;
using Avalonia.Data.Core.Plugins;
using Avalonia.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace Datagent.Plugins;

/// <summary>
/// Reads a property from a dynamic C# object that optionally supports the
/// <see cref="INotifyPropertyChanged"/> interface.
/// </summary>
internal class DynamicPropertyAccessorPlugin : IPropertyAccessorPlugin
{
    /// <inheritdoc/>
    public bool Match(object obj, string propertyName)
    {
        if (obj is IDictionary<string, object> exp)
            return exp.ContainsKey(propertyName);

        return false;
    }

    /// <summary>
    /// Starts monitoring the value of a property on an object.
    /// </summary>
    /// <param name="reference">A weak reference to the dynamic object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>
    /// An <see cref="IPropertyAccessor"/> interface through which future interactions with the 
    /// property will be made.
    /// </returns>
    public IPropertyAccessor? Start(WeakReference<object?> reference, string propertyName)
    {
        _ = reference ?? throw new ArgumentNullException(nameof(reference));
        _ = propertyName ?? throw new ArgumentNullException(nameof(propertyName));

        if (!reference.TryGetTarget(out var instance) || instance is null)
            return null;

        if (Match(instance, propertyName))
        {
            return new Accessor(reference, propertyName);
        }
        else
        {
            var message = $"Could not find dynamic property '{propertyName}' on '{instance}'";
            var exception = new MissingMemberException(message);
            return new PropertyError(new BindingNotification(exception, BindingErrorType.Error));
        }
    }

    private class Accessor : PropertyAccessorBase, IWeakEventSubscriber<PropertyChangedEventArgs>
    {
        private readonly WeakReference<object?> _reference;
        private readonly string _property;
        private bool _eventRaised;

        public Accessor(WeakReference<object?> reference, string property)
        {
            _ = reference ?? throw new ArgumentNullException(nameof(reference));
            _ = property ?? throw new ArgumentNullException(nameof(property));

            _reference = reference;
            _property = property;
        }

        public override Type? PropertyType => Value?.GetType();

        public override object? Value => GetReferenceTarget()?[_property];

        public override bool SetValue(object? value, BindingPriority priority)
        {
            _eventRaised = false;

            // See PropertyInfo.SetValue(obj, value)
            var target = GetReferenceTarget();
            if (target == null)
                throw new TargetException(nameof(target));
             target[_property] = value;

            if (!_eventRaised)
                SendCurrentValue();

            return true;
        }

        void IWeakEventSubscriber<PropertyChangedEventArgs>.
            OnEvent(object? notifyPropertyChanged, WeakEvent ev, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == _property || string.IsNullOrEmpty(e.PropertyName))
            {
                _eventRaised = true;
                SendCurrentValue();
            }
        }

        protected override void SubscribeCore()
        {
            SubscribeToChanges();
            SendCurrentValue();
        }

        protected override void UnsubscribeCore()
        {
            if (GetReferenceTarget() is INotifyPropertyChanged inpc)
                WeakEvents.ThreadSafePropertyChanged.Unsubscribe(inpc, this);
        }

        private IDictionary<string, object>? GetReferenceTarget()
        {
            _reference.TryGetTarget(out var target);

            return target as IDictionary<string, object>;
        }

        private void SendCurrentValue()
        {
            try
            {
                PublishValue(Value);
            }
            catch { }
        }

        private void SubscribeToChanges()
        {
            if (GetReferenceTarget() is INotifyPropertyChanged inpc)
                WeakEvents.ThreadSafePropertyChanged.Subscribe(inpc, this);
        }
    }
}
