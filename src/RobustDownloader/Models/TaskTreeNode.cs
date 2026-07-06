using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RobustDownloader.Models;

public sealed partial class TaskTreeNode : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isExpanded = true;

    public TaskTreeGroup Group { get; init; }
    public string? Extension { get; init; }
    public int Count { get; set; }
    public string Label { get; set; } = "";
    public bool HasChildren => Children.Count > 0;
    public string ExpansionGlyph => IsExpanded ? "▾" : "▸";
    public ObservableCollection<TaskTreeNode> Children { get; } = [];

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpansionGlyph));
    }
}
