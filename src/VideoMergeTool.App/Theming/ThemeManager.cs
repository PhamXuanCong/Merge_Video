using System.IO;
using System.Security;
using System.Windows;
using Microsoft.Win32;
using VideoMergeTool.Core.Enums;

namespace VideoMergeTool.App.Theming;

/// <summary>
/// Swaps the palette dictionary merged at index 0 of the application resources, and keeps it in
/// step with the Windows app theme while the preference is <see cref="ThemePreference.System"/>.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private static readonly Uri LightPalette = new("Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkPalette = new("Themes/Dark.xaml", UriKind.Relative);

    private ThemePreference _preference = ThemePreference.System;
    private bool _isDark;
    private bool _hasApplied;
    private bool _isDisposed;

    public ThemeManager()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Raised after the palette actually changed, so windows can restyle their chrome.</summary>
    public event EventHandler? ThemeChanged;

    public bool IsDark => _isDark;

    public void Apply(ThemePreference preference)
    {
        _preference = preference;

        var shouldBeDark = preference switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            _ => IsSystemDark()
        };

        if (_hasApplied && shouldBeDark == _isDark)
        {
            return;
        }

        var dictionaries = Application.Current?.Resources.MergedDictionaries;
        if (dictionaries is null || dictionaries.Count == 0)
        {
            return;
        }

        dictionaries[0] = new ResourceDictionary { Source = shouldBeDark ? DarkPalette : LightPalette };
        _isDark = shouldBeDark;
        _hasApplied = true;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightThemeValue) is int value && value == 0;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || _preference != ThemePreference.System)
        {
            return;
        }

        // SystemEvents raises this on its own listener thread.
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(ThemePreference.System));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
