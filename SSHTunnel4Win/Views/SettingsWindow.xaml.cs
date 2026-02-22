using System.Windows;
using SSHTunnel4Win.Converters;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Resources.Add("InvBool", new InverseBoolConverter());
    }
}
