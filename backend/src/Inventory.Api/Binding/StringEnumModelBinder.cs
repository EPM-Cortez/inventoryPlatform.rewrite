using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Inventory.Api.Binding;

internal sealed class StringEnumModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var modelType =
            Nullable.GetUnderlyingType(context.Metadata.ModelType) ??
            context.Metadata.ModelType;
        return modelType.IsEnum
            ? new StringEnumModelBinder(modelType)
            : null;
    }
}

internal sealed class StringEnumModelBinder : IModelBinder
{
    private readonly Type _enumType;
    private readonly IReadOnlyDictionary<string, string> _names;

    public StringEnumModelBinder(Type enumType)
    {
        _enumType = enumType;
        _names = Enum.GetNames(enumType)
            .ToDictionary(
                name => name,
                name => JsonNamingPolicy.CamelCase.ConvertName(name),
                StringComparer.OrdinalIgnoreCase);
    }

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
        var valueResult = bindingContext.ValueProvider.GetValue(
            bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(
            bindingContext.ModelName,
            valueResult);
        var value = valueResult.FirstValue?.Trim();
        var matchingName = _names.Keys.FirstOrDefault(
            name =>
                string.Equals(name, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    _names[name],
                    value,
                    StringComparison.OrdinalIgnoreCase));
        if (matchingName is null)
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                $"The value '{value}' is not valid for " +
                $"{bindingContext.ModelName}. Allowed values: " +
                $"{string.Join(", ", _names.Values)}.");
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(
            Enum.Parse(_enumType, matchingName));
        return Task.CompletedTask;
    }
}
