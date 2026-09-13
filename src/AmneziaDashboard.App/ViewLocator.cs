using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using AmneziaDashboard.App.ViewModels;

using AmneziaDashboard.App.Services;
namespace AmneziaDashboard.App;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var viewModelType = data.GetType();

        var viewModelName = viewModelType.Name;

        if (!viewModelName.EndsWith("ViewModel"))
        {
            return new TextBlock
            {
                Text = LocalizationService.T($"Unknown view model: {viewModelName}", $"Неизвестная модель: {viewModelName}")
            };
        }

        var viewName =
            viewModelName.Substring(
                0,
                viewModelName.Length - "ViewModel".Length)
            + "View";

        var fullViewName =
            $"AmneziaDashboard.App.Views.{viewName}";

        var viewType =
            viewModelType.Assembly.GetType(fullViewName);

        if (viewType is null)
        {
            return new TextBlock
            {
                Text = LocalizationService.T($"View not found: {fullViewName}", $"Представление не найдено: {fullViewName}")
            };
        }

        return Activator.CreateInstance(viewType) as Control;
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}