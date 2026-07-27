using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WowServer.Core;

namespace WowServer.Gui.Views;

public partial class SettingsView : UserControl
{
    private readonly ScriptRunner _runner = Session.Current.CreateRunner();

    public SettingsView()
    {
        InitializeComponent();
        TxtRepo.Text = Session.Current.RepoRoot;
        SaidaReparo.Title = "Reparos e backup";

        // Sem isto o backup e os reparos rodavam mudos: a tela dizia so
        // "terminou com codigo N" e o motivo ficava invisivel.
        _runner.Output += linha =>
            Dispatcher.Invoke(() => SaidaReparo.Append(linha.Text, linha.Kind));

        Loaded += async (_, _) => await CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        try
        {
            var s = await Session.Current.Settings.LoadAsync();
            Session.Current.Loaded = s;

            TxtClient.Text = s.ClientDir;
            TxtRoot.Text = s.Root;
            TxtSource.Text = s.SourceDir;
            TxtBuild.Text = s.BuildDir;
            TxtServer.Text = s.ServerDir;

            TxtHost.Text = s.MySql.Host;
            TxtPort.Text = s.MySql.Port.ToString(CultureInfo.InvariantCulture);
            TxtUser.Text = s.MySql.User;
            TxtPass.Text = s.MySql.Password;
            TxtAuthDb.Text = s.MySql.AuthDb;
            TxtWorldDb.Text = s.MySql.WorldDb;
            TxtCharDb.Text = s.MySql.CharDb;

            TxtRealmName.Text = s.RealmName;
            TxtRealmAddr.Text = s.RealmAddress;

            TxtSourceRepo.Text = s.SourceRepository;
            TxtSourceBranch.Text = s.SourceBranch;

            TxtThreads.Text = s.Threads.ToString(CultureInfo.InvariantCulture);
            ChkVmaps.IsChecked = s.ExtractVmaps;
            ChkMmaps.IsChecked = s.ExtractMmaps;

            TxtStatus.Text = "";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message + "\n\nSe o arquivo ainda não existe, ele é criado a partir do exemplo ao salvar.",
                "Não consegui ler as configurações");
        }
    }

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = Session.Current.Loaded ?? new ServerSettings();

            s.ClientDir = TxtClient.Text.Trim();
            s.Root = TxtRoot.Text.Trim();
            s.SourceDir = TxtSource.Text.Trim();
            s.BuildDir = TxtBuild.Text.Trim();
            s.ServerDir = TxtServer.Text.Trim();

            s.MySql.Host = TxtHost.Text.Trim();
            s.MySql.Port = int.TryParse(TxtPort.Text.Trim(), out var porta) ? porta : 3306;
            s.MySql.User = TxtUser.Text.Trim();
            s.MySql.Password = TxtPass.Text;
            s.MySql.AuthDb = TxtAuthDb.Text.Trim();
            s.MySql.WorldDb = TxtWorldDb.Text.Trim();
            s.MySql.CharDb = TxtCharDb.Text.Trim();

            s.RealmName = TxtRealmName.Text.Trim();
            s.RealmAddress = TxtRealmAddr.Text.Trim();

            s.SourceRepository = TxtSourceRepo.Text.Trim();
            s.SourceBranch = TxtSourceBranch.Text.Trim();

            s.Threads = int.TryParse(TxtThreads.Text.Trim(), out var t) ? t : 0;
            s.ExtractVmaps = ChkVmaps.IsChecked == true;
            s.ExtractMmaps = ChkMmaps.IsChecked == true;

            Session.Current.Settings.Save(s);
            Session.Current.Loaded = s;

            TxtStatus.Text = $"Salvo em {Session.Current.Settings.SettingsPath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não consegui salvar");
        }
    }

    private async void Recarregar_Click(object sender, RoutedEventArgs e) => await CarregarAsync();

    private void EscolherClient_Click(object sender, RoutedEventArgs e)
    {
        // OpenFolderDialog existe a partir do .NET 8 / WPF moderno; pedimos o
        // Wow.exe em vez da pasta porque e mais dificil errar.
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Selecione o Wow.exe do seu client 3.3.5a",
            Filter = "Wow.exe|Wow.exe|Executáveis|*.exe",
            CheckFileExists = true,
        };

        if (dlg.ShowDialog() == true)
        {
            var pasta = System.IO.Path.GetDirectoryName(dlg.FileName);
            if (pasta is not null) TxtClient.Text = pasta;
        }
    }

    private void AbrirArquivo_Click(object sender, RoutedEventArgs e)
    {
        var caminho = Session.Current.Settings.SettingsPath;
        if (!System.IO.File.Exists(caminho))
        {
            Session.Current.Settings.EnsureExists();
        }
        Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Fazendo backup...";
        try
        {
            var codigo = await _runner.RunAsync("backup-db.ps1");
            TxtStatus.Text = codigo == 0
                ? "Backup concluído."
                : $"O backup terminou com código {codigo} — veja o console na aba Instalação.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = ex.Message;
        }
    }

    /// <summary>
    /// Tira chaves repetidas do settings.psd1.
    ///
    /// Uma chave duplicada faz o Import-PowerShellDataFile recusar o arquivo
    /// INTEIRO, e aí nenhum script roda — inclusive este, se ele dependesse de
    /// ler as configurações. Por isso o reparo existe separado.
    /// </summary>
    private async void RepararSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_runner.ScriptExists("repair-settings.ps1"))
        {
            SaidaReparo.Append("[erro] scripts\\repair-settings.ps1 não encontrado", OutputKind.Error);
            return;
        }

        // Este script grava por padrao e so simula com -WhatIf - ao contrario
        // dos outros, que preveem por padrao e gravam com -Apply. A previa aqui
        // e o -WhatIf, nao a chamada sem argumento.
        SaidaReparo.Append("==> conferindo o settings.psd1");
        var previa = await _runner.RunAsync("repair-settings.ps1", new[] { "-WhatIf" });
        if (previa != 0) return;

        var r = MessageBox.Show(
            "Gravar o arquivo corrigido?\n\n"
            + "O console mostra o que seria removido. Uma cópia do original é guardada antes.",
            "Consertar o settings.psd1", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        await _runner.RunAsync("repair-settings.ps1");
        await CarregarAsync();
    }

    /// <summary>
    /// Apaga o que da para reconstruir e preserva a pasta Data, que leva horas
    /// para extrair. O script mostra tudo antes; aqui so se confirma.
    /// </summary>
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (!_runner.ScriptExists("reset-server.ps1"))
        {
            SaidaReparo.Append("[erro] scripts\\reset-server.ps1 não encontrado", OutputKind.Error);
            return;
        }

        SaidaReparo.Append("==> o que seria apagado");
        var previa = await _runner.RunAsync("reset-server.ps1");
        if (previa != 0)
        {
            MessageBox.Show("O script recusou — o motivo está no console.", "Não dá para recomeçar assim");
            return;
        }

        var r = MessageBox.Show(
            "Apagar o código, a compilação e a instalação do servidor?\n\n"
            + "A pasta Data (dbc, maps, vmaps, mmaps) é preservada — é ela que leva horas.\n\n"
            + "O banco de dados NÃO é apagado: seus personagens continuam lá.\n\n"
            + "O console ao lado lista exatamente o que será apagado.",
            "Recomeçar do zero", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        await _runner.RunAsync("reset-server.ps1", new[] { "-Apply" });
    }
}
