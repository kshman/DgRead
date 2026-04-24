using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DgRead.Chaek;

namespace DgRead;

internal partial class OptionWindow : Window
{
    public OptionWindow()
    {
        InitializeComponent();
        ApplyLocalizedTexts();
        LoadValues();
    }

    private void ApplyLocalizedTexts()
    {
        Title = T("Settings");

        GeneralTabItem.Header = T("General");
        RunOnceCheckBox.Content = T("Run once");
        EscToExitCheckBox.Content = T("Exit with ESC");
        FileConfirmDeleteCheckBox.Content = T("Confirm before deleting file");
        ExternalProgramTextBlock.Text = T("External program");
        SearchPrefixTextBlock.Text = T("Search prefix");
        CacheSizeTextBlock.Text = T("Cache size (MB)");
        PageJumpTextBlock.Text = T("Page jump size");

        InputTabItem.Header = T("Input");
        KeyboardTabItem.Header = T("Keyboard");
        KeyboardDisabledTextBlock.Text = T("Keyboard settings are not available yet.");
        MouseTabItem.Header = T("Mouse");
        MouseDoubleFullscreenCheckBox.Content = T("Double click to toggle fullscreen");
        MouseClickPagingCheckBox.Content = T("Click edge to change page");
        ControllerTabItem.Header = T("Controller");
        ControllerDisabledTextBlock.Text = T("Controller settings are not available yet.");

        SecurityTabItem.Header = T("Security");
        PasswordCodeTextBlock.Text = T("Password");
        PasswordUsageTextBlock.Text = T("Password usage");
        PasswordStartCheckBox.Content = T("At startup");
        PasswordOptionCheckBox.Content = T("Open settings");
        PasswordBookmarkCheckBox.Content = T("Open bookmark manager");
        PasswordMoveCheckBox.Content = T("Open move dialog");
        PasswordRenameCheckBox.Content = T("Open rename dialog");
        PasswordLastBookCheckBox.Content = T("Open last book");

        AboutTabItem.Header = T("About");
        AboutAppTextBlock.Text = T("DgRead");
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AboutVersionTextBlock.Text = $"{T("Version")}: {version}";
        AboutDescriptionTextBlock.Text = T("Simple comic and image reader.");

        OkButton.Content = T("OK");
        CancelButton.Content = T("Cancel");
    }

    private void LoadValues()
    {
        RunOnceCheckBox.IsChecked = Configs.RunOnce;
        EscToExitCheckBox.IsChecked = Configs.EscToExit;
        FileConfirmDeleteCheckBox.IsChecked = Configs.FileConfirmDelete;
        ExternalProgramTextBox.Text = Configs.ExternalProgram;
        SearchPrefixTextBox.Text = Configs.SearchPrefix;
        CacheSizeNumericUpDown.Value = Configs.CacheMaxSize;
        PageJumpNumericUpDown.Value = Configs.PageJumpCount;
        MouseDoubleFullscreenCheckBox.IsChecked = Configs.MouseDoubleFullScreen;
        MouseClickPagingCheckBox.IsChecked = Configs.MouseClickPaging;
        PasswordCodeTextBox.Text = Configs.PasswordCode;

        var usages = new HashSet<PasswordUsage>(Configs.PasswordUsages);
        PasswordStartCheckBox.IsChecked = usages.Contains(PasswordUsage.Start);
        PasswordOptionCheckBox.IsChecked = usages.Contains(PasswordUsage.Option);
        PasswordBookmarkCheckBox.IsChecked = usages.Contains(PasswordUsage.Bookmark);
        PasswordMoveCheckBox.IsChecked = usages.Contains(PasswordUsage.Move);
        PasswordRenameCheckBox.IsChecked = usages.Contains(PasswordUsage.Rename);
        PasswordLastBookCheckBox.IsChecked = usages.Contains(PasswordUsage.LastBook);
    }

    public async Task<bool> ShowAsync(Window owner)
    {
        var result = await ShowDialog<bool>(owner);
        return result;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Configs.RunOnce = RunOnceCheckBox.IsChecked == true;
        Configs.EscToExit = EscToExitCheckBox.IsChecked == true;
        Configs.FileConfirmDelete = FileConfirmDeleteCheckBox.IsChecked == true;
        Configs.ExternalProgram = ExternalProgramTextBox.Text?.Trim() ?? string.Empty;
        Configs.SearchPrefix = SearchPrefixTextBox.Text?.Trim() ?? string.Empty;
        Configs.CacheMaxSize = (int)Math.Round(Convert.ToDouble(CacheSizeNumericUpDown.Value));
        Configs.PageJumpCount = (int)Math.Round(Convert.ToDouble(PageJumpNumericUpDown.Value));
        Configs.MouseDoubleFullScreen = MouseDoubleFullscreenCheckBox.IsChecked == true;
        Configs.MouseClickPaging = MouseClickPagingCheckBox.IsChecked == true;

        Configs.PasswordCode = PasswordCodeTextBox.Text ?? string.Empty;
        var usages = new List<PasswordUsage>();
        if (PasswordStartCheckBox.IsChecked == true) usages.Add(PasswordUsage.Start);
        if (PasswordOptionCheckBox.IsChecked == true) usages.Add(PasswordUsage.Option);
        if (PasswordBookmarkCheckBox.IsChecked == true) usages.Add(PasswordUsage.Bookmark);
        if (PasswordMoveCheckBox.IsChecked == true) usages.Add(PasswordUsage.Move);
        if (PasswordRenameCheckBox.IsChecked == true) usages.Add(PasswordUsage.Rename);
        if (PasswordLastBookCheckBox.IsChecked == true) usages.Add(PasswordUsage.LastBook);
        Configs.SetPasswordUsages(usages);

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) =>
        Close(false);
}
