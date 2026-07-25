using System.IO.Compression;

namespace WowServer.Core;

/// <summary>
/// Leitor de MPQ, o formato de arquivo do WoW, so para LER um arquivo pelo nome.
///
/// Nao e uma biblioteca completa de MPQ: cobre o que os icones precisam -
/// arquivo nao criptografado, guardado inteiro ou em setores comprimidos com
/// zlib. Qualquer outra combinacao levanta excecao com o motivo, em vez de
/// devolver bytes errados em silencio.
/// </summary>
public sealed class MpqArchive : IDisposable
{
    private const uint Assinatura = 0x1A51504D;   // 'MPQ\x1A'

    private const uint FlagExiste      = 0x80000000;
    private const uint FlagComprimido  = 0x00000200;
    private const uint FlagImplodido   = 0x00000100;
    private const uint FlagUnidadeUnica = 0x01000000;
    private const uint FlagCriptografado = 0x00010000;

    private static readonly uint[] TabelaCripto = MontarTabelaCripto();

    private readonly FileStream _arquivo;
    private readonly long _inicio;
    private readonly int _tamanhoSetor;
    private readonly uint[] _hash;     // 4 uints por entrada
    private readonly uint[] _bloco;    // 4 uints por entrada
    private readonly int _entradasHash;

    public string Path { get; }

    private MpqArchive(string caminho, FileStream fs, long inicio, int tamanhoSetor,
                       uint[] hash, uint[] bloco, int entradasHash)
    {
        Path = caminho;
        _arquivo = fs;
        _inicio = inicio;
        _tamanhoSetor = tamanhoSetor;
        _hash = hash;
        _bloco = bloco;
        _entradasHash = entradasHash;
    }

    public static MpqArchive Open(string caminho)
    {
        var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);

        // O cabecalho nem sempre esta no byte 0: alguns MPQ vem depois de um
        // executavel ou de padding. O formato manda procurar de 512 em 512.
        long inicio = -1;
        var buf = new byte[32];
        for (long p = 0; p + 32 <= fs.Length; p += 512)
        {
            fs.Position = p;
            if (fs.Read(buf, 0, 32) < 32) break;
            if (BitConverter.ToUInt32(buf, 0) == Assinatura) { inicio = p; break; }
        }

        if (inicio < 0)
        {
            fs.Dispose();
            throw new InvalidDataException($"{System.IO.Path.GetFileName(caminho)} nao parece um MPQ");
        }

        // Cabecalho MPQ v1, 32 bytes:
        //   0 magic   4 headerSize   8 archiveSize   12 formatVersion(2)
        //  14 sectorSizeShift(2)     16 hashTablePos 20 blockTablePos
        //  24 hashTableSize          28 blockTableSize
        var tamanhoSetor = 512 << BitConverter.ToUInt16(buf, 14);
        var posHash = inicio + BitConverter.ToUInt32(buf, 16);
        var posBloco = inicio + BitConverter.ToUInt32(buf, 20);
        var nHash = (int)BitConverter.ToUInt32(buf, 24);
        var nBloco = (int)BitConverter.ToUInt32(buf, 28);

        if (nHash <= 0 || (nHash & (nHash - 1)) != 0)
            throw new InvalidDataException($"tabela hash com tamanho invalido ({nHash})");
        if (nBloco <= 0)
            throw new InvalidDataException($"tabela de blocos vazia ({nBloco})");

        var hash = LerTabela(fs, posHash, nHash, "(hash table)");
        var bloco = LerTabela(fs, posBloco, nBloco, "(block table)");

        return new MpqArchive(caminho, fs, inicio, tamanhoSetor, hash, bloco, nHash);
    }

    /// <summary>As tabelas sao sempre criptografadas, com chave derivada do nome fixo.</summary>
    private static uint[] LerTabela(FileStream fs, long pos, int entradas, string nome)
    {
        var bytes = new byte[entradas * 16];
        fs.Position = pos;
        if (fs.Read(bytes, 0, bytes.Length) != bytes.Length)
            throw new InvalidDataException($"nao consegui ler {nome}");

        var dados = new uint[entradas * 4];
        Buffer.BlockCopy(bytes, 0, dados, 0, bytes.Length);

        Descriptografar(dados, Hash(nome, 3));
        return dados;
    }

    /// <summary>Devolve os bytes do arquivo, ou null se ele nao existe no MPQ.</summary>
    public byte[]? Read(string nomeInterno)
    {
        var i = Localizar(nomeInterno);
        if (i < 0) return null;

        var indiceBloco = (int)_hash[i * 4 + 3];
        if (indiceBloco * 4 + 3 >= _bloco.Length) return null;

        var posicao = _inicio + _bloco[indiceBloco * 4 + 0];
        var tamComprimido = (int)_bloco[indiceBloco * 4 + 1];
        var tamReal = (int)_bloco[indiceBloco * 4 + 2];
        var flags = _bloco[indiceBloco * 4 + 3];

        if ((flags & FlagExiste) == 0) return null;

        if ((flags & FlagCriptografado) != 0)
            throw new NotSupportedException($"'{nomeInterno}' esta criptografado dentro do MPQ");

        if ((flags & FlagImplodido) != 0)
            throw new NotSupportedException($"'{nomeInterno}' usa compressao PKWARE, que este leitor nao faz");

        // Guardado inteiro: um bloco so, sem tabela de setores.
        if ((flags & FlagUnidadeUnica) != 0)
        {
            var bruto = LerBytes(posicao, tamComprimido);
            return (flags & FlagComprimido) != 0 ? Descomprimir(bruto, tamReal) : bruto;
        }

        if ((flags & FlagComprimido) == 0)
            return LerBytes(posicao, tamReal);

        var setores = (tamReal + _tamanhoSetor - 1) / _tamanhoSetor;
        var tabela = LerBytes(posicao, (setores + 1) * 4);

        var saida = new byte[tamReal];
        var escrito = 0;

        for (var s = 0; s < setores; s++)
        {
            var de = BitConverter.ToUInt32(tabela, s * 4);
            var ate = BitConverter.ToUInt32(tabela, (s + 1) * 4);
            if (ate <= de) throw new InvalidDataException("tabela de setores inconsistente");

            var bruto = LerBytes(posicao + de, (int)(ate - de));
            var esperado = Math.Min(_tamanhoSetor, tamReal - escrito);

            // Setor que nao comprimiu vem do mesmo tamanho, sem byte de metodo.
            var pedaco = bruto.Length == esperado ? bruto : Descomprimir(bruto, esperado);

            pedaco.CopyTo(saida, escrito);
            escrito += pedaco.Length;
        }

        return saida;
    }

    private byte[] LerBytes(long posicao, int quantos)
    {
        var b = new byte[quantos];
        _arquivo.Position = posicao;
        var lido = 0;
        while (lido < quantos)
        {
            var n = _arquivo.Read(b, lido, quantos - lido);
            if (n <= 0) throw new InvalidDataException("fim de arquivo inesperado");
            lido += n;
        }
        return b;
    }

    /// <summary>O primeiro byte diz como o setor foi comprimido.</summary>
    private static byte[] Descomprimir(byte[] bruto, int tamanhoReal)
    {
        if (bruto.Length == 0) return bruto;

        var metodo = bruto[0];
        var corpo = new ReadOnlySpan<byte>(bruto, 1, bruto.Length - 1);

        // 0x02 = zlib. E o que os arquivos de Interface usam.
        if (metodo == 0x02)
        {
            using var entrada = new MemoryStream(corpo.ToArray());
            using var zlib = new ZLibStream(entrada, CompressionMode.Decompress);
            var saida = new byte[tamanhoReal];

            var lido = 0;
            while (lido < tamanhoReal)
            {
                var n = zlib.Read(saida, lido, tamanhoReal - lido);
                if (n <= 0) break;
                lido += n;
            }

            if (lido != tamanhoReal)
                throw new InvalidDataException($"zlib devolveu {lido} bytes, esperava {tamanhoReal}");

            return saida;
        }

        throw new NotSupportedException(
            $"compressao 0x{metodo:X2} nao suportada por este leitor");
    }

    /// <summary>
    /// Acha a entrada na tabela hash. O MPQ nao guarda o nome: guarda tres
    /// hashes dele, e a busca e linear a partir da posicao indicada pelo
    /// primeiro.
    /// </summary>
    private int Localizar(string nome)
    {
        var inicio = (int)(Hash(nome, 0) & (uint)(_entradasHash - 1));
        var a = Hash(nome, 1);
        var b = Hash(nome, 2);

        for (var i = 0; i < _entradasHash; i++)
        {
            var e = (inicio + i) % _entradasHash;
            var indiceBloco = _hash[e * 4 + 3];

            if (indiceBloco == 0xFFFFFFFF) return -1;   // entrada vazia: acabou
            if (_hash[e * 4 + 0] == a && _hash[e * 4 + 1] == b) return e;
        }

        return -1;
    }

    private static uint[] MontarTabelaCripto()
    {
        var t = new uint[0x500];
        uint semente = 0x00100001;

        for (uint i = 0; i < 0x100; i++)
        {
            for (uint j = 0; j < 5; j++)
            {
                semente = (semente * 125 + 3) % 0x2AAAAB;
                var a = (semente & 0xFFFF) << 16;

                semente = (semente * 125 + 3) % 0x2AAAAB;
                var b = semente & 0xFFFF;

                t[i + j * 0x100] = a | b;
            }
        }

        return t;
    }

    /// <summary>
    /// Hash do MPQ. O nome e sempre maiusculo e com '\' - por isso '/' e
    /// convertido: "Interface/Icons/x.blp" e "Interface\Icons\x.blp" tem que
    /// dar o mesmo hash.
    /// </summary>
    public static uint Hash(string nome, int tipo)
    {
        uint semente1 = 0x7FED7FED;
        uint semente2 = 0xEEEEEEEE;

        foreach (var caractere in nome)
        {
            var c = caractere == '/' ? '\\' : char.ToUpperInvariant(caractere);
            semente1 = TabelaCripto[tipo * 0x100 + c] ^ (semente1 + semente2);
            semente2 = c + semente1 + semente2 + (semente2 << 5) + 3;
        }

        return semente1;
    }

    private static void Descriptografar(uint[] dados, uint chave)
    {
        uint semente = 0xEEEEEEEE;

        for (var i = 0; i < dados.Length; i++)
        {
            semente += TabelaCripto[0x400 + (chave & 0xFF)];
            var v = dados[i] ^ (chave + semente);

            chave = ((~chave << 0x15) + 0x11111111) | (chave >> 0x0B);
            semente = v + semente + (semente << 5) + 3;
            dados[i] = v;
        }
    }

    public void Dispose() => _arquivo.Dispose();
}
