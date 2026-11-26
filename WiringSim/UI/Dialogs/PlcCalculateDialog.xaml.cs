using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WiringSim.Models.Enums;
using WiringSim.Models.Plc;
using WiringSim.UI.Services;

namespace WiringSim.UI.Dialogs;

/// <summary>
/// Hộp thoại chỉnh sửa biểu thức và kiểu dữ liệu cho khối CALCULATE chuẩn IEC 61131-3.
/// Tự động phân tích số chân IN1..INn cần thiết từ biểu thức toán học.
/// </summary>
public partial class PlcCalculateDialog : Window
{
    public string SelectedDataType { get; private set; }
    public string ExpressionText { get; private set; }
    public int RequiredInputs { get; private set; } = 2;

    public PlcCalculateDialog(string currentDataType, string currentExpression)
    {
        InitializeComponent();

        SelectedDataType = string.IsNullOrWhiteSpace(currentDataType) ? "Real" : currentDataType;
        ExpressionText = string.IsNullOrWhiteSpace(currentExpression) ? "IN1 + IN2" : currentExpression;

        foreach (string dt in PlcInstructionCatalog.GetSupportedDataTypesForInstruction(LadderElementKind.Calculate))
            CbDataType.Items.Add(dt);
        CbDataType.SelectedItem = CbDataType.Items.Cast<string>()
            .FirstOrDefault(x => x.Equals(SelectedDataType, StringComparison.OrdinalIgnoreCase)) ?? "Real";

        BuildQuickTokens();
        TbExpression.Text = ExpressionText;
        ValidateLive();
    }

    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        TbExpression.Focus();
        TbExpression.CaretIndex = TbExpression.Text.Length;
    }

    private void BuildQuickTokens()
    {
        string[] operators = ["+", "-", "*", "/", "MOD", "**", "(", ")", "IN1", "IN2", "IN3", "IN4", "IN5"];
        foreach (string op in operators)
            AddTokenButton(op, op, "#38BDF8");

        foreach (string fn in PlcExpressionEvaluator.SupportedFunctions)
            AddTokenButton(fn, $"{fn}()", "#EC4899");
    }

    private void AddTokenButton(string label, string insertText, string accentHex)
    {
        var btn = new Button
        {
            Content = label,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(7, 2.5, 7, 2.5),
            Margin = new Thickness(0, 0, 5, 5),
            Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(accentHex)!
        };
        btn.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btn.Click += (_, _) =>
        {
            int idx = TbExpression.CaretIndex;
            string cur = TbExpression.Text ?? string.Empty;
            TbExpression.Text = cur.Insert(idx, insertText);
            TbExpression.Focus();
            TbExpression.CaretIndex = insertText.EndsWith("()", StringComparison.Ordinal)
                ? idx + insertText.Length - 1
                : idx + insertText.Length;
        };
        FuncsWrapPanel.Children.Add(btn);
    }

    private void OnExpressionChanged(object sender, TextChangedEventArgs e) => ValidateLive();

    private void ValidateLive()
    {
        string expr = TbExpression.Text ?? string.Empty;
        if (PlcExpressionEvaluator.TryValidate(expr, out int req, out string err))
        {
            RequiredInputs = req;
            TxtDetectedInputs.Text = LocalizationManager.Format("Str.Plc.CalcInputsDetected", req);
            TxtValidation.Text = LocalizationManager.Get("Str.Plc.CalcExprOk");
            TxtValidation.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#22C55E")!;
            BtnOk.IsEnabled = true;
        }
        else
        {
            TxtDetectedInputs.Text = string.Empty;
            TxtValidation.Text = "⚠ " + err;
            TxtValidation.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#EF4444")!;
            BtnOk.IsEnabled = false;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!PlcExpressionEvaluator.TryValidate(TbExpression.Text, out int req, out _))
            return;

        SelectedDataType = CbDataType.SelectedItem as string ?? "Real";
        ExpressionText = TbExpression.Text.Trim();
        RequiredInputs = req;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
