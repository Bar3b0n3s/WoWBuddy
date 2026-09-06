using System.Windows;
using Microsoft.Win32;
using WoWBuddy.Presentation;

namespace WoWBuddy.UI;

/// <summary>
/// The main window.
/// </summary>
/// <remarks>
/// Deliberately almost empty. Everything the window decides lives in
/// <see cref="MainViewModel"/>, where a test can reach it: the rules about what the user may do
/// next are the part of a UI that goes wrong, and they are only checkable if they are not
/// tangled up with the controls.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow(MainViewModel model)
    {
        _model = model ?? throw new System.ArgumentNullException(nameof(model));

        InitializeComponent();

        DataContext = _model;
    }

    /// <summary>
    /// Picks a profile file.
    /// </summary>
    /// <remarks>
    /// One of the two things that genuinely belong in code-behind: a file dialog is a window,
    /// and a view model that opened one could not be tested without one.
    /// </remarks>
    private void OnBrowseProfileClicked(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Choose a profile",
            Filter = "WoWBuddy profiles (*.xml)|*.xml|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            _model.ProfilePath = dialog.FileName;
        }
    }
}
