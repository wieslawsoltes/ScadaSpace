using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Designer;

namespace ScadaSpace.Controls;

public partial class PropertyInspector
{
    private readonly Dictionary<string, MenuFlyoutItemBase> _searchOptions = new(StringComparer.Ordinal);
    private ReadOnlyDictionary<string, MenuFlyoutItemBase>? _searchOptionView;
    public IReadOnlyDictionary<string, MenuFlyoutItemBase> SearchOptions => _searchOptionView ??= new(_searchOptions);
    public bool IsSearchOptionsOpen { get; private set; }

    private MenuFlyout BuildSearchOptions()
    {
        var menu = new MenuFlyout();
        void Toggle(string key, string text, Action action)
        {
            var item = new ToggleMenuFlyoutItem { Text = text };
            AutomationProperties.SetAutomationId(item, "inspector-search-" + key);
            item.Click += (_, _) => action(); menu.Items.Add(item); _searchOptions[key] = item;
        }
        void Command(string key, string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            AutomationProperties.SetAutomationId(item, "inspector-search-" + key);
            item.Click += (_, _) => action(); menu.Items.Add(item); _searchOptions[key] = item;
        }
        Toggle("case", "Case Sensitive", () => ChangePropertyFilter(sensitive: ((ToggleMenuFlyoutItem)_searchOptions["case"]).IsChecked));
        menu.Items.Add(new MenuFlyoutSeparator());
        Toggle("literal", "Literal Text", () => ChangePropertyFilter(syntax: PropertySearchSyntax.Literal));
        Toggle("wildcard", "Wildcards (* and ?)", () => ChangePropertyFilter(syntax: PropertySearchSyntax.Wildcard));
        Toggle("regex", "Regular Expression", () => ChangePropertyFilter(syntax: PropertySearchSyntax.RegularExpression));
        ToolTipService.SetToolTip(_searchOptions["regex"], "Bounded nonbacktracking expressions: no counted repetition, lookaround, backreferences or inline options.");
        menu.Items.Add(new MenuFlyoutSeparator());
        Toggle("anywhere", "Match Anywhere", () => ChangePropertyFilter(match: PropertySearchMatch.Anywhere));
        Toggle("start", "Match from Start", () => ChangePropertyFilter(match: PropertySearchMatch.StartsWith));
        Toggle("exact", "Match Exactly", () => ChangePropertyFilter(match: PropertySearchMatch.Exact));
        menu.Items.Add(new MenuFlyoutSeparator());
        Command("expand", "Expand All Categories", () => ExpandCategories(true));
        Command("collapse", "Collapse All Categories", () => ExpandCategories(false));
        Command("reset", "Reset Search Options", () => ChangePropertyFilter(PropertySearchSyntax.Literal, PropertySearchMatch.Anywhere, false));
        menu.Opened += (_, _) => { IsSearchOptionsOpen = true; SynchronizeSearchOptions(); };
        menu.Closed += (_, _) => IsSearchOptionsOpen = false;
        return menu;
    }

    private void SynchronizeSearchOptions()
    {
        void Check(string key, bool value) { if (_searchOptions.TryGetValue(key, out var item)) ((ToggleMenuFlyoutItem)item).IsChecked = value; }
        Check("case", FilterCaseSensitive); Check("literal", FilterSyntax == PropertySearchSyntax.Literal);
        Check("wildcard", FilterSyntax == PropertySearchSyntax.Wildcard); Check("regex", FilterSyntax == PropertySearchSyntax.RegularExpression);
        Check("anywhere", FilterMatchMode == PropertySearchMatch.Anywhere); Check("start", FilterMatchMode == PropertySearchMatch.StartsWith); Check("exact", FilterMatchMode == PropertySearchMatch.Exact);
        if (_filterCommands.TryGetValue("options", out var button))
            ToolTipService.SetToolTip(button, $"Search options: {FilterSyntax}, {FilterMatchMode}, {(FilterCaseSensitive ? "case sensitive" : "ignore case")}");
    }
}
