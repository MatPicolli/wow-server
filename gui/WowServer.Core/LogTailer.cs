using System.Text;

namespace WowServer.Core;

/// <summary>
/// Acompanha um arquivo de log e emite cada linha nova.
///
/// Existe por causa do authserver. Quando a saida padrao de um processo e
/// redirecionada para um pipe (e nao para um console), o runtime C troca
/// buffer de linha por buffer de bloco - tipicamente 4 KB. O worldserver
/// despeja tanta coisa que o buffer esvazia o tempo todo; o authserver
/// escreve poucas linhas no start e depois so em evento de conexao, entao o
/// buffer nunca enche e a saida fica presa.
///
/// O arquivo de log nao sofre disso: o appender de arquivo grava direto.
/// </summary>
public sealed class LogTailer : IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _intervalo;
    private readonly CancellationTokenSource _cts = new();
    private long _posicao;

    public LogTailer(string path, TimeSpan? intervalo = null)
    {
        _path = path;
        _intervalo = intervalo ?? TimeSpan.FromMilliseconds(500);
    }

    public event Action<string>? Line;

    /// <param name="fromStart">
    /// true le o arquivo desde o inicio. O appender do AzerothCore abre em
    /// modo 'w' (sobrescreve) a cada start, entao ler desde o inicio traz
    /// exatamente a sessao atual.
    /// </param>
    public void Start(bool fromStart = true)
    {
        _posicao = fromStart ? 0 : TamanhoAtual();
        _ = Task.Run(LacoAsync);
    }

    private long TamanhoAtual()
    {
        try { return new FileInfo(_path).Exists ? new FileInfo(_path).Length : 0; }
        catch { return 0; }
    }

    private async Task LacoAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try { Ler(); }
            catch { /* arquivo em uso ou ainda nao criado: tenta de novo */ }

            try { await Task.Delay(_intervalo, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Ler()
    {
        if (!File.Exists(_path)) return;

        // FileShare generoso: o servidor mantem o arquivo aberto para escrita
        // enquanto roda. Sem isso, a leitura falharia com "em uso".
        using var fs = new FileStream(
            _path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        // Reinicio do servidor trunca o arquivo; se ele encolheu, voltamos ao
        // comeco em vez de ficar lendo lixo a partir de um offset invalido.
        if (fs.Length < _posicao) _posicao = 0;
        if (fs.Length == _posicao) return;

        fs.Seek(_posicao, SeekOrigin.Begin);

        using var leitor = new StreamReader(fs, Encoding.UTF8);
        string? linha;
        while ((linha = leitor.ReadLine()) is not null)
        {
            if (linha.Length > 0) Line?.Invoke(linha);
        }

        _posicao = fs.Position;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
