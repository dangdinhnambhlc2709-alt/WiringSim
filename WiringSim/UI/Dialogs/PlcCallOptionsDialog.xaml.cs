using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WiringSim.UI.Services;

namespace WiringSim.UI.Dialogs;

/// <summary>
/// Hộp thoại Call options chuẩn TIA Portal V21 khi thêm khối Timer / Counter trên Ladder:
/// - Cột trái: Single instance + biểu tượng DB.
/// - Khung phải: Data block (Name, Number, Automatic / Manual) + OK / Cancel.
/// </summary>
public partial class PlcCallOptionsDialog : Window
{
    private readonly int _autoDbNumber;
    private readonly string _suggestedName;

    public string SelectedDbName { get; private set; }
    public int SelectedDbNumber { get; private set; }
    public bool IsAutomaticMode => RbAutomatic.IsChecked == true;

    public PlcCallOptionsDialog(string instructionSymbol, string suggestedName, int autoDbNumber)
    {
        InitializeComponent();

        _autoDbNumber = Math.Clamp(autoDbNumber, 1, 99);
        _suggestedName = string.IsNullOrWhiteSpace(suggestedName) ? $"IEC_Timer_0_DB" : suggestedName.Trim();

        SelectedDbName = _suggestedName;
        SelectedDbNumber = _autoDbNumber;

        TxtSubtitle.Text = LocalizationManager.Format("Str.Plc.CallOptSubtitle", instructionSymbol);
        TxtAutoDesc.Text = LocalizationManager.Format("Str.Plc.CallOptAutoDesc", _autoDbNumber);
        TbDbName.Text = _suggestedName;
        TbDbNumber.Text = _autoDbNumber.ToString(CultureInfo.InvariantCulture);
        UpdateNumberControlsState();
    }

    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        TbDbName.Focus();
        TbDbName.SelectAll();
    }

    private void OnModeRadioChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        if (RbAutomatic.IsChecked == true)
            TbDbNumber.Text = _autoDbNumber.ToString(CultureInfo.InvariantCulture);
        UpdateNumberControlsState();
    }

    private void UpdateNumberControlsState()
    {
        bool manual = RbManual.IsChecked == true;
        TbDbNumber.IsEnabled = manual;
        BtnNumUp.IsEnabled = manual;
        BtnNumDown.IsEnabled = manual;
        UpdatePreview();
    }

    private void OnDbNumberTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        int num = ParseCurrentNumber();
        TxtDbPreview.Text = $"→ %DB{num}";
    }

    private int ParseCurrentNumber()
    {
        if (RbAutomatic.IsChecked == true) return _autoDbNumber;
        return int.TryParse(TbDbNumber.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? Math.Clamp(n, 1, 99)
            : _autoDbNumber;
    }

    private void OnNumberUpClick(object sender, RoutedEventArgs e)
    {
        int next = Math.Clamp(ParseCurrentNumber() + 1, 1, 99);
        TbDbNumber.Text = next.ToString(CultureInfo.InvariantCulture);
    }

    private void OnNumberDownClick(object sender, RoutedEventArgs e)
    {
        int prev = Math.Clamp(ParseCurrentNumber() - 1, 1, 99);
        TbDbNumber.Text = prev.ToString(CultureInfo.InvariantCulture);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        string name = TbDbName.Text.Trim();
        SelectedDbName = string.IsNullOrWhiteSpace(name) ? _suggestedName : name;
        SelectedDbNumber = ParseCurrentNumber();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
