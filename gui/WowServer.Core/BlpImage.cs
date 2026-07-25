namespace WowServer.Core;

/// <summary>
/// Decodificador de BLP2, o formato de imagem do WoW.
///
/// Os icones dos itens sao BLP dentro dos MPQ do client. Aqui so se cuida da
/// imagem: quem tira o arquivo do MPQ e <see cref="MpqArchive"/>.
///
/// O 3.3.5a usa BLP2 em tres formas, e todas aparecem entre os icones:
///   encoding 1 = paleta de 256 cores
///   encoding 2 = DXT (DXT1, DXT3 ou DXT5, conforme alphaEncoding)
///   encoding 3 = BGRA direto
/// </summary>
public static class BlpImage
{
    /// <summary>Imagem decodificada, em BGRA de 8 bits por canal.</summary>
    public sealed record Decoded(int Width, int Height, byte[] Bgra);

    public static bool IsBlp(ReadOnlySpan<byte> dados) =>
        dados.Length >= 4 && dados[0] == (byte)'B' && dados[1] == (byte)'L'
        && dados[2] == (byte)'P' && dados[3] == (byte)'2';

    /// <summary>
    /// Decodifica o mip 0 (a imagem em tamanho cheio).
    /// </summary>
    /// <exception cref="InvalidDataException">Formato nao reconhecido.</exception>
    public static Decoded Decode(byte[] dados)
    {
        if (!IsBlp(dados)) throw new InvalidDataException("nao e um arquivo BLP2");
        if (dados.Length < 148) throw new InvalidDataException("BLP truncado");

        var encoding = dados[8];
        var alphaDepth = dados[9];
        var alphaEncoding = dados[10];

        var width = (int)BitConverter.ToUInt32(dados, 12);
        var height = (int)BitConverter.ToUInt32(dados, 16);

        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new InvalidDataException($"tamanho invalido: {width}x{height}");

        var mipOffset = (int)BitConverter.ToUInt32(dados, 20);
        var mipSize = (int)BitConverter.ToUInt32(dados, 20 + 16 * 4);

        if (mipOffset <= 0 || mipSize <= 0 || mipOffset + mipSize > dados.Length)
            throw new InvalidDataException("mip 0 fora do arquivo");

        var corpo = new ReadOnlySpan<byte>(dados, mipOffset, mipSize);
        var saida = new byte[width * height * 4];

        switch (encoding)
        {
            case 1:
                DecodificarPaleta(dados, corpo, width, height, alphaDepth, saida);
                break;

            case 2:
                // alphaEncoding manda: 0 = DXT1, 1 = DXT3, 7 = DXT5.
                var formato = alphaEncoding switch
                {
                    0 => 1,
                    1 => 3,
                    7 => 5,
                    _ => alphaDepth > 1 ? 3 : 1,
                };
                DecodificarDxt(corpo, width, height, formato, saida);
                break;

            case 3:
                if (corpo.Length < saida.Length)
                    throw new InvalidDataException("dados BGRA insuficientes");
                corpo[..saida.Length].CopyTo(saida);
                break;

            default:
                throw new InvalidDataException($"encoding BLP nao suportado: {encoding}");
        }

        return new Decoded(width, height, saida);
    }

    /// <summary>
    /// Paleta de 256 cores BGRA logo depois do cabecalho, um indice por pixel.
    /// Com alphaDepth 8, o alfa vem num bloco separado, depois dos indices.
    /// </summary>
    private static void DecodificarPaleta(
        byte[] arquivo, ReadOnlySpan<byte> corpo, int w, int h, byte alphaDepth, byte[] saida)
    {
        const int paletaOffset = 148;
        if (arquivo.Length < paletaOffset + 256 * 4)
            throw new InvalidDataException("paleta truncada");

        var pixels = w * h;
        if (corpo.Length < pixels) throw new InvalidDataException("indices insuficientes");

        for (var i = 0; i < pixels; i++)
        {
            var cor = paletaOffset + corpo[i] * 4;
            saida[i * 4 + 0] = arquivo[cor + 0];
            saida[i * 4 + 1] = arquivo[cor + 1];
            saida[i * 4 + 2] = arquivo[cor + 2];
            saida[i * 4 + 3] = 255;
        }

        if (alphaDepth == 8 && corpo.Length >= pixels * 2)
        {
            for (var i = 0; i < pixels; i++) saida[i * 4 + 3] = corpo[pixels + i];
        }
        else if (alphaDepth == 1 && corpo.Length >= pixels + (pixels + 7) / 8)
        {
            for (var i = 0; i < pixels; i++)
            {
                var bit = corpo[pixels + i / 8] >> (i % 8) & 1;
                saida[i * 4 + 3] = (byte)(bit == 1 ? 255 : 0);
            }
        }
    }

    /// <summary>
    /// DXT1/3/5. Blocos de 4x4 pixels; a imagem e percorrida bloco a bloco.
    /// </summary>
    private static void DecodificarDxt(
        ReadOnlySpan<byte> corpo, int w, int h, int formato, byte[] saida)
    {
        var blocosX = (w + 3) / 4;
        var blocosY = (h + 3) / 4;
        var tamanhoBloco = formato == 1 ? 8 : 16;

        if (corpo.Length < blocosX * blocosY * tamanhoBloco)
            throw new InvalidDataException("dados DXT insuficientes");

        var cores = new byte[4 * 4];   // 4 cores BGRA
        var alfa = new byte[16];

        for (var by = 0; by < blocosY; by++)
        {
            for (var bx = 0; bx < blocosX; bx++)
            {
                var p = (by * blocosX + bx) * tamanhoBloco;

                // DXT3 e DXT5 trazem 8 bytes de alfa antes das cores.
                var pCor = formato == 1 ? p : p + 8;

                if (formato == 3) LerAlfaDxt3(corpo.Slice(p, 8), alfa);
                else if (formato == 5) LerAlfaDxt5(corpo.Slice(p, 8), alfa);
                else Array.Fill(alfa, (byte)255);

                LerCoresDxt(corpo.Slice(pCor, 8), formato == 1, cores);
                var indices = BitConverter.ToUInt32(corpo.Slice(pCor + 4, 4));

                for (var py = 0; py < 4; py++)
                {
                    for (var px = 0; px < 4; px++)
                    {
                        var x = bx * 4 + px;
                        var y = by * 4 + py;
                        if (x >= w || y >= h) continue;

                        var i = py * 4 + px;
                        var idx = (int)(indices >> (i * 2) & 3);
                        var destino = (y * w + x) * 4;

                        saida[destino + 0] = cores[idx * 4 + 0];
                        saida[destino + 1] = cores[idx * 4 + 1];
                        saida[destino + 2] = cores[idx * 4 + 2];

                        // No DXT1, o indice 3 com c0 <= c1 e transparente.
                        var a = cores[idx * 4 + 3];
                        saida[destino + 3] = formato == 1 ? a : alfa[i];
                    }
                }
            }
        }
    }

    private static void LerCoresDxt(ReadOnlySpan<byte> bloco, bool dxt1, byte[] cores)
    {
        var c0 = BitConverter.ToUInt16(bloco[..2]);
        var c1 = BitConverter.ToUInt16(bloco.Slice(2, 2));

        Rgb565(c0, cores, 0);
        Rgb565(c1, cores, 1);

        // No DXT1, c0 <= c1 troca a interpolacao por 3 cores + transparente.
        var quatroCores = !dxt1 || c0 > c1;

        for (var canal = 0; canal < 3; canal++)
        {
            var a = cores[0 * 4 + canal];
            var b = cores[1 * 4 + canal];

            if (quatroCores)
            {
                cores[2 * 4 + canal] = (byte)((2 * a + b) / 3);
                cores[3 * 4 + canal] = (byte)((a + 2 * b) / 3);
            }
            else
            {
                cores[2 * 4 + canal] = (byte)((a + b) / 2);
                cores[3 * 4 + canal] = 0;
            }
        }

        cores[0 * 4 + 3] = 255;
        cores[1 * 4 + 3] = 255;
        cores[2 * 4 + 3] = 255;
        cores[3 * 4 + 3] = (byte)(quatroCores ? 255 : 0);
    }

    private static void Rgb565(ushort c, byte[] cores, int i)
    {
        var r = (c >> 11) & 0x1F;
        var g = (c >> 5) & 0x3F;
        var b = c & 0x1F;

        // Repetir os bits altos nos baixos: 5 bits -> 8 bits sem perder o branco.
        cores[i * 4 + 0] = (byte)(b << 3 | b >> 2);
        cores[i * 4 + 1] = (byte)(g << 2 | g >> 4);
        cores[i * 4 + 2] = (byte)(r << 3 | r >> 2);
    }

    private static void LerAlfaDxt3(ReadOnlySpan<byte> bloco, byte[] alfa)
    {
        for (var i = 0; i < 16; i++)
        {
            var nibble = i % 2 == 0 ? bloco[i / 2] & 0x0F : bloco[i / 2] >> 4;
            alfa[i] = (byte)(nibble * 17);   // 0..15 -> 0..255
        }
    }

    private static void LerAlfaDxt5(ReadOnlySpan<byte> bloco, byte[] alfa)
    {
        var a = new int[8];
        a[0] = bloco[0];
        a[1] = bloco[1];

        if (a[0] > a[1])
        {
            for (var i = 0; i < 6; i++) a[2 + i] = ((6 - i) * a[0] + (1 + i) * a[1]) / 7;
        }
        else
        {
            for (var i = 0; i < 4; i++) a[2 + i] = ((4 - i) * a[0] + (1 + i) * a[1]) / 5;
            a[6] = 0;
            a[7] = 255;
        }

        // Os 16 indices de 3 bits ficam em 6 bytes, dois grupos de 24 bits.
        for (var metade = 0; metade < 2; metade++)
        {
            var bits = (long)bloco[2 + metade * 3]
                     | (long)bloco[3 + metade * 3] << 8
                     | (long)bloco[4 + metade * 3] << 16;

            for (var i = 0; i < 8; i++)
                alfa[metade * 8 + i] = (byte)a[(int)(bits >> (i * 3) & 7)];
        }
    }

    /// <summary>
    /// Grava a imagem como PNG, sem depender de biblioteca de imagem.
    ///
    /// PNG e escrito na mao de proposito: o Core tem que continuar compilando
    /// em qualquer plataforma, e System.Drawing nao serve para isso.
    /// </summary>
    public static byte[] ToPng(Decoded img)
    {
        // Cada linha do PNG comeca com o byte de filtro (0 = nenhum).
        var bruto = new byte[img.Height * (1 + img.Width * 4)];
        var d = 0;
        for (var y = 0; y < img.Height; y++)
        {
            bruto[d++] = 0;
            for (var x = 0; x < img.Width; x++)
            {
                var s = (y * img.Width + x) * 4;
                bruto[d++] = img.Bgra[s + 2];   // R
                bruto[d++] = img.Bgra[s + 1];   // G
                bruto[d++] = img.Bgra[s + 0];   // B
                bruto[d++] = img.Bgra[s + 3];   // A
            }
        }

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        EscreverBigEndian(ihdr, 0, (uint)img.Width);
        EscreverBigEndian(ihdr, 4, (uint)img.Height);
        ihdr[8] = 8;    // bits por canal
        ihdr[9] = 6;    // RGBA
        EscreverChunk(ms, "IHDR", ihdr);

        EscreverChunk(ms, "IDAT", ComprimirZlib(bruto));
        EscreverChunk(ms, "IEND", Array.Empty<byte>());

        return ms.ToArray();
    }

    private static byte[] ComprimirZlib(byte[] dados)
    {
        using var saida = new MemoryStream();

        // Cabecalho zlib: o PNG exige zlib, e DeflateStream produz deflate cru.
        saida.WriteByte(0x78);
        saida.WriteByte(0x9C);

        using (var deflate = new System.IO.Compression.DeflateStream(
                   saida, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(dados, 0, dados.Length);
        }

        var adler = Adler32(dados);
        saida.WriteByte((byte)(adler >> 24));
        saida.WriteByte((byte)(adler >> 16));
        saida.WriteByte((byte)(adler >> 8));
        saida.WriteByte((byte)adler);

        return saida.ToArray();
    }

    private static uint Adler32(byte[] dados)
    {
        uint a = 1, b = 0;
        foreach (var x in dados)
        {
            a = (a + x) % 65521;
            b = (b + a) % 65521;
        }
        return b << 16 | a;
    }

    private static void EscreverChunk(Stream saida, string tipo, byte[] dados)
    {
        var cabecalho = new byte[4];
        EscreverBigEndian(cabecalho, 0, (uint)dados.Length);
        saida.Write(cabecalho);

        var corpo = new byte[4 + dados.Length];
        for (var i = 0; i < 4; i++) corpo[i] = (byte)tipo[i];
        dados.CopyTo(corpo, 4);
        saida.Write(corpo);

        var crc = new byte[4];
        EscreverBigEndian(crc, 0, Crc32(corpo));
        saida.Write(crc);
    }

    private static void EscreverBigEndian(byte[] destino, int i, uint v)
    {
        destino[i + 0] = (byte)(v >> 24);
        destino[i + 1] = (byte)(v >> 16);
        destino[i + 2] = (byte)(v >> 8);
        destino[i + 3] = (byte)v;
    }

    private static uint[]? _tabelaCrc;

    private static uint Crc32(byte[] dados)
    {
        if (_tabelaCrc is null)
        {
            _tabelaCrc = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                _tabelaCrc[n] = c;
            }
        }

        var crc = 0xFFFFFFFFu;
        foreach (var b in dados) crc = _tabelaCrc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
