namespace NutManager.App.ViewModels;

public abstract class PageViewModel
{
    protected PageViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }

    public string Description { get; }
}

public sealed class OverviewPageViewModel : PageViewModel
{
    public OverviewPageViewModel()
        : base("Visão geral", "Acompanhe o estado geral do seu ambiente de energia.")
    {
    }
}

public sealed class DevicesPageViewModel : PageViewModel
{
    public DevicesPageViewModel()
        : base("Dispositivos", "Veja os dispositivos disponíveis quando uma conexão for configurada.")
    {
    }
}

public sealed class DiagnosticsPageViewModel : PageViewModel
{
    public DiagnosticsPageViewModel()
        : base("Diagnóstico", "Consulte informações de diagnóstico da aplicação.")
    {
    }
}

public sealed class SettingsPageViewModel : PageViewModel
{
    public SettingsPageViewModel()
        : base("Configurações", "Ajuste as preferências visuais do aplicativo.")
    {
    }
}
