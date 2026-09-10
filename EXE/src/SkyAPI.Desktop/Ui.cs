using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SkyAPI.Desktop;

/// <summary>
/// Estilos dos controles. Tudo é montado em código a partir da paleta de <see cref="Theme"/>,
/// para que o tema claro e o escuro fiquem consistentes até nos campos, tabelas e barras de rolagem.
/// </summary>
public static class Ui {
    public const string Icons = "Segoe MDL2 Assets";

    /// <summary>
    /// Exemplo mostrado enquanto o campo está vazio, inclusive com foco. O template acompanha o padding
    /// do editor e a margem interna reservada pelo WPF para o cursor; nunca insere o exemplo em Text.
    /// </summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached(
        "Hint", typeof(string), typeof(Ui), new PropertyMetadata(""));
    public static void SetHint(DependencyObject target, string value) => target.SetValue(HintProperty, value);
    public static string GetHint(DependencyObject target) => (string)target.GetValue(HintProperty);

    public static readonly ComponentResourceKey PrimaryButton = new(typeof(Ui), "PrimaryButton");
    public static readonly ComponentResourceKey QuietButton = new(typeof(Ui), "QuietButton");
    public static readonly ComponentResourceKey NavButton = new(typeof(Ui), "NavButton");
    public static readonly ComponentResourceKey NavButtonActive = new(typeof(Ui), "NavButtonActive");
    public static readonly ComponentResourceKey CardButton = new(typeof(Ui), "CardButton");
    public static readonly ComponentResourceKey LinkButton = new(typeof(Ui), "LinkButton");

    private static FrameworkElementFactory F<T>(string? name = null) {
        var f = new FrameworkElementFactory(typeof(T));
        if (name != null) f.Name = name;
        return f;
    }

    private static void Tie(FrameworkElementFactory f, DependencyProperty target, string path) =>
        f.SetBinding(target, new Binding(path) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

    private static Trigger On(DependencyProperty property, object value, params (string element, DependencyProperty prop, object val)[] setters) {
        var trigger = new Trigger { Property = property, Value = value };
        foreach (var (element, prop, val) in setters)
            trigger.Setters.Add(element.Length == 0 ? new Setter(prop, val) : new Setter(prop, val, element));
        return trigger;
    }

    /// <summary>Reinstala todos os estilos no dicionário da janela. Chamado a cada troca de tema.</summary>
    public static void Install(FrameworkElement target) {
        var r = target.Resources;
        r[typeof(Button)] = Button(false);
        r[PrimaryButton] = Button(true);
        r[QuietButton] = Quiet();
        r[NavButton] = Nav(false);
        r[NavButtonActive] = Nav(true);
        r[CardButton] = Card();
        r[LinkButton] = Link();
        r[typeof(TextBox)] = TextBoxStyle();
        r[typeof(PasswordBox)] = PasswordStyle();
        r[typeof(ComboBox)] = ComboStyle();
        r[typeof(DatePicker)] = DatePickerStyle();
        r[typeof(DatePickerTextBox)] = DatePickerFieldStyle();
        r[typeof(ComboBoxItem)] = ComboItemStyle();
        r[typeof(Label)] = LabelStyle();
        r[typeof(ProgressBar)] = ProgressStyle();
        r[typeof(ScrollBar)] = ScrollBarStyle();
        r[typeof(DataGrid)] = GridStyle();
        r[typeof(DataGridColumnHeader)] = HeaderStyle();
        r[typeof(DataGridCell)] = CellStyle();
        r[typeof(DataGridRow)] = RowStyle();
        r[typeof(ToolTip)] = TipStyle();
    }

    // ---------------------------------------------------------------- botões

    private static ControlTemplate ButtonTemplate(double radius) {
        var border = F<Border>("bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        Tie(border, Border.BackgroundProperty, "Background");
        Tie(border, Border.BorderBrushProperty, "BorderBrush");
        Tie(border, Border.BorderThicknessProperty, "BorderThickness");
        Tie(border, Border.PaddingProperty, "Padding");

        var presenter = F<ContentPresenter>();
        Tie(presenter, FrameworkElement.HorizontalAlignmentProperty, "HorizontalContentAlignment");
        Tie(presenter, FrameworkElement.VerticalAlignmentProperty, "VerticalContentAlignment");
        presenter.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static Style Button(bool primary) {
        var s = new Style(typeof(Button));
        s.Setters.Add(new Setter(Control.BackgroundProperty, primary ? Theme.Accent : Theme.Surface));
        s.Setters.Add(new Setter(Control.ForegroundProperty, primary ? Theme.AccentInk : Theme.Accent));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, primary ? Theme.Accent : Theme.Line));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 9, 16, 9)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 14.0));
        s.Setters.Add(new Setter(Control.FontWeightProperty, primary ? FontWeights.SemiBold : FontWeights.Normal));
        s.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        var template = ButtonTemplate(8);
        template.Triggers.Add(On(UIElement.IsMouseOverProperty, true,
            ("bd", Border.BackgroundProperty, primary ? Theme.AccentHover : Theme.Hover),
            ("bd", Border.BorderBrushProperty, primary ? Theme.AccentHover : Theme.Accent)));
        template.Triggers.Add(On(ButtonBase.IsPressedProperty, true,
            ("bd", Border.BackgroundProperty, primary ? Theme.AccentHover : Theme.AccentSoft)));
        template.Triggers.Add(On(UIElement.IsKeyboardFocusedProperty, true,
            ("bd", Border.BorderBrushProperty, Theme.Accent)));
        template.Triggers.Add(On(UIElement.IsEnabledProperty, false, ("", UIElement.OpacityProperty, 0.42)));
        s.Setters.Add(new Setter(Control.TemplateProperty, template));
        return s;
    }

    /// <summary>Botão sem moldura, para ações discretas (ajuste de texto, tema).</summary>
    private static Style Quiet() {
        var s = new Style(typeof(Button), null);
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.Line));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        s.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        var template = ButtonTemplate(7);
        template.Triggers.Add(On(UIElement.IsMouseOverProperty, true,
            ("bd", Border.BackgroundProperty, Theme.Hover),
            ("bd", Border.BorderBrushProperty, Theme.Accent)));
        template.Triggers.Add(On(UIElement.IsEnabledProperty, false, ("", UIElement.OpacityProperty, 0.35)));
        s.Setters.Add(new Setter(Control.TemplateProperty, template));
        return s;
    }

    /// <summary>Item do menu lateral. O ativo ganha fundo e texto de destaque.</summary>
    private static Style Nav(bool active) {
        var s = new Style(typeof(Button), null);
        s.Setters.Add(new Setter(Control.BackgroundProperty, active ? Theme.AccentSoft : Brushes.Transparent));
        s.Setters.Add(new Setter(Control.ForegroundProperty, active ? Theme.Accent : Theme.Ink));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 10, 12, 10)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 13.5));
        s.Setters.Add(new Setter(Control.FontWeightProperty, active ? FontWeights.SemiBold : FontWeights.Normal));
        s.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        var template = ButtonTemplate(8);
        if (!active)
            template.Triggers.Add(On(UIElement.IsMouseOverProperty, true, ("bd", Border.BackgroundProperty, Theme.Hover)));
        template.Triggers.Add(On(UIElement.IsEnabledProperty, false, ("", UIElement.OpacityProperty, 0.4)));
        s.Setters.Add(new Setter(Control.TemplateProperty, template));
        return s;
    }

    /// <summary>Cartão clicável da tela de operações.</summary>
    private static Style Card() {
        var s = new Style(typeof(Button), null);
        s.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Surface));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.Line));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(20, 18, 20, 18)));
        s.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Top));
        var template = ButtonTemplate(12);
        template.Triggers.Add(On(UIElement.IsMouseOverProperty, true,
            ("bd", Border.BackgroundProperty, Theme.Hover),
            ("bd", Border.BorderBrushProperty, Theme.Accent)));
        template.Triggers.Add(On(UIElement.IsKeyboardFocusedProperty, true,
            ("bd", Border.BorderBrushProperty, Theme.Accent)));
        template.Triggers.Add(On(UIElement.IsEnabledProperty, false, ("", UIElement.OpacityProperty, 0.45)));
        s.Setters.Add(new Setter(Control.TemplateProperty, template));
        return s;
    }

    /// <summary>Texto clicável, no estilo de link, para ações auxiliares e ajuda.</summary>
    private static Style Link() {
        var s = new Style(typeof(Button), null);
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Accent));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 3, 0, 3)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
        s.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left));
        var template = ButtonTemplate(4);
        template.Triggers.Add(On(UIElement.IsMouseOverProperty, true, ("", Control.ForegroundProperty, Theme.AccentHover)));
        template.Triggers.Add(On(UIElement.IsKeyboardFocusedProperty, true, ("bd", Border.BackgroundProperty, Theme.AccentSoft)));
        template.Triggers.Add(On(UIElement.IsEnabledProperty, false, ("", UIElement.OpacityProperty, 0.4)));
        s.Setters.Add(new Setter(Control.TemplateProperty, template));
        return s;
    }

    // ---------------------------------------------------------------- campos

    private static ControlTemplate FieldTemplate(Type owner, string hostName) {
        var border = F<Border>("bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        Tie(border, Border.BackgroundProperty, "Background");
        Tie(border, Border.BorderBrushProperty, "BorderBrush");
        Tie(border, Border.BorderThicknessProperty, "BorderThickness");
        // TextBoxBase já transfere Padding para PART_ContentHost. Repeti-lo na borda duplica o recuo.
        if (owner != typeof(TextBox)) Tie(border, Border.PaddingProperty, "Padding");
        var host = F<ScrollViewer>(hostName);
        host.SetValue(ScrollViewer.FocusableProperty, false);
        // Antes as barras eram fixas em Hidden aqui, e uma linha longa ficava presa no fim sem como voltar.
        if (owner == typeof(TextBox)) {
            Tie(host, ScrollViewer.HorizontalScrollBarVisibilityProperty, "HorizontalScrollBarVisibility");
            Tie(host, ScrollViewer.VerticalScrollBarVisibilityProperty, "VerticalScrollBarVisibility");
        } else {
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        }
        if (owner == typeof(TextBox)) {
            // O ScrollViewer aplica o padding do TextBox; a camada do exemplo aplica-o uma única vez também.
            var layers = F<Grid>();
            layers.SetValue(UIElement.ClipToBoundsProperty, true);
            var hintArea = F<Border>();
            Tie(hintArea, Border.PaddingProperty, "Padding");
            hintArea.SetValue(UIElement.IsHitTestVisibleProperty, false);
            var ghost = F<TextBlock>("hint");
            ghost.SetValue(TextBlock.ForegroundProperty, Theme.Muted);
            ghost.SetValue(UIElement.IsHitTestVisibleProperty, false);
            ghost.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            ghost.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            // TextBoxView reserva 2 DIPs em cada lado para o indicador bidirecional do cursor.
            ghost.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0));
            Tie(ghost, TextBlock.FontSizeProperty, "FontSize");
            Tie(ghost, TextBlock.FontFamilyProperty, "FontFamily");
            Tie(ghost, TextBlock.FontWeightProperty, "FontWeight");
            Tie(ghost, TextBlock.FontStyleProperty, "FontStyle");
            Tie(ghost, TextBlock.FontStretchProperty, "FontStretch");
            Tie(ghost, TextBlock.TextAlignmentProperty, "TextAlignment");
            Tie(ghost, TextBlock.TextWrappingProperty, "TextWrapping");
            Tie(ghost, FrameworkElement.VerticalAlignmentProperty, "VerticalContentAlignment");
            ghost.SetBinding(TextBlock.TextProperty, new Binding {
                Path = new PropertyPath(HintProperty),
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            layers.AppendChild(host);
            hintArea.AppendChild(ghost);
            layers.AppendChild(hintArea);
            border.AppendChild(layers);
        } else border.AppendChild(host);
        var template = new ControlTemplate(owner) { VisualTree = border };
        template.Triggers.Add(On(UIElement.IsKeyboardFocusWithinProperty, true,
            ("bd", Border.BorderBrushProperty, Theme.Accent),
            ("bd", Border.BorderThicknessProperty, new Thickness(1.6))));
        template.Triggers.Add(On(UIElement.IsMouseOverProperty, true, ("bd", Border.BorderBrushProperty, Theme.Accent)));
        template.Triggers.Add(On(UIElement.IsEnabledProperty, false, ("", UIElement.OpacityProperty, 0.5)));
        if (owner == typeof(TextBox))
            template.Triggers.Add(On(TextBox.TextProperty, "", ("hint", UIElement.VisibilityProperty, Visibility.Visible)));
        return template;
    }

    private static void FieldBasics(Style s) {
        s.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Field));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.Line));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 9, 12, 9)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 14.0));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
    }

    private static Style TextBoxStyle() {
        var s = new Style(typeof(TextBox));
        FieldBasics(s);
        s.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Theme.Ink));
        s.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, Theme.Accent));
        s.Setters.Add(new Setter(Control.TemplateProperty, FieldTemplate(typeof(TextBox), "PART_ContentHost")));
        return s;
    }

    private static Style PasswordStyle() {
        var s = new Style(typeof(PasswordBox));
        FieldBasics(s);
        s.Setters.Add(new Setter(PasswordBox.CaretBrushProperty, Theme.Ink));
        s.Setters.Add(new Setter(PasswordBox.SelectionBrushProperty, Theme.Accent));
        s.Setters.Add(new Setter(Control.TemplateProperty, FieldTemplate(typeof(PasswordBox), "PART_ContentHost")));
        return s;
    }

    // O seletor de data vinha com a moldura padrão do Windows: fundo branco no tema escuro e cantos retos ao
    // lado dos outros campos. Aqui a moldura passa a ser a do aplicativo e o campo interno fica transparente.
    private static Style DatePickerStyle() {
        var s = new Style(typeof(DatePicker));
        FieldBasics(s);
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 4, 4)));
        return s;
    }

    private static Style DatePickerFieldStyle() {
        var s = new Style(typeof(DatePickerTextBox));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 14.0));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Theme.Ink));
        s.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, Theme.Accent));
        var host = F<ScrollViewer>("PART_ContentHost");
        host.SetValue(ScrollViewer.FocusableProperty, false);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        var shell = F<Border>("bd");
        shell.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        shell.AppendChild(host);
        s.Setters.Add(new Setter(Control.TemplateProperty,
            new ControlTemplate(typeof(DatePickerTextBox)) { VisualTree = shell }));
        return s;
    }

    private static Style LabelStyle() {
        var s = new Style(typeof(Label));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        return s;
    }

    // ---------------------------------------------------------------- lista suspensa

    private static Style ComboStyle() {
        var s = new Style(typeof(ComboBox));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Field));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.Line));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 9, 12, 9)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 14.0));
        s.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));

        var root = F<Grid>();

        // A moldura fica no template do ComboBox (e n\u00E3o no do ToggleButton) para que
        // os gatilhos de foco e de abertura consigam alcan\u00E7\u00E1-la pelo nome.
        var frame = F<Border>("bd");
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        Tie(frame, Border.BackgroundProperty, "Background");
        Tie(frame, Border.BorderBrushProperty, "BorderBrush");
        Tie(frame, Border.BorderThicknessProperty, "BorderThickness");
        var chevron = F<TextBlock>();
        chevron.SetValue(TextBlock.TextProperty, "\uE70D");
        chevron.SetValue(TextBlock.FontFamilyProperty, new FontFamily(Icons));
        chevron.SetValue(TextBlock.FontSizeProperty, 10.0);
        chevron.SetValue(TextBlock.ForegroundProperty, Theme.Muted);
        chevron.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        chevron.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        chevron.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 13, 0));
        frame.AppendChild(chevron);
        root.AppendChild(frame);

        var selected = F<ContentPresenter>("content");
        selected.SetValue(UIElement.IsHitTestVisibleProperty, false);
        selected.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 9, 34, 9));
        selected.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        selected.SetValue(TextBlock.ForegroundProperty, Theme.Ink);
        Tie(selected, ContentPresenter.ContentProperty, "SelectionBoxItem");
        Tie(selected, ContentPresenter.ContentTemplateProperty, "SelectionBoxItemTemplate");
        Tie(selected, ContentPresenter.ContentTemplateSelectorProperty, "ItemTemplateSelector");
        Tie(selected, ContentPresenter.ContentStringFormatProperty, "SelectionBoxItemStringFormat");
        root.AppendChild(selected);

        // Sem PART_EditableTextBox um ComboBox editável não mostra nem aceita texto: o campo aparecia vazio.
        var editor = F<TextBox>("PART_EditableTextBox");
        editor.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 9, 34, 9));
        editor.SetValue(Control.PaddingProperty, new Thickness(0));
        editor.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        editor.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        editor.SetValue(Control.ForegroundProperty, Theme.Ink);
        editor.SetValue(TextBoxBase.CaretBrushProperty, Theme.Ink);
        editor.SetValue(TextBoxBase.SelectionBrushProperty, Theme.Accent);
        editor.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        editor.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        root.AppendChild(editor);

        // Camada transparente por cima: é ela que abre e fecha a lista.
        var toggle = F<ToggleButton>();
        toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
        toggle.SetValue(ToggleButton.FocusableProperty, false);
        toggle.SetValue(ToggleButton.BackgroundProperty, Brushes.Transparent);
        toggle.SetBinding(ToggleButton.IsCheckedProperty,
            new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
        var toggleSurface = F<Border>();
        toggleSurface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        toggle.SetValue(Control.TemplateProperty, new ControlTemplate(typeof(ToggleButton)) { VisualTree = toggleSurface });
        root.AppendChild(toggle);

        var popup = F<Popup>("PART_Popup");
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
        popup.SetValue(Popup.VerticalOffsetProperty, 4.0);
        popup.SetBinding(Popup.IsOpenProperty,
            new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var popupBorder = F<Border>();
        popupBorder.SetValue(Border.BackgroundProperty, Theme.Surface);
        popupBorder.SetValue(Border.BorderBrushProperty, Theme.Line);
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
        popupBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 6));
        popupBorder.SetBinding(FrameworkElement.MinWidthProperty,
            new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var scroll = F<ScrollViewer>();
        scroll.SetValue(ScrollViewer.MaxHeightProperty, 320.0);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.AppendChild(F<ItemsPresenter>());
        popupBorder.AppendChild(scroll);
        popup.AppendChild(popupBorder);
        root.AppendChild(popup);

        var template = new ControlTemplate(typeof(ComboBox)) { VisualTree = root };
        template.Triggers.Add(On(UIElement.IsMouseOverProperty, true, ("bd", Border.BorderBrushProperty, Theme.Accent)));
        template.Triggers.Add(On(ComboBox.IsDropDownOpenProperty, true, ("bd", Border.BorderBrushProperty, Theme.Accent)));
        template.Triggers.Add(On(ComboBox.IsEditableProperty, true,
            ("PART_EditableTextBox", UIElement.VisibilityProperty, Visibility.Visible),
            ("content", UIElement.VisibilityProperty, Visibility.Collapsed)));
        template.Triggers.Add(On(UIElement.IsEnabledProperty, false, ("", UIElement.OpacityProperty, 0.5)));
        s.Setters.Add(new Setter(Control.TemplateProperty, template));
        return s;
    }

    private static Style ComboItemStyle() {
        var s = new Style(typeof(ComboBoxItem));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(11, 9, 11, 9)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 14.0));
        s.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        var border = F<Border>("bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        Tie(border, Border.BackgroundProperty, "Background");
        Tie(border, Border.PaddingProperty, "Padding");
        var presenter = F<ContentPresenter>();
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = border };
        template.Triggers.Add(On(ComboBoxItem.IsHighlightedProperty, true, ("bd", Border.BackgroundProperty, Theme.Hover)));
        template.Triggers.Add(On(ComboBoxItem.IsSelectedProperty, true,
            ("bd", Border.BackgroundProperty, Theme.AccentSoft),
            ("", Control.ForegroundProperty, Theme.Accent)));
        s.Setters.Add(new Setter(Control.TemplateProperty, template));
        return s;
    }

    // ---------------------------------------------------------------- barra de progresso

    private static Style ProgressStyle() {
        var s = new Style(typeof(ProgressBar));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Theme.LineSoft));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Cyan));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        var root = F<Grid>();
        var track = F<Border>();
        Tie(track, Border.BackgroundProperty, "Background");
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        root.AppendChild(track);
        root.AppendChild(F<Rectangle>("PART_Track"));
        var indicator = F<Border>("PART_Indicator");
        Tie(indicator, Border.BackgroundProperty, "Foreground");
        indicator.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        indicator.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        root.AppendChild(indicator);
        s.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ProgressBar)) { VisualTree = root }));
        return s;
    }

    // ---------------------------------------------------------------- barra de rolagem

    /// <summary>Cor do pincel no formato aceito pelo XAML.</summary>
    private static string Hex(Brush brush) => ((SolidColorBrush)brush).Color.ToString();

    /// <summary>
    /// Barra de rolagem fina, sem setas. Montada por XAML porque o Track recebe
    /// Thumb e RepeatButton em propriedades próprias, e não como filhos comuns.
    /// </summary>
    private static Style ScrollBarStyle() {
        string xaml = $@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='ScrollBar'>
  <Style.Resources>
    <Style x:Key='sbThumb' TargetType='Thumb'>
      <Setter Property='OverridesDefaultStyle' Value='True'/>
      <Setter Property='IsTabStop' Value='False'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Thumb'>
            <Border x:Name='bd' Margin='2' CornerRadius='4' Background='{Hex(Theme.Line)}'/>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{Hex(Theme.Muted)}'/>
              </Trigger>
              <Trigger Property='IsDragging' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{Hex(Theme.Accent)}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='sbPage' TargetType='RepeatButton'>
      <Setter Property='OverridesDefaultStyle' Value='True'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Focusable' Value='False'/>
      <Setter Property='IsTabStop' Value='False'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='RepeatButton'>
            <Border Background='Transparent'/>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Style.Resources>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='BorderThickness' Value='0'/>
  <Setter Property='Width' Value='11'/>
  <Setter Property='MinWidth' Value='11'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Style='{{StaticResource sbPage}}' Command='ScrollBar.PageUpCommand'/>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb Style='{{StaticResource sbThumb}}'/>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Style='{{StaticResource sbPage}}' Command='ScrollBar.PageDownCommand'/>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
  <Style.Triggers>
    <Trigger Property='Orientation' Value='Horizontal'>
      <Setter Property='Width' Value='Auto'/>
      <Setter Property='MinWidth' Value='0'/>
      <Setter Property='Height' Value='11'/>
      <Setter Property='MinHeight' Value='11'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ScrollBar'>
            <Grid Background='Transparent'>
              <Track x:Name='PART_Track' Orientation='Horizontal'>
                <Track.DecreaseRepeatButton>
                  <RepeatButton Style='{{StaticResource sbPage}}' Command='ScrollBar.PageLeftCommand'/>
                </Track.DecreaseRepeatButton>
                <Track.Thumb>
                  <Thumb Style='{{StaticResource sbThumb}}'/>
                </Track.Thumb>
                <Track.IncreaseRepeatButton>
                  <RepeatButton Style='{{StaticResource sbPage}}' Command='ScrollBar.PageRightCommand'/>
                </Track.IncreaseRepeatButton>
              </Track>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Trigger>
  </Style.Triggers>
</Style>";
        return (Style)XamlReader.Parse(xaml);
    }

    // ---------------------------------------------------------------- tabela

    private static Style GridStyle() {
        var s = new Style(typeof(DataGrid));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Surface));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.Line));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(DataGrid.RowBackgroundProperty, Theme.Surface));
        s.Setters.Add(new Setter(DataGrid.AlternatingRowBackgroundProperty, Theme.SurfaceAlt));
        s.Setters.Add(new Setter(DataGrid.HorizontalGridLinesBrushProperty, Theme.LineSoft));
        s.Setters.Add(new Setter(DataGrid.VerticalGridLinesBrushProperty, Theme.LineSoft));
        return s;
    }

    private static Style HeaderStyle() {
        var s = new Style(typeof(DataGridColumnHeader));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Theme.SurfaceAlt));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Muted));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        s.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 10, 14, 10)));
        s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        var border = F<Border>();
        border.SetValue(Border.BackgroundProperty, Theme.SurfaceAlt);
        border.SetValue(Border.BorderBrushProperty, Theme.Line);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 1));
        Tie(border, Border.PaddingProperty, "Padding");
        var presenter = F<ContentPresenter>();
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        s.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(DataGridColumnHeader)) { VisualTree = border }));
        return s;
    }

    private static Style CellStyle() {
        var s = new Style(typeof(DataGridCell));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 9, 14, 9)));
        s.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        var border = F<Border>("bd");
        Tie(border, Border.BackgroundProperty, "Background");
        Tie(border, Border.PaddingProperty, "Padding");
        var presenter = F<ContentPresenter>();
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(DataGridCell)) { VisualTree = border };
        template.Triggers.Add(On(DataGridCell.IsSelectedProperty, true,
            ("bd", Border.BackgroundProperty, Theme.AccentSoft),
            ("", Control.ForegroundProperty, Theme.Accent)));
        s.Setters.Add(new Setter(Control.TemplateProperty, template));
        return s;
    }

    private static Style RowStyle() {
        var s = new Style(typeof(DataGridRow));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.LineSoft));
        return s;
    }

    private static Style TipStyle() {
        var s = new Style(typeof(ToolTip));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Surface));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Ink));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.Line));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(11, 8, 11, 8)));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
        var border = F<Border>();
        Tie(border, Border.BackgroundProperty, "Background");
        Tie(border, Border.BorderBrushProperty, "BorderBrush");
        Tie(border, Border.BorderThicknessProperty, "BorderThickness");
        Tie(border, Border.PaddingProperty, "Padding");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.AppendChild(F<ContentPresenter>());
        s.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ToolTip)) { VisualTree = border }));
        return s;
    }
}
