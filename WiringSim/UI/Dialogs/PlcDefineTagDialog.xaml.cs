using System.Windows;
using System.Windows.Controls;
using WiringSim.Models.Plc;
using WiringSim.UI.Services;

namespace WiringSim.UI.Dialogs;

/// <summary>
/// Hộp thoại khai báo Tag tại chỗ (Define Tag — Alt+Shift+D) trên trình soạn thảo Ladder.
/// </summary>
public partial class PlcDefineTagDialog : Window
{
    private readonly PlcProgramModel _program;
    private bool _addressManuallyEdited;

    public string TagName { get; private set; }
    public string TagDataType { get; private set; }
    public string TagAddress { get; private set; }
    public string TagComment { get; private set; } = string.Empty;

    public PlcDefineTagDialog(PlcProgramModel program, string suggestedName, string suggestedAddress, string suggestedType)
    {
        InitializeComponent();
        _program = program;

        TagName = PlcAddressHelper.StripQuotes(suggestedName);
        TagDataType = string.IsNullOrWhiteSpace(suggestedType) ? "Bool" : suggestedType;
        TagAddress = string.IsNullOrWhiteSpace(suggestedAddress) ? SuggestFreeAddress(TagDataType) : PlcProgramModel.NormalizeAddress(suggestedAddress);

        foreach (string dt in PlcAddressHelper.SupportedDataTypes)
            CbDataType.Items.Add(dt);
        CbDataType.SelectedItem = CbDataType.Items.Cast<string>()
            .FirstOrDefault(x => x.Equals(TagDataType, StringComparison.OrdinalIgnoreCase)) ?? "Bool";

        TbTagName.Text = TagName;
        TbAddress.Text = TagAddress;
        TbAddress.TextChanged += (_, _) => _addressManuallyEdited = true;
    }

    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbTagName.Text))
        {
            TbTagName.Focus();
            TbTagName.SelectAll();
        }
        else
        {
            TbAddress.Focus();
            TbAddress.SelectAll();
        }
    }

    private void OnDataTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _addressManuallyEdited) return;
        string dt = CbDataType.SelectedItem as string ?? "Bool";
        TbAddress.Text = SuggestFreeAddress(dt);
        _addressManuallyEdited = false;
    }

    private string SuggestFreeAddress(string dataType)
    {
        var used = new HashSet<string>(_program.Tags.Select(t => PlcProgramModel.NormalizeAddress(t.Address)), StringComparer.OrdinalIgnoreCase);
        foreach (var sys in _program.GetSystemAndClockTags())
            used.Add(PlcProgramModel.NormalizeAddress(sys.Address));

        if (dataType.Equals("Bool", StringComparison.OrdinalIgnoreCase))
        {
            for (int b = 2; b < 50; b++)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    string candidate = $"%M{b}.{bit}";
                    if (!used.Contains(candidate)) return candidate;
                }
            }
            return "%M10.0";
        }

        if (dataType is "Byte" or "SInt" or "USInt")
        {
            for (int b = 2; b < 190; b++)
            {
                string candidate = $"%MB{b}";
                if (!used.Contains(candidate)) return candidate;
            }
            return "%MB10";
        }

        if (dataType is "Int" or "UInt" or "Word")
        {
            for (int b = 2; b < 190; b += 2)
            {
                string candidate = $"%MW{b}";
                if (!used.Contains(candidate)) return candidate;
            }
            return "%MW10";
        }

        for (int b = 4; b < 190; b += 4)
        {
            string candidate = $"%MD{b}";
            if (!used.Contains(candidate)) return candidate;
        }
        return "%MD10";
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        string name = PlcAddressHelper.StripQuotes(TbTagName.Text.Trim());
        string dt = CbDataType.SelectedItem as string ?? "Bool";
        string addr = PlcProgramModel.NormalizeAddress(TbAddress.Text.Trim());

        if (string.IsNullOrWhiteSpace(name))
        {
            TxtError.Text = LocalizationManager.Get("Str.Plc.DefineTagErrName");
            TbTagName.Focus();
            return;
        }

        var op = OperandResolver.Resolve(addr, _program);
        if (!op.IsValid || op.IsLiteral || op.IsDbInstance)
        {
            TxtError.Text = op.IsValid ? LocalizationManager.Format("Str.Plc.ErrInvalidAddr", addr) : op.ErrorMessage;
            TbAddress.Focus();
            return;
        }

        if (op.IsSystemOrClockBit)
        {
            TxtError.Text = LocalizationManager.Format("Str.Plc.DefineTagErrSysClk", op.NormalizedText);
            TbAddress.Focus();
            return;
        }

        // Kiểm tra tương thích kích thước giữa DataType và địa chỉ (Bit / Byte / Word / DWord)
        string opType = op.AccessSizeBytes switch
        {
            0 => "Bit",
            1 => "Byte",
            2 => "Word",
            _ => "DWord"
        };
        if (op.SliceKind == PlcSliceKind.None && !PlcAddressHelper.IsDataTypeCompatibleWithOperandType(dt, opType))
        {
            TxtError.Text = LocalizationManager.Format("Str.Plc.DefineTagErrTypeMismatch", dt, op.NormalizedText);
            CbDataType.Focus();
            return;
        }

        TagName = name;
        TagDataType = dt;
        TagAddress = op.NormalizedText;
        TagComment = TbComment.Text.Trim();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
