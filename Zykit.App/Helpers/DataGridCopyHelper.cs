using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace Zykit.App.Helpers;

/// <summary>
/// DataGrid 右键复制辅助类：支持复制整行（制表符分隔）和复制单个字段值。
/// </summary>
public static class DataGridCopyHelper
{
    /// <summary>
    /// 复制 DataGrid 选中行到剪贴板（单元格以制表符分隔，行以换行分隔，可直接粘贴到 Excel）。
    /// 优先从可视化树提取已渲染单元格文本，回退到绑定路径反射。
    /// </summary>
    public static async void CopySelectedRows(DataGrid grid)
    {
        if (grid == null) return;

        var lines = new List<string>();

        // 方式一：从可视化树提取已渲染行的单元格文本（最可靠）
        var renderedRows = grid.GetVisualDescendants().OfType<DataGridRow>()
            .Where(r => r.DataContext != null && grid.SelectedItems.Contains(r.DataContext))
            .ToList();

        if (renderedRows.Count > 0)
        {
            foreach (var row in renderedRows)
            {
                var texts = row.GetVisualDescendants().OfType<DataGridCell>()
                    .Select(GetCellText);
                var line = string.Join("\t", texts);
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            }
        }
        else
        {
            // 方式二（回退）：通过绑定路径反射提取
            foreach (var item in grid.SelectedItems)
            {
                if (item == null) continue;
                var line = BuildRowText(grid, item);
                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
        }

        var text = string.Join("\n", lines);
        if (string.IsNullOrEmpty(text)) return;

        var topLevel = TopLevel.GetTopLevel(grid);
        if (topLevel?.Clipboard != null)
            await topLevel.Clipboard.SetTextAsync(text);
    }

    /// <summary>
    /// 复制选中行某个属性的值到剪贴板（如 UDID、AppId 等）。
    /// </summary>
    public static async void CopyCellValue(DataGrid grid, string propertyName)
    {
        if (grid?.SelectedItem == null || string.IsNullOrEmpty(propertyName)) return;

        var value = GetPropertyValue(grid.SelectedItem, propertyName);
        if (value == null) return;

        var text = value.ToString();
        if (string.IsNullOrEmpty(text)) return;

        var topLevel = TopLevel.GetTopLevel(grid);
        if (topLevel?.Clipboard != null)
            await topLevel.Clipboard.SetTextAsync(text);
    }

    /// <summary>从 DataGridCell 提取显示文本</summary>
    private static string GetCellText(DataGridCell cell)
    {
        var tb = cell.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
        return tb?.Text ?? "";
    }

    /// <summary>通过绑定路径反射构建行文本（回退方案）</summary>
    private static string BuildRowText(DataGrid grid, object item)
    {
        var cells = grid.Columns
            .OfType<DataGridTextColumn>()
            .Select(c => GetCellValue(c, item));
        return string.Join("\t", cells);
    }

    private static string GetCellValue(DataGridTextColumn column, object item)
    {
        if (column.Binding is Binding b && !string.IsNullOrEmpty(b.Path))
            return GetPropertyValue(item, b.Path)?.ToString() ?? "";
        return "";
    }

    /// <summary>支持点分路径（如 A.B.C）的属性取值</summary>
    private static object? GetPropertyValue(object? obj, string path)
    {
        if (obj == null || string.IsNullOrEmpty(path)) return null;
        var current = obj;
        foreach (var part in path.Split('.'))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(part,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) return null;
            current = prop.GetValue(current);
        }
        return current;
    }
}
