namespace WowServer.Core;

/// <summary>
/// Leitor mínimo de .dbc — só o suficiente para o ItemDisplayInfo.
///
/// O formato é simples: cabeçalho de 20 bytes, uma matriz de uint32 e um bloco
/// de strings no fim. Campo de texto guarda o deslocamento dentro desse bloco.
/// </summary>
public static class DbcFile
{
    public sealed record Table(int Records, int Fields, uint[] Data, byte[] Strings);

    public static Table Read(string caminho)
    {
        var b = File.ReadAllBytes(caminho);
        if (b.Length < 20 || b[0] != 'W' || b[1] != 'D' || b[2] != 'B' || b[3] != 'C')
            throw new InvalidDataException($"{Path.GetFileName(caminho)} nao e um DBC");

        var registros = (int)BitConverter.ToUInt32(b, 4);
        var campos = (int)BitConverter.ToUInt32(b, 8);
        var tamanhoRegistro = (int)BitConverter.ToUInt32(b, 12);
        var tamanhoStrings = (int)BitConverter.ToUInt32(b, 16);

        var inicioDados = 20;
        var bytesDados = registros * tamanhoRegistro;

        if (inicioDados + bytesDados + tamanhoStrings > b.Length)
            throw new InvalidDataException("DBC truncado");

        var dados = new uint[registros * campos];
        Buffer.BlockCopy(b, inicioDados, dados, 0, Math.Min(bytesDados, dados.Length * 4));

        var strings = new byte[tamanhoStrings];
        Array.Copy(b, inicioDados + bytesDados, strings, 0, tamanhoStrings);

        return new Table(registros, campos, dados, strings);
    }

    public static uint Field(Table t, int registro, int campo) =>
        t.Data[registro * t.Fields + campo];

    /// <summary>Texto a partir do deslocamento no bloco de strings.</summary>
    public static string Text(Table t, uint offset)
    {
        if (offset >= t.Strings.Length) return "";

        var fim = (int)offset;
        while (fim < t.Strings.Length && t.Strings[fim] != 0) fim++;

        return System.Text.Encoding.UTF8.GetString(t.Strings, (int)offset, fim - (int)offset);
    }
}

/// <summary>
/// Descobre o ícone de cada item e extrai os BLP do client, convertendo para PNG.
///
/// O caminho completo é:
///   item_template.displayid  ->  ItemDisplayInfo.dbc  ->  nome do ícone
///   Interface\Icons\NOME.blp  dentro de um MPQ        ->  PNG em disco
///
/// O .dbc já está em Data\dbc desde a instalação — o extrator do AzerothCore
/// copia todos. O que falta é só o BLP, que mora nos MPQ.
/// </summary>
public static class IconExtractor
{
    /// <summary>
    /// No ItemDisplayInfo.dbc do 3.3.5a, o nome do ícone é o campo 5.
    ///
    /// Layout: 0 = ID, 1..2 = modelos, 3..4 = texturas de modelo, 5 = ícone.
    /// O valor é validado ao ser lido: se o texto não parecer nome de ícone,
    /// o campo é procurado nos vizinhos em vez de produzir lixo.
    /// </summary>
    public const int CampoIconePadrao = 5;

    /// <summary>displayid -> nome do ícone.</summary>
    public static Dictionary<uint, string> ReadIconNames(string itemDisplayInfoDbc)
    {
        var t = DbcFile.Read(itemDisplayInfoDbc);
        var mapa = new Dictionary<uint, string>();

        var campo = DescobrirCampoDoIcone(t);

        for (var r = 0; r < t.Records; r++)
        {
            var id = DbcFile.Field(t, r, 0);
            var nome = DbcFile.Text(t, DbcFile.Field(t, r, campo));
            if (nome.Length > 0) mapa[id] = nome;
        }

        return mapa;
    }

    /// <summary>
    /// Confere o campo esperado e, se ele não trouxer nomes plausíveis, procura
    /// outro. Um deslocamento de campo entre versões do DBC produziria centenas
    /// de nomes vazios em silêncio.
    /// </summary>
    public static int DescobrirCampoDoIcone(DbcFile.Table t)
    {
        var candidatos = new[] { CampoIconePadrao }
            .Concat(Enumerable.Range(1, Math.Min(t.Fields - 1, 12)))
            .Distinct();

        var melhor = CampoIconePadrao;
        var melhorNota = -1;

        foreach (var campo in candidatos)
        {
            if (campo >= t.Fields) continue;

            var nota = 0;
            var amostra = Math.Min(t.Records, 200);

            for (var r = 0; r < amostra; r++)
            {
                var texto = DbcFile.Text(t, DbcFile.Field(t, r, campo));
                if (ParecemNomeDeIcone(texto)) nota++;
            }

            if (nota > melhorNota) { melhorNota = nota; melhor = campo; }
        }

        return melhor;
    }

    /// <summary>
    /// Nomes de ícone são coisas como "INV_Sword_39" ou "Spell_Holy_Heal":
    /// letras, dígitos e sublinhado, sem extensão nem barra.
    /// </summary>
    public static bool ParecemNomeDeIcone(string texto) =>
        texto.Length is > 2 and < 64
        && !texto.Contains('\\') && !texto.Contains('/') && !texto.Contains('.')
        && texto.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-');

    public static string CaminhoNoMpq(string nomeDoIcone) =>
        $@"Interface\Icons\{nomeDoIcone}.blp";

    /// <summary>Os MPQ do client, na ordem em que devem ser consultados.</summary>
    /// <remarks>
    /// Os patches vêm por último de propósito: eles substituem arquivos dos
    /// arquivos base, então quem responde primeiro tem que ser o mais recente.
    /// </remarks>
    public static IReadOnlyList<string> ListarMpqs(string clientDir)
    {
        var data = Path.Combine(clientDir, "Data");
        if (!Directory.Exists(data)) return Array.Empty<string>();

        var todos = new List<string>();

        // patch-3.MPQ ganha de patch-2.MPQ, que ganha de common.MPQ
        foreach (var arquivo in Directory.GetFiles(data, "*.MPQ", SearchOption.TopDirectoryOnly))
            todos.Add(arquivo);

        // e as pastas de idioma (enUS, ptBR...), que trazem os patches locais
        foreach (var pasta in Directory.GetDirectories(data))
            todos.AddRange(Directory.GetFiles(pasta, "*.MPQ", SearchOption.TopDirectoryOnly));

        // Ordem alfabetica nao serve: "patch.MPQ" vem depois de "patch-3.MPQ"
        // porque '.' e maior que '-'. O que decide e o numero do patch.
        return todos
            .OrderByDescending(f => EhPatch(Path.GetFileName(f)))
            .ThenByDescending(f => NumeroDoPatch(Path.GetFileName(f)))
            .ThenByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool EhPatch(string nome) =>
        nome.StartsWith("patch", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// O numero no fim do nome: patch-3.MPQ -> 3, patch-ptBR-2.MPQ -> 2,
    /// patch.MPQ -> 0. Quanto maior, mais recente, e mais prioridade tem.
    /// </summary>
    public static int NumeroDoPatch(string nome)
    {
        var semExtensao = Path.GetFileNameWithoutExtension(nome);
        var traco = semExtensao.LastIndexOf('-');
        if (traco < 0) return 0;

        return int.TryParse(semExtensao[(traco + 1)..], out var n) ? n : 0;
    }

    public sealed record Resultado(int Extraidos, int JaExistiam, int NaoAchados, int Falharam);

    /// <summary>
    /// Extrai os ícones pedidos para <paramref name="destino"/>, em PNG.
    /// </summary>
    /// <param name="progresso">Chamado a cada ícone: (feitos, total, nome).</param>
    public static Resultado Extract(
        string clientDir,
        string destino,
        IEnumerable<string> nomesDeIcone,
        Action<int, int, string>? progresso = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(destino);

        var nomes = nomesDeIcone
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var arquivos = ListarMpqs(clientDir);
        if (arquivos.Count == 0)
            throw new DirectoryNotFoundException($"nenhum .MPQ em {Path.Combine(clientDir, "Data")}");

        var abertos = new List<MpqArchive>();
        int extraidos = 0, jaExistiam = 0, naoAchados = 0, falharam = 0;

        try
        {
            foreach (var caminho in arquivos)
            {
                // Um MPQ ilegível não pode derrubar a extração inteira: os
                // ícones podem estar em qualquer um dos outros.
                try { abertos.Add(MpqArchive.Open(caminho)); }
                catch { /* segue para o proximo */ }
            }

            var feitos = 0;
            foreach (var nome in nomes)
            {
                ct.ThrowIfCancellationRequested();
                feitos++;
                progresso?.Invoke(feitos, nomes.Count, nome);

                var png = Path.Combine(destino, nome + ".png");
                if (File.Exists(png)) { jaExistiam++; continue; }

                var interno = CaminhoNoMpq(nome);
                byte[]? blp = null;

                foreach (var mpq in abertos)
                {
                    try
                    {
                        blp = mpq.Read(interno);
                        if (blp is not null) break;
                    }
                    catch { /* formato nao suportado neste MPQ; tenta o proximo */ }
                }

                if (blp is null) { naoAchados++; continue; }

                try
                {
                    File.WriteAllBytes(png, BlpImage.ToPng(BlpImage.Decode(blp)));
                    extraidos++;
                }
                catch
                {
                    falharam++;
                }
            }
        }
        finally
        {
            foreach (var m in abertos) m.Dispose();
        }

        return new Resultado(extraidos, jaExistiam, naoAchados, falharam);
    }
}
