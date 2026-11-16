using AtomUI.Desktop.Controls;
using Avalonia.Controls.Primitives;

namespace TickerQ.TestLicenseIssuer.Desktop;

public sealed class SelectionDatePicker : DatePicker
{
    protected override Type StyleKeyOverride => typeof(DatePicker);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // The picker's read-only setting also disables its popup. Protect only the text field.
        if (InfoInputBox is not null)
            InfoInputBox.IsReadOnly = true;
    }
}
