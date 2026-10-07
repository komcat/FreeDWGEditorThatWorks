using System.Windows;

namespace FreeDWGEditorThatWorks.Views;

/// <summary>Asks for a name. An empty one is not an answer, so OK stays put.</summary>
public partial class NameWindow : Window
{
    public NameWindow(string title, string prompt, string name)
    {
        InitializeComponent();

        Title = title;
        Prompt.Text = prompt;
        NameBox.Text = name;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>The name typed, trimmed; meaningful once the dialog returns true.</summary>
    public string Result => NameBox.Text.Trim();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (Result.Length == 0)
        {
            NameBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
