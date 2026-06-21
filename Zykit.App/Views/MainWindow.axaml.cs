using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Controls;
using Zykit.App.ViewModels;

namespace Zykit.App.Views;

public partial class MainWindow : SukiWindow
{
    private const string LinkageIconData = "M17 0.775C17 0.348 17.348 0 17.775 0h4.45C22.652 0 23 0.348 23 0.775v4.45C23 5.652 22.652 6 22.225 6h-4.45C17.348 6 17 5.652 17 5.225v-4.45zM17 9.775C17 9.348 17.348 9 17.775 9h4.45C22.652 9 23 9.348 23 9.775v4.45C23 14.652 22.652 15 22.225 15h-4.45C17.348 15 17 14.652 17 14.225v-4.45zM17 18.775C17 18.348 17.348 18 17.775 18h4.45C22.652 18 23 18.348 23 18.775v4.45C23 23.652 22.652 24 22.225 24h-4.45C17.348 24 17 23.652 17 23.225v-4.45zM8 0.775C8 0.348 8.348 0 8.775 0h4.45C13.652 0 14 0.348 14 0.775v4.45C14 5.652 13.652 6 13.225 6h-4.45C8.348 6 8 5.652 8 5.225v-4.45zM8 9.775C8 9.348 8.348 9 8.775 9h4.45C13.652 9 14 9.348 14 9.775v4.45C14 14.652 13.652 15 13.225 15h-4.45C8.348 15 8 14.652 8 14.225v-4.45zM8 18.775C8 18.348 8.348 18 8.775 18h4.45C13.652 18 14 18.348 14 18.775v4.45C14 23.652 13.652 24 13.225 24h-4.45C8.348 24 8 23.652 8 23.225v-4.45z";

    private SukiSideMenuItem? _linkageMenuItem;

    public MainWindow()
    {
        InitializeComponent();

        // 订阅联动操作面板的关闭请求：点击叉号时从侧边栏移除该菜单项
        var linkageVm = App.Services.GetRequiredService<LinkageViewModel>();
        linkageVm.CloseRequested += OnLinkageCloseRequested;
    }

    private void OnLinkageCloseRequested()
    {
        HideLinkageMenuItem();
    }

    /// <summary>
    /// 显示联动操作菜单项（如不存在则创建），并导航到该页面。
    /// 仅在 hokit:// 协议唤起时调用，默认侧边栏不显示此项。
    /// </summary>
    public void ShowLinkageMenuItem()
    {
        if (_linkageMenuItem == null)
        {
            _linkageMenuItem = new SukiSideMenuItem
            {
                Header = "联动操作",
                Icon = new PathIcon { Data = Geometry.Parse(LinkageIconData) },
                PageContent = new LinkageView()
            };
        }

        if (!SideMenu.Items.Contains(_linkageMenuItem))
        {
            // 插入到「快速开始」之后（索引 1）
            SideMenu.Items.Insert(1, _linkageMenuItem);
        }

        SideMenu.SelectedItem = _linkageMenuItem;
    }

    /// <summary>
    /// 隐藏联动操作菜单项并从侧边栏移除，导航回首页。
    /// </summary>
    public void HideLinkageMenuItem()
    {
        if (_linkageMenuItem != null && SideMenu.Items.Contains(_linkageMenuItem))
        {
            SideMenu.Items.Remove(_linkageMenuItem);
        }

        // 导航回「快速开始」
        if (SideMenu.Items.Count > 0 && SideMenu.Items[0] is SukiSideMenuItem firstItem)
        {
            SideMenu.SelectedItem = firstItem;
        }
    }

    /// <summary>
    /// 导航到指定标题的侧边菜单项（用于 hokit:// 协议唤起时跳转到「联动操作」页面）。
    /// </summary>
    public void NavigateToMenuItem(string header)
    {
        foreach (var item in SideMenu.Items)
        {
            if (item is SukiSideMenuItem menuItem && menuItem.Header?.ToString() == header)
            {
                SideMenu.SelectedItem = menuItem;
                return;
            }
        }
    }
}
