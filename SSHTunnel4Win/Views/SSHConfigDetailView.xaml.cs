using System.Windows.Controls;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public partial class SSHConfigDetailView : UserControl
{
    public SSHConfigDetailView()
    {
        InitializeComponent();
    }

    public void Initialize(SSHConfigDetailViewModel vm)
    {
        DataContext = vm;
    }
}
