using System.Windows;

namespace ZeepCut;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 由后续阶段接入：单实例、托盘、热键、首次引导。
        Shutdown();
    }
}
