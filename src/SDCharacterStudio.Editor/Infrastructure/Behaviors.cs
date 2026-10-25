using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace SDCharacterStudio.Editor.Infrastructure;

/// <summary>Small attached behaviours used by the views.</summary>
public static class Behaviors
{
    // ---- Enter commits a single-line text box (its binding updates on lost focus otherwise). ----
    public static readonly DependencyProperty CommitOnEnterProperty = DependencyProperty.RegisterAttached(
        "CommitOnEnter", typeof(bool), typeof(Behaviors), new PropertyMetadata(false, OnCommitOnEnterChanged));
    public static bool GetCommitOnEnter(DependencyObject d) => (bool)d.GetValue(CommitOnEnterProperty);
    public static void SetCommitOnEnter(DependencyObject d, bool value) => d.SetValue(CommitOnEnterProperty, value);

    private static void OnCommitOnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewKeyDown -= CommitKeyDown;
        if ((bool)e.NewValue) element.PreviewKeyDown += CommitKeyDown;
    }

    private static void CommitKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not DependencyObject d) return;
        if (sender is TextBox { AcceptsReturn: true } && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        Commit(d);
        if (sender is TextBox box) box.SelectAll();
    }

    /// <summary>Push a text box's (or editable combo box's) pending text to its source.</summary>
    public static void Commit(DependencyObject? element)
    {
        for (var d = element; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            switch (d)
            {
                case Wpf.Ui.Controls.NumberBox number:
                    if (double.TryParse(number.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double value)
                        || double.TryParse(number.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                        number.Value = value;
                    BindingOperations.GetBindingExpression(number, Wpf.Ui.Controls.NumberBox.ValueProperty)?.UpdateSource();
                    return;
                case ComboBox { IsEditable: true } combo:
                    BindingOperations.GetBindingExpression(combo, ComboBox.TextProperty)?.UpdateSource();
                    return;
                case TextBox box:
                    BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.UpdateSource();
                    if (box.TemplatedParent is not (ComboBox or Wpf.Ui.Controls.NumberBox)) return;
                    break;
            }
            if (d is Window) return;
        }
    }

    // ---- Select all on focus (number and tile boxes). ----
    public static readonly DependencyProperty SelectAllOnFocusProperty = DependencyProperty.RegisterAttached(
        "SelectAllOnFocus", typeof(bool), typeof(Behaviors), new PropertyMetadata(false, (d, e) =>
        {
            if (d is TextBox box && (bool)e.NewValue) box.GotKeyboardFocus += (_, _) => box.Dispatcher.BeginInvoke(box.SelectAll);
        }));
    public static bool GetSelectAllOnFocus(DependencyObject d) => (bool)d.GetValue(SelectAllOnFocusProperty);
    public static void SetSelectAllOnFocus(DependencyObject d, bool value) => d.SetValue(SelectAllOnFocusProperty, value);

    // ---- Keep a list scrolled to its newest line (Output panel). ----
    public static readonly DependencyProperty AutoScrollProperty = DependencyProperty.RegisterAttached(
        "AutoScroll", typeof(bool), typeof(Behaviors), new PropertyMetadata(false, (d, e) =>
        {
            if (d is ListBox list && (bool)e.NewValue && list.Items is System.Collections.Specialized.INotifyCollectionChanged items)
                items.CollectionChanged += (_, args) =>
                {
                    if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && list.Items.Count > 0)
                        list.ScrollIntoView(list.Items[^1]);
                };
        }));
    public static bool GetAutoScroll(DependencyObject d) => (bool)d.GetValue(AutoScrollProperty);
    public static void SetAutoScroll(DependencyObject d, bool value) => d.SetValue(AutoScrollProperty, value);
}

public static class DialogBehaviors
{
    /// <summary>Double-clicking a list item in the item picker chooses it.</summary>
    public static readonly DependencyProperty ChooseOnDoubleClickProperty = DependencyProperty.RegisterAttached(
        "ChooseOnDoubleClick", typeof(bool), typeof(DialogBehaviors), new PropertyMetadata(false, (d, e) =>
        {
            if (d is ListBox list && (bool)e.NewValue)
                list.MouseDoubleClick += (_, args) =>
                {
                    if ((args.OriginalSource as FrameworkElement)?.DataContext is ViewModels.ItemPickerViewModel.ItemOption
                        && list.DataContext is ViewModels.ItemPickerViewModel picker)
                        picker.ChooseCommand.Execute(null);
                };
        }));
    public static bool GetChooseOnDoubleClick(DependencyObject d) => (bool)d.GetValue(ChooseOnDoubleClickProperty);
    public static void SetChooseOnDoubleClick(DependencyObject d, bool value) => d.SetValue(ChooseOnDoubleClickProperty, value);
}
